using CadToRevit.Models.Rooms.EquipmentValidation;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace CadToRevit.Services.Rooms.EquipmentValidation
{
    public sealed class AhuPlacementValidationService
    {
        private const string CheckRoomFitUrl = "http://127.0.0.1:8000/api/check_room_fit";

        public async Task<AhuPlacementValidationResult> ValidateAsync(AhuPlacementValidationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidOperationException("Route API session is not initialized.");
            }

            if (request.FamilyId <= 0)
            {
                throw new InvalidOperationException("AHU model id is invalid.");
            }

            string requestJson = BuildRequestJson(request);
            DiagnosticRecorder.AppendDebug("[AhuRoomFitApi] requestUrl=" + CheckRoomFitUrl);
            DiagnosticRecorder.AppendDebug("[AhuRoomFitApi] requestBody=" + requestJson);

            string responseText;
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                using (StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await client.PostAsync(CheckRoomFitUrl, content).ConfigureAwait(true))
                {
                    responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] responseStatus=" +
                        ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                        " " +
                        response.StatusCode);
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] responseBody=" +
                        (string.IsNullOrWhiteSpace(responseText) ? "(empty)" : responseText));

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            "Room fit validation API failed. HTTP " +
                            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            ": " +
                            responseText);
                    }
                }
            }

            RoomFitResponseDto responseDto = DeserializeResponse(responseText);
            if (responseDto == null)
            {
                throw new InvalidOperationException("Room fit validation API returned an invalid response.");
            }

            // An HTTP-200 response means the room-fit call itself completed.
            // The UI/placement workflow must be driven by `fit`, not by `success`,
            // because the customer still needs the AHU inserted at placement_point
            // when Python says it cannot fit.  In particular, Python may return
            // success=false, fit=false for diagnostic cases such as
            // "Placement point is not inside the selected room."  Treat that as an
            // Oversized/non-fit result, show the returned reason/message, and keep
            // going with Revit insertion so the point/equipment can be inspected.
            bool? physicalFitValue = responseDto.PhysicalFit ?? responseDto.Fit;
            bool physicalFit = physicalFitValue == true;
            bool apiSuccess = responseDto.Success || physicalFitValue.HasValue;
            bool repositioned = responseDto.Repositioned == true ||
                (responseDto.CurrentPlacementFit == false && responseDto.FeasiblePlacementFound == true);
            double placementXmm = request.PlacementPointXmm;
            double placementYmm = request.PlacementPointYmm;
            if (responseDto.PlacementPointMm != null && responseDto.PlacementPointMm.Length >= 2 &&
                IsFinite(responseDto.PlacementPointMm[0]) && IsFinite(responseDto.PlacementPointMm[1]))
            {
                placementXmm = responseDto.PlacementPointMm[0];
                placementYmm = responseDto.PlacementPointMm[1];
            }

            List<string> reasons = BuildReasons(responseDto, physicalFit);
            string status = !apiSuccess || !physicalFitValue.HasValue
                ? AhuPlacementValidationStatuses.ApiError
                : physicalFit
                    ? (responseDto.MaintenanceFit == false
                        ? AhuPlacementValidationStatuses.MaintenanceClearanceInsufficient
                        : (repositioned ? AhuPlacementValidationStatuses.ValidAfterRepositioning : AhuPlacementValidationStatuses.Valid))
                    : (responseDto.FeasiblePlacementFound == false ||
                       string.Equals(responseDto.ViolationType, "NoFeasiblePlacement", StringComparison.OrdinalIgnoreCase)
                        ? AhuPlacementValidationStatuses.NoFeasiblePlacement
                        : AhuPlacementValidationStatuses.CurrentPlacementInvalid);

            DiagnosticRecorder.AppendDebug(
                "[AhuRoomFitApi] apiSuccess=" + responseDto.Success +
                ", fit=" + physicalFit +
                ", maintenanceFit=" + (responseDto.MaintenanceFit.HasValue ? responseDto.MaintenanceFit.Value.ToString() : "(null)") +
                ", repositioned=" + repositioned +
                ", modelId=" + request.FamilyId.ToString(CultureInfo.InvariantCulture) +
                ", placementPointMm=[" +
                placementXmm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                placementYmm.ToString("0.###", CultureInfo.InvariantCulture) +
                "], orientationDeg=" +
                (responseDto.OrientationDeg.HasValue
                    ? responseDto.OrientationDeg.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "(null)"));

            return new AhuPlacementValidationResult
            {
                HasResult = true,
                IsValid = physicalFit,
                Status = status,
                Reasons = reasons,
                Source = "API",
                RawResponse = responseText ?? string.Empty,
                OrientationDeg = responseDto.OrientationDeg,
                PlacementPointXmm = placementXmm,
                PlacementPointYmm = placementYmm,
                PhysicalFit = physicalFit,
                MaintenanceFit = responseDto.MaintenanceFit,
                MaintenanceReason = responseDto.MaintenanceReason,
                CurrentPlacementFit = responseDto.CurrentPlacementFit,
                FeasiblePlacementFound = responseDto.FeasiblePlacementFound,
                Repositioned = repositioned,
                CanInsert = apiSuccess && physicalFitValue.HasValue,
                ViolationType = responseDto.ViolationType
            };
        }

        private static string BuildRequestJson(AhuPlacementValidationRequest request)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"session_id\":\"").Append(EscapeJson(request.SessionId)).Append("\",");
            sb.Append("\"model_id\":").Append(request.FamilyId.ToString(CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"evaluation_mode\":\"")
                .Append(EscapeJson(string.IsNullOrWhiteSpace(request.EvaluationMode)
                    ? "find_feasible_placement"
                    : request.EvaluationMode.Trim()))
                .Append("\",");
            sb.Append("\"room_height_mm\":")
                .Append(request.RoomHeightMm.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",");
            sb.Append("\"orientation\":");
            if (request.Orientation.HasValue)
            {
                sb.Append(request.Orientation.Value.ToString("0.########", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("null");
            }

            sb.Append(",\"point_in_room\":[")
                .Append(request.PointInRoomXmm.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",")
                .Append(request.PointInRoomYmm.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("]");

            sb.Append(",\"placement_point\":[")
                .Append(request.PlacementPointXmm.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(",")
                .Append(request.PlacementPointYmm.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("]");

            sb.Append(",\"use_maintenance_space\":")
                .Append(request.UseMaintenanceSpace ? "true" : "false");

            sb.Append(",\"evaluate_maintenance_space\":")
                .Append(request.EvaluateMaintenanceSpace ? "true" : "false");

            AppendNumberArrayJson(sb, "door_direction", request.DoorDirection);

            if (!string.IsNullOrWhiteSpace(request.DoorFacingSide))
            {
                sb.Append(",\"door_facing_side\":\"")
                    .Append(EscapeJson(request.DoorFacingSide.Trim().ToLowerInvariant()))
                    .Append("\"");
            }

            AppendWallFacingSidesJson(sb, request.WallFacingSides);
            AppendStringArrayJson(sb, "door_facing_side_options", request.DoorFacingSideOptions);
            AppendMaintenanceSpacesJson(sb, request.MaintenanceSpaces);
            AppendSubModulesJson(sb, request.SubModules);
            AppendRestrictedAreasJson(sb, request.RestrictedAreas);

            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendNumberArrayJson(StringBuilder sb, string name, double[] values)
        {
            if (sb == null || string.IsNullOrWhiteSpace(name) || values == null || values.Length == 0)
            {
                return;
            }

            sb.Append(",\"").Append(name).Append("\":[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(values[i].ToString("0.########", CultureInfo.InvariantCulture));
            }
            sb.Append("]");
        }

        private static void AppendStringArrayJson(StringBuilder sb, string name, IReadOnlyList<string> values)
        {
            if (sb == null || string.IsNullOrWhiteSpace(name) || values == null || values.Count == 0)
            {
                return;
            }

            sb.Append(",\"").Append(name).Append("\":[");
            bool first = true;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(value.Trim().ToLowerInvariant())).Append("\"");
            }
            sb.Append("]");
        }

        private static void AppendRestrictedAreasJson(
            StringBuilder sb,
            IReadOnlyList<CadToRevit.Services.PathPreview.RestrictedAreaRequestItem> areas)
        {
            if (sb == null || areas == null || areas.Count == 0)
            {
                return;
            }

            sb.Append(",\"restricted_areas\":[");
            bool first = true;
            foreach (CadToRevit.Services.PathPreview.RestrictedAreaRequestItem area in areas)
            {
                if (area == null || area.Bounds == null || area.Bounds.Length < 4) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"name\":\"").Append(EscapeJson(area.Name ?? string.Empty)).Append("\",\"bounds\":[");
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(area.Bounds[i].ToString("0.###", CultureInfo.InvariantCulture));
                }
                sb.Append("]}");
            }
            sb.Append("]");
        }

        private static void AppendWallFacingSidesJson(
            StringBuilder sb,
            IReadOnlyList<string> wallFacingSides)
        {
            if (sb == null || wallFacingSides == null || wallFacingSides.Count == 0)
            {
                return;
            }

            sb.Append(",\"wall_facing_sides\":[");
            bool first = true;
            foreach (string side in wallFacingSides)
            {
                if (string.IsNullOrWhiteSpace(side))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(",");
                }

                first = false;
                sb.Append("\"")
                    .Append(EscapeJson(side.Trim().ToLowerInvariant()))
                    .Append("\"");
            }

            sb.Append("]");
        }

        private static void AppendMaintenanceSpacesJson(
            StringBuilder sb,
            IReadOnlyList<AhuPlacementMaintenanceSpaceRequest> maintenanceSpaces)
        {
            if (sb == null || maintenanceSpaces == null || maintenanceSpaces.Count == 0)
            {
                return;
            }

            sb.Append(",\"maintenance_spaces\":[");
            bool first = true;
            foreach (AhuPlacementMaintenanceSpaceRequest maintenanceSpace in maintenanceSpaces)
            {
                if (maintenanceSpace == null || string.IsNullOrWhiteSpace(maintenanceSpace.Side))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(",");
                }
                first = false;

                sb.Append("{\"maintenance\":\"")
                    .Append(EscapeJson(maintenanceSpace.Maintenance ?? string.Empty))
                    .Append("\",\"side\":\"")
                    .Append(EscapeJson(maintenanceSpace.Side.Trim().ToLowerInvariant()))
                    .Append("\",\"dimension_mm\":")
                    .Append(maintenanceSpace.DimensionMm.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\"is_wall_side\":")
                    .Append(maintenanceSpace.IsWallSide ? "true" : "false")
                    .Append(",\"is_door_side\":")
                    .Append(maintenanceSpace.IsDoorSide ? "true" : "false")
                    .Append("}");
            }

            sb.Append("]");
        }

        private static void AppendSubModulesJson(
            StringBuilder sb,
            IReadOnlyList<AhuPlacementSubModuleRequest> subModules)
        {
            if (sb == null || subModules == null || subModules.Count == 0)
            {
                return;
            }

            sb.Append(",\"sub_modules\":[");
            bool firstModule = true;
            foreach (AhuPlacementSubModuleRequest subModule in subModules)
            {
                if (subModule == null)
                {
                    continue;
                }

                if (!firstModule)
                {
                    sb.Append(",");
                }
                firstModule = false;

                sb.Append("{\"module\":\"")
                    .Append(EscapeJson(subModule.Module ?? string.Empty))
                    .Append("\",\"name\":\"")
                    .Append(EscapeJson(subModule.Name ?? string.Empty))
                    .Append("\",\"points\":[");

                IReadOnlyList<AhuPlacementPoint2D> points = subModule.Points;
                if (points != null)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(",");
                        }

                        AhuPlacementPoint2D point = points[i] ?? new AhuPlacementPoint2D();
                        sb.Append("[")
                            .Append(point.X.ToString("0.###", CultureInfo.InvariantCulture))
                            .Append(",")
                            .Append(point.Y.ToString("0.###", CultureInfo.InvariantCulture))
                            .Append("]");
                    }
                }

                sb.Append("]}");
            }
            sb.Append("]");
        }

        private static RoomFitResponseDto DeserializeResponse(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(responseText);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    DataContractJsonSerializer serializer =
                        new DataContractJsonSerializer(typeof(RoomFitResponseDto));
                    return serializer.ReadObject(stream) as RoomFitResponseDto;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[AhuRoomFitApi] deserializeFailed=" + ex.Message);
                return null;
            }
        }

        private static List<string> BuildReasons(RoomFitResponseDto response, bool physicalFit)
        {
            List<string> reasons = new List<string>();
            if (response == null)
            {
                return reasons;
            }

            if (physicalFit && response.MaintenanceFit == false)
            {
                string maintenanceReason = NormalizeMessage(response.MaintenanceReason);
                reasons.Add(string.IsNullOrWhiteSpace(maintenanceReason)
                    ? "Maintenance clearance is insufficient."
                    : maintenanceReason);
                return reasons;
            }

            if (physicalFit)
            {
                return reasons;
            }

            // The prototype shows one yellow reason row. Prefer Python's own
            // human-readable reason, then message. Only synthesize a one-line
            // exceed summary when neither field is provided.
            string reason = NormalizeMessage(response.Reason);
            string message = NormalizeMessage(response.Message);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                // Keep a single yellow row. Include Python's message when it adds
                // information, but do not create a second warning card.
                if (!string.IsNullOrWhiteSpace(message) &&
                    reason.IndexOf(message, StringComparison.OrdinalIgnoreCase) < 0 &&
                    message.IndexOf(reason, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    reasons.Add(reason + " " + message);
                }
                else
                {
                    reasons.Add(reason);
                }
                return reasons;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                reasons.Add(message);
                return reasons;
            }

            List<string> parts = new List<string>();
            AddExceedReasonPart(parts, "Access Side", response.ExceedDoorSideMm);
            AddExceedReasonPart(parts, "Other Side", response.ExceedOtherSideMm);
            AddExceedReasonPart(parts, "Front Side", response.ExceedFrontSideMm);
            AddExceedReasonPart(parts, "Back Side", response.ExceedBackSideMm);
            if (parts.Count > 0)
            {
                reasons.Add(string.Join(", ", parts));
            }
            else
            {
                reasons.Add("Device cannot fit in this room.");
            }

            return reasons;
        }

        private static string NormalizeMessage(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void AddExceedReasonPart(List<string> parts, string sideName, double? exceedMm)
        {
            if (parts == null || !exceedMm.HasValue || exceedMm.Value <= 0.01)
            {
                return;
            }

            parts.Add(
                sideName +
                " exceeds wall by " +
                Math.Round(exceedMm.Value).ToString("0", CultureInfo.InvariantCulture) +
                " mm");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        [DataContract]
        private sealed class RoomFitResponseDto
        {
            [DataMember(Name = "success")]
            public bool Success { get; set; }

            [DataMember(Name = "fit")]
            public bool? Fit { get; set; }

            [DataMember(Name = "physical_fit")]
            public bool? PhysicalFit { get; set; }

            [DataMember(Name = "maintenance_fit")]
            public bool? MaintenanceFit { get; set; }

            [DataMember(Name = "maintenance_reason")]
            public string MaintenanceReason { get; set; }

            [DataMember(Name = "current_placement_fit")]
            public bool? CurrentPlacementFit { get; set; }

            [DataMember(Name = "feasible_placement_found")]
            public bool? FeasiblePlacementFound { get; set; }

            [DataMember(Name = "repositioned")]
            public bool? Repositioned { get; set; }

            [DataMember(Name = "placement_point_mm")]
            public double[] PlacementPointMm { get; set; }

            [DataMember(Name = "violation_type")]
            public string ViolationType { get; set; }

            [DataMember(Name = "room_area_m2")]
            public double? RoomAreaM2 { get; set; }

            [DataMember(Name = "room_index")]
            public int? RoomIndex { get; set; }

            [DataMember(Name = "device_length_mm")]
            public double? DeviceLengthMm { get; set; }

            [DataMember(Name = "device_width_mm")]
            public double? DeviceWidthMm { get; set; }

            [DataMember(Name = "device_height_mm")]
            public double? DeviceHeightMm { get; set; }

            [DataMember(Name = "orientation_deg")]
            public double? OrientationDeg { get; set; }

            [DataMember(Name = "exceed_door_side_mm")]
            public double? ExceedDoorSideMm { get; set; }

            [DataMember(Name = "exceed_other_side_mm")]
            public double? ExceedOtherSideMm { get; set; }

            [DataMember(Name = "exceed_front_side_mm")]
            public double? ExceedFrontSideMm { get; set; }

            [DataMember(Name = "exceed_back_side_mm")]
            public double? ExceedBackSideMm { get; set; }

            [DataMember(Name = "reason")]
            public string Reason { get; set; }

            [DataMember(Name = "message")]
            public string Message { get; set; }
        }
    }
}
