using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Path;
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

namespace CadToRevit.Services.PathPreview
{
    public sealed class CalculatePathExecutionResult
    {
        public bool Success { get; set; }

        public bool Drawn { get; set; }

        public string Message { get; set; }

        public string ResponseBody { get; set; }

        public double? PathLengthMeters { get; set; }
    }


    public sealed class RestrictedAreaRequestItem
    {
        public string Name { get; set; }

        public double[] Bounds { get; set; } = new double[0];
    }

    public static class CalculatePathApiService
    {
        private const string CalculatePathUrl = "http://127.0.0.1:8000/api/calculate_path";
        private const string CutAndReplanUrl = "http://127.0.0.1:8000/api/cut_and_replan";

        public static string BuildRequestJson(
            string sessionId,
            double startXmm,
            double startYmm,
            double goalXmm,
            double goalYmm)
        {
            return "{" +
                   "\"session_id\":\"" + EscapeJson(sessionId) + "\"," +
                   "\"start_point\":[" +
                   startXmm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   startYmm.ToString("0.###", CultureInfo.InvariantCulture) +
                   "]," +
                   "\"goal_point\":[" +
                   goalXmm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   goalYmm.ToString("0.###", CultureInfo.InvariantCulture) +
                   "]," +
                   "\"start_orientation\":0," +
                   "\"goal_orientation\":0" +
                   "}";
        }

        public static string BuildCutAndReplanRequestJson(
            string sessionId,
            int originalModelId,
            double startXmm,
            double startYmm,
            double goalXmm,
            double goalYmm)
        {
            return BuildCutAndReplanRequestJson(
                sessionId,
                originalModelId,
                startXmm,
                startYmm,
                goalXmm,
                goalYmm,
                null);
        }

        public static string BuildCutAndReplanRequestJson(
            string sessionId,
            int originalModelId,
            double startXmm,
            double startYmm,
            double goalXmm,
            double goalYmm,
            IList<RestrictedAreaRequestItem> restrictedAreas)
        {
            return "{" +
                   "\"session_id\":\"" + EscapeJson(sessionId) + "\"," +
                   "\"original_model_id\":" +
                   originalModelId.ToString(CultureInfo.InvariantCulture) +
                   "," +
                   "\"start_point\":[" +
                   startXmm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   startYmm.ToString("0.###", CultureInfo.InvariantCulture) +
                   "]," +
                   "\"goal_point\":[" +
                   goalXmm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   goalYmm.ToString("0.###", CultureInfo.InvariantCulture) +
                   "]," +
                   "\"start_orientation\":0," +
                   "\"goal_orientation\":0," +
                   "\"restricted_area\":" + BuildRestrictedAreaJson(restrictedAreas) + "," +
                   "\"handling_clearance_mm\":0," +
                   "\"handling_tool_type\":\"pallet_jack\"" +
                   "}";
        }

        private static string BuildRestrictedAreaJson(IList<RestrictedAreaRequestItem> restrictedAreas)
        {
            if (restrictedAreas == null || restrictedAreas.Count == 0)
            {
                return "[]";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('[');
            bool wroteAny = false;

            foreach (RestrictedAreaRequestItem area in restrictedAreas)
            {
                if (area == null || area.Bounds == null || area.Bounds.Length < 4)
                {
                    continue;
                }

                if (wroteAny)
                {
                    builder.Append(',');
                }

                builder.Append("{\"name\":\"");
                builder.Append(EscapeJson(area.Name));
                builder.Append("\",\"bounds\":[");
                builder.Append(area.Bounds[0].ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(area.Bounds[1].ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(area.Bounds[2].ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(area.Bounds[3].ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append("]}");
                wroteAny = true;
            }

            builder.Append(']');
            return builder.ToString();
        }

        public static string PostCalculatePath(string requestJson)
        {
            DiagnosticRecorder.AppendDebug("[CalculatePathApi] requestUrl=" + CalculatePathUrl);
            DiagnosticRecorder.AppendDebug("[CalculatePathApi] requestBody=" + (string.IsNullOrWhiteSpace(requestJson) ? "(empty)" : requestJson));

            using (HttpClient client = new HttpClient())
            using (StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = client.PostAsync(CalculatePathUrl, content).GetAwaiter().GetResult())
            {
                string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                DiagnosticRecorder.AppendDebug(
                    "[CalculatePathApi] responseStatus=" +
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                    " " +
                    response.StatusCode.ToString());
                DiagnosticRecorder.AppendDebug(
                    "[CalculatePathApi] responseHeaders=" +
                    BuildHeadersText(response));
                DiagnosticRecorder.AppendDebug(
                    "[CalculatePathApi] responseBody=" +
                    (string.IsNullOrEmpty(responseText) ? "(empty)" : responseText));
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                        ": " + responseText);
                }

                return string.IsNullOrWhiteSpace(responseText) ? "(empty)" : responseText;
            }
        }

        public static string PostCutAndReplan(string requestJson)
        {
            DiagnosticRecorder.AppendDebug("[CutAndReplanApi] requestUrl=" + CutAndReplanUrl);
            DiagnosticRecorder.AppendDebug("[CutAndReplanApi] requestBody=" + (string.IsNullOrWhiteSpace(requestJson) ? "(empty)" : requestJson));

            try
            { 
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    using (StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = client.PostAsync(CutAndReplanUrl, content).GetAwaiter().GetResult())
                    {
                        string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        DiagnosticRecorder.AppendDebug(  
                            "[CutAndReplanApi] responseStatus=" +
                            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            "   " +
                            response.StatusCode.ToString());
                        DiagnosticRecorder.AppendDebug(
                            "[CutAndReplanApi] responseHeaders=" +
                            BuildHeadersText(response));
                        DiagnosticRecorder.AppendDebug(
                            "[CutAndReplanApi] responseBody=" +
                            (string.IsNullOrEmpty(responseText) ? "(empty)" : responseText));
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException(
                                "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                                ": " + responseText);
                        }

                        return string.IsNullOrWhiteSpace(responseText) ? "(empty)" : responseText;
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                DiagnosticRecorder.AppendDebug("[CutAndReplanApi] timedOut=True, message=" + ex.Message);
                throw new TimeoutException("Route generation timed out. Please try again or reduce the route complexity.", ex);
            }
            catch (OperationCanceledException ex)
            {
                DiagnosticRecorder.AppendDebug("[CutAndReplanApi] timedOut=True, message=" + ex.Message);
                throw new TimeoutException("Route generation timed out. Please try again or reduce the route complexity.", ex);
            }
        }

        public static string ExtractResponseMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            int jsonStart = responseText.IndexOf('{');
            if (jsonStart > 0)
            {
                responseText = responseText.Substring(jsonStart);
            }

            try
            {
                CalculatePathResponseDto response = DeserializeResponse(responseText);
                return response == null || string.IsNullOrWhiteSpace(response.Message)
                    ? null
                    : response.Message;
            }
            catch
            {
                return null;
            }
        }

        public static bool DrawPathFromResponse(Document doc, UIDocument uiDoc, string responseText)
        {
            CalculatePathResponseDto response = DeserializeResponse(responseText);
            PathPolyline path = BuildPathPolyline(response);
            if (path == null || path.Points == null || path.Points.Count < 2)
            {
                DiagnosticRecorder.AppendDebug("[CalculatePathApi] No drawable path_points found in response.");
                return false;
            }

            View3D previewView = null;
            using (Transaction tx = new Transaction(doc, "Draw Calculate Path Result"))
            {
                tx.Start();
                previewView = PathPreviewViewService.GetOrCreate(doc);
                PathPreviewViewService.PrepareForSourceDocPreview(previewView);
                Path3DVisualizationService.Clear(doc);
                Path3DVisualizationService.Draw(doc, previewView, path, false);
                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug(
                "[CalculatePathApi] Draw path_points count=" +
                path.Points.Count.ToString(CultureInfo.InvariantCulture));

            if (previewView != null && uiDoc != null)
            {
                uiDoc.RequestViewChange(previewView);
            }

            return true;
        }

        public static CalculatePathExecutionResult DrawPathInActiveViewFromResponse(Document doc, UIDocument uiDoc, string responseText)
        {
            if (doc == null || uiDoc == null)
            {
                return CreateFailure("Failed to generate delivery route.", responseText, null);
            }

            CalculatePathResponseDto response = DeserializeResponse(responseText);
            if (response == null || !response.Success.HasValue)
            {
                return CreateFailure("Failed to generate delivery route.", responseText, null);
            }

            if (response.Success.Value != true)
            {
                return CreateFailure(
                    string.IsNullOrWhiteSpace(response.Message) ? "Path planning failed." : response.Message,
                    responseText,
                    response.PathLengthMeters);
            }

            View3D activeView = uiDoc.ActiveView as View3D;
            if (activeView == null)
            {
                return CreateFailure("Please open a 3D view before generating delivery route.", responseText, response.PathLengthMeters);
            }

            PathPolyline path = BuildPathPolyline(response);
            if (path == null || path.Points == null || path.Points.Count < 2)
            {
                return CreateFailure(
                    string.IsNullOrWhiteSpace(response.Message) ? "Path planning failed." : response.Message,
                    responseText,
                    response.PathLengthMeters);
            }

            using (Transaction tx = new Transaction(doc, "Draw Delivery Route Path"))
            {
                tx.Start();
                Path3DVisualizationService.Clear(doc);
                Path3DVisualizationService.Draw(doc, activeView, path, false);
                tx.Commit();
            }

            return new CalculatePathExecutionResult
            {
                Success = true,
                Drawn = true,
                Message = string.IsNullOrWhiteSpace(response.Message) ? "Delivery route generated." : response.Message,
                ResponseBody = responseText,
                PathLengthMeters = response.PathLengthMeters
            };
        }

        internal static PathPolyline BuildPathPolylineFromResponse(string responseText, string pathId)
        {
            CalculatePathResponseDto response = DeserializeResponse(responseText);
            if (response == null || response.Success != true)
            {
                return null;
            }

            return BuildPathPolyline(response, pathId);
        }


        public static CalculatePathExecutionResult DrawMultipleSavedPathsInActiveViewFromResponses(
            Document doc,
            UIDocument uiDoc,
            IList<string> responseTexts,
            IList<string> pathIds)
        {
            if (doc == null || uiDoc == null)
            {
                return CreateFailure("Failed to draw route comparison.", null, null);
            }

            View3D activeView = uiDoc.ActiveView as View3D;
            if (activeView == null)
            {
                return CreateFailure("Please open a 3D view before comparing delivery routes.", null, null);
            }

            List<PathPolyline> paths = new List<PathPolyline>();
            string firstResponseBody = null;
            double? firstPathLengthMeters = null;

            if (responseTexts != null)
            {
                for (int i = 0; i < responseTexts.Count; i++)
                {
                    string responseText = responseTexts[i];
                    if (string.IsNullOrWhiteSpace(responseText))
                    {
                        continue;
                    }

                    CalculatePathResponseDto response = DeserializeResponse(responseText);
                    if (response == null || response.Success != true)
                    {
                        continue;
                    }

                    string pathId = null;
                    if (pathIds != null && i < pathIds.Count)
                    {
                        pathId = pathIds[i];
                    }

                    PathPolyline path = BuildPathPolyline(response, string.IsNullOrWhiteSpace(pathId)
                        ? "LAYOUT_ROUTE_COMPARE_" + (i + 1).ToString(CultureInfo.InvariantCulture)
                        : pathId);
                    if (path == null || path.Points == null || path.Points.Count < 2)
                    {
                        continue;
                    }

                    paths.Add(path);
                    if (firstResponseBody == null)
                    {
                        firstResponseBody = responseText;
                        firstPathLengthMeters = response.PathLengthMeters;
                    }
                }
            }

            if (paths.Count == 0)
            {
                using (Transaction tx = new Transaction(doc, "Clear Delivery Route Comparison"))
                {
                    tx.Start();
                    Path3DVisualizationService.Clear(doc);
                    tx.Commit();
                }

                return CreateFailure("No saved delivery route data is available for the selected layout plans.", firstResponseBody, firstPathLengthMeters);
            }

            using (Transaction tx = new Transaction(doc, "Draw Delivery Route Comparison"))
            {
                tx.Start();
                Path3DVisualizationService.Clear(doc);
                Path3DVisualizationService.DrawMany(doc, activeView, paths, false);
                tx.Commit();
            }

            return new CalculatePathExecutionResult
            {
                Success = true,
                Drawn = true,
                Message = "Delivery route comparison drawn.",
                ResponseBody = firstResponseBody,
                PathLengthMeters = firstPathLengthMeters
            };
        }

        public static CalculatePathExecutionResult CalculateAndDraw(
            Document doc,
            UIDocument uiDoc,
            string sessionId,
            XYZ startPointFeet,
            XYZ goalPointFeet)
        {
            if (doc == null || uiDoc == null || string.IsNullOrWhiteSpace(sessionId) || startPointFeet == null || goalPointFeet == null)
            {
                return CreateFailure("Calculate path inputs are incomplete.", null, null);
            }

            string responseText = null;

            try
            {
                string requestJson = BuildRequestJson(
                    sessionId,
                    ToMillimeters(startPointFeet.X),
                    ToMillimeters(startPointFeet.Y),
                    ToMillimeters(goalPointFeet.X),
                    ToMillimeters(goalPointFeet.Y));
                DiagnosticRecorder.AppendDebug("[CalculatePathApi] requestJson=" + requestJson);

                responseText = PostCalculatePath(requestJson);
                CalculatePathResponseDto response = DeserializeResponse(responseText);
                if (response == null || !response.Success.HasValue)
                {
                    return CreateFailure("Failed to generate delivery route.", responseText, null);
                }

                if (response.Success.Value != true)
                {
                    return CreateFailure(
                        string.IsNullOrWhiteSpace(response.Message) ? "Path planning failed." : response.Message,
                        responseText,
                        response.PathLengthMeters);
                }

                bool drawn = DrawPathFromResponse(doc, uiDoc, responseText);
                if (!drawn)
                {
                    return CreateFailure(
                        string.IsNullOrWhiteSpace(response.Message) ? "Path planning failed." : response.Message,
                        responseText,
                        response.PathLengthMeters);
                }

                return new CalculatePathExecutionResult
                {
                    Success = true,
                    Drawn = true,
                    Message = string.IsNullOrWhiteSpace(response.Message) ? "Delivery route generated." : response.Message,
                    ResponseBody = responseText,
                    PathLengthMeters = response.PathLengthMeters
                };
            }
            catch (Exception ex)
            {
                return CreateFailure(
                    "Failed to generate delivery route." + Environment.NewLine + ex.Message,
                    responseText,
                    null);
            }
        }

        private static double ToMillimeters(double feetValue)
        {
            return feetValue * 304.8;
        }

        private static CalculatePathResponseDto DeserializeResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(CalculatePathResponseDto));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as CalculatePathResponseDto;
            }
        }

        private static PathPolyline BuildPathPolyline(CalculatePathResponseDto response)
        {
            return BuildPathPolyline(response, "CALCULATE_PATH_TEST");
        }

        private static PathPolyline BuildPathPolyline(CalculatePathResponseDto response, string pathId)
        {
            if (response == null || response.PathPoints == null || response.PathPoints.Count < 2)
            {
                return null;
            }

            int submoduleMaxIndex;
            string dimensionSource;
            LargestSegmentDto pathBoxSegment = ResolveLargestSegment(
                response,
                out submoduleMaxIndex,
                out dimensionSource);

            double boxLengthMm = ResolveSegmentDimensionMm(
                pathBoxSegment?.LengthMillimeters,
                pathBoxSegment?.LengthMeters,
                PathPreviewConstants.PathBoxLengthMm);
            double boxWidthMm = ResolveSegmentDimensionMm(
                pathBoxSegment?.WidthMillimeters,
                pathBoxSegment?.WidthMeters,
                PathPreviewConstants.PathBoxWidthMm);
            double boxHeightMm = ResolveSegmentDimensionMm(
                pathBoxSegment?.HeightMillimeters,
                pathBoxSegment?.HeightMeters,
                PathPreviewConstants.PathBoxHeightMm);

            PathPolyline path = new PathPolyline
            {
                PathId = string.IsNullOrWhiteSpace(pathId) ? "CALCULATE_PATH_TEST" : pathId,
                CoordinateBase = "MODEL_MM",
                Frame = "XY",
                Unit = "mm",
                BoxLengthMm = boxLengthMm,
                BoxWidthMm = boxWidthMm,
                BoxHeightMm = boxHeightMm
            };

            DiagnosticRecorder.AppendDebug(
                "[CutAndReplanApi] pathBoxDimensionSource=" + dimensionSource +
                (submoduleMaxIndex >= 0
                    ? "[" + submoduleMaxIndex.ToString(CultureInfo.InvariantCulture) + "]"
                    : string.Empty) +
                ", name=" +
                (pathBoxSegment == null || string.IsNullOrWhiteSpace(pathBoxSegment.Name)
                    ? "-"
                    : pathBoxSegment.Name) +
                ", dimensionsMm=[" +
                boxLengthMm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                boxWidthMm.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                boxHeightMm.ToString("0.###", CultureInfo.InvariantCulture) + "]");

            DiagnosticRecorder.AppendDebug(
                "[CutAndReplanApi] BuildPathPolyline points=" +
                response.PathPoints.Count.ToString(CultureInfo.InvariantCulture) +
                ", orientations=" +
                (response.OrientationPath == null ? "0" : response.OrientationPath.Count.ToString(CultureInfo.InvariantCulture)));
            if (response.OrientationPath != null && response.PathPoints.Count != response.OrientationPath.Count)
            {
                DiagnosticRecorder.AppendDebug(
                    "[CutAndReplanApi] path_points/orientation_path count mismatch. points=" +
                    response.PathPoints.Count.ToString(CultureInfo.InvariantCulture) +
                    ", orientations=" +
                    response.OrientationPath.Count.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < response.PathPoints.Count; i++)
            {
                List<double> point = response.PathPoints[i];
                if (point == null || point.Count < 2)
                {
                    continue;
                }

                double? orientationRadians = null;
                if (response.OrientationPath != null && i < response.OrientationPath.Count)
                {
                    double apiRadians = response.OrientationPath[i];
                    if (IsFinite(apiRadians))
                    {
                        orientationRadians = ConvertApiOrientationToRevitRadians(apiRadians);
                    }
                }

                path.Points.Add(new PathPoint3D(
                    point[0],
                    point[1],
                    point.Count >= 3 ? point[2] : 0.0,
                    orientationRadians));
            }

            return path.Points.Count >= 2 ? path : null;
        }

        private static LargestSegmentDto ResolveLargestSegment(
            CalculatePathResponseDto response,
            out int submoduleMaxIndex,
            out string source)
        {
            submoduleMaxIndex = -1;
            source = "fallback PathPreviewConstants";

            // /api/cut_and_replan now returns the route-box dimensions in
            // cut_options.submodule_results. Use the item at the maximum list
            // index (Count - 1), exactly matching the API contract.
            List<LargestSegmentDto> submoduleResults = response?.CutOptions?.SubmoduleResults;
            if (submoduleResults != null && submoduleResults.Count > 0)
            {
                submoduleMaxIndex = submoduleResults.Count - 1;
                LargestSegmentDto maxIndexResult = submoduleResults[submoduleMaxIndex];
                if (maxIndexResult != null)
                {
                    source = "cut_options.submodule_results";
                    return maxIndexResult;
                }
            }

            // Backward-compatible fallback for older API response payloads.
            LargestSegmentDto legacySegment =
                response?.CutOptions?.LargestSegment ?? response?.CutOptions?.SelectedSegment;
            if (legacySegment != null)
            {
                source = "legacy cut_options.largest_segment/selected_segment";
            }

            return legacySegment;
        }

        private static double ConvertApiOrientationToRevitRadians(double apiRadians)
        {
            return NormalizeRadians(-apiRadians);
        }

        private static double NormalizeRadians(double angle)
        {
            while (angle > Math.PI)
            {
                angle -= Math.PI * 2.0;
            }

            while (angle < -Math.PI)
            {
                angle += Math.PI * 2.0;
            }

            return angle;
        }

        private static double ResolveSegmentDimensionMm(double? millimeters, double? meters, double fallbackMm)
        {
            if (millimeters.HasValue && IsPositiveFinite(millimeters.Value))
            {
                return millimeters.Value;
            }

            if (meters.HasValue && IsPositiveFinite(meters.Value))
            {
                return meters.Value * 1000.0;
            }

            return fallbackMm;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 1.0e-6 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string BuildHeadersText(HttpResponseMessage response)
        {
            if (response == null)
            {
                return "(null)";
            }

            StringBuilder sb = new StringBuilder();
            AppendHeaders(sb, response.Headers, false);
            if (response.Content != null)
            {
                AppendHeaders(sb, response.Content.Headers, sb.Length > 0);
            }

            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        private static void AppendHeaders(
            StringBuilder sb,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
            bool prependSeparator)
        {
            if (sb == null || headers == null)
            {
                return;
            }

            bool hasAny = false;
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                if (prependSeparator || hasAny)
                {
                    sb.Append("; ");
                }

                sb.Append(header.Key);
                sb.Append("=");
                sb.Append(string.Join(",", header.Value ?? new string[0]));
                hasAny = true;
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static CalculatePathExecutionResult CreateFailure(string message, string responseBody, double? pathLengthMeters)
        {
            return new CalculatePathExecutionResult
            {
                Success = false,
                Drawn = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Failed to generate delivery route." : message,
                ResponseBody = responseBody,
                PathLengthMeters = pathLengthMeters
            };
        }

        [DataContract]
        private sealed class CalculatePathResponseDto
        {
            [DataMember(Name = "success")]
            public bool? Success { get; set; }

            [DataMember(Name = "path_points")]
            public List<List<double>> PathPoints { get; set; }

            [DataMember(Name = "orientation_path")]
            public List<double> OrientationPath { get; set; }

            [DataMember(Name = "path_length_meters")]
            public double? PathLengthMeters { get; set; }

            [DataMember(Name = "path_length_mm")]
            public double? PathLengthMillimeters { get; set; }

            [DataMember(Name = "message")]
            public string Message { get; set; }

            [DataMember(Name = "need_cut")]
            public bool? NeedCut { get; set; }

            [DataMember(Name = "cut_options")]
            public CutOptionsDto CutOptions { get; set; }
        }

        [DataContract]
        private sealed class CutOptionsDto
        {
            [DataMember(Name = "submodule_results")]
            public List<LargestSegmentDto> SubmoduleResults { get; set; }

            // Legacy response fields retained for backward compatibility.
            [DataMember(Name = "largest_segment")]
            public LargestSegmentDto LargestSegment { get; set; }

            [DataMember(Name = "selected_segment")]
            public LargestSegmentDto SelectedSegment { get; set; }
        }

        [DataContract]
        private sealed class LargestSegmentDto
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "length_m")]
            public double? LengthMeters { get; set; }

            [DataMember(Name = "width_m")]
            public double? WidthMeters { get; set; }

            [DataMember(Name = "height_m")]
            public double? HeightMeters { get; set; }

            [DataMember(Name = "length_mm")]
            public double? LengthMillimeters { get; set; }

            [DataMember(Name = "width_mm")]
            public double? WidthMillimeters { get; set; }

            [DataMember(Name = "height_mm")]
            public double? HeightMillimeters { get; set; }
        }
    }
}
