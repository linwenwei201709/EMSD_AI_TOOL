using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomCustomFamilyPlacementService
    {
        private const string MetadataPrefix = "ROOM_CUSTOM_FAMILY__";
        private const string ConvertedDoorOpeningComment = "RVT_DoorFamilyConvertedToOpening";
        private const double DoorTargetBoundaryToleranceMm = 1500.0;
        private const double DoorTargetExpandedBBoxMarginMm = 3000.0;

        internal sealed class PlacementResult
        {
            public bool Succeeded { get; set; }
            public string ErrorCode { get; set; }
            public string Message { get; set; }
            public string RoomKey { get; set; }
            public string FamilyKey { get; set; }
            public string FileName { get; set; }
            public XYZ PlacementPoint { get; set; }
            public string PlacementSource { get; set; }
            public int DeletedOldCount { get; set; }
            public ElementId CreatedElementId { get; set; } = ElementId.InvalidElementId;
            public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
            public string MaintenanceSpaceFitStatus { get; set; }
            public string MaintenanceSpaceFitWarningMessage { get; set; }
            public bool MaintenanceSpaceFitPassed { get; set; } = true;

            // Placement operation can succeed even when the configured room-fit
            // constraints are not feasible.  In that case the AHU is intentionally
            // kept in Revit as a review placement so the user can visually inspect
            // where the body / maintenance envelope exceeds the room.
            public bool PlacementFeasible { get; set; } = true;
            public bool RetainedForManualReview { get; set; }

            // When both Sub-Module and Maintenance configurations are empty, the
            // family is still intentionally placed at the resolved room center.
            // No room-fit / Restricted Area analysis is meaningful in this fallback
            // mode because the configured clearance envelope does not exist.
            public bool IsNotConfigured { get; set; }
        }

        internal static PlacementResult PlaceOrReplace(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option)
        {
            return PlaceOrReplace(doc, room, option, null, null);
        }

        internal static PlacementResult PlaceOrReplace(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            XYZ placementPointOverride)
        {
            return PlaceOrReplace(doc, room, option, placementPointOverride, null);
        }

        internal static PlacementResult PlaceOrReplace(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            XYZ placementPointOverride,
            double? orientationDegOverride)
        {
            PlacementResult result = new PlacementResult
            {
                RoomKey = room != null ? room.Key ?? string.Empty : string.Empty,
                FamilyKey = option != null ? option.Key ?? string.Empty : string.Empty,
                FileName = option != null ? option.FileName ?? string.Empty : string.Empty
            };

            if (doc == null)
            {
                return Fail(result, "DocumentMissing", "Document is null.");
            }

            if (room == null || string.IsNullOrWhiteSpace(room.Key))
            {
                return Fail(result, "RoomMissing", "Room is null or room key is empty.");
            }

            if (option == null)
            {
                return Fail(result, "FamilyOptionMissing", "Family option is null.");
            }

            if (string.IsNullOrWhiteSpace(option.FullPath) || !File.Exists(option.FullPath))
            {
                return Fail(result, "FamilyFileMissing", "Family file missing.");
            }

            List<TargetRoomSeed> seeds = TargetRoomSeedStorageService.LoadSeeds(doc);
            Room3DVisualizationService.RoomMarkerPlacementInfo placement = Room3DVisualizationService.ResolveMarkerPlacement(room, seeds);
            if (placement == null || placement.Position == null)
            {
                return Fail(result, "PlacementPointMissing", "Placement point is missing.");
            }

            result.PlacementPoint = ResolveFamilyPlacementPoint(room, placement);
            result.PlacementSource = placement.Source ?? string.Empty;

            // When the room-fit API was checked against an explicit placement_point,
            // use exactly that same XY point for the Revit family instance. Keep the
            // existing resolved Z/level so this change does not alter vertical placement.
            if (placementPointOverride != null && result.PlacementPoint != null)
            {
                result.PlacementPoint = new XYZ(
                    placementPointOverride.X,
                    placementPointOverride.Y,
                    result.PlacementPoint.Z);
                result.PlacementSource = "API placement_point";
            }

            result.LevelId = ResolveLevelId(doc, room, placement);

            using (Transaction tx = new Transaction(doc, "Set Room Custom Family"))
            {
                tx.Start();
                try
                {
                    List<ElementId> oldIds = FindManagedInstances(doc, room.Key).Select(x => x.Id).Distinct().ToList();
                    result.DeletedOldCount = oldIds.Count;
                    if (oldIds.Count > 0)
                    {
                        doc.Delete(oldIds);
                    }

                    FamilySymbol symbol = EnsureFamilySymbol(doc, option.FullPath, out string symbolError);
                    if (symbol == null)
                    {
                        throw new InvalidOperationException(symbolError ?? "Family symbol resolve failed.");
                    }

                    FamilyInstance instance = CreateInstance(doc, symbol, result.PlacementPoint, result.LevelId, out string createError);
                    if (instance == null)
                    {
                        throw new InvalidOperationException(createError ?? "Family instance create failed.");
                    }

                    ApplyMetadata(instance, room.Key, option.Key);
                    result.CreatedElementId = instance.Id;
                    result.Succeeded = true;
                    result.ErrorCode = string.Empty;
                    result.Message = "Success";
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    return Fail(result, "PlacementFailed", ex.Message);
                }
            }

            // Legacy special 180-degree rotation for AHU.
            // Temporarily disabled because AHU should be placed at room center and face the target room door.
            // string rotateError;
            // if (!TryRotatePlacedEquipment180Degrees(doc, result.CreatedElementId, result.PlacementPoint, out rotateError) &&
            //     !string.IsNullOrWhiteSpace(rotateError))
            // {
            //     DiagnosticRecorder.AppendDebug(
            //         "[RoomCustomFamily] 180-degree rotation skipped. RoomKey=" + room.Key +
            //         ", FamilyKey=" + option.Key +
            //         ", ElementId=" + (result.CreatedElementId != null ? result.CreatedElementId.IntegerValue.ToString() : string.Empty) +
            //         ", Error=" + rotateError);
            // }

            string orientError;
            MaintenanceSpaceFitResult maintenanceSpaceCheck = null;

            if (orientationDegOverride.HasValue)
            {
                // API orientation test path.
                //
                // IMPORTANT: do NOT run the old Service Side / RoomLong / RoomShort
                // candidate logic when orientation_deg is available. That legacy logic
                // was only a local approximation before /api/check_room_fit existed and
                // can overwrite the API angle, making calibration impossible.
                //
                // For the first calibration round the family offset is deliberately 0°.
                // We align the Revit family's detected LONG AXIS to orientation_deg and
                // inspect the result in Revit. If a fixed family requires 180° / ±90°,
                // that will be added later as an explicit per-family mapping instead of
                // reviving the dynamic Service Side guess.
                if (!TryOrientPlacedEquipmentToApiAngle(
                        doc,
                        room,
                        option,
                        result.CreatedElementId,
                        result.PlacementPoint,
                        orientationDegOverride.Value,
                        out orientError) &&
                    !string.IsNullOrWhiteSpace(orientError))
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuApiOrientation] API orientation failed; legacy orientation intentionally NOT executed. RoomKey=" + room.Key +
                        ", FamilyKey=" + option.Key +
                        ", ElementId=" + (result.CreatedElementId != null ? result.CreatedElementId.IntegerValue.ToString() : string.Empty) +
                        ", ApiOrientationDeg=" + orientationDegOverride.Value.ToString("F3", CultureInfo.InvariantCulture) +
                        ", Error=" + orientError);
                }
            }
            else
            {
                // Explicit fallback for an AHU family that has not been configured in
                // Family Library yet.  If BOTH Sub-Modules and Maintenance are empty,
                // keep the freshly-created family exactly at the resolved room center
                // and preserve the RFA's native/default orientation.  Do not require
                // Door Side / Wall Side / Maintenance checks in this mode.
                IReadOnlyList<RoomCustomFamilySubModuleDto> configuredSubModules =
                    RoomCustomFamilyCatalogService.GetSubModules(option.Key);
                IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> configuredMaintenance =
                    RoomCustomFamilyCatalogService.GetMaintenanceSpaces(option.Key);

                bool hasSubModules =
                    configuredSubModules != null && configuredSubModules.Count > 0;
                bool hasMaintenance =
                    configuredMaintenance != null && configuredMaintenance.Count > 0;

                if (!hasSubModules && !hasMaintenance)
                {
                    // Best-effort correction: CreateInstance() receives the room-center
                    // point, but some AHU RFAs have an insertion origin that is offset
                    // from the actual physical body center.  Center the detected body
                    // without rotating it.  A geometry-read failure is not fatal in
                    // this fallback mode; the instance remains at its original
                    // room-center insertion point and is still considered placed.
                    string centerMode;
                    string centerWarning;
                    TryCenterUnconfiguredAhuAtRoomCenter(
                        doc,
                        room,
                        result.CreatedElementId,
                        result.PlacementPoint,
                        out centerMode,
                        out centerWarning);

                    result.IsNotConfigured = true;
                    result.PlacementFeasible = true;
                    result.RetainedForManualReview = false;
                    result.MaintenanceSpaceFitStatus = "NotConfigured";
                    result.MaintenanceSpaceFitWarningMessage = string.Empty;
                    result.MaintenanceSpaceFitPassed = true;
                    result.ErrorCode = string.Empty;
                    result.Message =
                        "Placed at room center. Sub-Module and Maintenance are not configured.";

                    DiagnosticRecorder.AppendDebug(
                        "[AhuLocalPlacement] PlacementMode=CenterNotConfigured, RoomKey=" +
                        (room != null ? room.Key ?? string.Empty : string.Empty) +
                        ", FamilyKey=" + (option != null ? option.Key ?? string.Empty : string.Empty) +
                        ", ElementId=" + FormatElementId(result.CreatedElementId) +
                        ", PlacementPoint=" + FormatPoint(result.PlacementPoint) +
                        ", CenterMode=" + (centerMode ?? string.Empty) +
                        ", CenterWarning=" + (centerWarning ?? string.Empty) +
                        ", SubModules=0, MaintenanceSpaces=0");

                    return result;
                }

                // Current AHU placement path is solved locally in Revit.
                //
                // The configured Maintenance side is authoritative:
                //   - IsDoorSide decides which AHU-local side must face the room door.
                //   - IsWallSide + DimensionMm decides the exact AHU-body-to-wall gap.
                //   - The real transparent/pink Maintenance Space solids in the family are
                //     used as the final room-boundary clearance envelope whenever available.
                //
                // No /api/check_room_fit orientation or XY result is required for this path.
                bool retainedForManualReview;
                if (!TryPlaceAhuByConfiguredRoomRules(
                        doc,
                        room,
                        option,
                        result.CreatedElementId,
                        result.PlacementPoint,
                        result.LevelId,
                        out orientError,
                        out maintenanceSpaceCheck,
                        out retainedForManualReview))
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuLocalPlacement] Local placement failed. RoomKey=" + room.Key +
                        ", FamilyKey=" + option.Key +
                        ", ElementId=" + (result.CreatedElementId != null ? result.CreatedElementId.IntegerValue.ToString() : string.Empty) +
                        ", RetainedForManualReview=" + retainedForManualReview +
                        ", Error=" + (orientError ?? string.Empty));

                    // A geometric no-fit is not an insertion failure. Keep the best
                    // wall-aligned / door-facing placement in Revit so the user can
                    // inspect exactly which body or maintenance region exceeds the room.
                    // Technical/configuration failures still remove the temporary AHU.
                    if (retainedForManualReview &&
                        result.CreatedElementId != null &&
                        result.CreatedElementId != ElementId.InvalidElementId &&
                        doc.GetElement(result.CreatedElementId) != null)
                    {
                        result.Succeeded = true;
                        result.PlacementFeasible = false;
                        result.RetainedForManualReview = true;
                        result.ErrorCode = "LocalPlacementExceeded";
                        result.Message = string.IsNullOrWhiteSpace(orientError)
                            ? "No feasible AHU placement was found; the AHU was retained for manual review."
                            : orientError;
                        ApplyMaintenanceSpaceFitResult(result, room, option, maintenanceSpaceCheck);
                        return result;
                    }

                    TryDeleteFailedPlacementInstance(doc, result.CreatedElementId);
                    result.CreatedElementId = ElementId.InvalidElementId;
                    result.Succeeded = false;
                    result.PlacementFeasible = false;
                    result.RetainedForManualReview = false;
                    result.ErrorCode = "LocalPlacementFailed";
                    result.Message = string.IsNullOrWhiteSpace(orientError)
                        ? "No feasible AHU placement was found in the selected room."
                        : orientError;
                    ApplyMaintenanceSpaceFitResult(result, room, option, maintenanceSpaceCheck);
                    return result;
                }
            }

            ApplyMaintenanceSpaceFitResult(result, room, option, maintenanceSpaceCheck);

            return result;
        }


        private static XYZ ResolveFamilyPlacementPoint(
            RoomSemanticRecord room,
            Room3DVisualizationService.RoomMarkerPlacementInfo placement)
        {
            XYZ fallback = placement != null ? placement.Position : null;
            double z = fallback != null
                ? fallback.Z
                : room != null && room.BBox != null && room.BBox.Min != null
                    ? room.BBox.Min.Z + Room3DVisualizationConstants.MarkerOffsetMm * Room3DVisualizationConstants.MmToFeet
                    : 0.0;

            XYZ center;
            if (TryResolveRoomCenterXY(room, out center))
            {
                return new XYZ(center.X, center.Y, z);
            }

            return fallback;
        }

        internal static bool TryResolveValidationPlacementPoint(
            Document doc,
            RoomSemanticRecord room,
            out XYZ placementPoint,
            out string placementSource)
        {
            placementPoint = null;
            placementSource = string.Empty;

            if (doc == null || room == null || string.IsNullOrWhiteSpace(room.Key))
            {
                return false;
            }

            List<TargetRoomSeed> seeds = TargetRoomSeedStorageService.LoadSeeds(doc);
            Room3DVisualizationService.RoomMarkerPlacementInfo placement =
                Room3DVisualizationService.ResolveMarkerPlacement(room, seeds);
            if (placement == null || placement.Position == null)
            {
                return false;
            }

            placementPoint = ResolveFamilyPlacementPoint(room, placement);
            placementSource = placement.Source ?? string.Empty;
            return placementPoint != null;
        }

        private static bool TryResolveRoomCenterXY(RoomSemanticRecord room, out XYZ center)
        {
            center = null;
            if (room == null)
            {
                return false;
            }

            if (TryComputePolygonCentroid(room.LoopPoints, out center))
            {
                return true;
            }

            if (room.Centroid != null)
            {
                center = room.Centroid;
                return true;
            }

            if (room.BBox != null && room.BBox.Min != null && room.BBox.Max != null)
            {
                center = new XYZ(
                    (room.BBox.Min.X + room.BBox.Max.X) * 0.5,
                    (room.BBox.Min.Y + room.BBox.Max.Y) * 0.5,
                    (room.BBox.Min.Z + room.BBox.Max.Z) * 0.5);
                return true;
            }

            return false;
        }

        private static bool TryComputePolygonCentroid(IList<XYZ> points, out XYZ centroid)
        {
            centroid = null;
            if (points == null || points.Count < 3)
            {
                return false;
            }

            List<XYZ> valid = points.Where(x => x != null).ToList();
            if (valid.Count < 3)
            {
                return false;
            }

            double signedArea2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;
            for (int i = 0; i < valid.Count; i++)
            {
                XYZ p0 = valid[i];
                XYZ p1 = valid[(i + 1) % valid.Count];
                double cross = p0.X * p1.Y - p1.X * p0.Y;
                signedArea2 += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }

            if (Math.Abs(signedArea2) > 1e-9)
            {
                centroid = new XYZ(cx / (3.0 * signedArea2), cy / (3.0 * signedArea2), valid.Average(x => x.Z));
                return true;
            }

            centroid = new XYZ(valid.Average(x => x.X), valid.Average(x => x.Y), valid.Average(x => x.Z));
            return true;
        }

        internal static string BuildMetadataValue(string roomKey, string familyKey)
        {
            return MetadataPrefix + (roomKey ?? string.Empty) + "__" + (familyKey ?? string.Empty);
        }

        internal static IEnumerable<FamilyInstance> FindManagedInstances(Document doc, string roomKey)
        {
            if (doc == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return Enumerable.Empty<FamilyInstance>();
            }

            string prefix = MetadataPrefix + roomKey + "__";
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(x => HasManagedMetadata(x, prefix))
                .ToList();
        }

        internal static bool TryGetPlacedFamilyKey(Document doc, string roomKey, out string familyKey)
        {
            familyKey = string.Empty;
            if (doc == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            FamilyInstance instance = FindManagedInstances(doc, roomKey).FirstOrDefault();
            if (instance == null)
            {
                return false;
            }

            return TryExtractFamilyKey(instance, roomKey, out familyKey);
        }

        internal static bool TryGetPlacedFamilyInstanceId(Document doc, string roomKey, out ElementId instanceId)
        {
            instanceId = ElementId.InvalidElementId;
            if (doc == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            FamilyInstance instance = FindManagedInstances(doc, roomKey).FirstOrDefault();
            if (instance == null)
            {
                return false;
            }

            instanceId = instance.Id;
            return instanceId != null && instanceId != ElementId.InvalidElementId;
        }

        private static PlacementResult Fail(PlacementResult result, string code, string message)
        {
            result.Succeeded = false;
            result.ErrorCode = code;
            result.Message = message ?? string.Empty;
            return result;
        }

        private static ElementId ResolveLevelId(
            Document doc,
            RoomSemanticRecord room,
            Room3DVisualizationService.RoomMarkerPlacementInfo placement)
        {
            if (placement != null && placement.LevelId != null && placement.LevelId != ElementId.InvalidElementId)
            {
                LogResolvedLevel(doc, room, placement.LevelId, "PlacementLevelId");
                return placement.LevelId;
            }

            ElementId boundaryWallLevelId = ResolveDominantBoundaryWallLevelId(doc, room);
            if (boundaryWallLevelId != ElementId.InvalidElementId)
            {
                LogResolvedLevel(doc, room, boundaryWallLevelId, "BoundaryWallsDominantLevel");
                return boundaryWallLevelId;
            }

            ElementId nearestLevelId = ResolveNearestLevelByRoomZ(doc, room, placement);
            if (nearestLevelId != ElementId.InvalidElementId)
            {
                LogResolvedLevel(doc, room, nearestLevelId, "NearestRoomZLevel");
                return nearestLevelId;
            }

            Level fallbackLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            ElementId fallbackLevelId = fallbackLevel != null ? fallbackLevel.Id : ElementId.InvalidElementId;
            LogResolvedLevel(doc, room, fallbackLevelId, "FallbackLowestLevel");
            return fallbackLevelId;
        }

        private static ElementId ResolveDominantBoundaryWallLevelId(Document doc, RoomSemanticRecord room)
        {
            if (doc == null || room == null || room.BoundaryWalls == null || room.BoundaryWalls.Count == 0)
            {
                return ElementId.InvalidElementId;
            }

            Dictionary<int, LevelVote> votes = new Dictionary<int, LevelVote>();
            foreach (RoomBoundaryWallReference wallRef in room.BoundaryWalls)
            {
                if (wallRef == null || wallRef.ElementId <= 0)
                {
                    continue;
                }

                Wall wall = doc.GetElement(new ElementId(wallRef.ElementId)) as Wall;
                if (wall == null)
                {
                    continue;
                }

                ElementId levelId = ResolveWallBaseLevelId(doc, wall);
                if (levelId == ElementId.InvalidElementId)
                {
                    continue;
                }

                int key = levelId.IntegerValue;
                LevelVote vote;
                if (!votes.TryGetValue(key, out vote))
                {
                    vote = new LevelVote { LevelId = levelId, Count = 0, TotalLengthMm = 0.0 };
                    votes[key] = vote;
                }

                vote.Count++;
                vote.TotalLengthMm += Math.Max(0.0, wallRef.LengthMm);
            }

            LevelVote best = votes.Values
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.TotalLengthMm)
                .FirstOrDefault();

            return best != null ? best.LevelId : ElementId.InvalidElementId;
        }

        private static ElementId ResolveWallBaseLevelId(Document doc, Wall wall)
        {
            if (doc == null || wall == null)
            {
                return ElementId.InvalidElementId;
            }

            Parameter baseConstraint = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId levelId = baseConstraint != null ? baseConstraint.AsElementId() : ElementId.InvalidElementId;
            if (IsValidLevelId(doc, levelId))
            {
                return levelId;
            }

            levelId = wall.LevelId;
            return IsValidLevelId(doc, levelId) ? levelId : ElementId.InvalidElementId;
        }

        private static ElementId ResolveNearestLevelByRoomZ(
            Document doc,
            RoomSemanticRecord room,
            Room3DVisualizationService.RoomMarkerPlacementInfo placement)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            double roomZ;
            if (!TryResolveRoomReferenceZ(room, placement, out roomZ))
            {
                return ElementId.InvalidElementId;
            }

            Level nearestLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => Math.Abs(x.Elevation - roomZ))
                .FirstOrDefault();

            return nearestLevel != null ? nearestLevel.Id : ElementId.InvalidElementId;
        }

        private static bool TryResolveRoomReferenceZ(
            RoomSemanticRecord room,
            Room3DVisualizationService.RoomMarkerPlacementInfo placement,
            out double z)
        {
            z = 0.0;

            if (room != null && room.BBox != null && room.BBox.Min != null)
            {
                z = room.BBox.Min.Z;
                return true;
            }

            if (room != null && room.Centroid != null)
            {
                z = room.Centroid.Z;
                return true;
            }

            if (room != null && room.LoopPoints != null && room.LoopPoints.Count > 0)
            {
                z = room.LoopPoints.Average(x => x != null ? x.Z : 0.0);
                return true;
            }

            if (placement != null && placement.Position != null)
            {
                z = placement.Position.Z;
                return true;
            }

            return false;
        }

        private static bool IsValidLevelId(Document doc, ElementId levelId)
        {
            return doc != null &&
                   levelId != null &&
                   levelId != ElementId.InvalidElementId &&
                   doc.GetElement(levelId) is Level;
        }

        private static void LogResolvedLevel(Document doc, RoomSemanticRecord room, ElementId levelId, string source)
        {
            try
            {
                Level level = IsValidLevelId(doc, levelId) ? doc.GetElement(levelId) as Level : null;
                DiagnosticRecorder.AppendDebug(
                    "[RoomCustomFamily] ResolveLevel. RoomKey=" + (room != null ? room.Key ?? string.Empty : string.Empty) +
                    ", Source=" + (source ?? string.Empty) +
                    ", Level=" + (level != null ? level.Name ?? string.Empty : string.Empty) +
                    ", LevelId=" + (levelId != null && levelId != ElementId.InvalidElementId ? levelId.IntegerValue.ToString() : string.Empty));
            }
            catch
            {
                // Level logging must never block family placement.
            }
        }

        private sealed class LevelVote
        {
            public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
            public int Count { get; set; }
            public double TotalLengthMm { get; set; }
        }

        private static FamilySymbol EnsureFamilySymbol(Document doc, string familyPath, out string error)
        {
            error = string.Empty;
            string familyName = Path.GetFileNameWithoutExtension(familyPath) ?? string.Empty;
            FamilySymbol symbol = FindLoadedSymbol(doc, familyName);
            if (symbol == null)
            {
                if (!doc.LoadFamily(familyPath, out Family loadedFamily) || loadedFamily == null)
                {
                    error = "LoadFamily failed.";
                    return null;
                }

                symbol = GetDefaultSymbol(loadedFamily, doc);
            }

            if (symbol == null)
            {
                error = "No family symbol found.";
                return null;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            return symbol;
        }

        private static FamilySymbol FindLoadedSymbol(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x =>
                    x != null &&
                    (string.Equals(x.FamilyName ?? string.Empty, familyName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(x.Family != null ? x.Family.Name ?? string.Empty : string.Empty, familyName, StringComparison.OrdinalIgnoreCase)));
        }

        private static FamilySymbol GetDefaultSymbol(Family family, Document doc)
        {
            if (family == null)
            {
                return null;
            }

            foreach (ElementId symbolId in family.GetFamilySymbolIds())
            {
                FamilySymbol symbol = doc.GetElement(symbolId) as FamilySymbol;
                if (symbol != null)
                {
                    return symbol;
                }
            }

            return null;
        }

        private static FamilyInstance CreateInstance(
            Document doc,
            FamilySymbol symbol,
            XYZ point,
            ElementId levelId,
            out string error)
        {
            error = string.Empty;
            FamilyPlacementType placementType = symbol.Family != null ? symbol.Family.FamilyPlacementType : FamilyPlacementType.Invalid;

            if (placementType == FamilyPlacementType.ViewBased ||
                placementType == FamilyPlacementType.CurveBased ||
                placementType == FamilyPlacementType.CurveBasedDetail ||
                placementType == FamilyPlacementType.CurveDrivenStructural ||
                placementType == FamilyPlacementType.Adaptive ||
                placementType == FamilyPlacementType.OneLevelBasedHosted)
            {
                error = "Unsupported hosted or curve-based family placement type: " + placementType;
                return null;
            }

            Level level = levelId != ElementId.InvalidElementId ? doc.GetElement(levelId) as Level : null;

            try
            {
                if (placementType == FamilyPlacementType.OneLevelBased ||
                    placementType == FamilyPlacementType.TwoLevelsBased)
                {
                    if (level == null)
                    {
                        error = "Level is required for level-based family.";
                        return null;
                    }

                    return doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
                }

                return doc.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static bool TryRotatePlacedEquipment180Degrees(Document doc, ElementId instanceId, XYZ fallbackOrigin, out string error)
        {
            error = string.Empty;
            if (doc == null || instanceId == null || instanceId == ElementId.InvalidElementId)
            {
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                error = "Placed family instance not found.";
                return false;
            }

            Transaction rotateTx = null;
            try
            {
                rotateTx = new Transaction(doc, "Rotate Room Custom Family 180 Degrees");
                rotateTx.Start();

                XYZ origin = ResolveRotationOrigin(instance, fallbackOrigin);
                if (origin == null)
                {
                    rotateTx.RollBack();
                    error = "Rotation origin missing.";
                    return false;
                }

                Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                LocationPoint locationPoint = instance.Location as LocationPoint;
                bool rotated = false;
                if (locationPoint != null)
                {
                    rotated = locationPoint.Rotate(axis, Math.PI);
                }

                if (!rotated)
                {
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, Math.PI);
                    rotated = true;
                }

                doc.Regenerate();
                rotateTx.Commit();
                return rotated;
            }
            catch (Exception ex)
            {
                if (rotateTx != null && rotateTx.HasStarted())
                {
                    rotateTx.RollBack();
                }

                error = ex.Message;
                return false;
            }
        }

        private static XYZ ResolveRotationOrigin(FamilyInstance instance, XYZ fallbackOrigin)
        {
            LocationPoint locationPoint = instance != null ? instance.Location as LocationPoint : null;
            if (locationPoint != null && locationPoint.Point != null)
            {
                return locationPoint.Point;
            }

            return fallbackOrigin;
        }


        private static bool TryOrientPlacedEquipmentToApiAngle(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            ElementId instanceId,
            XYZ placementPoint,
            double apiOrientationDeg,
            out string error)
        {
            error = string.Empty;
            if (doc == null || room == null || instanceId == null ||
                instanceId == ElementId.InvalidElementId || placementPoint == null)
            {
                error = "Invalid API orientation context.";
                return false;
            }

            if (double.IsNaN(apiOrientationDeg) || double.IsInfinity(apiOrientationDeg))
            {
                error = "API orientation is not a finite number.";
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                error = "Placed family instance not found.";
                return false;
            }

            XYZ targetCenter = placementPoint;
            string initialCenterMode;
            XYZ initialEquipmentCenter =
                ResolveEquipmentCoreCenterForFinalPlacement(doc, instance, targetCenter, out initialCenterMode)
                ?? GetElementBoundingBoxCenter(instance)
                ?? ResolveRotationOrigin(instance, targetCenter)
                ?? targetCenter;

            XYZ equipmentLongAxis;
            XYZ equipmentShortAxis;
            string equipmentAxisMode;
            if (!TryResolveEquipmentLongAxis(
                    instance,
                    out equipmentLongAxis,
                    out equipmentShortAxis,
                    out equipmentAxisMode) ||
                !IsUsableDirection(equipmentLongAxis))
            {
                error = "Equipment long axis resolve failed.";
                return false;
            }

            equipmentLongAxis = equipmentLongAxis.Normalize();

            // Python's door-based orientation_deg is treated as an absolute IFC/Revit
            // XY world angle: 0° = +X, +90° = +Y, counter-clockwise positive.
            // Visual calibration confirmed one shared +180° family offset for all
            // fixed AHU model_id 1..10 so the PIP/Valve connection side faces the door.
            double familyOffsetDeg = ResolveApiOrientationOffsetDeg(option);
            double targetAngleDeg = apiOrientationDeg + familyOffsetDeg;
            double targetAngleRad = targetAngleDeg * Math.PI / 180.0;
            XYZ targetLongAxis = new XYZ(
                Math.Cos(targetAngleRad),
                Math.Sin(targetAngleRad),
                0.0);

            if (!IsUsableDirection(targetLongAxis))
            {
                error = "API target direction is invalid.";
                return false;
            }
            targetLongAxis = targetLongAxis.Normalize();

            // RotateElement expects a DELTA angle, while orientation_deg is an
            // ABSOLUTE world direction. SignedAngleOnXY converts current long-axis
            // direction -> API target direction into the required delta.
            double deltaAngle = SignedAngleOnXY(equipmentLongAxis, targetLongAxis);

            Transaction orientTx = null;
            try
            {
                orientTx = new Transaction(doc, "Orient Room Custom Family By API");
                orientTx.Start();

                bool rotated = false;
                bool moved = false;
                const double rotationTolerance = 1e-6;
                double moveTolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

                if (Math.Abs(deltaAngle) >= rotationTolerance)
                {
                    Line axis = Line.CreateBound(
                        initialEquipmentCenter,
                        initialEquipmentCenter + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, deltaAngle);
                    rotated = true;
                    doc.Regenerate();
                }

                // The AHU family insertion origin is not necessarily its visible/core
                // center. Re-center after rotation so the real model body remains on the
                // exact placement_point that was sent to /api/check_room_fit.
                string centerBeforeFinalMoveMode;
                XYZ centerBeforeFinalMove =
                    ResolveEquipmentCoreCenterForFinalPlacement(
                        doc,
                        instance,
                        initialEquipmentCenter,
                        out centerBeforeFinalMoveMode)
                    ?? GetElementBoundingBoxCenter(instance)
                    ?? initialEquipmentCenter;

                XYZ moveDelta = ResolveHorizontalCenteringDelta(centerBeforeFinalMove, targetCenter);
                double moveDistance = moveDelta != null ? moveDelta.GetLength() : 0.0;
                if (moveDelta != null && moveDistance > moveTolerance)
                {
                    ElementTransformUtils.MoveElement(doc, instance.Id, moveDelta);
                    moved = true;
                    doc.Regenerate();
                }

                string finalCenterMode;
                XYZ finalEquipmentCenter =
                    ResolveEquipmentCoreCenterForFinalPlacement(
                        doc,
                        instance,
                        centerBeforeFinalMove,
                        out finalCenterMode)
                    ?? GetElementBoundingBoxCenter(instance)
                    ?? centerBeforeFinalMove;

                XYZ finalLongAxis;
                XYZ finalShortAxis;
                string finalAxisMode;
                bool finalAxisResolved = TryResolveEquipmentLongAxis(
                    instance,
                    out finalLongAxis,
                    out finalShortAxis,
                    out finalAxisMode);

                orientTx.Commit();

                DiagnosticRecorder.AppendDebug(
                    "[AhuApiOrientation] Applied. RoomKey=" + (room.Key ?? string.Empty) +
                    ", FamilyKey=" + (option != null ? option.Key ?? string.Empty : string.Empty) +
                    ", ElementId=" + FormatElementId(instanceId) +
                    ", ApiOrientationDeg=" + apiOrientationDeg.ToString("F3", CultureInfo.InvariantCulture) +
                    ", FamilyOffsetDeg=" + familyOffsetDeg.ToString("F3", CultureInfo.InvariantCulture) +
                    ", TargetAngleDeg=" + targetAngleDeg.ToString("F3", CultureInfo.InvariantCulture) +
                    ", CurrentLongAxis=(" + FormatVector(equipmentLongAxis) + ")" +
                    ", TargetLongAxis=(" + FormatVector(targetLongAxis) + ")" +
                    ", DeltaRotationDeg=" + (deltaAngle * 180.0 / Math.PI).ToString("F3", CultureInfo.InvariantCulture) +
                    ", EquipmentAxisMode=" + (equipmentAxisMode ?? string.Empty) +
                    ", InitialEquipmentCenter=(" + FormatPoint(initialEquipmentCenter) + ")" +
                    ", InitialCenterMode=" + (initialCenterMode ?? string.Empty) +
                    ", CenterBeforeFinalMove=(" + FormatPoint(centerBeforeFinalMove) + ")" +
                    ", CenterBeforeFinalMoveMode=" + (centerBeforeFinalMoveMode ?? string.Empty) +
                    ", MoveDelta=(" + FormatPoint(moveDelta) + ")" +
                    ", MoveDistanceMm=" + FormatMm(moveDistance) +
                    ", Rotated=" + (rotated ? "True" : "False") +
                    ", Moved=" + (moved ? "True" : "False") +
                    ", FinalEquipmentCenter=(" + FormatPoint(finalEquipmentCenter) + ")" +
                    ", FinalCenterMode=" + (finalCenterMode ?? string.Empty) +
                    ", FinalLongAxis=(" + (finalAxisResolved ? FormatVector(finalLongAxis) : string.Empty) + ")" +
                    ", FinalAxisMode=" + (finalAxisResolved ? finalAxisMode ?? string.Empty : "Unresolved") +
                    ", LegacyServiceSideUsed=False");

                return true;
            }
            catch (Exception ex)
            {
                if (orientTx != null && orientTx.HasStarted())
                {
                    orientTx.RollBack();
                }

                error = ex.Message;
                return false;
            }
        }

        private static double ResolveApiOrientationOffsetDeg(RoomCustomFamilyOption option)
        {
            // Visual calibration confirmed that all fixed AHU model_id 1..10 share
            // the same Revit family direction: Python's positive-length / Door Side
            // must be reversed by 180 degrees so the required PIP/Valve connection
            // side faces the door.  Keep this deterministic; do NOT restore the old
            // dynamic Service Side / connector inference for the API-driven path.
            return 180.0;
        }


        private static bool TryCenterUnconfiguredAhuAtRoomCenter(
            Document doc,
            RoomSemanticRecord room,
            ElementId instanceId,
            XYZ fallbackRoomCenter,
            out string mode,
            out string warning)
        {
            mode = "InsertionPoint";
            warning = string.Empty;

            if (doc == null ||
                room == null ||
                instanceId == null ||
                instanceId == ElementId.InvalidElementId)
            {
                warning = "Invalid center-placement context.";
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                warning = "Placed AHU family instance could not be resolved.";
                return false;
            }

            XYZ roomCenter;
            if (!TryResolveRoomCenterXY(room, out roomCenter) || roomCenter == null)
            {
                roomCenter = fallbackRoomCenter;
            }

            if (roomCenter == null)
            {
                warning = "Room center could not be resolved.";
                return false;
            }

            string coreMode;
            XYZ coreCenter = ResolveEquipmentCoreCenterForFinalPlacement(
                                 doc,
                                 instance,
                                 roomCenter,
                                 out coreMode)
                             ?? GetElementBoundingBoxCenter(instance);

            if (coreCenter == null)
            {
                warning = "AHU physical body center could not be resolved.";
                return false;
            }

            XYZ delta = ResolveHorizontalCenteringDelta(coreCenter, roomCenter);
            if (delta == null ||
                delta.GetLength() <=
                UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters))
            {
                mode = "BodyCenterAlreadyAtRoomCenter/" + (coreMode ?? string.Empty);
                return true;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Center Unconfigured AHU"))
                {
                    tx.Start();
                    ElementTransformUtils.MoveElement(doc, instance.Id, delta);
                    doc.Regenerate();
                    tx.Commit();
                }

                mode = "BodyCenterToRoomCenter/" + (coreMode ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Solves the normal AHU room placement entirely in Revit/C#.
        ///
        /// The Family Library configuration is the source of truth:
        /// 1) SubModules (grid row/column + Name) are used to recover the AHU-local
        ///    Right/Bottom axes from the actual nested family geometry.
        /// 2) The Maintenance row marked IsDoorSide decides which local side faces
        ///    the selected room door.
        /// 3) Every IsWallSide row uses DimensionMm as the exact AHU BODY-to-wall gap.
        /// 4) The real transparent "Maintenance Space" solids in the loaded RFA are
        ///    used as the final clearance envelope.  When those solids cannot be read,
        ///    equivalent side strips are synthesized from the catalog dimensions.
        ///
        /// This path deliberately does not call /api/check_room_fit.
        /// </summary>
        private static bool TryPlaceAhuByConfiguredRoomRules(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            ElementId instanceId,
            XYZ initialTargetCenter,
            ElementId levelId,
            out string error,
            out MaintenanceSpaceFitResult maintenanceSpaceCheck,
            out bool retainedForManualReview)
        {
            error = string.Empty;
            retainedForManualReview = false;
            maintenanceSpaceCheck = new MaintenanceSpaceFitResult
            {
                Status = "NotChecked",
                Mode = "RevitLocal",
                OutsideToleranceMm = 20.0,
                TouchToleranceMm = 25.0
            };

            if (doc == null || room == null || option == null ||
                instanceId == null || instanceId == ElementId.InvalidElementId ||
                initialTargetCenter == null)
            {
                error = "Invalid local AHU placement context.";
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                error = "Placed AHU family instance could not be resolved.";
                return false;
            }

            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows =
                RoomCustomFamilyCatalogService.GetMaintenanceSpaces(option.Key);
            RoomCustomFamilyMaintenanceSpaceDto doorRule = maintenanceRows != null
                ? maintenanceRows
                    .Where(x => x != null && x.IsDoorSide)
                    .OrderBy(x => x.Sequence)
                    .FirstOrDefault()
                : null;

            if (doorRule == null || string.IsNullOrWhiteSpace(doorRule.Side))
            {
                error = "No Maintenance side is configured as Door Side for this AHU.";
                maintenanceSpaceCheck.Status = "Skipped";
                maintenanceSpaceCheck.Mode = "DoorSideMissing";
                return false;
            }

            XYZ doorCenter;
            string doorSource;
            ElementId doorElementId;
            if (!TryResolveRoomDoorCenter(
                    doc,
                    room,
                    initialTargetCenter,
                    levelId,
                    out doorCenter,
                    out doorSource,
                    out doorElementId))
            {
                error = "The room door could not be resolved for AHU placement.";
                maintenanceSpaceCheck.Status = "Skipped";
                maintenanceSpaceCheck.Mode = "DoorNotFound";
                return false;
            }

            XYZ roomCenter;
            if (!TryResolveRoomCenterXY(room, out roomCenter) || roomCenter == null)
            {
                roomCenter = initialTargetCenter;
            }

            XYZ doorBoundaryPoint;
            XYZ doorOutwardNormal;
            XYZ doorWallTangent;
            if (!TryResolveDoorBoundaryFrame(
                    room,
                    roomCenter,
                    doorCenter,
                    out doorBoundaryPoint,
                    out doorOutwardNormal,
                    out doorWallTangent))
            {
                error = "The room door wall direction could not be resolved.";
                maintenanceSpaceCheck.Status = "Skipped";
                maintenanceSpaceCheck.Mode = "DoorWallDirectionMissing";
                return false;
            }

            XYZ localRight;
            XYZ localBottom;
            string localAxisMode;
            if (!TryResolveConfiguredAhuLocalAxes(
                    doc,
                    instance,
                    option.Key,
                    out localRight,
                    out localBottom,
                    out localAxisMode))
            {
                error = "AHU local axes could not be resolved from the configured Sub-Modules.";
                maintenanceSpaceCheck.Status = "Skipped";
                maintenanceSpaceCheck.Mode = "SubModuleAxisMissing";
                return false;
            }

            XYZ configuredDoorDirection = ResolveConfiguredAhuSideDirection(
                doorRule.Side,
                localRight,
                localBottom);
            if (!IsUsableDirection(configuredDoorDirection))
            {
                error = "The configured AHU Door Side is invalid: " + (doorRule.Side ?? string.Empty) + ".";
                maintenanceSpaceCheck.Status = "Skipped";
                maintenanceSpaceCheck.Mode = "DoorSideInvalid";
                return false;
            }

            string initialCoreMode;
            XYZ initialCoreCenter = ResolveEquipmentCoreCenterForFinalPlacement(
                                        doc,
                                        instance,
                                        initialTargetCenter,
                                        out initialCoreMode)
                                    ?? GetElementBoundingBoxCenter(instance)
                                    ?? initialTargetCenter;

            double rotationAngle = SignedAngleOnXY(
                configuredDoorDirection.Normalize(),
                doorOutwardNormal.Normalize());

            Transaction tx = null;
            try
            {
                tx = new Transaction(doc, "Place AHU By Room Rules");
                tx.Start();

                if (Math.Abs(rotationAngle) > 1e-7)
                {
                    Line rotationAxis = Line.CreateBound(
                        initialCoreCenter,
                        initialCoreCenter + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(
                        doc,
                        instance.Id,
                        rotationAxis,
                        rotationAngle);
                    doc.Regenerate();
                }

                // The family insertion origin is not the AHU body center.  After the
                // required door-facing rotation, first bring the actual AHU body back
                // to the recognized room center.  Wall rules and clearance fitting are
                // then solved from this neutral position.
                string centeredCoreMode;
                XYZ coreCenterAfterRotation = ResolveEquipmentCoreCenterForFinalPlacement(
                                                  doc,
                                                  instance,
                                                  roomCenter,
                                                  out centeredCoreMode)
                                              ?? GetElementBoundingBoxCenter(instance)
                                              ?? roomCenter;
                XYZ centerDelta = ResolveHorizontalCenteringDelta(
                    coreCenterAfterRotation,
                    roomCenter);
                if (centerDelta != null &&
                    centerDelta.GetLength() >
                    UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters))
                {
                    ElementTransformUtils.MoveElement(doc, instance.Id, centerDelta);
                    doc.Regenerate();
                }

                // Re-read local axes after rotation.  Translation does not alter them,
                // but re-reading avoids carrying a stale pre-rotation basis.
                if (!TryResolveConfiguredAhuLocalAxes(
                        doc,
                        instance,
                        option.Key,
                        out localRight,
                        out localBottom,
                        out localAxisMode))
                {
                    throw new InvalidOperationException(
                        "AHU local axes were lost after rotation.");
                }

                string wallAlignError;
                if (!TryAlignConfiguredWallSideGaps(
                        doc,
                        room,
                        instance,
                        maintenanceRows,
                        localRight,
                        localBottom,
                        out wallAlignError))
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(wallAlignError)
                            ? "Configured AHU wall clearance could not be satisfied."
                            : wallAlignError);
                }

                List<XYZ> corePoints = CollectEquipmentCorePlanPoints(
                    doc,
                    instance,
                    out string corePointMode);
                List<XYZ> coreHull = ComputeConvexHullXY(corePoints);
                if (coreHull == null || coreHull.Count < 3)
                {
                    throw new InvalidOperationException(
                        "AHU body footprint could not be resolved.");
                }

                List<MaintenanceSpaceFootprint> maintenanceFootprints =
                    CollectMaintenanceSpaceFootprints(
                        doc,
                        instance,
                        out string maintenanceMode);

                string currentCoreMode;
                XYZ currentCoreCenter = ResolveEquipmentCoreCenterForFinalPlacement(
                                            doc,
                                            instance,
                                            roomCenter,
                                            out currentCoreMode)
                                        ?? GetElementBoundingBoxCenter(instance)
                                        ?? roomCenter;

                if (maintenanceFootprints == null || maintenanceFootprints.Count == 0)
                {
                    maintenanceFootprints = BuildCatalogMaintenanceFootprints(
                        currentCoreCenter,
                        coreHull,
                        maintenanceRows,
                        localRight,
                        localBottom);
                    maintenanceMode =
                        maintenanceFootprints.Count > 0
                            ? "CatalogMaintenanceFallback"
                            : "MaintenanceSpaceNotAvailable";
                }

                XYZ candidateTranslation;
                string candidateMode;
                if (!TryFindFeasibleLocalPlacementTranslation(
                        room,
                        currentCoreCenter,
                        coreHull,
                        maintenanceFootprints,
                        maintenanceRows,
                        localRight,
                        localBottom,
                        out candidateTranslation,
                        out candidateMode))
                {
                    maintenanceSpaceCheck.Status = "Exceeded";
                    maintenanceSpaceCheck.Mode =
                        "RevitLocal/" + (maintenanceMode ?? string.Empty) +
                        "/NoFeasibleCandidate/ReviewPlacement";
                    maintenanceSpaceCheck.SolidCount =
                        maintenanceFootprints != null ? maintenanceFootprints.Count : 0;

                    error =
                        "No feasible AHU placement was found. The AHU body / Maintenance Space envelope cannot fit inside the selected room while keeping the configured Door Side and Wall Side distances.";

                    // At this point the AHU has already been rotated so the configured
                    // Door Side faces the room door, centered, and aligned to every
                    // resolvable Wall Side gap.  That is the most useful deterministic
                    // review position when no fully feasible XY translation exists.
                    // Commit it instead of rolling it back/deleting it.
                    retainedForManualReview = true;
                    DiagnosticRecorder.AppendDebug(
                        "[AhuLocalPlacement] No feasible candidate; retaining wall-aligned review placement. RoomKey=" +
                        (room != null ? room.Key ?? string.Empty : string.Empty) +
                        ", FamilyKey=" + (option != null ? option.Key ?? string.Empty : string.Empty) +
                        ", ElementId=" + FormatElementId(instance.Id) +
                        ", DoorSide=" + (doorRule != null ? doorRule.Side ?? string.Empty : string.Empty) +
                        ", MaintenanceMode=" + (maintenanceMode ?? string.Empty));
                    tx.Commit();
                    return false;
                }

                if (candidateTranslation != null &&
                    candidateTranslation.GetLength() >
                    UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters))
                {
                    ElementTransformUtils.MoveElement(
                        doc,
                        instance.Id,
                        candidateTranslation);
                    doc.Regenerate();
                }

                // Final strict validation against the actual moved geometry.
                List<XYZ> finalCorePoints = CollectEquipmentCorePlanPoints(
                    doc,
                    instance,
                    out string finalCoreMode);
                List<XYZ> finalCoreHull = ComputeConvexHullXY(finalCorePoints);
                List<MaintenanceSpaceFootprint> finalMaintenance =
                    CollectMaintenanceSpaceFootprints(
                        doc,
                        instance,
                        out string finalMaintenanceMode);

                string finalCenterMode;
                XYZ finalCoreCenter = ResolveEquipmentCoreCenterForFinalPlacement(
                                          doc,
                                          instance,
                                          roomCenter,
                                          out finalCenterMode)
                                      ?? GetElementBoundingBoxCenter(instance)
                                      ?? roomCenter;

                if (finalMaintenance == null || finalMaintenance.Count == 0)
                {
                    finalMaintenance = BuildCatalogMaintenanceFootprints(
                        finalCoreCenter,
                        finalCoreHull,
                        maintenanceRows,
                        localRight,
                        localBottom);
                    finalMaintenanceMode =
                        finalMaintenance.Count > 0
                            ? "CatalogMaintenanceFallback"
                            : "MaintenanceSpaceNotAvailable";
                }

                string finalFitReason;
                bool finalGeometryFits = IsLocalPlacementGeometryInsideRoom(
                    room,
                    finalCoreHull,
                    finalMaintenance,
                    XYZ.Zero,
                    out finalFitReason);

                string finalWallReason;
                bool finalWallFits = ValidateConfiguredWallSideGaps(
                    room,
                    finalCoreCenter,
                    finalCoreHull,
                    maintenanceRows,
                    localRight,
                    localBottom,
                    35.0,
                    out finalWallReason);

                if (!finalGeometryFits || !finalWallFits)
                {
                    maintenanceSpaceCheck.Status = "Exceeded";
                    maintenanceSpaceCheck.Mode =
                        "RevitLocal/FinalValidation/" +
                        (finalMaintenanceMode ?? string.Empty) +
                        "/ReviewPlacement";

                    error = !string.IsNullOrWhiteSpace(finalWallReason)
                        ? finalWallReason
                        : (!string.IsNullOrWhiteSpace(finalFitReason)
                            ? finalFitReason
                            : "Final AHU placement validation failed.");

                    // A candidate was found and physically moved, but the final strict
                    // validation still reports overflow/touching. Preserve this exact
                    // candidate for visual/manual inspection instead of rolling it back.
                    retainedForManualReview = true;
                    DiagnosticRecorder.AppendDebug(
                        "[AhuLocalPlacement] Final validation exceeded; retaining candidate for manual review. RoomKey=" +
                        (room != null ? room.Key ?? string.Empty : string.Empty) +
                        ", FamilyKey=" + (option != null ? option.Key ?? string.Empty : string.Empty) +
                        ", ElementId=" + FormatElementId(instance.Id) +
                        ", GeometryFits=" + finalGeometryFits +
                        ", WallFits=" + finalWallFits +
                        ", Reason=" + (error ?? string.Empty));
                    tx.Commit();
                    return false;
                }

                maintenanceSpaceCheck.Status = "OK";
                maintenanceSpaceCheck.Mode =
                    "RevitLocal;Axes=" + (localAxisMode ?? string.Empty) +
                    ";Door=" + (doorRule.Side ?? string.Empty) +
                    ";DoorSource=" + (doorSource ?? string.Empty) +
                    ";Core=" + (finalCoreMode ?? string.Empty) +
                    ";Maintenance=" + (finalMaintenanceMode ?? string.Empty) +
                    ";Candidate=" + (candidateMode ?? string.Empty);
                maintenanceSpaceCheck.SolidCount =
                    finalMaintenance != null ? finalMaintenance.Count : 0;

                resultLogLocalPlacement(
                    room,
                    option,
                    instance,
                    doorRule,
                    doorCenter,
                    doorBoundaryPoint,
                    doorOutwardNormal,
                    localRight,
                    localBottom,
                    rotationAngle,
                    finalCoreCenter,
                    maintenanceRows,
                    maintenanceSpaceCheck);

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }

                error = ex.Message;
                if (maintenanceSpaceCheck != null &&
                    string.Equals(
                        maintenanceSpaceCheck.Status,
                        "NotChecked",
                        StringComparison.OrdinalIgnoreCase))
                {
                    maintenanceSpaceCheck.Status = "Exceeded";
                    maintenanceSpaceCheck.Mode =
                        "RevitLocal/Exception=" + ex.GetType().Name;
                }

                return false;
            }
        }

        private static void TryDeleteFailedPlacementInstance(
            Document doc,
            ElementId instanceId)
        {
            if (doc == null ||
                instanceId == null ||
                instanceId == ElementId.InvalidElementId ||
                doc.GetElement(instanceId) == null)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(
                    doc,
                    "Remove Failed AHU Placement"))
                {
                    tx.Start();
                    doc.Delete(instanceId);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuLocalPlacement] Failed instance cleanup skipped. ElementId=" +
                    FormatElementId(instanceId) +
                    ", Error=" + ex.Message);
            }
        }

        private static bool TryResolveConfiguredAhuLocalAxes(
            Document doc,
            FamilyInstance root,
            string familyKey,
            out XYZ rightAxis,
            out XYZ bottomAxis,
            out string mode)
        {
            rightAxis = null;
            bottomAxis = null;
            mode = string.Empty;
            if (doc == null || root == null || string.IsNullOrWhiteSpace(familyKey))
            {
                return false;
            }

            IReadOnlyList<RoomCustomFamilySubModuleDto> configured =
                RoomCustomFamilyCatalogService.GetSubModules(familyKey);
            if (configured == null || configured.Count < 2)
            {
                mode = "SubModuleCatalogMissing";
                return false;
            }

            List<FamilyInstance> nested = new List<FamilyInstance>();
            CollectNestedFamilyInstances(
                doc,
                root,
                nested,
                new HashSet<int>(),
                true);

            List<AhuConfiguredModuleCenter> centers =
                new List<AhuConfiguredModuleCenter>();

            foreach (RoomCustomFamilySubModuleDto row in configured
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Sequence))
            {
                string token = NormalizePlacementNameToken(row.Name);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                FamilyInstance matched = nested
                    .Where(x => x != null)
                    .Select(x => new
                    {
                        Instance = x,
                        Search = NormalizePlacementNameToken(
                            BuildElementSearchText(x))
                    })
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Search) &&
                        x.Search.Contains(token))
                    .OrderBy(x => x.Search.Length)
                    .Select(x => x.Instance)
                    .FirstOrDefault();

                XYZ center = matched != null
                    ? GetElementBoundingBoxCenter(matched)
                    : null;
                if (center == null)
                {
                    continue;
                }

                centers.Add(new AhuConfiguredModuleCenter
                {
                    ModuleCode = row.ModuleCode ?? string.Empty,
                    Name = row.Name ?? string.Empty,
                    GridRow = row.GridRow,
                    GridColumn = row.GridColumn,
                    Center = center
                });
            }

            if (centers.Count < 2)
            {
                // These AHU RFAs can contain non-shared nested content, so
                // GetSubComponentIds() may expose no named S1-S6 subcomponents even
                // though the visible family geometry is correct.  All current AHU
                // families use the same authoring convention:
                //   local Right  = FamilyInstance.HandOrientation
                //   local Bottom = -FamilyInstance.FacingOrientation
                // This fallback is deterministic and replaces the old connector/
                // Service-Side guessing.  If shared named modules are available,
                // the catalog-name/grid reconstruction above remains preferred.
                XYZ hand = Flatten(root.HandOrientation);
                XYZ facing = Flatten(root.FacingOrientation);
                if (IsUsableDirection(hand) && IsUsableDirection(facing))
                {
                    rightAxis = hand.Normalize();
                    bottomAxis = NegateXY(facing.Normalize());
                    mode = "FamilyHandFacingFallback(matches=" +
                           centers.Count.ToString(CultureInfo.InvariantCulture) +
                           ", Right=Hand, Bottom=-Facing)";
                    return IsUsableDirection(bottomAxis);
                }

                mode = "NestedModuleMatchFailed(count=" +
                       centers.Count.ToString(CultureInfo.InvariantCulture) + ")";
                return false;
            }

            List<IGrouping<int, AhuConfiguredModuleCenter>> columnGroups =
                centers.GroupBy(x => x.GridColumn)
                    .OrderBy(x => x.Key)
                    .ToList();
            if (columnGroups.Count >= 2)
            {
                XYZ leftCenter = AveragePlacementPoints(columnGroups.First()
                    .Select(x => x.Center));
                XYZ rightCenter = AveragePlacementPoints(columnGroups.Last()
                    .Select(x => x.Center));
                XYZ vector = Flatten(rightCenter - leftCenter);
                if (IsUsableDirection(vector))
                {
                    rightAxis = vector.Normalize();
                }
            }

            List<IGrouping<int, AhuConfiguredModuleCenter>> rowGroups =
                centers.GroupBy(x => x.GridRow)
                    .OrderBy(x => x.Key)
                    .ToList();
            if (rowGroups.Count >= 2)
            {
                XYZ topCenter = AveragePlacementPoints(rowGroups.First()
                    .Select(x => x.Center));
                XYZ bottomCenter = AveragePlacementPoints(rowGroups.Last()
                    .Select(x => x.Center));
                XYZ vector = Flatten(bottomCenter - topCenter);
                if (IsUsableDirection(vector))
                {
                    bottomAxis = vector.Normalize();
                }
            }

            if (!IsUsableDirection(rightAxis) || !IsUsableDirection(bottomAxis))
            {
                XYZ hand = Flatten(root.HandOrientation);
                XYZ facing = Flatten(root.FacingOrientation);
                if (IsUsableDirection(hand) && IsUsableDirection(facing))
                {
                    rightAxis = hand.Normalize();
                    bottomAxis = NegateXY(facing.Normalize());
                    mode = "FamilyHandFacingFallbackAfterPartialMatch(matches=" +
                           centers.Count.ToString(CultureInfo.InvariantCulture) +
                           ", Right=Hand, Bottom=-Facing)";
                    return IsUsableDirection(bottomAxis);
                }

                mode = "ConfiguredAxisInsufficient(matches=" +
                       centers.Count.ToString(CultureInfo.InvariantCulture) + ")";
                return false;
            }

            // Remove tiny non-orthogonal noise from nested-family bounding boxes
            // while preserving the observed Bottom sign from the catalog grid.
            XYZ rawBottom = bottomAxis.Normalize();
            XYZ r = rightAxis.Normalize();
            double dot = DotXY(rawBottom, r);
            XYZ orthogonalBottom = Flatten(rawBottom - r * dot);
            if (!IsUsableDirection(orthogonalBottom))
            {
                mode = "ConfiguredAxesParallel";
                return false;
            }

            orthogonalBottom = orthogonalBottom.Normalize();
            if (DotXY(orthogonalBottom, rawBottom) < 0.0)
            {
                orthogonalBottom = NegateXY(orthogonalBottom);
            }

            rightAxis = r;
            bottomAxis = orthogonalBottom;
            mode = "SubModules(matches=" +
                   centers.Count.ToString(CultureInfo.InvariantCulture) + ")";
            return true;
        }

        private static void CollectNestedFamilyInstances(
            Document doc,
            FamilyInstance instance,
            List<FamilyInstance> result,
            HashSet<int> seen,
            bool skipSelf)
        {
            if (doc == null || instance == null || result == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            if (!skipSelf)
            {
                result.Add(instance);
            }

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId id in subIds)
            {
                FamilyInstance child =
                    id != null && id != ElementId.InvalidElementId
                        ? doc.GetElement(id) as FamilyInstance
                        : null;
                if (child != null)
                {
                    CollectNestedFamilyInstances(
                        doc,
                        child,
                        result,
                        seen,
                        false);
                }
            }
        }

        private static string NormalizePlacementNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(
                value.ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
        }

        private static XYZ AveragePlacementPoints(IEnumerable<XYZ> points)
        {
            List<XYZ> rows = (points ?? Enumerable.Empty<XYZ>())
                .Where(x => x != null)
                .ToList();
            if (rows.Count == 0)
            {
                return null;
            }

            return new XYZ(
                rows.Average(x => x.X),
                rows.Average(x => x.Y),
                rows.Average(x => x.Z));
        }

        private static XYZ ResolveConfiguredAhuSideDirection(
            string side,
            XYZ rightAxis,
            XYZ bottomAxis)
        {
            if (!IsUsableDirection(rightAxis) ||
                !IsUsableDirection(bottomAxis))
            {
                return null;
            }

            string value = (side ?? string.Empty).Trim();
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return rightAxis.Normalize();
            }

            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase))
            {
                return NegateXY(rightAxis.Normalize());
            }

            if (string.Equals(value, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return bottomAxis.Normalize();
            }

            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase))
            {
                return NegateXY(bottomAxis.Normalize());
            }

            return null;
        }

        private static bool TryResolveDoorBoundaryFrame(
            RoomSemanticRecord room,
            XYZ roomCenter,
            XYZ doorCenter,
            out XYZ boundaryPoint,
            out XYZ outwardNormal,
            out XYZ tangent)
        {
            boundaryPoint = null;
            outwardNormal = null;
            tangent = null;
            if (room == null || roomCenter == null || doorCenter == null ||
                room.LoopPoints == null || room.LoopPoints.Count < 2)
            {
                return false;
            }

            List<XYZ> points = room.LoopPoints
                .Where(x => x != null)
                .ToList();
            if (points.Count < 2)
            {
                return false;
            }

            double bestDistance = double.MaxValue;
            XYZ bestA = null;
            XYZ bestB = null;
            XYZ bestPoint = null;

            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                XYZ closest = ClosestPointOnSegmentXY(
                    doorCenter,
                    a,
                    b);
                if (closest == null)
                {
                    continue;
                }

                double distance = HorizontalDistance(
                    doorCenter,
                    closest);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestA = a;
                    bestB = b;
                    bestPoint = closest;
                }
            }

            XYZ segment = bestA != null && bestB != null
                ? Flatten(bestB - bestA)
                : null;
            if (!IsUsableDirection(segment) || bestPoint == null)
            {
                return false;
            }

            tangent = segment.Normalize();
            XYZ n1 = new XYZ(-tangent.Y, tangent.X, 0.0);
            XYZ n2 = NegateXY(n1);
            XYZ centerToBoundary = Flatten(bestPoint - roomCenter);
            if (!IsUsableDirection(centerToBoundary))
            {
                centerToBoundary = Flatten(doorCenter - roomCenter);
            }

            if (!IsUsableDirection(centerToBoundary))
            {
                return false;
            }

            outwardNormal =
                DotXY(n1, centerToBoundary) >= DotXY(n2, centerToBoundary)
                    ? n1.Normalize()
                    : n2.Normalize();
            boundaryPoint = bestPoint;
            return IsUsableDirection(outwardNormal);
        }

        private static XYZ ClosestPointOnSegmentXY(
            XYZ point,
            XYZ a,
            XYZ b)
        {
            if (point == null || a == null || b == null)
            {
                return null;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-12)
            {
                return new XYZ(a.X, a.Y, point.Z);
            }

            double t =
                ((point.X - a.X) * dx +
                 (point.Y - a.Y) * dy) /
                lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return new XYZ(
                a.X + t * dx,
                a.Y + t * dy,
                point.Z);
        }

        private static List<XYZ> CollectEquipmentCorePlanPoints(
            Document doc,
            FamilyInstance instance,
            out string mode)
        {
            mode = string.Empty;
            List<XYZ> points = new List<XYZ>();
            if (doc == null || instance == null)
            {
                mode = "InvalidContext";
                return points;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = null;
            try
            {
                geometry = instance.get_Geometry(options);
            }
            catch
            {
                geometry = null;
            }

            if (geometry == null)
            {
                mode = "NoGeometry";
                return points;
            }

            int included;
            int maintenanceSkipped;
            int transparentSkipped;
            int tinySkipped;
            CollectEquipmentCorePlanPoints(
                doc,
                geometry,
                points,
                0,
                out included,
                out maintenanceSkipped,
                out transparentSkipped,
                out tinySkipped);

            mode =
                "PhysicalCore(included=" +
                included.ToString(CultureInfo.InvariantCulture) +
                ", maintenanceSkipped=" +
                maintenanceSkipped.ToString(CultureInfo.InvariantCulture) +
                ", transparentSkipped=" +
                transparentSkipped.ToString(CultureInfo.InvariantCulture) +
                ", tinySkipped=" +
                tinySkipped.ToString(CultureInfo.InvariantCulture) + ")";
            return points;
        }

        private static void CollectEquipmentCorePlanPoints(
            Document doc,
            GeometryElement geometry,
            List<XYZ> points,
            int depth,
            out int included,
            out int maintenanceSkipped,
            out int transparentSkipped,
            out int tinySkipped)
        {
            included = 0;
            maintenanceSkipped = 0;
            transparentSkipped = 0;
            tinySkipped = 0;

            if (doc == null || geometry == null || points == null || depth > 8)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                {
                    continue;
                }

                Solid solid = geometryObject as Solid;
                if (solid != null)
                {
                    if (IsTinySolid(solid))
                    {
                        tinySkipped++;
                        continue;
                    }

                    MaintenanceSpaceSolidKind maintenanceKind;
                    string maintenanceReason;
                    if (IsMaintenanceSpaceSolid(
                            doc,
                            geometryObject,
                            solid,
                            out maintenanceKind,
                            out maintenanceReason))
                    {
                        maintenanceSkipped++;
                        continue;
                    }

                    if (IsMostlyTransparentSolid(doc, solid, 70))
                    {
                        transparentSkipped++;
                        continue;
                    }

                    List<XYZ> solidPoints = ExtractSolidXyPoints(solid);
                    if (solidPoints.Count == 0)
                    {
                        BoundingBoxXYZ box = null;
                        try
                        {
                            box = solid.GetBoundingBox();
                        }
                        catch
                        {
                            box = null;
                        }

                        solidPoints = GetBoundingBoxXyCorners(box);
                    }

                    foreach (XYZ point in solidPoints)
                    {
                        AddUniquePointXY(points, point);
                    }
                    included++;
                    continue;
                }

                GeometryInstance nestedInstance =
                    geometryObject as GeometryInstance;
                if (nestedInstance == null)
                {
                    continue;
                }

                GeometryElement nested = null;
                try
                {
                    nested = nestedInstance.GetInstanceGeometry();
                }
                catch
                {
                    nested = null;
                }

                if (nested == null)
                {
                    continue;
                }

                int childIncluded;
                int childMaintenance;
                int childTransparent;
                int childTiny;
                CollectEquipmentCorePlanPoints(
                    doc,
                    nested,
                    points,
                    depth + 1,
                    out childIncluded,
                    out childMaintenance,
                    out childTransparent,
                    out childTiny);
                included += childIncluded;
                maintenanceSkipped += childMaintenance;
                transparentSkipped += childTransparent;
                tinySkipped += childTiny;
            }
        }

        private static bool TryAlignConfiguredWallSideGaps(
            Document doc,
            RoomSemanticRecord room,
            FamilyInstance instance,
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows,
            XYZ localRight,
            XYZ localBottom,
            out string error)
        {
            error = string.Empty;
            if (doc == null || room == null || instance == null)
            {
                error = "Invalid wall-side placement context.";
                return false;
            }

            List<RoomCustomFamilyMaintenanceSpaceDto> wallRules =
                (maintenanceRows ?? Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                    .Where(x =>
                        x != null &&
                        x.IsWallSide &&
                        x.DimensionMm >= 0 &&
                        !string.IsNullOrWhiteSpace(x.Side))
                    .OrderBy(x => x.Sequence)
                    .ToList();

            foreach (RoomCustomFamilyMaintenanceSpaceDto rule in wallRules)
            {
                XYZ sideDirection = ResolveConfiguredAhuSideDirection(
                    rule.Side,
                    localRight,
                    localBottom);
                if (!IsUsableDirection(sideDirection))
                {
                    error = "Invalid Wall Side configuration: " +
                            (rule.Side ?? string.Empty) + ".";
                    return false;
                }

                string centerMode;
                XYZ coreCenter = ResolveEquipmentCoreCenterForFinalPlacement(
                                     doc,
                                     instance,
                                     null,
                                     out centerMode)
                                 ?? GetElementBoundingBoxCenter(instance);
                List<XYZ> corePoints = CollectEquipmentCorePlanPoints(
                    doc,
                    instance,
                    out string coreMode);
                List<XYZ> coreHull = ComputeConvexHullXY(corePoints);
                if (coreCenter == null || coreHull == null || coreHull.Count < 3)
                {
                    error = "AHU body footprint could not be resolved while applying Wall Side.";
                    return false;
                }

                List<XYZ> validCoreHull = coreHull
                    .Where(x => x != null && IsUsablePlanPoint(x))
                    .ToList();
                if (validCoreHull.Count < 3)
                {
                    error = "AHU body footprint contains no usable plan geometry while applying Wall Side.";
                    return false;
                }

                DiagnosticRecorder.AppendDebug(
                    "[AhuLocalPlacement] WallSideResolve. Side=" +
                    (rule.Side ?? string.Empty) +
                    ", CoreMode=" + (coreMode ?? string.Empty) +
                    ", CoreCenter=(" + FormatPoint(coreCenter) + ")" +
                    ", CoreHullCount=" + validCoreHull.Count.ToString(CultureInfo.InvariantCulture) +
                    ", CoreXmm=[" +
                    FormatMm(validCoreHull.Min(x => x.X)) + "," +
                    FormatMm(validCoreHull.Max(x => x.X)) + "]" +
                    ", CoreYmm=[" +
                    FormatMm(validCoreHull.Min(x => x.Y)) + "," +
                    FormatMm(validCoreHull.Max(x => x.Y)) + "]" +
                    ", Direction=(" + FormatVector(sideDirection) + ")");

                double currentGap;
                if (!TryResolveCoreGapToBoundary(
                        room,
                        coreCenter,
                        validCoreHull,
                        sideDirection,
                        out currentGap))
                {
                    error = "No room boundary was found in the configured Wall Side direction " +
                            (rule.Side ?? string.Empty) + ".";
                    return false;
                }

                double desiredGap = UnitUtils.ConvertToInternalUnits(
                    Math.Max(0.0, rule.DimensionMm),
                    UnitTypeId.Millimeters);
                double moveAmount = currentGap - desiredGap;
                if (!IsReasonablePlacementSignedDistance(moveAmount))
                {
                    error = "Resolved Wall Side move is outside the valid placement range for " +
                            (rule.Side ?? string.Empty) + ".";
                    return false;
                }

                if (Math.Abs(moveAmount) >
                    UnitUtils.ConvertToInternalUnits(
                        1.0,
                        UnitTypeId.Millimeters))
                {
                    ElementTransformUtils.MoveElement(
                        doc,
                        instance.Id,
                        sideDirection.Normalize() * moveAmount);
                    doc.Regenerate();
                }

                DiagnosticRecorder.AppendDebug(
                    "[AhuLocalPlacement] WallSideAligned. Side=" +
                    (rule.Side ?? string.Empty) +
                    ", RequestedMm=" +
                    rule.DimensionMm.ToString(
                        CultureInfo.InvariantCulture) +
                    ", BeforeMm=" +
                    FormatMm(currentGap) +
                    ", MoveMm=" +
                    FormatMm(moveAmount));
            }

            return true;
        }

        private static bool TryResolveCoreGapToBoundary(
            RoomSemanticRecord room,
            XYZ coreCenter,
            IList<XYZ> coreHull,
            XYZ sideDirection,
            out double gap)
        {
            gap = 0.0;
            if (room == null || coreCenter == null ||
                coreHull == null || coreHull.Count < 3 ||
                !IsUsableDirection(sideDirection) ||
                !IsUsablePlanPoint(coreCenter))
            {
                return false;
            }

            // Revit geometry can occasionally expose sentinel-like coordinates
            // (for example ~1E30) from helper/reference solids.  Those points must
            // never participate in the AHU body extent or wall-gap calculation.
            List<XYZ> validHull = coreHull
                .Where(x => x != null && IsUsablePlanPoint(x))
                .ToList();
            if (validHull.Count < 3)
            {
                return false;
            }

            XYZ direction = sideDirection.Normalize();
            double boundaryDistance;
            if (!TryRayDistanceToRoomBoundary(
                    room,
                    coreCenter,
                    direction,
                    out boundaryDistance) ||
                !IsReasonablePlacementDistance(boundaryDistance))
            {
                return false;
            }

            List<double> extents = validHull
                .Select(x => DotXY(
                    Flatten(x - coreCenter),
                    direction))
                .Where(IsReasonablePlacementSignedDistance)
                .ToList();
            if (extents.Count < 3)
            {
                return false;
            }

            double bodyExtent = extents.Max();
            if (!IsReasonablePlacementSignedDistance(bodyExtent))
            {
                return false;
            }

            gap = boundaryDistance - bodyExtent;
            return IsReasonablePlacementSignedDistance(gap);
        }

        private static bool TryRayDistanceToRoomBoundary(
            RoomSemanticRecord room,
            XYZ origin,
            XYZ direction,
            out double distance)
        {
            distance = double.MaxValue;
            if (room == null || origin == null ||
                !IsUsableDirection(direction) ||
                room.LoopPoints == null ||
                room.LoopPoints.Count < 2)
            {
                return false;
            }

            XYZ d = direction.Normalize();
            List<XYZ> points = room.LoopPoints
                .Where(x => x != null)
                .ToList();
            if (points.Count < 2)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                double rayDistance;
                if (!TryIntersectRayWithSegmentXY(
                        origin,
                        d,
                        a,
                        b,
                        out rayDistance))
                {
                    continue;
                }

                if (rayDistance >= -1e-8 &&
                    rayDistance < distance)
                {
                    distance = Math.Max(0.0, rayDistance);
                    found = true;
                }
            }

            return found && distance != double.MaxValue;
        }

        private static bool TryIntersectRayWithSegmentXY(
            XYZ origin,
            XYZ direction,
            XYZ a,
            XYZ b,
            out double rayDistance)
        {
            rayDistance = 0.0;
            if (origin == null || direction == null ||
                a == null || b == null)
            {
                return false;
            }

            double rx = direction.X;
            double ry = direction.Y;
            double sx = b.X - a.X;
            double sy = b.Y - a.Y;
            double denominator = rx * sy - ry * sx;
            if (Math.Abs(denominator) < 1e-10)
            {
                return false;
            }

            double qpx = a.X - origin.X;
            double qpy = a.Y - origin.Y;
            double t = (qpx * sy - qpy * sx) / denominator;
            double u = (qpx * ry - qpy * rx) / denominator;
            if (t < -1e-8 || u < -1e-8 || u > 1.0 + 1e-8)
            {
                return false;
            }

            rayDistance = t;
            return true;
        }

        private static List<MaintenanceSpaceFootprint> BuildCatalogMaintenanceFootprints(
            XYZ coreCenter,
            IList<XYZ> coreHull,
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows,
            XYZ localRight,
            XYZ localBottom)
        {
            List<MaintenanceSpaceFootprint> result =
                new List<MaintenanceSpaceFootprint>();
            if (coreCenter == null || coreHull == null ||
                coreHull.Count < 3 ||
                !IsUsableDirection(localRight) ||
                !IsUsableDirection(localBottom))
            {
                return result;
            }

            XYZ right = localRight.Normalize();
            XYZ bottom = localBottom.Normalize();

            List<double> rightProjection = coreHull
                .Where(x => x != null)
                .Select(x => DotXY(
                    Flatten(x - coreCenter),
                    right))
                .ToList();
            List<double> bottomProjection = coreHull
                .Where(x => x != null)
                .Select(x => DotXY(
                    Flatten(x - coreCenter),
                    bottom))
                .ToList();
            if (rightProjection.Count == 0 ||
                bottomProjection.Count == 0)
            {
                return result;
            }

            double minR = rightProjection.Min();
            double maxR = rightProjection.Max();
            double minB = bottomProjection.Min();
            double maxB = bottomProjection.Max();

            foreach (RoomCustomFamilyMaintenanceSpaceDto row in
                (maintenanceRows ??
                 Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                    .Where(x =>
                        x != null &&
                        x.DimensionMm > 0 &&
                        !string.IsNullOrWhiteSpace(x.Side)))
            {
                double clearance = UnitUtils.ConvertToInternalUnits(
                    row.DimensionMm,
                    UnitTypeId.Millimeters);

                double r0 = minR;
                double r1 = maxR;
                double b0 = minB;
                double b1 = maxB;

                if (string.Equals(
                        row.Side,
                        "Top",
                        StringComparison.OrdinalIgnoreCase))
                {
                    b0 = minB - clearance;
                    b1 = minB;
                }
                else if (string.Equals(
                             row.Side,
                             "Bottom",
                             StringComparison.OrdinalIgnoreCase))
                {
                    b0 = maxB;
                    b1 = maxB + clearance;
                }
                else if (string.Equals(
                             row.Side,
                             "Left",
                             StringComparison.OrdinalIgnoreCase))
                {
                    r0 = minR - clearance;
                    r1 = minR;
                }
                else if (string.Equals(
                             row.Side,
                             "Right",
                             StringComparison.OrdinalIgnoreCase))
                {
                    r0 = maxR;
                    r1 = maxR + clearance;
                }
                else
                {
                    continue;
                }

                List<XYZ> hull = new List<XYZ>
                {
                    coreCenter + right * r0 + bottom * b0,
                    coreCenter + right * r1 + bottom * b0,
                    coreCenter + right * r1 + bottom * b1,
                    coreCenter + right * r0 + bottom * b1
                };

                result.Add(new MaintenanceSpaceFootprint
                {
                    Source = "Catalog:" +
                             (row.MaintenanceCode ?? row.Side ?? string.Empty),
                    HullPoints = hull
                });
            }

            return result;
        }

        private static bool TryFindFeasibleLocalPlacementTranslation(
            RoomSemanticRecord room,
            XYZ coreCenter,
            IList<XYZ> coreHull,
            IList<MaintenanceSpaceFootprint> maintenanceFootprints,
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows,
            XYZ localRight,
            XYZ localBottom,
            out XYZ translation,
            out string mode)
        {
            translation = XYZ.Zero;
            mode = string.Empty;
            if (room == null || coreCenter == null ||
                coreHull == null || coreHull.Count < 3)
            {
                mode = "InvalidContext";
                return false;
            }

            List<RoomCustomFamilyMaintenanceSpaceDto> wallRules =
                (maintenanceRows ??
                 Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                    .Where(x =>
                        x != null &&
                        x.IsWallSide &&
                        !string.IsNullOrWhiteSpace(x.Side))
                    .ToList();

            bool rightAxisFixed =
                wallRules.Any(x =>
                    string.Equals(
                        x.Side,
                        "Left",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        x.Side,
                        "Right",
                        StringComparison.OrdinalIgnoreCase));

            bool bottomAxisFixed =
                wallRules.Any(x =>
                    string.Equals(
                        x.Side,
                        "Top",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        x.Side,
                        "Bottom",
                        StringComparison.OrdinalIgnoreCase));

            int wallSideCount = wallRules
                .Select(x => (x.Side ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            // Placement modes are intentionally narrow so the already-proven
            // single-wall behavior is not changed:
            //   0 Wall Side  -> keep the AHU physical-core center at Room Center.
            //                   Do not drift/search away from the center.
            //   1 Wall Side  -> keep the existing exact wall-gap alignment and
            //                   search only along the remaining free local axis.
            //   2 Wall Sides -> adjacent sides fix both local axes; the current
            //                   aligned position is deterministic, so do not search.
            // Family Library configuration is expected to use adjacent pairs only.
            if (wallSideCount == 0)
            {
                string centerFitReason;
                if (!IsLocalPlacementGeometryInsideRoom(
                        room,
                        coreHull,
                        maintenanceFootprints,
                        XYZ.Zero,
                        out centerFitReason))
                {
                    mode = "CenterOnlyExceeded";
                    DiagnosticRecorder.AppendDebug(
                        "[AhuLocalPlacement] PlacementMode=CenterOnly, Result=Exceeded, Reason=" +
                        (centerFitReason ?? string.Empty));
                    return false;
                }

                translation = XYZ.Zero;
                mode = "CenterOnly";
                DiagnosticRecorder.AppendDebug(
                    "[AhuLocalPlacement] PlacementMode=CenterOnly, Result=Valid");
                return true;
            }

            List<XYZ> candidates = BuildLocalPlacementCandidateTranslations(
                room,
                rightAxisFixed,
                bottomAxisFixed,
                localRight,
                localBottom);

            int tested = 0;
            foreach (XYZ candidate in candidates)
            {
                tested++;
                string fitReason;
                if (!IsLocalPlacementGeometryInsideRoom(
                        room,
                        coreHull,
                        maintenanceFootprints,
                        candidate,
                        out fitReason))
                {
                    continue;
                }

                if (!ValidateConfiguredWallSideGaps(
                        room,
                        coreCenter + candidate,
                        TranslatePlacementPoints(coreHull, candidate),
                        maintenanceRows,
                        localRight,
                        localBottom,
                        35.0,
                        out string wallReason))
                {
                    continue;
                }

                translation = candidate;
                mode =
                    "Search(wallSides=" +
                    wallSideCount.ToString(CultureInfo.InvariantCulture) +
                    ", tested=" +
                    tested.ToString(CultureInfo.InvariantCulture) +
                    ", rightFixed=" + rightAxisFixed +
                    ", bottomFixed=" + bottomAxisFixed +
                    ", deltaMm=[" +
                    FormatMm(candidate.X) + "," +
                    FormatMm(candidate.Y) + "])";
                return true;
            }

            mode =
                "SearchFailed(wallSides=" +
                wallSideCount.ToString(CultureInfo.InvariantCulture) +
                ", tested=" +
                tested.ToString(CultureInfo.InvariantCulture) +
                ", rightFixed=" + rightAxisFixed +
                ", bottomFixed=" + bottomAxisFixed + ")";
            return false;
        }

        private static List<XYZ> BuildLocalPlacementCandidateTranslations(
            RoomSemanticRecord room,
            bool rightAxisFixed,
            bool bottomAxisFixed,
            XYZ localRight,
            XYZ localBottom)
        {
            List<XYZ> result = new List<XYZ>();
            result.Add(XYZ.Zero);

            XYZ right = IsUsableDirection(localRight)
                ? localRight.Normalize()
                : XYZ.BasisX;
            XYZ bottom = IsUsableDirection(localBottom)
                ? localBottom.Normalize()
                : XYZ.BasisY;

            double spanMm = 6000.0;
            if (room != null && room.BBox != null &&
                room.BBox.Min != null && room.BBox.Max != null)
            {
                spanMm = Math.Max(
                    Math.Abs(room.BBox.Max.X - room.BBox.Min.X),
                    Math.Abs(room.BBox.Max.Y - room.BBox.Min.Y)) *
                    304.8;
                spanMm = Math.Max(1000.0, spanMm);
            }

            if (rightAxisFixed && bottomAxisFixed)
            {
                return result;
            }

            if (rightAxisFixed ^ bottomAxisFixed)
            {
                XYZ freeAxis = rightAxisFixed ? bottom : right;
                double stepFt = UnitUtils.ConvertToInternalUnits(
                    100.0,
                    UnitTypeId.Millimeters);
                int maxSteps = Math.Min(
                    100,
                    Math.Max(
                        1,
                        (int)Math.Ceiling(spanMm / 100.0)));

                for (int i = 1; i <= maxSteps; i++)
                {
                    result.Add(freeAxis * (stepFt * i));
                    result.Add(freeAxis * (-stepFt * i));
                }

                return result;
            }

            // Safety fallback only.  Normal 0-Wall-Side placement is handled
            // above as CenterOnly and never reaches this branch.  Keep the legacy
            // grid search here so callers outside the normal AHU placement flow are
            // not silently broken if they invoke this helper directly.
            double gridStepFt = UnitUtils.ConvertToInternalUnits(
                250.0,
                UnitTypeId.Millimeters);
            int maxRing = Math.Min(
                40,
                Math.Max(
                    1,
                    (int)Math.Ceiling(spanMm / 250.0)));

            for (int ring = 1; ring <= maxRing; ring++)
            {
                for (int ix = -ring; ix <= ring; ix++)
                {
                    AddUniquePlacementTranslation(
                        result,
                        right * (gridStepFt * ix) +
                        bottom * (gridStepFt * ring));
                    AddUniquePlacementTranslation(
                        result,
                        right * (gridStepFt * ix) -
                        bottom * (gridStepFt * ring));
                }

                for (int iy = -ring + 1; iy <= ring - 1; iy++)
                {
                    AddUniquePlacementTranslation(
                        result,
                        right * (gridStepFt * ring) +
                        bottom * (gridStepFt * iy));
                    AddUniquePlacementTranslation(
                        result,
                        right * (-gridStepFt * ring) +
                        bottom * (gridStepFt * iy));
                }
            }

            return result;
        }

        private static void AddUniquePlacementTranslation(
            List<XYZ> values,
            XYZ candidate)
        {
            if (values == null || candidate == null)
            {
                return;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(
                1.0,
                UnitTypeId.Millimeters);
            if (values.Any(x =>
                x != null &&
                HorizontalDistance(x, candidate) <= tolerance))
            {
                return;
            }

            values.Add(candidate);
        }

        private static bool IsLocalPlacementGeometryInsideRoom(
            RoomSemanticRecord room,
            IList<XYZ> coreHull,
            IList<MaintenanceSpaceFootprint> maintenanceFootprints,
            XYZ translation,
            out string reason)
        {
            reason = string.Empty;
            if (room == null || coreHull == null || coreHull.Count < 3)
            {
                reason = "AHU body footprint is unavailable.";
                return false;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(
                20.0,
                UnitTypeId.Millimeters);
            XYZ delta = translation ?? XYZ.Zero;

            List<XYZ> coreSamples = BuildFootprintSamplePoints(
                TranslatePlacementPoints(coreHull, delta));
            foreach (XYZ sample in coreSamples)
            {
                if (!IsPointInsideRoomWithTolerance(
                        room,
                        sample,
                        tolerance))
                {
                    reason = "AHU body exceeds the selected room boundary.";
                    return false;
                }
            }

            foreach (MaintenanceSpaceFootprint footprint in
                maintenanceFootprints ??
                new List<MaintenanceSpaceFootprint>())
            {
                if (footprint == null ||
                    footprint.HullPoints == null ||
                    footprint.HullPoints.Count < 3)
                {
                    continue;
                }

                List<XYZ> samples = BuildFootprintSamplePoints(
                    TranslatePlacementPoints(
                        footprint.HullPoints,
                        delta));
                foreach (XYZ sample in samples)
                {
                    if (!IsPointInsideRoomWithTolerance(
                            room,
                            sample,
                            tolerance))
                    {
                        reason =
                            "Maintenance Space exceeds the selected room boundary.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsPointInsideRoomWithTolerance(
            RoomSemanticRecord room,
            XYZ point,
            double tolerance)
        {
            if (room == null || point == null)
            {
                return false;
            }

            if (IsPointInsideLoop(room.LoopPoints, point))
            {
                return true;
            }

            double boundaryDistance =
                DistanceToRoomBoundary(room, point);
            return boundaryDistance != double.MaxValue &&
                   boundaryDistance <= Math.Max(0.0, tolerance);
        }

        private static List<XYZ> TranslatePlacementPoints(
            IEnumerable<XYZ> points,
            XYZ translation)
        {
            XYZ delta = translation ?? XYZ.Zero;
            return (points ?? Enumerable.Empty<XYZ>())
                .Where(x => x != null && IsUsablePlanPoint(x))
                .Select(x => x + delta)
                .Where(IsUsablePlanPoint)
                .ToList();
        }

        private static bool ValidateConfiguredWallSideGaps(
            RoomSemanticRecord room,
            XYZ coreCenter,
            IList<XYZ> coreHull,
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows,
            XYZ localRight,
            XYZ localBottom,
            double toleranceMm,
            out string reason)
        {
            reason = string.Empty;
            if (room == null || coreCenter == null ||
                coreHull == null || coreHull.Count < 3)
            {
                reason = "AHU body footprint is unavailable.";
                return false;
            }

            foreach (RoomCustomFamilyMaintenanceSpaceDto rule in
                (maintenanceRows ??
                 Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                    .Where(x =>
                        x != null &&
                        x.IsWallSide &&
                        !string.IsNullOrWhiteSpace(x.Side)))
            {
                XYZ direction = ResolveConfiguredAhuSideDirection(
                    rule.Side,
                    localRight,
                    localBottom);
                double gap;
                if (!IsUsableDirection(direction) ||
                    !TryResolveCoreGapToBoundary(
                        room,
                        coreCenter,
                        coreHull,
                        direction,
                        out gap))
                {
                    reason =
                        "Configured Wall Side '" +
                        (rule.Side ?? string.Empty) +
                        "' cannot resolve a room boundary.";
                    return false;
                }

                double actualMm = gap * 304.8;
                double requestedMm = Math.Max(
                    0.0,
                    rule.DimensionMm);
                if (Math.Abs(actualMm - requestedMm) >
                    Math.Max(1.0, toleranceMm))
                {
                    reason =
                        "Configured Wall Side '" +
                        (rule.Side ?? string.Empty) +
                        "' requires " +
                        requestedMm.ToString(
                            "0",
                            CultureInfo.InvariantCulture) +
                        " mm from the AHU body to the wall, but the resolved gap is " +
                        actualMm.ToString(
                            "0",
                            CultureInfo.InvariantCulture) +
                        " mm.";
                    return false;
                }
            }

            return true;
        }

        private static void resultLogLocalPlacement(
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            FamilyInstance instance,
            RoomCustomFamilyMaintenanceSpaceDto doorRule,
            XYZ doorCenter,
            XYZ doorBoundaryPoint,
            XYZ doorNormal,
            XYZ localRight,
            XYZ localBottom,
            double rotationAngle,
            XYZ finalCoreCenter,
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows,
            MaintenanceSpaceFitResult fit)
        {
            DiagnosticRecorder.AppendDebug(
                "[AhuLocalPlacement] Success. RoomKey=" +
                (room != null ? room.Key ?? string.Empty : string.Empty) +
                ", FamilyKey=" +
                (option != null ? option.Key ?? string.Empty : string.Empty) +
                ", ElementId=" +
                FormatElementId(instance != null
                    ? instance.Id
                    : ElementId.InvalidElementId) +
                ", DoorSide=" +
                (doorRule != null ? doorRule.Side ?? string.Empty : string.Empty) +
                ", DoorCenter=(" + FormatPoint(doorCenter) + ")" +
                ", DoorBoundary=(" + FormatPoint(doorBoundaryPoint) + ")" +
                ", DoorNormal=(" + FormatVector(doorNormal) + ")" +
                ", LocalRight=(" + FormatVector(localRight) + ")" +
                ", LocalBottom=(" + FormatVector(localBottom) + ")" +
                ", RotationDeg=" +
                (rotationAngle * 180.0 / Math.PI).ToString(
                    "F3",
                    CultureInfo.InvariantCulture) +
                ", FinalCoreCenter=(" +
                FormatPoint(finalCoreCenter) + ")" +
                ", WallRules=" +
                string.Join(
                    "|",
                    (maintenanceRows ??
                     Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                        .Where(x => x != null && x.IsWallSide)
                        .Select(x =>
                            (x.Side ?? string.Empty) +
                            ":" +
                            x.DimensionMm.ToString(
                                CultureInfo.InvariantCulture) +
                            "mm")) +
                ", Fit=" +
                FormatMaintenanceSpaceFitResult(fit));
        }

        private static bool TryOrientPlacedEquipmentTowardRoomDoor(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            ElementId instanceId,
            XYZ placementPoint,
            ElementId levelId,
            out string error,
            out MaintenanceSpaceFitResult maintenanceSpaceCheck)
        {
            error = string.Empty;
            maintenanceSpaceCheck = null;
            if (doc == null || room == null || instanceId == null || instanceId == ElementId.InvalidElementId || placementPoint == null)
            {
                error = "Invalid orientation context.";
                return false;
            }

            XYZ doorCenter;
            string doorSource;
            ElementId doorElementId;
            if (!TryResolveRoomDoorCenter(doc, room, placementPoint, levelId, out doorCenter, out doorSource, out doorElementId))
            {
                error = "Door target not found.";
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                error = "Placed family instance not found.";
                return false;
            }

            XYZ targetCenter = placementPoint;
            string orientationCoreCenterMode;
            XYZ initialEquipmentCenter = ResolveEquipmentCoreCenterForFinalPlacement(doc, instance, targetCenter, out orientationCoreCenterMode)
                                         ?? GetElementBoundingBoxCenter(instance)
                                         ?? ResolveRotationOrigin(instance, targetCenter)
                                         ?? targetCenter;

            XYZ doorVector = Flatten(doorCenter - targetCenter);
            if (!IsUsableDirection(doorVector))
            {
                doorVector = Flatten(doorCenter - initialEquipmentCenter);
            }

            if (!IsUsableDirection(doorVector))
            {
                error = "Door target vector is too short.";
                return false;
            }
            doorVector = doorVector.Normalize();

            RoomAxisInfo roomAxis;
            if (!TryResolveRoomAxes(room, out roomAxis))
            {
                error = "Room axis resolve failed.";
                return false;
            }

            DoorWallSideInfo doorWallSide;
            if (!TryResolveDoorWallSide(roomAxis, targetCenter, doorCenter, out doorWallSide))
            {
                error = "Door wall side resolve failed.";
                return false;
            }
            XYZ doorWallNormal = new XYZ(
                doorWallSide.Axis.Normalize().X * doorWallSide.Sign,
                doorWallSide.Axis.Normalize().Y * doorWallSide.Sign,
                0.0);
            if (!IsUsableDirection(doorWallNormal))
            {
                error = "Door wall normal resolve failed.";
                return false;
            }
            doorWallNormal = doorWallNormal.Normalize();

            XYZ equipmentLongAxis;
            XYZ equipmentShortAxis;
            string equipmentAxisMode;
            if (!TryResolveEquipmentLongAxis(instance, out equipmentLongAxis, out equipmentShortAxis, out equipmentAxisMode))
            {
                error = "Equipment long axis resolve failed.";
                return false;
            }
            equipmentLongAxis = equipmentLongAxis.Normalize();
            equipmentShortAxis = equipmentShortAxis.Normalize();

            XYZ serviceSideDirection;
            XYZ serviceSideReferencePoint;
            string serviceSideMode;
            if (!TryResolveEquipmentDoorSideReference(doc, instance, option, initialEquipmentCenter, out serviceSideDirection, out serviceSideReferencePoint, out serviceSideMode))
            {
                // Keep the placement usable even when connector/nested-service detection fails.
                // The long-axis candidates still align the AHU to the room; this only affects the 180-degree flip.
                serviceSideDirection = equipmentShortAxis;
                serviceSideReferencePoint = EstimateSideReferencePoint(instance, initialEquipmentCenter, serviceSideDirection);
                serviceSideMode = "Fallback-EquipmentShortAxis";
            }
            serviceSideDirection = serviceSideDirection.Normalize();
            if (serviceSideReferencePoint == null)
            {
                serviceSideReferencePoint = EstimateSideReferencePoint(instance, initialEquipmentCenter, serviceSideDirection);
            }

            List<AxisLockedOrientationCandidate> candidates = BuildAxisLockedOrientationCandidates(
                room,
                instance,
                initialEquipmentCenter,
                targetCenter,
                equipmentLongAxis,
                serviceSideDirection,
                serviceSideReferencePoint,
                roomAxis,
                doorWallNormal,
                doorCenter,
                doorWallSide);

            AxisLockedOrientationCandidate best = candidates
                .OrderByDescending(x => x != null ? x.Score : double.MinValue)
                .FirstOrDefault();

            if (best == null)
            {
                error = "No axis-locked orientation candidate found.";
                return false;
            }

            Transaction orientTx = null;
            try
            {
                orientTx = new Transaction(doc, "Orient Room Custom Family By Room Axis");
                orientTx.Start();

                bool rotated = false;
                bool moved = false;
                double rotationTolerance = 1e-6;
                double moveTolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

                if (Math.Abs(best.Angle) >= rotationTolerance)
                {
                    Line axis = Line.CreateBound(initialEquipmentCenter, initialEquipmentCenter + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, best.Angle);
                    rotated = true;
                    doc.Regenerate();
                }

                // Direction is decided by the axis-locked candidate above. After the final direction is set,
                // center the visible AHU equipment in the target room. Do not use LocationPoint here because
                // this AHU family has an insertion origin far away from its visual/geometric center.
                // Also avoid using the parent instance bounding box as the primary center because this AHU
                // family contains large transparent clearance/service-space geometry. The parent bounding box
                // can be centered while the visible AHU body is still off-center.
                string centerBeforeFinalMoveMode;
                XYZ centerBeforeFinalMove = ResolveEquipmentCoreCenterForFinalPlacement(doc, instance, initialEquipmentCenter, out centerBeforeFinalMoveMode)
                                            ?? GetElementBoundingBoxCenter(instance)
                                            ?? initialEquipmentCenter;
                XYZ moveDelta = ResolveHorizontalCenteringDelta(centerBeforeFinalMove, targetCenter);
                double moveDistance = moveDelta != null ? moveDelta.GetLength() : 0.0;
                if (moveDelta != null && moveDistance > moveTolerance)
                {
                    ElementTransformUtils.MoveElement(doc, instance.Id, moveDelta);
                    moved = true;
                    doc.Regenerate();
                }

                string finalEquipmentCenterMode;
                XYZ finalEquipmentCenter = ResolveEquipmentCoreCenterForFinalPlacement(doc, instance, centerBeforeFinalMove, out finalEquipmentCenterMode);
                XYZ finalBoxCenter = GetElementBoundingBoxCenter(instance);

                maintenanceSpaceCheck = CheckMaintenanceSpaceFit(doc, room, instance, targetCenter, roomAxis, doorWallSide);

                orientTx.Commit();

                DiagnosticRecorder.AppendDebug(
                    "[RoomCustomFamily] Axis-locked AHU orientation applied. RoomKey=" + (room.Key ?? string.Empty) +
                    ", ElementId=" + FormatElementId(instanceId) +
                    ", DoorSource=" + (doorSource ?? string.Empty) +
                    ", DoorElementId=" + FormatElementId(doorElementId) +
                    ", TargetCenter=(" + FormatPoint(targetCenter) + ")" +
                    ", DoorCenter=(" + FormatPoint(doorCenter) + ")" +
                    ", InitialEquipmentCenter=(" + FormatPoint(initialEquipmentCenter) + ")" +
                    ", CoreCenterMode=" + (orientationCoreCenterMode ?? string.Empty) +
                    ", CenterBeforeFinalMove=(" + FormatPoint(centerBeforeFinalMove) + ")" +
                    ", CenterBeforeFinalMoveMode=" + (centerBeforeFinalMoveMode ?? string.Empty) +
                    ", FinalEquipmentCenter=(" + FormatPoint(finalEquipmentCenter) + ")" +
                    ", FinalEquipmentCenterMode=" + (finalEquipmentCenterMode ?? string.Empty) +
                    ", FinalBoxCenter=(" + FormatPoint(finalBoxCenter) + ")" +
                    ", RoomLongAxis=(" + FormatVector(roomAxis.LongAxis) + ")" +
                    ", RoomShortAxis=(" + FormatVector(roomAxis.ShortAxis) + ")" +
                    ", RoomAxisSource=" + (roomAxis.Source ?? string.Empty) +
                    ", EquipmentLongAxis=(" + FormatVector(equipmentLongAxis) + ")" +
                    ", EquipmentShortAxis=(" + FormatVector(equipmentShortAxis) + ")" +
                    ", EquipmentAxisMode=" + (equipmentAxisMode ?? string.Empty) +
                    ", ServiceSideDirection=(" + FormatVector(serviceSideDirection) + ")" +
                    ", ServiceReferencePoint=(" + FormatPoint(serviceSideReferencePoint) + ")" +
                    ", ServiceSideMode=" + (serviceSideMode ?? string.Empty) +
                    ", DoorVector=(" + FormatVector(doorVector) + ")" +
                    ", DoorWallAxis=(" + FormatVector(doorWallSide.Axis) + ")" +
                    ", DoorWallSign=" + doorWallSide.Sign.ToString(CultureInfo.InvariantCulture) +
                    ", DoorWallNormal=(" + FormatVector(doorWallNormal) + ")" +
                    ", DoorWallMode=" + (doorWallSide.Mode ?? string.Empty) +
                    ", SelectedCandidate=" + (best.Name ?? string.Empty) +
                    ", CandidateTargetAxis=(" + FormatVector(best.TargetLongAxis) + ")" +
                    ", PredictedServiceSide=(" + FormatVector(best.PredictedServiceSide) + ")" +
                    ", PredictedServicePoint=(" + FormatPoint(best.PredictedServicePoint) + ")" +
                    ", ServiceDotToDoorWallNormal=" + best.ServiceDot.ToString("F3", CultureInfo.InvariantCulture) +
                    ", AngleToDoorDeg=" + best.ServiceAngleToDoorDeg.ToString("F3", CultureInfo.InvariantCulture) +
                    ", ServiceTowardDoorScore=" + best.ServiceTowardDoorScore.ToString("F3", CultureInfo.InvariantCulture) +
                    ", ServiceDoorWallProjectionMm=" + FormatMm(Math.Abs(best.ServiceDoorWallProjection)) +
                    ", ServiceOnDoorWallSide=" + (best.ServiceOnDoorWallSide ? "True" : "False") +
                    ", DoorWallSideScore=" + best.DoorWallSideScore.ToString("F3", CultureInfo.InvariantCulture) +
                    ", ServiceDistanceMm=" + FormatMm(best.ServiceDistance) +
                    ", OppositeServiceDistanceMm=" + FormatMm(best.OppositeServiceDistance) +
                    ", ServiceCloserDeltaMm=" + FormatMm(best.OppositeServiceDistance - best.ServiceDistance) +
                    ", FitScore=" + best.FitScore.ToString("F3", CultureInfo.InvariantCulture) +
                    ", AxisScore=" + best.AxisScore.ToString("F3", CultureInfo.InvariantCulture) +
                    ", Score=" + best.Score.ToString("F3", CultureInfo.InvariantCulture) +
                    ", AngleDeg=" + (best.Angle * 180.0 / Math.PI).ToString("F3", CultureInfo.InvariantCulture) +
                    ", Rotated=" + (rotated ? "True" : "False") +
                    ", MoveDelta=(" + FormatPoint(moveDelta) + ")" +
                    ", MoveDistanceMm=" + FormatMm(moveDistance) +
                    ", Moved=" + (moved ? "True" : "False") +
                    ", MaintenanceSpaceCheck=" + FormatMaintenanceSpaceFitResult(maintenanceSpaceCheck) +
                    ", Candidates=" + FormatAxisLockedCandidateSummary(candidates));

                if (maintenanceSpaceCheck != null && !maintenanceSpaceCheck.IsOk)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[RoomCustomFamily] Maintenance Space check failed. RoomKey=" + (room.Key ?? string.Empty) +
                        ", ElementId=" + FormatElementId(instanceId) +
                        ", " + FormatMaintenanceSpaceFitResult(maintenanceSpaceCheck));

                    // Do not show a modal TaskDialog here. The Room Detail equipment card displays
                    // the Maintenance Space fit warning inline based on the placement result.
                    // ShowMaintenanceSpaceFitWarning(room, option, maintenanceSpaceCheck);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (orientTx != null && orientTx.HasStarted())
                {
                    orientTx.RollBack();
                }

                error = ex.Message;
                return false;
            }
        }

        private static bool TryResolveRoomAxes(RoomSemanticRecord room, out RoomAxisInfo axis)
        {
            axis = null;
            if (room == null)
            {
                return false;
            }

            List<XYZ> points = room.LoopPoints != null ? room.LoopPoints.Where(x => x != null).ToList() : new List<XYZ>();
            double bestLength = 0.0;
            XYZ bestAxis = null;
            if (points.Count >= 2)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    XYZ a = points[i];
                    XYZ b = points[(i + 1) % points.Count];
                    XYZ v = Flatten(b - a);
                    if (!IsUsableDirection(v))
                    {
                        continue;
                    }

                    double length = v.GetLength();
                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestAxis = v.Normalize();
                    }
                }
            }

            if (!IsUsableDirection(bestAxis) && room.BBox != null && room.BBox.Min != null && room.BBox.Max != null)
            {
                double dx = Math.Abs(room.BBox.Max.X - room.BBox.Min.X);
                double dy = Math.Abs(room.BBox.Max.Y - room.BBox.Min.Y);
                bestAxis = dx >= dy ? XYZ.BasisX : XYZ.BasisY;
                bestLength = Math.Max(dx, dy);
            }

            if (!IsUsableDirection(bestAxis))
            {
                return false;
            }

            bestAxis = CanonicalizeAxis(bestAxis.Normalize());
            axis = new RoomAxisInfo
            {
                LongAxis = bestAxis,
                ShortAxis = PerpendicularXY(bestAxis),
                LongLength = bestLength,
                Source = points.Count >= 2 ? "LoopLongestSegment" : "BBoxLongestSide"
            };
            return true;
        }

        private static bool TryResolveDoorWallSide(
            RoomAxisInfo roomAxis,
            XYZ targetCenter,
            XYZ doorCenter,
            out DoorWallSideInfo side)
        {
            side = null;
            if (roomAxis == null || targetCenter == null || doorCenter == null ||
                !IsUsableDirection(roomAxis.LongAxis) || !IsUsableDirection(roomAxis.ShortAxis))
            {
                return false;
            }

            XYZ doorOffset = Flatten(doorCenter - targetCenter);
            if (!IsUsableDirection(doorOffset))
            {
                return false;
            }

            XYZ longAxis = roomAxis.LongAxis.Normalize();
            XYZ shortAxis = roomAxis.ShortAxis.Normalize();
            double longProjection = DotXY(doorOffset, longAxis);
            double shortProjection = DotXY(doorOffset, shortAxis);

            bool useShortAxis = Math.Abs(shortProjection) >= Math.Abs(longProjection);
            XYZ axis = useShortAxis ? shortAxis : longAxis;
            double projection = useShortAxis ? shortProjection : longProjection;
            int sign = projection >= 0.0 ? 1 : -1;

            side = new DoorWallSideInfo
            {
                Axis = axis,
                Sign = sign,
                Projection = projection,
                Mode = useShortAxis ? "DoorOnRoomShortSide" : "DoorOnRoomLongSide"
            };
            return true;
        }

        private static bool TryResolveEquipmentLongAxis(
            FamilyInstance instance,
            out XYZ longAxis,
            out XYZ shortAxis,
            out string mode)
        {
            longAxis = null;
            shortAxis = null;
            mode = string.Empty;
            if (instance == null)
            {
                return false;
            }

            XYZ hand = Flatten(instance.HandOrientation);
            XYZ facing = Flatten(instance.FacingOrientation);
            if (IsUsableDirection(hand))
            {
                hand = hand.Normalize();
            }
            if (IsUsableDirection(facing))
            {
                facing = facing.Normalize();
            }

            if (IsUsableDirection(hand) && IsUsableDirection(facing))
            {
                double handExtent = ComputeBoundingBoxExtentAlongAxis(instance, hand);
                double facingExtent = ComputeBoundingBoxExtentAlongAxis(instance, facing);
                if (handExtent >= facingExtent)
                {
                    longAxis = hand;
                    shortAxis = facing;
                    mode = "HandAxisByBBoxExtent(handMm=" + FormatMm(handExtent) + ", facingMm=" + FormatMm(facingExtent) + ")";
                }
                else
                {
                    longAxis = facing;
                    shortAxis = hand;
                    mode = "FacingAxisByBBoxExtent(handMm=" + FormatMm(handExtent) + ", facingMm=" + FormatMm(facingExtent) + ")";
                }

                return true;
            }

            if (IsUsableDirection(hand))
            {
                longAxis = hand.Normalize();
                shortAxis = PerpendicularXY(longAxis);
                mode = "HandAxisFallback";
                return true;
            }

            if (IsUsableDirection(facing))
            {
                longAxis = facing.Normalize();
                shortAxis = PerpendicularXY(longAxis);
                mode = "FacingAxisFallback";
                return true;
            }

            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            if (box != null && box.Min != null && box.Max != null)
            {
                double dx = Math.Abs(box.Max.X - box.Min.X);
                double dy = Math.Abs(box.Max.Y - box.Min.Y);
                longAxis = dx >= dy ? XYZ.BasisX : XYZ.BasisY;
                shortAxis = PerpendicularXY(longAxis);
                mode = "WorldBBoxAxisFallback";
                return true;
            }

            return false;
        }

        private static double ComputeBoundingBoxExtentAlongAxis(Element element, XYZ axis)
        {
            if (element == null || !IsUsableDirection(axis))
            {
                return 0.0;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return 0.0;
            }

            List<XYZ> corners = GetBoundingBoxXyCorners(box);
            if (corners.Count == 0)
            {
                return 0.0;
            }

            XYZ normalized = axis.Normalize();
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (XYZ corner in corners)
            {
                double projection = DotXY(corner, normalized);
                min = Math.Min(min, projection);
                max = Math.Max(max, projection);
            }

            return Math.Max(0.0, max - min);
        }

        private static List<AxisLockedOrientationCandidate> BuildAxisLockedOrientationCandidates(
            RoomSemanticRecord room,
            FamilyInstance instance,
            XYZ initialEquipmentCenter,
            XYZ targetCenter,
            XYZ equipmentLongAxis,
            XYZ serviceSideDirection,
            XYZ serviceSideReferencePoint,
            RoomAxisInfo roomAxis,
            XYZ doorWallNormal,
            XYZ doorCenter,
            DoorWallSideInfo doorWallSide)
        {
            List<AxisLockedOrientationCandidate> candidates = new List<AxisLockedOrientationCandidate>();
            if (room == null || instance == null || initialEquipmentCenter == null || targetCenter == null ||
                !IsUsableDirection(equipmentLongAxis) || !IsUsableDirection(serviceSideDirection) ||
                serviceSideReferencePoint == null ||
                roomAxis == null || !IsUsableDirection(roomAxis.LongAxis) || !IsUsableDirection(roomAxis.ShortAxis) ||
                !IsUsableDirection(doorWallNormal) || doorCenter == null ||
                doorWallSide == null || !IsUsableDirection(doorWallSide.Axis) || doorWallSide.Sign == 0)
            {
                return candidates;
            }

            // Main behavior: keep the AHU rectangular body locked to the room long axis.
            // Choose the 0/180 degree flip by the door-wall normal, not by raw distance to
            // the door center. Distance is kept only as a secondary tie-breaker.
            AddAxisLockedOrientationCandidate(candidates, room, instance, initialEquipmentCenter, targetCenter,
                equipmentLongAxis, serviceSideDirection, serviceSideReferencePoint, roomAxis.LongAxis, doorWallNormal, doorCenter, doorWallSide, "RoomLong-Forward", 300.0);
            AddAxisLockedOrientationCandidate(candidates, room, instance, initialEquipmentCenter, targetCenter,
                equipmentLongAxis, serviceSideDirection, serviceSideReferencePoint, NegateXY(roomAxis.LongAxis), doorWallNormal, doorCenter, doorWallSide, "RoomLong-Reverse", 300.0);

            // Keep short-axis candidates only as low-priority emergency fallbacks.
            AddAxisLockedOrientationCandidate(candidates, room, instance, initialEquipmentCenter, targetCenter,
                equipmentLongAxis, serviceSideDirection, serviceSideReferencePoint, roomAxis.ShortAxis, doorWallNormal, doorCenter, doorWallSide, "RoomShort-Forward", -900.0);
            AddAxisLockedOrientationCandidate(candidates, room, instance, initialEquipmentCenter, targetCenter,
                equipmentLongAxis, serviceSideDirection, serviceSideReferencePoint, NegateXY(roomAxis.ShortAxis), doorWallNormal, doorCenter, doorWallSide, "RoomShort-Reverse", -900.0);

            return candidates;
        }

        private static void AddAxisLockedOrientationCandidate(
            List<AxisLockedOrientationCandidate> candidates,
            RoomSemanticRecord room,
            FamilyInstance instance,
            XYZ initialEquipmentCenter,
            XYZ targetCenter,
            XYZ equipmentLongAxis,
            XYZ serviceSideDirection,
            XYZ serviceSideReferencePoint,
            XYZ targetLongAxis,
            XYZ doorWallNormal,
            XYZ doorCenter,
            DoorWallSideInfo doorWallSide,
            string name,
            double axisScore)
        {
            if (candidates == null || !IsUsableDirection(equipmentLongAxis) || !IsUsableDirection(serviceSideDirection) ||
                serviceSideReferencePoint == null || !IsUsableDirection(targetLongAxis) ||
                !IsUsableDirection(doorWallNormal) || doorCenter == null ||
                doorWallSide == null || !IsUsableDirection(doorWallSide.Axis) || doorWallSide.Sign == 0)
            {
                return;
            }

            XYZ normalizedTarget = targetLongAxis.Normalize();
            double angle = SignedAngleOnXY(equipmentLongAxis.Normalize(), normalizedTarget);
            XYZ predictedServiceSide = RotateVectorOnXY(serviceSideDirection.Normalize(), angle);
            double serviceDot = DotXY(predictedServiceSide.Normalize(), doorWallNormal.Normalize());
            XYZ moveDelta = ResolveHorizontalCenteringDelta(initialEquipmentCenter, targetCenter) ?? new XYZ(0.0, 0.0, 0.0);
            XYZ predictedServicePoint = RotatePointAroundCenterOnXY(serviceSideReferencePoint, initialEquipmentCenter, angle) + moveDelta;
            XYZ predictedOppositeServicePoint = targetCenter - Flatten(predictedServicePoint - targetCenter);
            double serviceDistance = HorizontalDistance(predictedServicePoint, doorCenter);
            double oppositeServiceDistance = HorizontalDistance(predictedOppositeServicePoint, doorCenter);
            double roomScale = ResolveRoomScale(room);
            double serviceCloserDelta = oppositeServiceDistance - serviceDistance;
            double serviceDistanceScore = Math.Max(-50.0, Math.Min(50.0, serviceCloserDelta / Math.Max(roomScale, 1e-6) * 50.0));
            double doorDistanceScore = Math.Max(0.0, (roomScale - serviceDistance) / Math.Max(roomScale, 1e-6) * 20.0);
            double serviceDoorWallProjection = DotXY(Flatten(predictedServicePoint - targetCenter), doorWallSide.Axis.Normalize());
            bool serviceOnDoorWallSide = Math.Sign(serviceDoorWallProjection) == doorWallSide.Sign;
            double doorWallSideScore = serviceOnDoorWallSide ? 600.0 : -600.0;
            double sideDepthScore = Math.Min(120.0, Math.Abs(serviceDoorWallProjection) / Math.Max(roomScale, 1e-6) * 240.0);
            if (!serviceOnDoorWallSide)
            {
                sideDepthScore = -sideDepthScore;
            }

            double fitScore = EvaluateAxisLockedCandidateFit(room, instance, initialEquipmentCenter, targetCenter, angle);
            double serviceTowardDoorScore = serviceDot * 900.0;
            double serviceAngleToDoorDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, serviceDot))) * 180.0 / Math.PI;
            double score = serviceTowardDoorScore + axisScore + doorWallSideScore + sideDepthScore + serviceDistanceScore + doorDistanceScore + fitScore;

            candidates.Add(new AxisLockedOrientationCandidate
            {
                Name = name,
                TargetLongAxis = normalizedTarget,
                Angle = angle,
                PredictedServiceSide = predictedServiceSide.Normalize(),
                PredictedServicePoint = predictedServicePoint,
                ServiceDot = serviceDot,
                ServiceDoorWallProjection = serviceDoorWallProjection,
                ServiceOnDoorWallSide = serviceOnDoorWallSide,
                DoorWallSideScore = doorWallSideScore,
                ServiceDistance = serviceDistance,
                OppositeServiceDistance = oppositeServiceDistance,
                ServiceScore = serviceDistanceScore + doorDistanceScore,
                ServiceTowardDoorScore = serviceTowardDoorScore,
                ServiceAngleToDoorDeg = serviceAngleToDoorDeg,
                AxisScore = axisScore,
                FitScore = fitScore,
                Score = score
            });
        }

        private static double ResolveRoomScale(RoomSemanticRecord room)
        {
            if (room != null && room.BBox != null && room.BBox.Min != null && room.BBox.Max != null)
            {
                double dx = Math.Abs(room.BBox.Max.X - room.BBox.Min.X);
                double dy = Math.Abs(room.BBox.Max.Y - room.BBox.Min.Y);
                double diagonal = Math.Sqrt(dx * dx + dy * dy);
                if (diagonal > 1e-6)
                {
                    return diagonal;
                }
            }

            if (room != null && room.LoopPoints != null)
            {
                List<XYZ> points = room.LoopPoints.Where(x => x != null).ToList();
                if (points.Count >= 2)
                {
                    double minX = points.Min(x => x.X);
                    double maxX = points.Max(x => x.X);
                    double minY = points.Min(x => x.Y);
                    double maxY = points.Max(x => x.Y);
                    double dx = Math.Abs(maxX - minX);
                    double dy = Math.Abs(maxY - minY);
                    double diagonal = Math.Sqrt(dx * dx + dy * dy);
                    if (diagonal > 1e-6)
                    {
                        return diagonal;
                    }
                }
            }

            return UnitUtils.ConvertToInternalUnits(5000.0, UnitTypeId.Millimeters);
        }

        private static double EvaluateAxisLockedCandidateFit(
            RoomSemanticRecord room,
            FamilyInstance instance,
            XYZ initialEquipmentCenter,
            XYZ targetCenter,
            double angle)
        {
            if (room == null || instance == null || initialEquipmentCenter == null || targetCenter == null)
            {
                return 0.0;
            }

            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return 0.0;
            }

            List<XYZ> corners = GetBoundingBoxXyCorners(box);
            if (corners.Count == 0)
            {
                return 0.0;
            }

            int insideCount = 0;
            double nearBoundaryBonus = 0.0;
            double nearTolerance = UnitUtils.ConvertToInternalUnits(600.0, UnitTypeId.Millimeters);
            XYZ moveDelta = ResolveHorizontalCenteringDelta(initialEquipmentCenter, targetCenter) ?? new XYZ(0.0, 0.0, 0.0);

            foreach (XYZ corner in corners)
            {
                XYZ rotated = RotatePointAroundCenterOnXY(corner, initialEquipmentCenter, angle) + moveDelta;
                if (IsPointInsideLoop(room.LoopPoints, rotated))
                {
                    insideCount++;
                    nearBoundaryBonus += 1.0;
                    continue;
                }

                double boundaryDistance = DistanceToRoomBoundary(room, rotated);
                if (boundaryDistance <= nearTolerance)
                {
                    nearBoundaryBonus += 0.35;
                }
            }

            double insideRatio = (double)insideCount / Math.Max(1, corners.Count);
            return insideRatio * 15.0 + nearBoundaryBonus * 2.0;
        }

        private static List<XYZ> GetBoundingBoxXyCorners(BoundingBoxXYZ box)
        {
            List<XYZ> corners = new List<XYZ>();
            if (box == null || box.Min == null || box.Max == null ||
                !IsUsablePlanPoint(box.Min) || !IsUsablePlanPoint(box.Max))
            {
                return corners;
            }

            double z = (box.Min.Z + box.Max.Z) * 0.5;
            AddUniquePointXY(corners, new XYZ(box.Min.X, box.Min.Y, z));
            AddUniquePointXY(corners, new XYZ(box.Min.X, box.Max.Y, z));
            AddUniquePointXY(corners, new XYZ(box.Max.X, box.Max.Y, z));
            AddUniquePointXY(corners, new XYZ(box.Max.X, box.Min.Y, z));
            return corners;
        }

        private static XYZ RotatePointAroundCenterOnXY(XYZ point, XYZ center, double angle)
        {
            if (point == null || center == null)
            {
                return point;
            }

            XYZ relative = point - center;
            XYZ rotated = RotateVectorOnXY(relative, angle);
            return center + rotated;
        }

        private static XYZ RotateVectorOnXY(XYZ vector, double angle)
        {
            if (vector == null)
            {
                return null;
            }

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return new XYZ(
                vector.X * cos - vector.Y * sin,
                vector.X * sin + vector.Y * cos,
                0.0);
        }

        private static XYZ PerpendicularXY(XYZ axis)
        {
            if (!IsUsableDirection(axis))
            {
                return null;
            }

            XYZ normalized = axis.Normalize();
            return new XYZ(-normalized.Y, normalized.X, 0.0).Normalize();
        }

        private static XYZ CanonicalizeAxis(XYZ axis)
        {
            if (!IsUsableDirection(axis))
            {
                return axis;
            }

            XYZ normalized = axis.Normalize();
            if (normalized.X < -1e-9 || (Math.Abs(normalized.X) <= 1e-9 && normalized.Y < 0.0))
            {
                return NegateXY(normalized).Normalize();
            }

            return normalized;
        }

        private static double DotXY(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }

            return a.X * b.X + a.Y * b.Y;
        }

        private static string FormatAxisLockedCandidateSummary(IEnumerable<AxisLockedOrientationCandidate> candidates)
        {
            if (candidates == null)
            {
                return string.Empty;
            }

            return string.Join(" | ", candidates.Select(x =>
                (x != null ? x.Name ?? string.Empty : string.Empty) +
                ":score=" + (x != null ? x.Score.ToString("F1", CultureInfo.InvariantCulture) : string.Empty) +
                ",svcMm=" + (x != null ? FormatMm(x.ServiceDistance) : string.Empty) +
                ",oppMm=" + (x != null ? FormatMm(x.OppositeServiceDistance) : string.Empty) +
                ",dotNormal=" + (x != null ? x.ServiceDot.ToString("F2", CultureInfo.InvariantCulture) : string.Empty) +
                ",angDeg=" + (x != null ? x.ServiceAngleToDoorDeg.ToString("F1", CultureInfo.InvariantCulture) : string.Empty) +
                ",wall=" + (x != null ? (x.ServiceOnDoorWallSide ? "Y" : "N") : string.Empty) +
                ",wallProjMm=" + (x != null ? FormatMm(Math.Abs(x.ServiceDoorWallProjection)) : string.Empty) +
                ",fit=" + (x != null ? x.FitScore.ToString("F1", CultureInfo.InvariantCulture) : string.Empty) +
                ",angle=" + (x != null ? (x.Angle * 180.0 / Math.PI).ToString("F1", CultureInfo.InvariantCulture) : string.Empty)));
        }

        private static XYZ ResolveHorizontalCenteringDelta(XYZ currentCenter, XYZ targetCenter)
        {
            if (currentCenter == null || targetCenter == null)
            {
                return null;
            }

            return new XYZ(targetCenter.X - currentCenter.X, targetCenter.Y - currentCenter.Y, 0.0);
        }

        private static XYZ EstimateSideReferencePoint(Element element, XYZ center, XYZ direction)
        {
            if (center == null || !IsUsableDirection(direction))
            {
                return center;
            }

            XYZ normalized = direction.Normalize();
            double halfExtent = 0.0;
            if (element != null)
            {
                halfExtent = ComputeBoundingBoxExtentAlongAxis(element, normalized) * 0.5;
            }

            if (halfExtent <= 1e-6)
            {
                halfExtent = UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);
            }

            return center + normalized * halfExtent;
        }


        private static void ApplyMaintenanceSpaceFitResult(
            PlacementResult result,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            MaintenanceSpaceFitResult check)
        {
            if (result == null)
            {
                return;
            }

            if (check == null)
            {
                result.MaintenanceSpaceFitStatus = string.Empty;
                result.MaintenanceSpaceFitWarningMessage = string.Empty;
                result.MaintenanceSpaceFitPassed = true;
                return;
            }

            result.MaintenanceSpaceFitStatus = check.Status ?? string.Empty;
            result.MaintenanceSpaceFitPassed = check.IsOk;
            result.MaintenanceSpaceFitWarningMessage = check.IsOk
                ? string.Empty
                : BuildMaintenanceSpaceFitInlineWarning(room, option, check);
        }

        private static string BuildMaintenanceSpaceFitInlineWarning(
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            MaintenanceSpaceFitResult check)
        {
            if (check == null || check.IsOk)
            {
                return string.Empty;
            }

            string side = string.IsNullOrWhiteSpace(check.FailedSide)
                ? string.Empty
                : " Side: " + check.FailedSide + ".";

            if (string.Equals(check.Status, "Exceeded", StringComparison.OrdinalIgnoreCase))
            {
                return "Maintenance Space exceeds the selected room boundary." + side;
            }

            if (string.Equals(check.Status, "TouchWall", StringComparison.OrdinalIgnoreCase))
            {
                return "Maintenance Space touches the selected room boundary." + side;
            }

            return "Maintenance Space does not fit in the selected room." + side;
        }

        private static MaintenanceSpaceFitResult CheckMaintenanceSpaceFit(
            Document doc,
            RoomSemanticRecord room,
            FamilyInstance instance,
            XYZ roomCenter,
            RoomAxisInfo roomAxis,
            DoorWallSideInfo doorWallSide)
        {
            MaintenanceSpaceFitResult result = new MaintenanceSpaceFitResult
            {
                Status = "NotChecked",
                Mode = string.Empty,
                OutsideToleranceMm = 20.0,
                TouchToleranceMm = 50.0
            };

            if (doc == null || room == null || instance == null || roomCenter == null)
            {
                result.Status = "Skipped";
                result.Mode = "InvalidContext";
                return result;
            }

            double outsideTolerance = UnitUtils.ConvertToInternalUnits(result.OutsideToleranceMm, UnitTypeId.Millimeters);
            double touchTolerance = UnitUtils.ConvertToInternalUnits(result.TouchToleranceMm, UnitTypeId.Millimeters);

            List<MaintenanceSpaceFootprint> footprints = CollectMaintenanceSpaceFootprints(doc, instance, out string collectMode);
            result.Mode = collectMode ?? string.Empty;
            result.SolidCount = footprints != null ? footprints.Count : 0;

            if (footprints == null || footprints.Count == 0)
            {
                result.Status = "Skipped";
                result.Mode = string.IsNullOrWhiteSpace(result.Mode) ? "MaintenanceSpaceNotFound" : result.Mode;
                return result;
            }

            double minBoundaryDistance = double.MaxValue;
            double maxOutsideDistance = 0.0;
            int outsideCount = 0;
            int touchCount = 0;
            int checkPointCount = 0;
            List<string> failedSides = new List<string>();

            foreach (MaintenanceSpaceFootprint footprint in footprints)
            {
                if (footprint == null || footprint.HullPoints == null || footprint.HullPoints.Count < 3)
                {
                    continue;
                }

                List<XYZ> samplePoints = BuildFootprintSamplePoints(footprint.HullPoints);
                foreach (XYZ sample in samplePoints)
                {
                    if (sample == null)
                    {
                        continue;
                    }

                    checkPointCount++;
                    double boundaryDistance = DistanceToRoomBoundary(room, sample);
                    if (!double.IsNaN(boundaryDistance) && !double.IsInfinity(boundaryDistance) && boundaryDistance != double.MaxValue)
                    {
                        minBoundaryDistance = Math.Min(minBoundaryDistance, boundaryDistance);
                    }

                    bool inside = IsPointInsideLoop(room.LoopPoints, sample);
                    if (!inside)
                    {
                        if (boundaryDistance > outsideTolerance)
                        {
                            outsideCount++;
                            maxOutsideDistance = Math.Max(maxOutsideDistance, boundaryDistance);
                            AddUniqueText(failedSides, ResolveMaintenanceSpaceFailedSide(sample, roomCenter, roomAxis, doorWallSide));
                        }
                        else
                        {
                            touchCount++;
                            AddUniqueText(failedSides, ResolveMaintenanceSpaceFailedSide(sample, roomCenter, roomAxis, doorWallSide));
                        }
                        continue;
                    }

                    if (boundaryDistance <= touchTolerance)
                    {
                        touchCount++;
                        AddUniqueText(failedSides, ResolveMaintenanceSpaceFailedSide(sample, roomCenter, roomAxis, doorWallSide));
                    }
                }
            }

            result.CheckPointCount = checkPointCount;
            result.OutsidePointCount = outsideCount;
            result.TouchPointCount = touchCount;
            result.MinBoundaryDistance = minBoundaryDistance == double.MaxValue ? 0.0 : minBoundaryDistance;
            result.MaxOverflowDistance = maxOutsideDistance;
            result.FailedSide = failedSides.Count > 0 ? string.Join("/", failedSides) : string.Empty;

            if (outsideCount > 0)
            {
                result.Status = "Exceeded";
            }
            else if (touchCount > 0)
            {
                result.Status = "TouchWall";
            }
            else
            {
                result.Status = "OK";
            }

            return result;
        }

        private static List<MaintenanceSpaceFootprint> CollectMaintenanceSpaceFootprints(
            Document doc,
            FamilyInstance instance,
            out string mode)
        {
            mode = string.Empty;
            List<MaintenanceSpaceFootprint> footprints = new List<MaintenanceSpaceFootprint>();
            if (doc == null || instance == null)
            {
                mode = "InvalidContext";
                return footprints;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = null;
            try
            {
                geometry = instance.get_Geometry(options);
            }
            catch
            {
                geometry = null;
            }

            if (geometry == null)
            {
                mode = "NoGeometry";
                return footprints;
            }

            MaintenanceSpaceCollectionStats stats = new MaintenanceSpaceCollectionStats();
            CollectMaintenanceSpaceFootprints(doc, geometry, footprints, 0, stats);

            if (footprints.Count > 0)
            {
                mode = "MaintenanceSpaceGeometry(named=" + stats.NamedCount.ToString(CultureInfo.InvariantCulture) +
                       ", material=" + stats.MaterialCount.ToString(CultureInfo.InvariantCulture) +
                       ", transparent=" + stats.TransparentFallbackCount.ToString(CultureInfo.InvariantCulture) +
                       ", totalSolids=" + stats.TotalSolidCount.ToString(CultureInfo.InvariantCulture) + ")";
            }
            else
            {
                mode = "MaintenanceSpaceNotFound(totalSolids=" + stats.TotalSolidCount.ToString(CultureInfo.InvariantCulture) +
                       ", transparentSkipped=" + stats.TransparentFallbackCount.ToString(CultureInfo.InvariantCulture) + ")";
            }

            return footprints;
        }

        private static void CollectMaintenanceSpaceFootprints(
            Document doc,
            GeometryElement geometry,
            List<MaintenanceSpaceFootprint> footprints,
            int depth,
            MaintenanceSpaceCollectionStats stats)
        {
            if (doc == null || geometry == null || footprints == null || stats == null || depth > 8)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                {
                    continue;
                }

                Solid solid = geometryObject as Solid;
                if (solid != null)
                {
                    stats.TotalSolidCount++;
                    MaintenanceSpaceSolidKind kind;
                    string reason;
                    if (IsMaintenanceSpaceSolid(doc, geometryObject, solid, out kind, out reason))
                    {
                        MaintenanceSpaceFootprint footprint = BuildMaintenanceSpaceFootprint(solid, reason);
                        if (footprint != null && footprint.HullPoints != null && footprint.HullPoints.Count >= 3)
                        {
                            footprints.Add(footprint);
                            if (kind == MaintenanceSpaceSolidKind.NamedSubcategory)
                            {
                                stats.NamedCount++;
                            }
                            else if (kind == MaintenanceSpaceSolidKind.MaterialName)
                            {
                                stats.MaterialCount++;
                            }
                            else if (kind == MaintenanceSpaceSolidKind.TransparentFallback)
                            {
                                stats.TransparentFallbackCount++;
                            }
                        }
                    }
                    continue;
                }

                GeometryInstance geometryInstance = geometryObject as GeometryInstance;
                if (geometryInstance != null)
                {
                    GeometryElement nested = null;
                    try
                    {
                        nested = geometryInstance.GetInstanceGeometry();
                    }
                    catch
                    {
                        nested = null;
                    }

                    if (nested != null)
                    {
                        CollectMaintenanceSpaceFootprints(doc, nested, footprints, depth + 1, stats);
                    }
                }
            }
        }

        private static bool IsMaintenanceSpaceSolid(
            Document doc,
            GeometryObject geometryObject,
            Solid solid,
            out MaintenanceSpaceSolidKind kind,
            out string reason)
        {
            kind = MaintenanceSpaceSolidKind.None;
            reason = string.Empty;
            if (doc == null || solid == null || solid.Faces == null || solid.Faces.Size == 0)
            {
                return false;
            }

            if (IsTinySolid(solid))
            {
                return false;
            }

            string subcategoryName = ResolveGeometrySubcategoryName(doc, geometryObject);
            if (ContainsMaintenanceSpaceToken(subcategoryName))
            {
                kind = MaintenanceSpaceSolidKind.NamedSubcategory;
                reason = "Subcategory=" + subcategoryName;
                return true;
            }

            foreach (Face face in solid.Faces)
            {
                if (face == null)
                {
                    continue;
                }

                ElementId materialId = ElementId.InvalidElementId;
                try
                {
                    materialId = face.MaterialElementId;
                }
                catch
                {
                    materialId = ElementId.InvalidElementId;
                }

                if (materialId == null || materialId == ElementId.InvalidElementId)
                {
                    continue;
                }

                Material material = doc.GetElement(materialId) as Material;
                if (material == null)
                {
                    continue;
                }

                if (ContainsMaintenanceSpaceToken(material.Name))
                {
                    kind = MaintenanceSpaceSolidKind.MaterialName;
                    reason = "Material=" + (material.Name ?? string.Empty);
                    return true;
                }
            }

            if (IsMostlyTransparentSolid(doc, solid, 70) && LooksLikeMaintenanceSpaceBySize(solid))
            {
                kind = MaintenanceSpaceSolidKind.TransparentFallback;
                reason = "TransparentFallback";
                return true;
            }

            return false;
        }

        private static string ResolveGeometrySubcategoryName(Document doc, GeometryObject geometryObject)
        {
            if (doc == null || geometryObject == null)
            {
                return string.Empty;
            }

            ElementId graphicsStyleId = ElementId.InvalidElementId;
            try
            {
                graphicsStyleId = geometryObject.GraphicsStyleId;
            }
            catch
            {
                graphicsStyleId = ElementId.InvalidElementId;
            }

            if (graphicsStyleId == null || graphicsStyleId == ElementId.InvalidElementId)
            {
                return string.Empty;
            }

            GraphicsStyle graphicsStyle = doc.GetElement(graphicsStyleId) as GraphicsStyle;
            if (graphicsStyle == null || graphicsStyle.GraphicsStyleCategory == null)
            {
                return string.Empty;
            }

            return graphicsStyle.GraphicsStyleCategory.Name ?? string.Empty;
        }

        private static bool ContainsMaintenanceSpaceToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim().ToLowerInvariant();
            return value.Contains("maintenance space") ||
                   value.Contains("maintenance_space") ||
                   value.Contains("maintenance-space") ||
                   value.Contains("clearance") ||
                   value.Contains("service space") ||
                   value.Contains("working space") ||
                   value.Contains("access space");
        }

        private static bool IsTinySolid(Solid solid)
        {
            if (solid == null)
            {
                return true;
            }

            double volume = 0.0;
            try
            {
                volume = Math.Abs(solid.Volume);
            }
            catch
            {
                volume = 0.0;
            }

            double tinyVolume = Math.Pow(UnitUtils.ConvertToInternalUnits(20.0, UnitTypeId.Millimeters), 3.0);
            if (volume > 0.0 && volume <= tinyVolume)
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeMaintenanceSpaceBySize(Solid solid)
        {
            if (solid == null)
            {
                return false;
            }

            BoundingBoxXYZ box = null;
            try
            {
                box = solid.GetBoundingBox();
            }
            catch
            {
                box = null;
            }

            if (box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);
            double minPlanSize = UnitUtils.ConvertToInternalUnits(250.0, UnitTypeId.Millimeters);
            double minHeight = UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters);
            return Math.Max(dx, dy) >= minPlanSize && dz >= minHeight;
        }

        private static MaintenanceSpaceFootprint BuildMaintenanceSpaceFootprint(Solid solid, string source)
        {
            if (solid == null)
            {
                return null;
            }

            List<XYZ> points = ExtractSolidXyPoints(solid);
            if (points.Count < 3)
            {
                BoundingBoxXYZ box = null;
                try
                {
                    box = solid.GetBoundingBox();
                }
                catch
                {
                    box = null;
                }

                points = GetBoundingBoxXyCorners(box);
            }

            List<XYZ> hull = ComputeConvexHullXY(points);
            if (hull == null || hull.Count < 3)
            {
                return null;
            }

            return new MaintenanceSpaceFootprint
            {
                Source = source ?? string.Empty,
                HullPoints = hull
            };
        }

        private static List<XYZ> ExtractSolidXyPoints(Solid solid)
        {
            List<XYZ> points = new List<XYZ>();
            if (solid == null)
            {
                return points;
            }

            try
            {
                foreach (Edge edge in solid.Edges)
                {
                    if (edge == null)
                    {
                        continue;
                    }

                    Curve curve = null;
                    try
                    {
                        curve = edge.AsCurve();
                    }
                    catch
                    {
                        curve = null;
                    }

                    if (curve == null)
                    {
                        continue;
                    }

                    AddUniquePointXY(points, curve.GetEndPoint(0));
                    AddUniquePointXY(points, curve.GetEndPoint(1));
                }
            }
            catch
            {
                // Fall back to face triangulation below.
            }

            if (points.Count >= 3)
            {
                return points;
            }

            try
            {
                foreach (Face face in solid.Faces)
                {
                    if (face == null)
                    {
                        continue;
                    }

                    Mesh mesh = null;
                    try
                    {
                        mesh = face.Triangulate();
                    }
                    catch
                    {
                        mesh = null;
                    }

                    if (mesh == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle triangle = null;
                        try
                        {
                            triangle = mesh.get_Triangle(i);
                        }
                        catch
                        {
                            triangle = null;
                        }

                        if (triangle == null)
                        {
                            continue;
                        }

                        AddUniquePointXY(points, triangle.get_Vertex(0));
                        AddUniquePointXY(points, triangle.get_Vertex(1));
                        AddUniquePointXY(points, triangle.get_Vertex(2));
                    }
                }
            }
            catch
            {
                // Ignore; caller can fallback to bbox.
            }

            return points;
        }

        private static List<XYZ> ComputeConvexHullXY(List<XYZ> points)
        {
            List<XYZ> unique = new List<XYZ>();
            if (points == null)
            {
                return unique;
            }

            foreach (XYZ point in points)
            {
                AddUniquePointXY(unique, point);
            }

            if (unique.Count <= 1)
            {
                return unique;
            }

            unique = unique
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();

            List<XYZ> lower = new List<XYZ>();
            foreach (XYZ point in unique)
            {
                while (lower.Count >= 2 && CrossXY(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 1e-9)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(point);
            }

            List<XYZ> upper = new List<XYZ>();
            for (int i = unique.Count - 1; i >= 0; i--)
            {
                XYZ point = unique[i];
                while (upper.Count >= 2 && CrossXY(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 1e-9)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(point);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double CrossXY(XYZ origin, XYZ a, XYZ b)
        {
            if (origin == null || a == null || b == null)
            {
                return 0.0;
            }

            return (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
        }

        private static bool IsUsablePlanPoint(XYZ point)
        {
            if (point == null ||
                double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                double.IsNaN(point.Y) || double.IsInfinity(point.Y) ||
                double.IsNaN(point.Z) || double.IsInfinity(point.Z))
            {
                return false;
            }

            // Normal Revit project coordinates are many orders of magnitude below
            // this limit.  The guard intentionally only rejects impossible/sentinel
            // geometry while remaining safe for large shared-coordinate projects.
            const double maxAbsoluteCoordinateFeet = 10000000.0;
            return Math.Abs(point.X) <= maxAbsoluteCoordinateFeet &&
                   Math.Abs(point.Y) <= maxAbsoluteCoordinateFeet &&
                   Math.Abs(point.Z) <= maxAbsoluteCoordinateFeet;
        }

        private static bool IsReasonablePlacementDistance(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < -1e-8)
            {
                return false;
            }

            // One kilometre is deliberately far beyond any AHU room-placement
            // distance, while still preventing sentinel geometry from becoming a
            // gigantic MoveElement translation.
            double maxDistance = UnitUtils.ConvertToInternalUnits(1000000.0, UnitTypeId.Millimeters);
            return value <= maxDistance;
        }

        private static bool IsReasonablePlacementSignedDistance(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            double maxDistance = UnitUtils.ConvertToInternalUnits(1000000.0, UnitTypeId.Millimeters);
            return Math.Abs(value) <= maxDistance;
        }

        private static void AddUniquePointXY(List<XYZ> points, XYZ point)
        {
            if (points == null || !IsUsablePlanPoint(point))
            {
                return;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
            foreach (XYZ existing in points)
            {
                if (existing != null && HorizontalDistance(existing, point) <= tolerance)
                {
                    return;
                }
            }

            points.Add(point);
        }

        private static List<XYZ> BuildFootprintSamplePoints(IList<XYZ> hullPoints)
        {
            List<XYZ> samples = new List<XYZ>();
            if (hullPoints == null || hullPoints.Count == 0)
            {
                return samples;
            }

            double[] fractions = new[] { 0.0, 0.25, 0.5, 0.75 };
            for (int i = 0; i < hullPoints.Count; i++)
            {
                XYZ a = hullPoints[i];
                XYZ b = hullPoints[(i + 1) % hullPoints.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                foreach (double fraction in fractions)
                {
                    XYZ point = a + (b - a) * fraction;
                    AddUniquePointXY(samples, point);
                }
            }

            return samples;
        }

        private static string ResolveMaintenanceSpaceFailedSide(
            XYZ point,
            XYZ roomCenter,
            RoomAxisInfo roomAxis,
            DoorWallSideInfo doorWallSide)
        {
            if (point == null || roomCenter == null)
            {
                return "UnknownSide";
            }

            XYZ delta = Flatten(point - roomCenter);
            if (doorWallSide != null && IsUsableDirection(doorWallSide.Axis) && doorWallSide.Sign != 0)
            {
                double doorProjection = DotXY(delta, doorWallSide.Axis.Normalize()) * doorWallSide.Sign;
                if (doorProjection > 0.0)
                {
                    return "DoorSide";
                }

                return "OppositeDoorSide";
            }

            if (roomAxis != null && IsUsableDirection(roomAxis.LongAxis) && IsUsableDirection(roomAxis.ShortAxis))
            {
                double longProjection = DotXY(delta, roomAxis.LongAxis.Normalize());
                double shortProjection = DotXY(delta, roomAxis.ShortAxis.Normalize());
                if (Math.Abs(longProjection) >= Math.Abs(shortProjection))
                {
                    return longProjection >= 0.0 ? "RoomLongAxis+" : "RoomLongAxis-";
                }

                return shortProjection >= 0.0 ? "RoomShortAxis+" : "RoomShortAxis-";
            }

            return "UnknownSide";
        }

        private static void AddUniqueText(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!values.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(value);
            }
        }

        private static string FormatMaintenanceSpaceFitResult(MaintenanceSpaceFitResult result)
        {
            if (result == null)
            {
                return "NotChecked";
            }

            return "Status=" + (result.Status ?? string.Empty) +
                   ", Mode=" + (result.Mode ?? string.Empty) +
                   ", SolidCount=" + result.SolidCount.ToString(CultureInfo.InvariantCulture) +
                   ", CheckPointCount=" + result.CheckPointCount.ToString(CultureInfo.InvariantCulture) +
                   ", OutsidePointCount=" + result.OutsidePointCount.ToString(CultureInfo.InvariantCulture) +
                   ", TouchPointCount=" + result.TouchPointCount.ToString(CultureInfo.InvariantCulture) +
                   ", TouchToleranceMm=" + result.TouchToleranceMm.ToString("F0", CultureInfo.InvariantCulture) +
                   ", OutsideToleranceMm=" + result.OutsideToleranceMm.ToString("F0", CultureInfo.InvariantCulture) +
                   ", MinBoundaryDistanceMm=" + FormatMm(result.MinBoundaryDistance) +
                   ", MaxOverflowMm=" + FormatMm(result.MaxOverflowDistance) +
                   ", FailedSide=" + (result.FailedSide ?? string.Empty);
        }

        private static void ShowMaintenanceSpaceFitWarning(
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            MaintenanceSpaceFitResult check)
        {
            if (check == null || check.IsOk)
            {
                return;
            }

            try
            {
                string equipmentName = option != null
                    ? FirstNonEmpty(option.DisplayName, option.FileName, option.Key)
                    : string.Empty;

                string message =
                    "The selected AHU cannot fit in this room.\n\n" +
                    "Room: " + (room != null ? room.Key ?? string.Empty : string.Empty) + "\n" +
                    "Equipment: " + (equipmentName ?? string.Empty) + "\n" +
                    "Reason: Maintenance Space touches or exceeds the room boundary.\n" +
                    "Status: " + (check.Status ?? string.Empty) + "\n" +
                    "Side: " + (check.FailedSide ?? string.Empty) + "\n" +
                    "Minimum boundary distance: " + FormatMm(check.MinBoundaryDistance) + " mm\n" +
                    "Maximum overflow: " + FormatMm(check.MaxOverflowDistance) + " mm\n\n" +
                    "Please select a larger room or a smaller AHU.";

                TaskDialog.Show("AHU Room Size Check", message);
            }
            catch
            {
                // Dialog is best-effort only. Logs already contain the full check result.
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }


        private static bool TryResolveRoomDoorCenter(
            Document doc,
            RoomSemanticRecord room,
            XYZ placementPoint,
            ElementId levelId,
            out XYZ doorCenter,
            out string source,
            out ElementId elementId)
        {
            doorCenter = null;
            source = string.Empty;
            elementId = ElementId.InvalidElementId;
            if (doc == null || room == null || placementPoint == null)
            {
                return false;
            }

            List<DoorTargetCandidate> candidates = new List<DoorTargetCandidate>();
            AddBoundaryWallInsertDoorCandidates(doc, room, levelId, candidates);
            AddOpeningDoorCandidates(doc, room, levelId, candidates);
            AddDoorFamilyCandidates(doc, room, levelId, candidates);

            foreach (DoorTargetCandidate candidate in candidates)
            {
                if (candidate == null || candidate.Center == null)
                {
                    continue;
                }

                candidate.DistanceToPlacement = HorizontalDistance(candidate.Center, placementPoint);
                candidate.BoundaryDistance = DistanceToRoomBoundary(room, candidate.Center);
            }

            DoorTargetCandidate best = candidates
                .Where(x => x != null && x.Center != null && IsAcceptableDoorTarget(room, x))
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.BoundaryDistance)
                .ThenBy(x => x.DistanceToPlacement)
                .FirstOrDefault();

            if (best == null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomCustomFamily] Door-facing target not found. RoomKey=" + (room.Key ?? string.Empty) +
                    ", CandidateCount=" + candidates.Count.ToString(CultureInfo.InvariantCulture) +
                    ", Placement=(" + FormatPoint(placementPoint) + ")");
                return false;
            }

            DiagnosticRecorder.AppendDebug(
                "[RoomCustomFamily] Door-facing target selected. RoomKey=" + (room.Key ?? string.Empty) +
                ", DoorSource=" + (best.Source ?? string.Empty) +
                ", DoorElementId=" + FormatElementId(best.ElementId) +
                ", Priority=" + best.Priority.ToString(CultureInfo.InvariantCulture) +
                ", Placement=(" + FormatPoint(placementPoint) + ")" +
                ", DoorCenter=(" + FormatPoint(best.Center) + ")" +
                ", BoundaryDistanceMm=" + FormatMm(best.BoundaryDistance) +
                ", DistanceToPlacementMm=" + FormatMm(best.DistanceToPlacement) +
                ", CandidateCount=" + candidates.Count.ToString(CultureInfo.InvariantCulture));

            doorCenter = best.Center;
            source = best.Source ?? string.Empty;
            elementId = best.ElementId ?? ElementId.InvalidElementId;
            return true;
        }

        private static void AddBoundaryWallInsertDoorCandidates(
            Document doc,
            RoomSemanticRecord room,
            ElementId levelId,
            List<DoorTargetCandidate> candidates)
        {
            if (doc == null || room == null || room.BoundaryWalls == null || candidates == null)
            {
                return;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (RoomBoundaryWallReference wallRef in room.BoundaryWalls)
            {
                if (wallRef == null || wallRef.ElementId <= 0)
                {
                    continue;
                }

                Wall wall = doc.GetElement(new ElementId(wallRef.ElementId)) as Wall;
                if (wall == null)
                {
                    continue;
                }

                ICollection<ElementId> insertIds;
                try
                {
                    insertIds = wall.FindInserts(true, true, true, true);
                }
                catch
                {
                    insertIds = null;
                }

                if (insertIds == null)
                {
                    continue;
                }

                foreach (ElementId insertId in insertIds)
                {
                    if (insertId == null || insertId == ElementId.InvalidElementId || !seen.Add(insertId.IntegerValue))
                    {
                        continue;
                    }

                    Element insert = doc.GetElement(insertId);
                    if (!IsDoorLikeInsert(insert))
                    {
                        continue;
                    }

                    XYZ center = GetElementBoundingBoxCenter(insert);
                    if (center == null || !IsElementNearLevel(doc, insert, levelId))
                    {
                        continue;
                    }

                    bool convertedOpening = IsConvertedDoorOpening(insert);
                    bool openingInsert = insert is Opening;
                    candidates.Add(new DoorTargetCandidate
                    {
                        Center = center,
                        ElementId = insert.Id,
                        Source = convertedOpening
                            ? "ConvertedBoundaryWallOpening"
                            : openingInsert
                                ? "BoundaryWallOpening"
                                : "BoundaryWallInsert",
                        Priority = convertedOpening ? 0 : 10
                    });
                }
            }
        }

        private static void AddOpeningDoorCandidates(
            Document doc,
            RoomSemanticRecord room,
            ElementId levelId,
            List<DoorTargetCandidate> candidates)
        {
            if (doc == null || room == null || candidates == null)
            {
                return;
            }

            foreach (Opening opening in new FilteredElementCollector(doc)
                .OfClass(typeof(Opening))
                .Cast<Opening>())
            {
                if (opening == null || !IsElementNearLevel(doc, opening, levelId))
                {
                    continue;
                }

                XYZ center = GetElementBoundingBoxCenter(opening);
                if (center == null)
                {
                    continue;
                }

                bool convertedOpening = IsConvertedDoorOpening(opening);
                if (!convertedOpening && !IsDoorTargetNearRoomBoundary(room, center, DoorTargetBoundaryToleranceMm))
                {
                    continue;
                }

                candidates.Add(new DoorTargetCandidate
                {
                    Center = center,
                    ElementId = opening.Id,
                    Source = convertedOpening ? "ConvertedOpeningNearBoundary" : "OpeningNearBoundary",
                    Priority = convertedOpening ? 20 : 50
                });
            }
        }

        private static void AddDoorFamilyCandidates(
            Document doc,
            RoomSemanticRecord room,
            ElementId levelId,
            List<DoorTargetCandidate> candidates)
        {
            if (doc == null || room == null || candidates == null)
            {
                return;
            }

            foreach (FamilyInstance door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                if (door == null || !IsElementNearLevel(doc, door, levelId))
                {
                    continue;
                }

                XYZ center = GetElementBoundingBoxCenter(door) ?? ((door.Location as LocationPoint)?.Point);
                if (center == null)
                {
                    continue;
                }

                if (!IsDoorTargetNearRoomBoundary(room, center, DoorTargetBoundaryToleranceMm))
                {
                    continue;
                }

                candidates.Add(new DoorTargetCandidate
                {
                    Center = center,
                    ElementId = door.Id,
                    Source = "DoorFamilyNearBoundary",
                    Priority = 30
                });
            }
        }

        private static bool IsDoorLikeInsert(Element element)
        {
            if (element == null)
            {
                return false;
            }

            if (element is Opening)
            {
                return true;
            }

            Category category = element.Category;
            if (category != null && category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
            {
                return true;
            }

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null)
            {
                string text = ((familyInstance.Symbol != null ? familyInstance.Symbol.Name : string.Empty) + " " +
                               (familyInstance.Symbol != null && familyInstance.Symbol.Family != null ? familyInstance.Symbol.Family.Name : string.Empty) + " " +
                               (familyInstance.Name ?? string.Empty)).ToLowerInvariant();
                return text.Contains("door") || text.Contains("opening");
            }

            string name = (element.Name ?? string.Empty).ToLowerInvariant();
            return name.Contains("door") || name.Contains("opening");
        }

        private static bool IsAcceptableDoorTarget(RoomSemanticRecord room, DoorTargetCandidate candidate)
        {
            if (room == null || candidate == null || candidate.Center == null)
            {
                return false;
            }

            XYZ point = candidate.Center;
            if (!IsPointInsideExpandedBoundingBox(room, point, DoorTargetExpandedBBoxMarginMm))
            {
                return false;
            }

            bool insideLoop = IsPointInsideLoop(room.LoopPoints, point);
            double boundaryTolerance = UnitUtils.ConvertToInternalUnits(DoorTargetExpandedBBoxMarginMm, UnitTypeId.Millimeters);
            bool nearBoundary = candidate.BoundaryDistance <= boundaryTolerance;

            // Prefer actual openings/doors on the target room boundary. A point inside the room is accepted
            // only for high-confidence boundary wall inserts, so unrelated nearby doors are not selected.
            if (candidate.Priority <= 10)
            {
                return insideLoop || nearBoundary;
            }

            return nearBoundary;
        }

        private static bool IsDoorTargetNearRoomBoundary(RoomSemanticRecord room, XYZ point, double maxDistanceMm)
        {
            if (room == null || point == null)
            {
                return false;
            }

            double distance = DistanceToRoomBoundary(room, point);
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance == double.MaxValue)
            {
                return false;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, maxDistanceMm), UnitTypeId.Millimeters);
            return distance <= tolerance;
        }

        private static bool IsConvertedDoorOpening(Element element)
        {
            if (element == null)
            {
                return false;
            }

            string comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;
            string mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
            string name = element.Name ?? string.Empty;
            return ContainsInvariant(comments, ConvertedDoorOpeningComment) ||
                   ContainsInvariant(mark, ConvertedDoorOpeningComment) ||
                   ContainsInvariant(name, ConvertedDoorOpeningComment);
        }

        private static bool ContainsInvariant(string text, string token)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPointInsideExpandedBoundingBox(RoomSemanticRecord room, XYZ point, double marginMm)
        {
            if (room == null || point == null || room.BBox == null || room.BBox.Min == null || room.BBox.Max == null)
            {
                return true;
            }

            double margin = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, marginMm), UnitTypeId.Millimeters);
            return point.X >= Math.Min(room.BBox.Min.X, room.BBox.Max.X) - margin &&
                   point.X <= Math.Max(room.BBox.Min.X, room.BBox.Max.X) + margin &&
                   point.Y >= Math.Min(room.BBox.Min.Y, room.BBox.Max.Y) - margin &&
                   point.Y <= Math.Max(room.BBox.Min.Y, room.BBox.Max.Y) + margin;
        }

        private static bool IsPointInsideLoop(IList<XYZ> loopPoints, XYZ point)
        {
            if (loopPoints == null || loopPoints.Count < 3 || point == null)
            {
                return false;
            }

            List<XYZ> pts = loopPoints.Where(x => x != null).ToList();
            if (pts.Count < 3)
            {
                return false;
            }

            bool inside = false;
            int j = pts.Count - 1;
            for (int i = 0; i < pts.Count; i++)
            {
                double xi = pts[i].X;
                double yi = pts[i].Y;
                double xj = pts[j].X;
                double yj = pts[j].Y;

                bool intersect = ((yi > point.Y) != (yj > point.Y)) &&
                                 (point.X < (xj - xi) * (point.Y - yi) / ((yj - yi) == 0.0 ? 1e-12 : (yj - yi)) + xi);
                if (intersect)
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        private static double DistanceToRoomBoundary(RoomSemanticRecord room, XYZ point)
        {
            if (room == null || point == null || room.LoopPoints == null || room.LoopPoints.Count < 2)
            {
                return double.MaxValue;
            }

            List<XYZ> pts = room.LoopPoints.Where(x => x != null).ToList();
            if (pts.Count < 2)
            {
                return double.MaxValue;
            }

            double min = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a = pts[i];
                XYZ b = pts[(i + 1) % pts.Count];
                double d = DistancePointToSegmentXY(point, a, b);
                if (d < min)
                {
                    min = d;
                }
            }

            return min;
        }

        private static double DistancePointToSegmentXY(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                return HorizontalDistance(p, a);
            }

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            XYZ proj = new XYZ(a.X + t * dx, a.Y + t * dy, p.Z);
            return HorizontalDistance(p, proj);
        }

        private static bool IsElementNearLevel(Document doc, Element element, ElementId levelId)
        {
            if (doc == null || element == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            Level level = doc.GetElement(levelId) as Level;
            if (level == null)
            {
                return true;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                ElementId elementLevelId = ResolveElementLevelId(element);
                return elementLevelId == null || elementLevelId == ElementId.InvalidElementId || elementLevelId.IntegerValue == levelId.IntegerValue;
            }

            double z = (box.Min.Z + box.Max.Z) * 0.5;
            double tolerance = UnitUtils.ConvertToInternalUnits(5000.0, UnitTypeId.Millimeters);
            return Math.Abs(z - level.Elevation) <= tolerance ||
                   (box.Min.Z <= level.Elevation + tolerance && box.Max.Z >= level.Elevation - tolerance);
        }

        private static ElementId ResolveElementLevelId(Element element)
        {
            if (element == null)
            {
                return ElementId.InvalidElementId;
            }

            Parameter levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) ??
                                   element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ??
                                   element.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT) ??
                                   element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            ElementId levelId = levelParam != null ? levelParam.AsElementId() : ElementId.InvalidElementId;
            return levelId != null ? levelId : ElementId.InvalidElementId;
        }

        private static XYZ ResolveEquipmentCoreCenterForFinalPlacement(
            Document doc,
            FamilyInstance instance,
            XYZ fallbackCenter,
            out string mode)
        {
            mode = string.Empty;
            if (doc == null || instance == null)
            {
                return null;
            }

            XYZ center;
            int count;
            string names;
            if (TryResolveNamedAhuCoreCenter(doc, instance, out center, out count, out names))
            {
                mode = "NamedAhuCoreSubcomponents(count=" + count.ToString(CultureInfo.InvariantCulture) + ", names=" + (names ?? string.Empty) + ")";
                return center;
            }

            string solidMode;
            if (TryResolvePhysicalSolidCoreCenter(doc, instance, out center, out count, out solidMode))
            {
                mode = "PhysicalSolidCore(count=" + count.ToString(CultureInfo.InvariantCulture) + ", mode=" + (solidMode ?? string.Empty) + ")";
                return center;
            }

            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            if (box != null && box.Min != null && box.Max != null)
            {
                mode = "ParentBoundingBoxFallback";
                return new XYZ(
                    (box.Min.X + box.Max.X) * 0.5,
                    (box.Min.Y + box.Max.Y) * 0.5,
                    (box.Min.Z + box.Max.Z) * 0.5);
            }

            mode = "FallbackCenter";
            return fallbackCenter;
        }

        private static bool TryResolveNamedAhuCoreCenter(
            Document doc,
            FamilyInstance instance,
            out XYZ center,
            out int count,
            out string names)
        {
            center = null;
            count = 0;
            names = string.Empty;
            if (doc == null || instance == null)
            {
                return false;
            }

            AhuCoreBounds bounds = new AhuCoreBounds();
            CollectNamedAhuCoreBounds(doc, instance, bounds, new HashSet<int>(), true);
            if (bounds.Count <= 0)
            {
                return false;
            }

            center = bounds.Center;
            count = bounds.Count;
            names = bounds.NameSummary;
            return center != null;
        }

        private static void CollectNamedAhuCoreBounds(
            Document doc,
            FamilyInstance instance,
            AhuCoreBounds bounds,
            HashSet<int> seen,
            bool skipSelf)
        {
            if (doc == null || instance == null || bounds == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            if (!skipSelf)
            {
                AddNamedAhuCoreBounds(instance, bounds);
            }

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId subId in subIds)
            {
                if (subId == null || subId == ElementId.InvalidElementId)
                {
                    continue;
                }

                FamilyInstance subInstance = doc.GetElement(subId) as FamilyInstance;
                if (subInstance == null)
                {
                    continue;
                }

                CollectNamedAhuCoreBounds(doc, subInstance, bounds, seen, false);
            }
        }

        private static void AddNamedAhuCoreBounds(Element element, AhuCoreBounds bounds)
        {
            if (element == null || bounds == null)
            {
                return;
            }

            string text = BuildElementSearchText(element);
            if (!IsAhuCoreComponentText(text))
            {
                return;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);
            double minSize = UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters);
            if (dx < minSize && dy < minSize && dz < minSize)
            {
                return;
            }

            bounds.Include(box, element.Name ?? string.Empty);
        }

        private static bool IsAhuCoreComponentText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.ToLowerInvariant();
            if (value.Contains("clear") ||
                value.Contains("clearance") ||
                value.Contains("maintenance") ||
                value.Contains("access") ||
                value.Contains("service space") ||
                value.Contains("working") ||
                value.Contains("operation") ||
                value.Contains("swing") ||
                value.Contains("opening") ||
                value.Contains("room") ||
                value.Contains("label") ||
                value.Contains("tag") ||
                value.Contains("text") ||
                value.Contains("annotation") ||
                value.Contains("reference") ||
                value.Contains("symbolic") ||
                value.Contains("void") ||
                value.Contains("zone"))
            {
                return false;
            }

            return value.Contains("mixing box") ||
                   value.Contains("mixing_box") ||
                   value.Contains("mixing-box") ||
                   value.Contains("mixing") ||
                   value.Contains("filter chamber") ||
                   value.Contains("filter_chamber") ||
                   value.Contains("filter-chamber") ||
                   value.Contains("filter") ||
                   value.Contains("coil section") ||
                   value.Contains("coil_section") ||
                   value.Contains("coil-section") ||
                   value.Contains("coil") ||
                   value.Contains("fan section") ||
                   value.Contains("fan_section") ||
                   value.Contains("fan-section") ||
                   value.Contains("fan") ||
                   value.Contains("valve chamber") ||
                   value.Contains("valve_chamber") ||
                   value.Contains("valve-chamber") ||
                   value.Contains("valve") ||
                   value.Contains("electrical chamber") ||
                   value.Contains("electrical_chamber") ||
                   value.Contains("electrical-chamber") ||
                   value.Contains("electrical") ||
                   value.Contains("electric");
        }

        private static bool TryResolvePhysicalSolidCoreCenter(
            Document doc,
            FamilyInstance instance,
            out XYZ center,
            out int count,
            out string mode)
        {
            center = null;
            count = 0;
            mode = string.Empty;
            if (doc == null || instance == null)
            {
                return false;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = null;
            try
            {
                geometry = instance.get_Geometry(options);
            }
            catch
            {
                geometry = null;
            }

            if (geometry == null)
            {
                return false;
            }

            AhuCoreBounds bounds = new AhuCoreBounds();
            int transparentSkipped;
            int tinySkipped;
            CollectPhysicalSolidCoreBounds(doc, geometry, bounds, 0, out transparentSkipped, out tinySkipped);
            if (bounds.Count <= 0)
            {
                mode = "NoIncludedSolids, transparentSkipped=" + transparentSkipped.ToString(CultureInfo.InvariantCulture) +
                       ", tinySkipped=" + tinySkipped.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            center = bounds.Center;
            count = bounds.Count;
            mode = "transparentSkipped=" + transparentSkipped.ToString(CultureInfo.InvariantCulture) +
                   ", tinySkipped=" + tinySkipped.ToString(CultureInfo.InvariantCulture);
            return center != null;
        }

        private static void CollectPhysicalSolidCoreBounds(
            Document doc,
            GeometryElement geometry,
            AhuCoreBounds bounds,
            int depth,
            out int transparentSkipped,
            out int tinySkipped)
        {
            transparentSkipped = 0;
            tinySkipped = 0;
            if (doc == null || geometry == null || bounds == null || depth > 8)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                {
                    continue;
                }

                Solid solid = geometryObject as Solid;
                if (solid != null)
                {
                    double volume = 0.0;
                    try
                    {
                        volume = Math.Abs(solid.Volume);
                    }
                    catch
                    {
                        volume = 0.0;
                    }

                    double tinyVolume = Math.Pow(UnitUtils.ConvertToInternalUnits(20.0, UnitTypeId.Millimeters), 3.0);
                    if (volume <= tinyVolume || solid.Faces == null || solid.Faces.Size == 0)
                    {
                        tinySkipped++;
                        continue;
                    }

                    if (IsMostlyTransparentSolid(doc, solid, 70))
                    {
                        transparentSkipped++;
                        continue;
                    }

                    BoundingBoxXYZ solidBox = null;
                    try
                    {
                        solidBox = solid.GetBoundingBox();
                    }
                    catch
                    {
                        solidBox = null;
                    }

                    if (solidBox == null || solidBox.Min == null || solidBox.Max == null)
                    {
                        tinySkipped++;
                        continue;
                    }

                    IncludeBoundingBoxCorners(bounds, solidBox, "solid");
                    continue;
                }

                GeometryInstance geometryInstance = geometryObject as GeometryInstance;
                if (geometryInstance != null)
                {
                    GeometryElement nested = null;
                    try
                    {
                        nested = geometryInstance.GetInstanceGeometry();
                    }
                    catch
                    {
                        nested = null;
                    }

                    if (nested == null)
                    {
                        continue;
                    }

                    int childTransparentSkipped;
                    int childTinySkipped;
                    CollectPhysicalSolidCoreBounds(doc, nested, bounds, depth + 1, out childTransparentSkipped, out childTinySkipped);
                    transparentSkipped += childTransparentSkipped;
                    tinySkipped += childTinySkipped;
                }
            }
        }

        private static bool IsMostlyTransparentSolid(Document doc, Solid solid, int transparentThreshold)
        {
            if (doc == null || solid == null || solid.Faces == null || solid.Faces.Size == 0)
            {
                return false;
            }

            int materialFaces = 0;
            int transparentFaces = 0;
            foreach (Face face in solid.Faces)
            {
                if (face == null)
                {
                    continue;
                }

                ElementId materialId = ElementId.InvalidElementId;
                try
                {
                    materialId = face.MaterialElementId;
                }
                catch
                {
                    materialId = ElementId.InvalidElementId;
                }

                if (materialId == null || materialId == ElementId.InvalidElementId)
                {
                    continue;
                }

                Material material = doc.GetElement(materialId) as Material;
                if (material == null)
                {
                    continue;
                }

                materialFaces++;
                int transparency = 0;
                try
                {
                    transparency = material.Transparency;
                }
                catch
                {
                    transparency = 0;
                }

                if (transparency >= transparentThreshold)
                {
                    transparentFaces++;
                }
            }

            return materialFaces > 0 && transparentFaces >= Math.Max(1, materialFaces / 2);
        }

        private static void IncludeBoundingBoxCorners(AhuCoreBounds bounds, BoundingBoxXYZ box, string name)
        {
            if (bounds == null || box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            Transform transform = box.Transform ?? Transform.Identity;
            double minX = Math.Min(box.Min.X, box.Max.X);
            double maxX = Math.Max(box.Min.X, box.Max.X);
            double minY = Math.Min(box.Min.Y, box.Max.Y);
            double maxY = Math.Max(box.Min.Y, box.Max.Y);
            double minZ = Math.Min(box.Min.Z, box.Max.Z);
            double maxZ = Math.Max(box.Min.Z, box.Max.Z);

            bounds.IncludePoint(transform.OfPoint(new XYZ(minX, minY, minZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(minX, minY, maxZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(minX, maxY, minZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(minX, maxY, maxZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(maxX, minY, minZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(maxX, minY, maxZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(maxX, maxY, minZ)), name);
            bounds.IncludePoint(transform.OfPoint(new XYZ(maxX, maxY, maxZ)), name);
        }

        private static XYZ GetElementBoundingBoxCenter(Element element)
        {
            if (element == null)
            {
                return null;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return null;
            }

            return new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);
        }

        private static bool TryResolveEquipmentDoorSideReference(
            Document doc,
            FamilyInstance instance,
            RoomCustomFamilyOption option,
            XYZ equipmentCenter,
            out XYZ doorSideDirection,
            out XYZ doorSideReferencePoint,
            out string doorSideMode)
        {
            doorSideDirection = null;
            doorSideReferencePoint = null;
            doorSideMode = string.Empty;
            if (doc == null || instance == null)
            {
                return false;
            }

            XYZ center = equipmentCenter ?? GetElementBoundingBoxCenter(instance) ?? ResolveRotationOrigin(instance, null);
            if (center == null)
            {
                return false;
            }

            bool isAhu = IsAhuCustomFamily(instance, option);

            if (isAhu && TryResolveAhuPipeSideReference(doc, instance, center, out doorSideDirection, out doorSideReferencePoint, out doorSideMode))
            {
                return true;
            }

            if (TryResolveConnectorSideReference(doc, instance, center, true, out doorSideDirection, out doorSideReferencePoint, out doorSideMode))
            {
                return true;
            }

            if (isAhu && TryResolveNamedServiceSideReference(doc, instance, center, out doorSideDirection, out doorSideReferencePoint, out doorSideMode, true))
            {
                doorSideMode = "AHUNamedServiceFallback-" + (doorSideMode ?? string.Empty);
                return true;
            }

            if (!isAhu && TryResolveNamedServiceSideReference(doc, instance, center, out doorSideDirection, out doorSideReferencePoint, out doorSideMode))
            {
                return true;
            }

            // Last service-side fallback: use all MEP connectors if no piping connector is available.
            if (TryResolveConnectorSideReference(doc, instance, center, false, out doorSideDirection, out doorSideReferencePoint, out doorSideMode))
            {
                return true;
            }

            // Keep non-AHU custom families backward compatible. For AHU this is only a final fallback
            // and the log will clearly say AHUServiceSideFallback.
            XYZ facing = Flatten(instance.FacingOrientation);
            if (IsUsableDirection(facing))
            {
                doorSideDirection = isAhu ? NegateXY(facing) : facing;
                doorSideReferencePoint = EstimateSideReferencePoint(instance, center, doorSideDirection);
                doorSideMode = isAhu ? "AHUServiceSideFallback-FacingOpposite" : "NonAhuFacingFallback";
                return true;
            }

            XYZ hand = Flatten(instance.HandOrientation);
            if (IsUsableDirection(hand))
            {
                doorSideDirection = isAhu ? NegateXY(hand) : hand;
                doorSideReferencePoint = EstimateSideReferencePoint(instance, center, doorSideDirection);
                doorSideMode = isAhu ? "AHUServiceSideFallback-HandOpposite" : "NonAhuHandFallback";
                return true;
            }

            return false;
        }

        private static bool TryResolveAhuPipeSideReference(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            out XYZ direction,
            out XYZ referencePoint,
            out string mode)
        {
            direction = null;
            referencePoint = null;
            mode = string.Empty;
            if (doc == null || instance == null || center == null)
            {
                return false;
            }

            XYZ valveCenter;
            int valveCount;
            string valveNames;
            if (TryResolveValveChamberReference(doc, instance, out valveCenter, out valveCount, out valveNames))
            {
                XYZ valveDirection = Flatten(valveCenter - center);
                if (IsUsableDirection(valveDirection))
                {
                    List<PipeConnectorSideCandidate> valveConnectorCandidates = new List<PipeConnectorSideCandidate>();
                    CollectAhuPipeConnectorCandidates(doc, instance, center, valveConnectorCandidates, new HashSet<int>());
                    PipeConnectorSideCandidate externalConnector = SelectExternalPipeConnectorNearValve(
                        valveConnectorCandidates,
                        center,
                        valveCenter,
                        valveDirection.Normalize());
                    if (externalConnector != null)
                    {
                        direction = Flatten(externalConnector.Origin - center).Normalize();
                        referencePoint = externalConnector.Origin;
                        mode = "ExternalConnectorNearValveChamber(" + (externalConnector.Source ?? string.Empty) +
                               ", valveCount=" + valveCount.ToString(CultureInfo.InvariantCulture) + ")";
                        DiagnosticRecorder.AppendDebug(
                            "[RoomCustomFamily] AHU pipe side resolved. PipeSideMode=ExternalConnector, ValveCenter=(" +
                            FormatPoint(valveCenter) + "), ReferencePoint=(" + FormatPoint(referencePoint) +
                            "), CoreCenter=(" + FormatPoint(center) + "), Direction=(" + FormatVector(direction) +
                            "), ValveNames=" + (valveNames ?? string.Empty) +
                            ", Candidates=" + FormatPipeConnectorCandidates(valveConnectorCandidates));
                        return true;
                    }

                    direction = valveDirection.Normalize();
                    referencePoint = valveCenter;
                    mode = "ValveChamber(count=" + valveCount.ToString(CultureInfo.InvariantCulture) +
                           ", names=" + (valveNames ?? string.Empty) + ")";
                    DiagnosticRecorder.AppendDebug(
                        "[RoomCustomFamily] AHU pipe side resolved. PipeSideMode=ValveChamber, ReferencePoint=(" +
                        FormatPoint(referencePoint) + "), CoreCenter=(" + FormatPoint(center) +
                        "), Direction=(" + FormatVector(direction) + ")");
                    return true;
                }
            }

            List<PipeConnectorSideCandidate> candidates = new List<PipeConnectorSideCandidate>();
            CollectAhuPipeConnectorCandidates(doc, instance, center, candidates, new HashSet<int>());
            candidates = candidates
                .Where(x => x != null && x.Origin != null && IsUsableDirection(x.Direction) && x.Weight > 0.0)
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            PipeConnectorSideCandidate bestDirectionSeed = candidates
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.Distance)
                .FirstOrDefault();
            if (bestDirectionSeed == null)
            {
                return false;
            }

            XYZ seedDirection = bestDirectionSeed.Direction.Normalize();
            List<PipeConnectorSideCandidate> sameSide = candidates
                .Where(x => DotXY(x.Direction.Normalize(), seedDirection) >= 0.25)
                .ToList();
            if (sameSide.Count == 0)
            {
                sameSide = candidates;
            }

            double sumX = 0.0;
            double sumY = 0.0;
            double pointSumX = 0.0;
            double pointSumY = 0.0;
            double pointSumZ = 0.0;
            double weightSum = 0.0;
            foreach (PipeConnectorSideCandidate candidate in sameSide)
            {
                XYZ normalized = candidate.Direction.Normalize();
                double weight = Math.Max(candidate.Weight, 1.0);
                sumX += normalized.X * weight;
                sumY += normalized.Y * weight;
                pointSumX += candidate.Origin.X * weight;
                pointSumY += candidate.Origin.Y * weight;
                pointSumZ += candidate.Origin.Z * weight;
                weightSum += weight;
            }

            if (weightSum <= 0.0)
            {
                return false;
            }

            XYZ weightedDirection = new XYZ(sumX, sumY, 0.0);
            if (!IsUsableDirection(weightedDirection))
            {
                weightedDirection = seedDirection;
            }

            direction = weightedDirection.Normalize();
            referencePoint = new XYZ(pointSumX / weightSum, pointSumY / weightSum, pointSumZ / weightSum);
            mode = "AHUPipeConnectorSide(" + (bestDirectionSeed.Source ?? string.Empty) +
                   ", count=" + sameSide.Count.ToString(CultureInfo.InvariantCulture) +
                   "/" + candidates.Count.ToString(CultureInfo.InvariantCulture) + ")";
            DiagnosticRecorder.AppendDebug(
                "[RoomCustomFamily] AHU pipe side resolved. Mode=" + mode +
                ", ReferencePoint=(" + FormatPoint(referencePoint) + ")" +
                ", Direction=(" + FormatVector(direction) + ")" +
                ", Candidates=" + FormatPipeConnectorCandidates(candidates));
            return true;
        }

        private static bool TryResolveConnectorSideReference(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            bool pipingOnly,
            out XYZ direction,
            out XYZ referencePoint,
            out string mode)
        {
            direction = null;
            referencePoint = null;
            mode = string.Empty;
            if (doc == null || instance == null || center == null)
            {
                return false;
            }

            List<XYZ> origins = new List<XYZ>();
            CollectConnectorOrigins(doc, instance, pipingOnly, origins, new HashSet<int>());
            if (origins.Count == 0)
            {
                return false;
            }

            double sumX = 0.0;
            double sumY = 0.0;
            double pointSumX = 0.0;
            double pointSumY = 0.0;
            double pointSumZ = 0.0;
            int used = 0;
            XYZ farthestOrigin = null;
            double farthestDistance = 0.0;

            foreach (XYZ origin in origins)
            {
                XYZ vector = Flatten(origin - center);
                if (!IsUsableDirection(vector))
                {
                    continue;
                }

                double distance = vector.GetLength();
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestOrigin = origin;
                }

                XYZ normalized = vector.Normalize();
                sumX += normalized.X;
                sumY += normalized.Y;
                pointSumX += origin.X;
                pointSumY += origin.Y;
                pointSumZ += origin.Z;
                used++;
            }

            if (used == 0)
            {
                return false;
            }

            XYZ meanDirection = new XYZ(sumX, sumY, 0.0);
            XYZ meanPoint = new XYZ(pointSumX / used, pointSumY / used, pointSumZ / used);
            if (meanDirection.GetLength() >= 0.25)
            {
                direction = meanDirection.Normalize();
                referencePoint = meanPoint;
                mode = pipingOnly
                    ? "PipingConnectorSide-MeanDistance(count=" + used.ToString(CultureInfo.InvariantCulture) + ")"
                    : "AnyConnectorSide-MeanDistance(count=" + used.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }

            if (farthestOrigin != null)
            {
                XYZ farthestDirection = Flatten(farthestOrigin - center);
                if (IsUsableDirection(farthestDirection))
                {
                    direction = farthestDirection.Normalize();
                    referencePoint = farthestOrigin;
                    mode = pipingOnly
                        ? "PipingConnectorSide-FarthestDistance(count=" + used.ToString(CultureInfo.InvariantCulture) + ")"
                        : "AnyConnectorSide-FarthestDistance(count=" + used.ToString(CultureInfo.InvariantCulture) + ")";
                    return true;
                }
            }

            return false;
        }

        private static void CollectConnectorOrigins(
            Document doc,
            FamilyInstance instance,
            bool pipingOnly,
            List<XYZ> origins,
            HashSet<int> seen)
        {
            if (doc == null || instance == null || origins == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            ConnectorManager manager = null;
            try
            {
                manager = instance.MEPModel != null ? instance.MEPModel.ConnectorManager : null;
            }
            catch
            {
                manager = null;
            }

            if (manager != null)
            {
                ConnectorSet connectorSet = null;
                try
                {
                    connectorSet = manager.Connectors;
                }
                catch
                {
                    connectorSet = null;
                }

                if (connectorSet != null)
                {
                    foreach (Connector connector in connectorSet)
                    {
                        if (connector == null)
                        {
                            continue;
                        }

                        if (pipingOnly && !IsPipingConnector(connector))
                        {
                            continue;
                        }

                        XYZ origin = null;
                        try
                        {
                            origin = connector.Origin;
                        }
                        catch
                        {
                            origin = null;
                        }

                        if (origin != null)
                        {
                            origins.Add(origin);
                        }
                    }
                }
            }

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId subId in subIds)
            {
                if (subId == null || subId == ElementId.InvalidElementId)
                {
                    continue;
                }

                FamilyInstance subInstance = doc.GetElement(subId) as FamilyInstance;
                if (subInstance == null)
                {
                    continue;
                }

                CollectConnectorOrigins(doc, subInstance, pipingOnly, origins, seen);
            }
        }

        private static bool IsPipingConnector(Connector connector)
        {
            if (connector == null)
            {
                return false;
            }

            try
            {
                return connector.Domain == Domain.DomainPiping;
            }
            catch
            {
                return false;
            }
        }

        private static void CollectAhuPipeConnectorCandidates(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            List<PipeConnectorSideCandidate> candidates,
            HashSet<int> seen)
        {
            if (doc == null || instance == null || center == null || candidates == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            ConnectorManager manager = null;
            try
            {
                manager = instance.MEPModel != null ? instance.MEPModel.ConnectorManager : null;
            }
            catch
            {
                manager = null;
            }

            if (manager != null)
            {
                ConnectorSet connectorSet = null;
                try
                {
                    connectorSet = manager.Connectors;
                }
                catch
                {
                    connectorSet = null;
                }

                if (connectorSet != null)
                {
                    string ownerText = BuildElementSearchText(instance);
                    foreach (Connector connector in connectorSet)
                    {
                        if (connector == null || !IsPipingConnector(connector))
                        {
                            continue;
                        }

                        XYZ origin = null;
                        try
                        {
                            origin = connector.Origin;
                        }
                        catch
                        {
                            origin = null;
                        }

                        XYZ direction = origin != null ? Flatten(origin - center) : null;
                        if (origin == null || !IsUsableDirection(direction))
                        {
                            continue;
                        }

                        double weight = ResolvePipeConnectorWeight(ownerText);
                        candidates.Add(new PipeConnectorSideCandidate
                        {
                            Origin = origin,
                            Direction = direction.Normalize(),
                            Distance = direction.GetLength(),
                            Weight = weight,
                            Source = ownerText
                        });
                    }
                }
            }

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId subId in subIds)
            {
                if (subId == null || subId == ElementId.InvalidElementId)
                {
                    continue;
                }

                FamilyInstance subInstance = doc.GetElement(subId) as FamilyInstance;
                if (subInstance == null)
                {
                    continue;
                }

                CollectAhuPipeConnectorCandidates(doc, subInstance, center, candidates, seen);
            }
        }

        private static bool TryResolveValveChamberReference(
            Document doc,
            FamilyInstance instance,
            out XYZ center,
            out int count,
            out string names)
        {
            center = null;
            count = 0;
            names = string.Empty;
            if (doc == null || instance == null)
            {
                return false;
            }

            AhuCoreBounds bounds = new AhuCoreBounds();
            CollectValveChamberBounds(doc, instance, bounds, new HashSet<int>(), true);
            if (bounds.Count <= 0)
            {
                return false;
            }

            center = bounds.Center;
            count = bounds.Count;
            names = bounds.NameSummary;
            return center != null;
        }

        private static void CollectValveChamberBounds(
            Document doc,
            FamilyInstance instance,
            AhuCoreBounds bounds,
            HashSet<int> seen,
            bool skipSelf)
        {
            if (doc == null || instance == null || bounds == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            if (!skipSelf)
            {
                AddValveChamberBounds(instance, bounds);
            }

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId subId in subIds)
            {
                if (subId == null || subId == ElementId.InvalidElementId)
                {
                    continue;
                }

                FamilyInstance subInstance = doc.GetElement(subId) as FamilyInstance;
                if (subInstance == null)
                {
                    continue;
                }

                CollectValveChamberBounds(doc, subInstance, bounds, seen, false);
            }
        }

        private static void AddValveChamberBounds(Element element, AhuCoreBounds bounds)
        {
            if (element == null || bounds == null)
            {
                return;
            }

            string text = BuildElementSearchText(element);
            if (!IsValveChamberText(text))
            {
                return;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);
            double minSize = UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters);
            if (dx < minSize && dy < minSize && dz < minSize)
            {
                return;
            }

            bounds.Include(box, element.Name ?? string.Empty);
        }

        private static bool IsValveChamberText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.ToLowerInvariant();
            if (value.Contains("maintenance") ||
                value.Contains("clearance") ||
                value.Contains("service space") ||
                value.Contains("electrical") ||
                value.Contains("electric") ||
                value.Contains("filter") ||
                value.Contains("fan") ||
                value.Contains("mixing"))
            {
                return false;
            }

            return value.Contains("valve chamber") ||
                   value.Contains("valve_chamber") ||
                   value.Contains("valve-chamber") ||
                   value.Contains("chw valve") ||
                   value.Contains("chws valve") ||
                   value.Contains("chwr valve");
        }

        private static PipeConnectorSideCandidate SelectExternalPipeConnectorNearValve(
            IList<PipeConnectorSideCandidate> candidates,
            XYZ center,
            XYZ valveCenter,
            XYZ valveDirection)
        {
            if (candidates == null || candidates.Count == 0 || center == null || valveCenter == null || !IsUsableDirection(valveDirection))
            {
                return null;
            }

            XYZ normalizedValveDirection = valveDirection.Normalize();
            double nearValveTolerance = UnitUtils.ConvertToInternalUnits(2500.0, UnitTypeId.Millimeters);
            return candidates
                .Where(x => x != null && x.Origin != null && IsUsableDirection(x.Direction))
                .Select(x => new
                {
                    Candidate = x,
                    Dot = DotXY(x.Direction.Normalize(), normalizedValveDirection),
                    ValveDistance = HorizontalDistance(x.Origin, valveCenter),
                    CenterDistance = HorizontalDistance(x.Origin, center)
                })
                .Where(x => x.Dot >= 0.35 && x.ValveDistance <= nearValveTolerance)
                .OrderByDescending(x => x.Candidate.Weight)
                .ThenByDescending(x => x.CenterDistance)
                .ThenBy(x => x.ValveDistance)
                .Select(x => x.Candidate)
                .FirstOrDefault();
        }

        private static double ResolvePipeConnectorWeight(string text)
        {
            double weight = 20.0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return weight;
            }

            string value = text.ToLowerInvariant();
            if (value.Contains("chws") || value.Contains("chwr"))
            {
                weight += 60.0;
            }
            if (value.Contains("chilled"))
            {
                weight += 36.0;
            }
            if (value.Contains("pipe") || value.Contains("water"))
            {
                weight += 30.0;
            }
            if (value.Contains("supply") || value.Contains("return"))
            {
                weight += 16.0;
            }
            if (value.Contains("valve"))
            {
                weight += 12.0;
            }
            if (value.Contains("electrical") || value.Contains("electric") || value.Contains("filter"))
            {
                weight -= 18.0;
            }
            if (value.Contains("chamber"))
            {
                weight -= 6.0;
            }

            return Math.Max(weight, 1.0);
        }

        private static string FormatPipeConnectorCandidates(IList<PipeConnectorSideCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("; ",
                candidates
                    .OrderByDescending(x => x != null ? x.Weight : 0.0)
                    .ThenByDescending(x => x != null ? x.Distance : 0.0)
                    .Take(12)
                    .Select(x =>
                        "w=" + (x != null ? x.Weight.ToString("F1", CultureInfo.InvariantCulture) : string.Empty) +
                        ",dMm=" + (x != null ? FormatMm(x.Distance) : string.Empty) +
                        ",dir=(" + (x != null ? FormatVector(x.Direction) : string.Empty) + ")" +
                        ",src=" + (x != null ? x.Source ?? string.Empty : string.Empty)));
        }

        private static bool TryResolveNamedServiceSideReference(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            out XYZ direction,
            out XYZ referencePoint,
            out string mode,
            bool ahuFallbackMode = false)
        {
            direction = null;
            referencePoint = null;
            mode = string.Empty;
            if (doc == null || instance == null || center == null)
            {
                return false;
            }

            List<NamedServiceCandidate> candidates = new List<NamedServiceCandidate>();
            CollectNamedServiceCandidates(doc, instance, center, candidates, new HashSet<int>(), ahuFallbackMode);
            if (candidates.Count == 0)
            {
                return false;
            }

            double sumX = 0.0;
            double sumY = 0.0;
            double pointSumX = 0.0;
            double pointSumY = 0.0;
            double pointSumZ = 0.0;
            double weightSum = 0.0;
            foreach (NamedServiceCandidate candidate in candidates)
            {
                if (candidate == null || candidate.Direction == null || candidate.ReferencePoint == null || candidate.Weight <= 0.0)
                {
                    continue;
                }

                XYZ normalized = candidate.Direction.Normalize();
                sumX += normalized.X * candidate.Weight;
                sumY += normalized.Y * candidate.Weight;
                pointSumX += candidate.ReferencePoint.X * candidate.Weight;
                pointSumY += candidate.ReferencePoint.Y * candidate.Weight;
                pointSumZ += candidate.ReferencePoint.Z * candidate.Weight;
                weightSum += candidate.Weight;
            }

            if (weightSum <= 0.0)
            {
                return false;
            }

            XYZ weighted = new XYZ(sumX, sumY, 0.0);
            if (!IsUsableDirection(weighted))
            {
                return false;
            }

            NamedServiceCandidate best = candidates
                .OrderByDescending(x => x != null ? x.Weight : 0.0)
                .FirstOrDefault();

            direction = weighted.Normalize();
            referencePoint = new XYZ(pointSumX / weightSum, pointSumY / weightSum, pointSumZ / weightSum);
            mode = "NamedServiceSideDistance(" + (best != null ? best.Name ?? string.Empty : string.Empty) + ", count=" +
                   candidates.Count.ToString(CultureInfo.InvariantCulture) + ")";
            return true;
        }

        private static void CollectNamedServiceCandidates(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            List<NamedServiceCandidate> candidates,
            HashSet<int> seen,
            bool ahuFallbackMode)
        {
            if (doc == null || instance == null || center == null || candidates == null || seen == null)
            {
                return;
            }

            if (instance.Id == null || !seen.Add(instance.Id.IntegerValue))
            {
                return;
            }

            AddNamedServiceCandidate(instance, center, candidates, ahuFallbackMode);

            ICollection<ElementId> subIds = null;
            try
            {
                subIds = instance.GetSubComponentIds();
            }
            catch
            {
                subIds = null;
            }

            if (subIds == null)
            {
                return;
            }

            foreach (ElementId subId in subIds)
            {
                if (subId == null || subId == ElementId.InvalidElementId)
                {
                    continue;
                }

                FamilyInstance subInstance = doc.GetElement(subId) as FamilyInstance;
                if (subInstance == null)
                {
                    continue;
                }

                CollectNamedServiceCandidates(doc, subInstance, center, candidates, seen, ahuFallbackMode);
            }
        }

        private static void AddNamedServiceCandidate(
            Element element,
            XYZ center,
            List<NamedServiceCandidate> candidates,
            bool ahuFallbackMode)
        {
            if (element == null || center == null || candidates == null)
            {
                return;
            }

            string text = BuildElementSearchText(element);
            double weight = ResolveServiceSideWeight(text, ahuFallbackMode);
            if (weight <= 0.0)
            {
                return;
            }

            XYZ elementCenter = GetElementBoundingBoxCenter(element);
            XYZ direction = elementCenter != null ? Flatten(elementCenter - center) : null;
            if (!IsUsableDirection(direction))
            {
                return;
            }

            candidates.Add(new NamedServiceCandidate
            {
                Direction = direction.Normalize(),
                ReferencePoint = elementCenter,
                Weight = weight,
                Name = element.Name ?? string.Empty
            });
        }

        private static string BuildElementSearchText(Element element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            parts.Add(element.Name ?? string.Empty);
            if (element.Category != null)
            {
                parts.Add(element.Category.Name ?? string.Empty);
            }

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null)
            {
                FamilySymbol symbol = familyInstance.Symbol;
                if (symbol != null)
                {
                    parts.Add(symbol.Name ?? string.Empty);
                    parts.Add(symbol.FamilyName ?? string.Empty);
                    if (symbol.Family != null)
                    {
                        parts.Add(symbol.Family.Name ?? string.Empty);
                    }
                }
            }

            return string.Join(" ", parts);
        }

        private static double ResolveServiceSideWeight(string text, bool ahuFallbackMode)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            string value = text.ToLowerInvariant();
            double weight = 0.0;
            if (ahuFallbackMode)
            {
                if (value.Contains("chws") || value.Contains("chwr"))
                {
                    weight += 28.0;
                }
                if (value.Contains("pipe") || value.Contains("water") || value.Contains("chilled"))
                {
                    weight += 18.0;
                }
                if (value.Contains("supply") || value.Contains("return"))
                {
                    weight += 8.0;
                }
                if (value.Contains("valve"))
                {
                    weight += 6.0;
                }
                if (value.Contains("electrical") || value.Contains("electric"))
                {
                    weight += 2.0;
                }
                if (value.Contains("chamber"))
                {
                    weight += 1.0;
                }
                if (value.Contains("filter"))
                {
                    weight += 0.5;
                }

                return weight;
            }

            if (value.Contains("valve chamber") || value.Contains("valve_chamber") || value.Contains("valve-chamber"))
            {
                weight += 10.0;
            }
            else if (value.Contains("valve"))
            {
                weight += 6.0;
            }

            if (value.Contains("electrical chamber") || value.Contains("electrical_chamber") || value.Contains("electrical-chamber"))
            {
                weight += 10.0;
            }
            else if (value.Contains("electrical") || value.Contains("electric"))
            {
                weight += 6.0;
            }

            if (value.Contains("pipe") || value.Contains("water"))
            {
                weight += 4.0;
            }

            if (value.Contains("chamber"))
            {
                weight += 2.0;
            }

            return weight;
        }

        private static bool IsAhuCustomFamily(FamilyInstance instance, RoomCustomFamilyOption option)
        {
            if (option != null &&
                (ContainsAhuToken(option.Key) ||
                 ContainsAhuToken(option.DisplayName) ||
                 ContainsAhuToken(option.FileName) ||
                 ContainsAhuToken(option.OriginalFileName) ||
                 ContainsAhuToken(option.StoredFileName)))
            {
                return true;
            }

            FamilySymbol symbol = instance != null ? instance.Symbol : null;
            return ContainsAhuToken(instance != null ? instance.Name : string.Empty) ||
                   ContainsAhuToken(symbol != null ? symbol.Name : string.Empty) ||
                   ContainsAhuToken(symbol != null ? symbol.FamilyName : string.Empty) ||
                   ContainsAhuToken(symbol != null && symbol.Family != null ? symbol.Family.Name : string.Empty);
        }

        private static bool ContainsAhuToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.ToLowerInvariant();
            return value.Contains("ahu") ||
                   value.Contains("air handling") ||
                   value.Contains("air-handling") ||
                   value.Contains("airhandling");
        }

        private static bool IsUsableDirection(XYZ vector)
        {
            return vector != null && vector.GetLength() >= 1e-9;
        }

        private static XYZ NegateXY(XYZ vector)
        {
            return vector == null ? null : new XYZ(-vector.X, -vector.Y, 0.0);
        }

        private static XYZ Flatten(XYZ v)
        {
            if (v == null)
            {
                return null;
            }

            return new XYZ(v.X, v.Y, 0.0);
        }

        private static double SignedAngleOnXY(XYZ from, XYZ to)
        {
            if (from == null || to == null)
            {
                return 0.0;
            }

            double crossZ = from.X * to.Y - from.Y * to.X;
            double dot = from.X * to.X + from.Y * to.Y;
            return Math.Atan2(crossZ, dot);
        }

        private static double HorizontalDistance(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string FormatVector(XYZ vector)
        {
            if (vector == null)
            {
                return string.Empty;
            }

            XYZ flat = Flatten(vector);
            if (flat == null || flat.GetLength() < 1e-9)
            {
                return string.Empty;
            }

            XYZ normalized = flat.Normalize();
            return normalized.X.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   normalized.Y.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   normalized.Z.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string FormatMm(double internalLength)
        {
            if (double.IsNaN(internalLength) || double.IsInfinity(internalLength) || internalLength == double.MaxValue)
            {
                return string.Empty;
            }

            double mm = UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
            return mm.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string FormatElementId(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId
                ? id.IntegerValue.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return string.Empty;
            }

            return point.X.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("F3", CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("F3", CultureInfo.InvariantCulture);
        }

        private sealed class AhuConfiguredModuleCenter
        {
            public string ModuleCode { get; set; }
            public string Name { get; set; }
            public int GridRow { get; set; }
            public int GridColumn { get; set; }
            public XYZ Center { get; set; }
        }

        private sealed class AhuCoreBounds
        {
            private readonly List<string> _names = new List<string>();
            private bool _hasBounds;
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            public int Count { get; private set; }

            public XYZ Center
            {
                get
                {
                    if (!_hasBounds)
                    {
                        return null;
                    }

                    return new XYZ(
                        (_minX + _maxX) * 0.5,
                        (_minY + _maxY) * 0.5,
                        (_minZ + _maxZ) * 0.5);
                }
            }

            public string NameSummary
            {
                get
                {
                    if (_names.Count == 0)
                    {
                        return string.Empty;
                    }

                    return string.Join("|", _names.Distinct().Take(8));
                }
            }

            public void Include(BoundingBoxXYZ box, string name)
            {
                if (box == null || box.Min == null || box.Max == null)
                {
                    return;
                }

                IncludePoint(new XYZ(Math.Min(box.Min.X, box.Max.X), Math.Min(box.Min.Y, box.Max.Y), Math.Min(box.Min.Z, box.Max.Z)), name);
                IncludePoint(new XYZ(Math.Max(box.Min.X, box.Max.X), Math.Max(box.Min.Y, box.Max.Y), Math.Max(box.Min.Z, box.Max.Z)), name);
            }

            public void IncludePoint(XYZ point, string name)
            {
                if (point == null)
                {
                    return;
                }

                if (!_hasBounds)
                {
                    _minX = point.X;
                    _minY = point.Y;
                    _minZ = point.Z;
                    _maxX = point.X;
                    _maxY = point.Y;
                    _maxZ = point.Z;
                    _hasBounds = true;
                }
                else
                {
                    _minX = Math.Min(_minX, point.X);
                    _minY = Math.Min(_minY, point.Y);
                    _minZ = Math.Min(_minZ, point.Z);
                    _maxX = Math.Max(_maxX, point.X);
                    _maxY = Math.Max(_maxY, point.Y);
                    _maxZ = Math.Max(_maxZ, point.Z);
                }

                Count++;
                if (!string.IsNullOrWhiteSpace(name) && _names.Count < 24)
                {
                    _names.Add(name);
                }
            }
        }

        private sealed class RoomAxisInfo
        {
            public XYZ LongAxis { get; set; }
            public XYZ ShortAxis { get; set; }
            public double LongLength { get; set; }
            public string Source { get; set; }
        }

        private sealed class DoorWallSideInfo
        {
            public XYZ Axis { get; set; }
            public int Sign { get; set; }
            public double Projection { get; set; }
            public string Mode { get; set; }
        }

        private sealed class AxisLockedOrientationCandidate
        {
            public string Name { get; set; }
            public XYZ TargetLongAxis { get; set; }
            public XYZ PredictedServiceSide { get; set; }
            public XYZ PredictedServicePoint { get; set; }
            public double Angle { get; set; }
            public double ServiceDot { get; set; }
            public double ServiceDoorWallProjection { get; set; }
            public bool ServiceOnDoorWallSide { get; set; }
            public double DoorWallSideScore { get; set; }
            public double ServiceDistance { get; set; }
            public double OppositeServiceDistance { get; set; }
            public double ServiceScore { get; set; }
            public double ServiceTowardDoorScore { get; set; }
            public double ServiceAngleToDoorDeg { get; set; }
            public double AxisScore { get; set; }
            public double FitScore { get; set; }
            public double Score { get; set; }
        }

        private sealed class PipeConnectorSideCandidate
        {
            public XYZ Origin { get; set; }
            public XYZ Direction { get; set; }
            public double Distance { get; set; }
            public double Weight { get; set; }
            public string Source { get; set; }
        }

        private sealed class NamedServiceCandidate
        {
            public XYZ Direction { get; set; }
            public XYZ ReferencePoint { get; set; }
            public double Weight { get; set; }
            public string Name { get; set; }
        }

        private sealed class MaintenanceSpaceFitResult
        {
            public string Status { get; set; }
            public string Mode { get; set; }
            public int SolidCount { get; set; }
            public int CheckPointCount { get; set; }
            public int OutsidePointCount { get; set; }
            public int TouchPointCount { get; set; }
            public double MinBoundaryDistance { get; set; }
            public double MaxOverflowDistance { get; set; }
            public double TouchToleranceMm { get; set; }
            public double OutsideToleranceMm { get; set; }
            public string FailedSide { get; set; }

            public bool IsOk
            {
                get
                {
                    return string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Status, "Skipped", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Status, "NotChecked", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private sealed class MaintenanceSpaceFootprint
        {
            public string Source { get; set; }
            public List<XYZ> HullPoints { get; set; }
        }

        private sealed class MaintenanceSpaceCollectionStats
        {
            public int TotalSolidCount { get; set; }
            public int NamedCount { get; set; }
            public int MaterialCount { get; set; }
            public int TransparentFallbackCount { get; set; }
        }

        private enum MaintenanceSpaceSolidKind
        {
            None = 0,
            NamedSubcategory = 1,
            MaterialName = 2,
            TransparentFallback = 3
        }


        private sealed class DoorTargetCandidate
        {
            public XYZ Center { get; set; }
            public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
            public string Source { get; set; }
            public int Priority { get; set; } = 100;
            public double BoundaryDistance { get; set; } = double.MaxValue;
            public double DistanceToPlacement { get; set; } = double.MaxValue;
        }


        internal sealed class PlacementAnalysisGeometry
        {
            public List<XYZ> CoreHull { get; set; } = new List<XYZ>();
            public XYZ LocalRight { get; set; }
            public XYZ LocalBottom { get; set; }
            public string Mode { get; set; }
        }

        /// <summary>
        /// Read-only bridge for post-placement spatial reporting.
        /// It reuses the same physical-core filtering and configured AHU-local axes
        /// already used by the local placement solver. No move/rotate/delete operation
        /// is performed here, so placement behaviour remains unchanged.
        /// </summary>
        internal static bool TryGetPlacedAhuAnalysisGeometry(
            Document doc,
            ElementId instanceId,
            string familyKey,
            out PlacementAnalysisGeometry geometry,
            out string error)
        {
            geometry = null;
            error = string.Empty;

            if (doc == null ||
                instanceId == null ||
                instanceId == ElementId.InvalidElementId)
            {
                error = "Placed AHU instance is unavailable.";
                return false;
            }

            FamilyInstance instance = doc.GetElement(instanceId) as FamilyInstance;
            if (instance == null)
            {
                error = "Placed AHU family instance was not found.";
                return false;
            }

            if (!TryResolveConfiguredAhuLocalAxes(
                    doc,
                    instance,
                    familyKey,
                    out XYZ localRight,
                    out XYZ localBottom,
                    out string localAxisMode))
            {
                error = "AHU local axes could not be resolved.";
                return false;
            }

            List<XYZ> corePoints = CollectEquipmentCorePlanPoints(
                doc,
                instance,
                out string corePointMode);
            List<XYZ> coreHull = ComputeConvexHullXY(corePoints);
            if (coreHull == null || coreHull.Count < 3)
            {
                error = "AHU physical body footprint could not be resolved.";
                return false;
            }

            List<XYZ> validHull = coreHull
                .Where(x => x != null && IsUsablePlanPoint(x))
                .ToList();
            if (validHull.Count < 3)
            {
                error = "AHU physical body footprint contains no usable plan points.";
                return false;
            }

            geometry = new PlacementAnalysisGeometry
            {
                CoreHull = validHull,
                LocalRight = localRight.Normalize(),
                LocalBottom = localBottom.Normalize(),
                Mode = (localAxisMode ?? string.Empty) + "/" + (corePointMode ?? string.Empty)
            };
            return true;
        }

        private static void ApplyMetadata(FamilyInstance instance, string roomKey, string familyKey)
        {
            if (instance == null)
            {
                return;
            }

            string value = BuildMetadataValue(roomKey, familyKey);
            Parameter comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set(value);
            }

            Parameter mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (mark != null && !mark.IsReadOnly)
            {
                mark.Set(value);
            }
        }

        private static bool HasManagedMetadata(FamilyInstance instance, string expectedPrefix)
        {
            if (instance == null || string.IsNullOrWhiteSpace(expectedPrefix))
            {
                return false;
            }

            string comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;
            if (comments.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
            return mark.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractFamilyKey(FamilyInstance instance, string roomKey, out string familyKey)
        {
            familyKey = string.Empty;
            if (instance == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            string prefix = MetadataPrefix + roomKey + "__";
            string[] candidates =
            {
                instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty,
                instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty
            };

            foreach (string candidate in candidates)
            {
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                familyKey = candidate.Substring(prefix.Length);
                return !string.IsNullOrWhiteSpace(familyKey);
            }

            return false;
        }
    }

    /// <summary>
    /// Post-placement spatial violation analyzer for regular four-sided rooms.
    ///
    /// Scope intentionally kept separate from the AHU placement solver:
    ///   - does NOT change AHU X/Y/rotation;
    ///   - does NOT delete the AHU;
    ///   - checks the already placed AHU physical body plus configured
    ///     Maintenance Clearance depths against the room clear rectangle;
    ///   - checks the same required-clearance envelope against every existing
    ///     Restricted Area and reports approximate overlap area.
    ///
    /// The analyzer projects both room boundary and AHU footprint onto the AHU's
    /// configured local Right/Bottom axes. This keeps the calculation valid for a
    /// regular rectangular room even when the room itself is rotated in plan.
    /// </summary>
    internal static class AhuPlacementViolationAnalyzerService
    {
        private const double MillimetersPerFoot = 304.8;
        private const double ReportToleranceMm = 1.0;
        private const double RestrictedOverlapToleranceM2 = 0.0001;
        private const double SquareMetersPerSquareFoot = 0.09290304;
        private const double MaxReasonableCoordinateFeet = 1000000.0;

        internal sealed class RestrictedAreaConflict
        {
            public string ObstacleId { get; set; }
            public string Name { get; set; }
            public int ElementIdValue { get; set; }
            public double OverlapAreaM2 { get; set; }
        }

        internal sealed class AnalysisResult
        {
            public bool Evaluated { get; set; }
            public string Error { get; set; }

            public bool IsPhysicalDimensionOversized { get; set; }
            public bool HasCurrentBoundaryOverrun { get; set; }
            public List<RestrictedAreaConflict> RestrictedAreaConflicts { get; } =
                new List<RestrictedAreaConflict>();

            public bool HasRestrictedAreaViolation
            {
                get
                {
                    return RestrictedAreaConflicts.Any(
                        x => x != null && x.OverlapAreaM2 > RestrictedOverlapToleranceM2);
                }
            }

            public bool HasMultipleSpatialViolations
            {
                get
                {
                    return IsPhysicalDimensionOversized && HasRestrictedAreaViolation;
                }
            }

            public bool HasAnyViolation
            {
                get
                {
                    return IsPhysicalDimensionOversized || HasRestrictedAreaViolation;
                }
            }

            public double TotalRestrictedOverlapAreaM2
            {
                get
                {
                    return RestrictedAreaConflicts
                        .Where(x => x != null)
                        .Sum(x => Math.Max(0.0, x.OverlapAreaM2));
                }
            }

            public double PhysicalBodyLengthMm { get; set; }
            public double PhysicalBodyWidthMm { get; set; }
            public double RequiredLengthMm { get; set; }
            public double RequiredWidthMm { get; set; }
            public double AvailableLengthMm { get; set; }
            public double AvailableWidthMm { get; set; }
            public double LengthExceedsMm { get; set; }
            public double WidthExceedsMm { get; set; }

            public double LeftExceedsMm { get; set; }
            public double RightExceedsMm { get; set; }
            public double TopExceedsMm { get; set; }
            public double BottomExceedsMm { get; set; }

            public double MaintenanceTopMm { get; set; }
            public double MaintenanceBottomMm { get; set; }
            public double MaintenanceLeftMm { get; set; }
            public double MaintenanceRightMm { get; set; }

            public string StatusCode
            {
                get
                {
                    if (HasMultipleSpatialViolations)
                    {
                        return "MultipleSpatialViolations";
                    }

                    if (HasRestrictedAreaViolation)
                    {
                        return "RestrictedAreaViolation";
                    }

                    if (IsPhysicalDimensionOversized)
                    {
                        return "PhysicalDimensionOversized";
                    }

                    return string.Empty;
                }
            }

            public string StatusTitle
            {
                get
                {
                    if (HasMultipleSpatialViolations)
                    {
                        return "Multiple Spatial Violations";
                    }

                    if (HasRestrictedAreaViolation)
                    {
                        return "Restricted Area Violation";
                    }

                    if (IsPhysicalDimensionOversized)
                    {
                        return "Physical Dimension Oversized";
                    }

                    return string.Empty;
                }
            }

            public string BuildWarningMessage()
            {
                if (HasMultipleSpatialViolations)
                {
                    return BuildMultipleViolationMessage();
                }

                if (HasRestrictedAreaViolation)
                {
                    return BuildRestrictedAreaMessage();
                }

                if (IsPhysicalDimensionOversized)
                {
                    return BuildPhysicalDimensionMessage();
                }

                return string.Empty;
            }

            private string BuildPhysicalDimensionMessage()
            {
                // UI text is intentionally limited to the customer-approved content.
                // Detailed geometry, room-clearance and side-overrun diagnostics remain
                // available in [AhuPlacementViolation] logs below.
                return
                    "The physical footprint of the equipment exceeds the maximum clear dimensions of the target room. " +
                    "Please select a more compact equipment or modify the room dimensions in the model." +
                    Environment.NewLine +
                    "Length exceeds (mm): " + FormatMm(LengthExceedsMm) +
                    Environment.NewLine +
                    "Width exceeds (mm): " + FormatMm(WidthExceedsMm);
            }

            private string BuildRestrictedAreaMessage()
            {
                // Keep the customer-approved wording.  Overlap Area replaces the
                // original Encroachment depth metric by confirmed requirement.
                return
                    "The equipment's bounding box encroaches on a defined Restricted Area within this room. " +
                    "Please remove or modify the zone in Restricted Area management, or select smaller equipment to avoid this area." +
                    Environment.NewLine +
                    BuildRestrictedAreaConflictText();
            }

            private string BuildMultipleViolationMessage()
            {
                return
                    "The physical footprint of the equipment exceeds the maximum clear dimensions of the target room, " +
                    "and its bounding box simultaneously encroaches on a defined Restricted Area." +
                    Environment.NewLine +
                    "Length exceeds (mm): " + FormatMm(LengthExceedsMm) +
                    Environment.NewLine +
                    "Width exceeds (mm): " + FormatMm(WidthExceedsMm) +
                    Environment.NewLine +
                    BuildRestrictedAreaConflictText();
            }

            private string BuildRestrictedAreaConflictText()
            {
                List<RestrictedAreaConflict> conflicts = RestrictedAreaConflicts
                    .Where(x => x != null && x.OverlapAreaM2 > RestrictedOverlapToleranceM2)
                    .OrderByDescending(x => x.OverlapAreaM2)
                    .ToList();

                if (conflicts.Count == 0)
                {
                    return string.Empty;
                }

                List<string> lines = new List<string>();
                foreach (RestrictedAreaConflict conflict in conflicts)
                {
                    lines.Add(
                        "Conflicting Zone: " +
                        (string.IsNullOrWhiteSpace(conflict.Name)
                            ? "Restricted Area"
                            : conflict.Name));
                    lines.Add(
                        "Overlap Area (m²): " +
                        FormatAreaM2(conflict.OverlapAreaM2));
                }

                // Keep the UI output strictly to the customer-required fields.
                // The aggregate overlap area is still retained in diagnostics via
                // TotalRestrictedOverlapAreaM2 and [AhuPlacementViolation] logging.
                return string.Join(Environment.NewLine, lines);
            }
        }

        internal static AnalysisResult Analyze(
            Document doc,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            ElementId placedInstanceId)
        {
            AnalysisResult result = new AnalysisResult();

            if (doc == null || room == null || option == null)
            {
                result.Error = "Required placement analysis input is missing.";
                Log(result, room, option, placedInstanceId, "InputMissing");
                return result;
            }

            if (!RoomCustomFamilyPlacementService.TryGetPlacedAhuAnalysisGeometry(
                    doc,
                    placedInstanceId,
                    option.Key,
                    out RoomCustomFamilyPlacementService.PlacementAnalysisGeometry geometry,
                    out string geometryError))
            {
                result.Error = geometryError ?? "AHU physical geometry could not be resolved.";
                Log(result, room, option, placedInstanceId, "GeometryUnavailable");
                return result;
            }

            List<XYZ> roomPoints = ResolveRoomPlanPoints(room);
            if (roomPoints.Count < 4)
            {
                result.Error = "Regular room boundary points are unavailable.";
                Log(result, room, option, placedInstanceId, "RoomBoundaryUnavailable");
                return result;
            }

            XYZ localRight = FlattenAndNormalize(geometry.LocalRight);
            XYZ localBottom = FlattenAndNormalize(geometry.LocalBottom);
            if (localRight == null || localBottom == null)
            {
                result.Error = "AHU local analysis axes are unavailable.";
                Log(result, room, option, placedInstanceId, "AxisUnavailable");
                return result;
            }

            if (!TryProject(roomPoints, localRight, out double roomRightMin, out double roomRightMax) ||
                !TryProject(roomPoints, localBottom, out double roomBottomMin, out double roomBottomMax) ||
                !TryProject(geometry.CoreHull, localRight, out double coreRightMin, out double coreRightMax) ||
                !TryProject(geometry.CoreHull, localBottom, out double coreBottomMin, out double coreBottomMax))
            {
                result.Error = "Room/AHU projection could not be resolved.";
                Log(result, room, option, placedInstanceId, "ProjectionFailed");
                return result;
            }

            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> maintenanceRows =
                RoomCustomFamilyCatalogService.GetMaintenanceSpaces(option.Key);

            double topMm = ResolveMaintenanceDepth(maintenanceRows, "Top");
            double bottomMm = ResolveMaintenanceDepth(maintenanceRows, "Bottom");
            double leftMm = ResolveMaintenanceDepth(maintenanceRows, "Left");
            double rightMm = ResolveMaintenanceDepth(maintenanceRows, "Right");

            result.MaintenanceTopMm = topMm;
            result.MaintenanceBottomMm = bottomMm;
            result.MaintenanceLeftMm = leftMm;
            result.MaintenanceRightMm = rightMm;

            double coreRightMm = Math.Max(0.0, (coreRightMax - coreRightMin) * MillimetersPerFoot);
            double coreBottomMm = Math.Max(0.0, (coreBottomMax - coreBottomMin) * MillimetersPerFoot);
            double roomRightMm = Math.Max(0.0, (roomRightMax - roomRightMin) * MillimetersPerFoot);
            double roomBottomMm = Math.Max(0.0, (roomBottomMax - roomBottomMin) * MillimetersPerFoot);

            double requiredRightMm = coreRightMm + leftMm + rightMm;
            double requiredBottomMm = coreBottomMm + topMm + bottomMm;

            // "Length" and "Width" stay tied to the AHU physical body rather than
            // world X/Y. The longer physical-body axis is reported as Length.
            bool rightAxisIsLength = coreRightMm >= coreBottomMm;
            result.PhysicalBodyLengthMm = rightAxisIsLength ? coreRightMm : coreBottomMm;
            result.PhysicalBodyWidthMm = rightAxisIsLength ? coreBottomMm : coreRightMm;
            result.RequiredLengthMm = rightAxisIsLength ? requiredRightMm : requiredBottomMm;
            result.RequiredWidthMm = rightAxisIsLength ? requiredBottomMm : requiredRightMm;
            result.AvailableLengthMm = rightAxisIsLength ? roomRightMm : roomBottomMm;
            result.AvailableWidthMm = rightAxisIsLength ? roomBottomMm : roomRightMm;

            result.LengthExceedsMm = Math.Max(0.0, result.RequiredLengthMm - result.AvailableLengthMm);
            result.WidthExceedsMm = Math.Max(0.0, result.RequiredWidthMm - result.AvailableWidthMm);

            double leftFeet = leftMm / MillimetersPerFoot;
            double rightFeet = rightMm / MillimetersPerFoot;
            double topFeet = topMm / MillimetersPerFoot;
            double bottomFeet = bottomMm / MillimetersPerFoot;

            // Along LocalRight: Left is the minimum side, Right is the maximum side.
            double envelopeRightMin = coreRightMin - leftFeet;
            double envelopeRightMax = coreRightMax + rightFeet;

            // Along LocalBottom: Top is the minimum side, Bottom is the maximum side.
            double envelopeBottomMin = coreBottomMin - topFeet;
            double envelopeBottomMax = coreBottomMax + bottomFeet;

            result.LeftExceedsMm =
                Math.Max(0.0, (roomRightMin - envelopeRightMin) * MillimetersPerFoot);
            result.RightExceedsMm =
                Math.Max(0.0, (envelopeRightMax - roomRightMax) * MillimetersPerFoot);
            result.TopExceedsMm =
                Math.Max(0.0, (roomBottomMin - envelopeBottomMin) * MillimetersPerFoot);
            result.BottomExceedsMm =
                Math.Max(0.0, (envelopeBottomMax - roomBottomMax) * MillimetersPerFoot);

            result.IsPhysicalDimensionOversized =
                result.LengthExceedsMm > ReportToleranceMm ||
                result.WidthExceedsMm > ReportToleranceMm;

            result.HasCurrentBoundaryOverrun =
                result.LeftExceedsMm > ReportToleranceMm ||
                result.RightExceedsMm > ReportToleranceMm ||
                result.TopExceedsMm > ReportToleranceMm ||
                result.BottomExceedsMm > ReportToleranceMm;

            // Phase-2: Restricted Area analysis. This is deliberately post-placement
            // reporting only: no AHU move/rotation is performed here. The tested shape
            // is the same required-clearance envelope used by the physical-dimension
            // report (real AHU physical body + M1/M2/M3/M4 Maintenance Clearance).
            AnalyzeRestrictedAreaOverlaps(
                doc,
                placedInstanceId,
                localRight,
                localBottom,
                envelopeRightMin,
                envelopeRightMax,
                envelopeBottomMin,
                envelopeBottomMax,
                result);

            result.Evaluated = true;
            Log(result, room, option, placedInstanceId, geometry.Mode ?? string.Empty);
            return result;
        }

        private static void AnalyzeRestrictedAreaOverlaps(
            Document doc,
            ElementId placedInstanceId,
            XYZ localRight,
            XYZ localBottom,
            double envelopeRightMin,
            double envelopeRightMax,
            double envelopeBottomMin,
            double envelopeBottomMax,
            AnalysisResult result)
        {
            if (doc == null ||
                result == null ||
                localRight == null ||
                localBottom == null)
            {
                return;
            }

            IList<CadToRevit.Models.PathObstacleRecord> records;
            try
            {
                records = CadToRevit.Services.PathObstacles.PathObstacleStoreService.Load(doc);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementViolation.Restricted] Restricted-area load skipped: " +
                    ex.Message);
                return;
            }

            if (records == null || records.Count == 0)
            {
                return;
            }

            string placedLevelName = ResolveElementLevelName(doc, placedInstanceId);

            List<XYZ> envelopeCorners = BuildEnvelopeCorners(
                localRight,
                localBottom,
                envelopeRightMin,
                envelopeRightMax,
                envelopeBottomMin,
                envelopeBottomMax,
                0.0);

            if (envelopeCorners.Count != 4)
            {
                return;
            }

            double envelopeMinX = envelopeCorners.Min(x => x.X);
            double envelopeMaxX = envelopeCorners.Max(x => x.X);
            double envelopeMinY = envelopeCorners.Min(x => x.Y);
            double envelopeMaxY = envelopeCorners.Max(x => x.Y);

            foreach (CadToRevit.Models.PathObstacleRecord record in records)
            {
                if (record == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(placedLevelName) &&
                    !string.IsNullOrWhiteSpace(record.LevelName) &&
                    !string.Equals(
                        placedLevelName.Trim(),
                        record.LevelName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Element obstacle =
                    CadToRevit.Services.PathObstacles.PathObstacleStoreService.FindElement(
                        doc,
                        record);

                if (obstacle == null)
                {
                    continue;
                }

                BoundingBoxXYZ obstacleBox = null;
                try
                {
                    obstacleBox = obstacle.get_BoundingBox(null);
                }
                catch
                {
                    obstacleBox = null;
                }

                if (obstacleBox == null ||
                    obstacleBox.Min == null ||
                    obstacleBox.Max == null)
                {
                    continue;
                }

                const double PlanToleranceFeet = 1.0 / 304.8;
                if (obstacleBox.Max.X < envelopeMinX - PlanToleranceFeet ||
                    obstacleBox.Min.X > envelopeMaxX + PlanToleranceFeet ||
                    obstacleBox.Max.Y < envelopeMinY - PlanToleranceFeet ||
                    obstacleBox.Min.Y > envelopeMaxY + PlanToleranceFeet)
                {
                    continue;
                }

                double overlapAreaM2 = CalculateRestrictedOverlapAreaM2(
                    obstacle,
                    localRight,
                    localBottom,
                    envelopeRightMin,
                    envelopeRightMax,
                    envelopeBottomMin,
                    envelopeBottomMax,
                    obstacleBox);

                if (overlapAreaM2 <= RestrictedOverlapToleranceM2)
                {
                    continue;
                }

                result.RestrictedAreaConflicts.Add(
                    new RestrictedAreaConflict
                    {
                        ObstacleId = record.ObstacleId ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(record.Name)
                            ? "Restricted Area"
                            : record.Name.Trim(),
                        ElementIdValue = obstacle.Id.IntegerValue,
                        OverlapAreaM2 = overlapAreaM2
                    });
            }
        }

        private static double CalculateRestrictedOverlapAreaM2(
            Element obstacle,
            XYZ localRight,
            XYZ localBottom,
            double envelopeRightMin,
            double envelopeRightMax,
            double envelopeBottomMin,
            double envelopeBottomMax,
            BoundingBoxXYZ obstacleBox)
        {
            if (obstacle == null || obstacleBox == null)
            {
                return 0.0;
            }

            double minZ = obstacleBox.Min != null ? obstacleBox.Min.Z : 0.0;
            double maxZ = obstacleBox.Max != null ? obstacleBox.Max.Z : minZ;
            double paddingFeet = 1.0 / 304.8;
            double baseZ = minZ - paddingFeet;
            double height = Math.Max(
                2.0 * paddingFeet,
                (maxZ - minZ) + (2.0 * paddingFeet));

            Solid clearanceSolid = CreateClearanceEnvelopeSolid(
                localRight,
                localBottom,
                envelopeRightMin,
                envelopeRightMax,
                envelopeBottomMin,
                envelopeBottomMax,
                baseZ,
                height);

            if (clearanceSolid == null)
            {
                return 0.0;
            }

            double totalAreaSquareFeet = 0.0;

            foreach (Solid obstacleSolid in CollectElementSolids(obstacle))
            {
                if (obstacleSolid == null ||
                    obstacleSolid.Faces == null ||
                    obstacleSolid.Faces.Size == 0)
                {
                    continue;
                }

                try
                {
                    Solid intersection =
                        BooleanOperationsUtils.ExecuteBooleanOperation(
                            clearanceSolid,
                            obstacleSolid,
                            BooleanOperationsType.Intersect);

                    if (intersection == null ||
                        intersection.Faces == null ||
                        intersection.Faces.Size == 0)
                    {
                        continue;
                    }

                    double horizontalTopArea = 0.0;
                    foreach (Face face in intersection.Faces)
                    {
                        PlanarFace planarFace = face as PlanarFace;
                        if (planarFace == null ||
                            planarFace.FaceNormal == null ||
                            planarFace.FaceNormal.Z < 0.99)
                        {
                            continue;
                        }

                        if (IsFinite(planarFace.Area) && planarFace.Area > 0.0)
                        {
                            horizontalTopArea += planarFace.Area;
                        }
                    }

                    if (horizontalTopArea > 0.0)
                    {
                        totalAreaSquareFeet += horizontalTopArea;
                        continue;
                    }

                    // Safe fallback for the Restricted Area solids created by this
                    // add-in: they are vertical extrusions, so volume / height gives
                    // their plan overlap area.
                    double intersectionHeight = ResolveSolidHeight(intersection);
                    if (intersectionHeight > 1e-9 &&
                        IsFinite(intersection.Volume) &&
                        intersection.Volume > 0.0)
                    {
                        totalAreaSquareFeet += intersection.Volume / intersectionHeight;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuPlacementViolation.Restricted] Boolean intersection skipped. ElementId=" +
                        obstacle.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                        ", Error=" + ex.Message);
                }
            }

            if (!IsFinite(totalAreaSquareFeet) || totalAreaSquareFeet <= 0.0)
            {
                return 0.0;
            }

            return totalAreaSquareFeet * SquareMetersPerSquareFoot;
        }

        private static Solid CreateClearanceEnvelopeSolid(
            XYZ localRight,
            XYZ localBottom,
            double rightMin,
            double rightMax,
            double bottomMin,
            double bottomMax,
            double baseZ,
            double height)
        {
            List<XYZ> corners = BuildEnvelopeCorners(
                localRight,
                localBottom,
                rightMin,
                rightMax,
                bottomMin,
                bottomMax,
                baseZ);

            if (corners.Count != 4 || height <= 1e-9)
            {
                return null;
            }

            try
            {
                CurveLoop loop = new CurveLoop();
                for (int i = 0; i < corners.Count; i++)
                {
                    XYZ start = corners[i];
                    XYZ end = corners[(i + 1) % corners.Count];
                    if (start == null ||
                        end == null ||
                        start.DistanceTo(end) < 1e-9)
                    {
                        return null;
                    }

                    loop.Append(Line.CreateBound(start, end));
                }

                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop },
                    XYZ.BasisZ,
                    height);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementViolation.Restricted] Clearance envelope solid could not be created: " +
                    ex.Message);
                return null;
            }
        }

        private static List<XYZ> BuildEnvelopeCorners(
            XYZ localRight,
            XYZ localBottom,
            double rightMin,
            double rightMax,
            double bottomMin,
            double bottomMax,
            double z)
        {
            List<XYZ> corners = new List<XYZ>();

            XYZ p1 = TryResolvePlanPointFromAxisCoordinates(
                localRight, localBottom, rightMin, bottomMin, z);
            XYZ p2 = TryResolvePlanPointFromAxisCoordinates(
                localRight, localBottom, rightMax, bottomMin, z);
            XYZ p3 = TryResolvePlanPointFromAxisCoordinates(
                localRight, localBottom, rightMax, bottomMax, z);
            XYZ p4 = TryResolvePlanPointFromAxisCoordinates(
                localRight, localBottom, rightMin, bottomMax, z);

            if (p1 != null && p2 != null && p3 != null && p4 != null)
            {
                corners.Add(p1);
                corners.Add(p2);
                corners.Add(p3);
                corners.Add(p4);
            }

            return corners;
        }

        private static XYZ TryResolvePlanPointFromAxisCoordinates(
            XYZ localRight,
            XYZ localBottom,
            double rightCoordinate,
            double bottomCoordinate,
            double z)
        {
            if (localRight == null || localBottom == null)
            {
                return null;
            }

            // rightCoordinate = dot(P, localRight)
            // bottomCoordinate = dot(P, localBottom)
            // Solve the 2x2 system rather than assuming the axes are perfectly
            // orthogonal; this protects rotated family/room cases from drift.
            double determinant =
                (localRight.X * localBottom.Y) -
                (localRight.Y * localBottom.X);

            if (!IsFinite(determinant) || Math.Abs(determinant) < 1e-9)
            {
                return null;
            }

            double x =
                ((rightCoordinate * localBottom.Y) -
                 (localRight.Y * bottomCoordinate)) /
                determinant;

            double y =
                ((localRight.X * bottomCoordinate) -
                 (rightCoordinate * localBottom.X)) /
                determinant;

            if (!IsFinite(x) || !IsFinite(y))
            {
                return null;
            }

            return new XYZ(x, y, z);
        }

        private static IEnumerable<Solid> CollectElementSolids(Element element)
        {
            if (element == null)
            {
                yield break;
            }

            GeometryElement geometry = null;
            try
            {
                Options options = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    DetailLevel = ViewDetailLevel.Fine
                };
                geometry = element.get_Geometry(options);
            }
            catch
            {
                geometry = null;
            }

            if (geometry == null)
            {
                yield break;
            }

            foreach (Solid solid in CollectSolidsRecursive(geometry))
            {
                yield return solid;
            }
        }

        private static IEnumerable<Solid> CollectSolidsRecursive(GeometryElement geometry)
        {
            if (geometry == null)
            {
                yield break;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                Solid solid = geometryObject as Solid;
                if (solid != null)
                {
                    if (solid.Faces != null && solid.Faces.Size > 0)
                    {
                        yield return solid;
                    }

                    continue;
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance == null)
                {
                    continue;
                }

                GeometryElement instanceGeometry = null;
                try
                {
                    instanceGeometry = instance.GetInstanceGeometry();
                }
                catch
                {
                    instanceGeometry = null;
                }

                if (instanceGeometry == null)
                {
                    continue;
                }

                foreach (Solid nested in CollectSolidsRecursive(instanceGeometry))
                {
                    yield return nested;
                }
            }
        }

        private static double ResolveSolidHeight(Solid solid)
        {
            if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
            {
                return 0.0;
            }

            double minZ = double.MaxValue;
            double maxZ = double.MinValue;

            foreach (Edge edge in solid.Edges)
            {
                if (edge == null)
                {
                    continue;
                }

                Curve curve = null;
                try
                {
                    curve = edge.AsCurve();
                }
                catch
                {
                    curve = null;
                }

                if (curve == null)
                {
                    continue;
                }

                XYZ a = curve.GetEndPoint(0);
                XYZ b = curve.GetEndPoint(1);

                if (a != null && IsFinite(a.Z))
                {
                    minZ = Math.Min(minZ, a.Z);
                    maxZ = Math.Max(maxZ, a.Z);
                }

                if (b != null && IsFinite(b.Z))
                {
                    minZ = Math.Min(minZ, b.Z);
                    maxZ = Math.Max(maxZ, b.Z);
                }
            }

            if (minZ == double.MaxValue || maxZ == double.MinValue)
            {
                return 0.0;
            }

            return Math.Max(0.0, maxZ - minZ);
        }

        private static string ResolveElementLevelName(Document doc, ElementId elementId)
        {
            if (doc == null ||
                elementId == null ||
                elementId == ElementId.InvalidElementId)
            {
                return string.Empty;
            }

            try
            {
                Element element = doc.GetElement(elementId);
                if (element == null ||
                    element.LevelId == null ||
                    element.LevelId == ElementId.InvalidElementId)
                {
                    return string.Empty;
                }

                Element level = doc.GetElement(element.LevelId);
                return level != null ? level.Name ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<XYZ> ResolveRoomPlanPoints(RoomSemanticRecord room)
        {
            List<XYZ> points = (room != null && room.LoopPoints != null
                    ? room.LoopPoints
                    : new List<XYZ>())
                .Where(IsUsablePlanPoint)
                .Select(x => new XYZ(x.X, x.Y, 0.0))
                .ToList();

            if (points.Count >= 4)
            {
                return points;
            }

            BoundingBoxXYZ box = room != null ? room.BBox : null;
            if (box == null || box.Min == null || box.Max == null)
            {
                return points;
            }

            XYZ[] fallback =
            {
                new XYZ(box.Min.X, box.Min.Y, 0.0),
                new XYZ(box.Max.X, box.Min.Y, 0.0),
                new XYZ(box.Max.X, box.Max.Y, 0.0),
                new XYZ(box.Min.X, box.Max.Y, 0.0)
            };
            return fallback.Where(IsUsablePlanPoint).ToList();
        }

        private static double ResolveMaintenanceDepth(
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> rows,
            string side)
        {
            return (rows ?? Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>())
                .Where(x =>
                    x != null &&
                    string.Equals(x.Side, side, StringComparison.OrdinalIgnoreCase))
                .Select(x => Math.Max(0.0, (double)x.DimensionMm))
                .DefaultIfEmpty(0.0)
                .Max();
        }

        private static bool TryProject(
            IEnumerable<XYZ> points,
            XYZ axis,
            out double min,
            out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;

            XYZ normalized = FlattenAndNormalize(axis);
            if (normalized == null)
            {
                return false;
            }

            int count = 0;
            foreach (XYZ point in points ?? Enumerable.Empty<XYZ>())
            {
                if (!IsUsablePlanPoint(point))
                {
                    continue;
                }

                double value = point.X * normalized.X + point.Y * normalized.Y;
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    continue;
                }

                min = Math.Min(min, value);
                max = Math.Max(max, value);
                count++;
            }

            return count >= 2 &&
                   min != double.MaxValue &&
                   max != double.MinValue &&
                   max >= min;
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
            {
                return null;
            }

            XYZ flat = new XYZ(vector.X, vector.Y, 0.0);
            double length = flat.GetLength();
            if (double.IsNaN(length) ||
                double.IsInfinity(length) ||
                length < 1e-9)
            {
                return null;
            }

            return flat.Normalize();
        }

        private static bool IsUsablePlanPoint(XYZ point)
        {
            if (point == null)
            {
                return false;
            }

            return IsFinite(point.X) &&
                   IsFinite(point.Y) &&
                   Math.Abs(point.X) <= MaxReasonableCoordinateFeet &&
                   Math.Abs(point.Y) <= MaxReasonableCoordinateFeet;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FormatMm(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "-";
            }

            return Math.Max(0.0, value).ToString(
                "0",
                CultureInfo.InvariantCulture);
        }

        private static string FormatAreaM2(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "-";
            }

            return Math.Max(0.0, value).ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        }

        private static void Log(
            AnalysisResult result,
            RoomSemanticRecord room,
            RoomCustomFamilyOption option,
            ElementId instanceId,
            string mode)
        {
            if (result == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[AhuPlacementViolation] Phase=PhysicalDimension" +
                ", RoomKey=" + (room != null ? room.Key ?? string.Empty : string.Empty) +
                ", FamilyKey=" + (option != null ? option.Key ?? string.Empty : string.Empty) +
                ", ElementId=" +
                (instanceId != null
                    ? instanceId.IntegerValue.ToString(CultureInfo.InvariantCulture)
                    : string.Empty) +
                ", Evaluated=" + result.Evaluated +
                ", PhysicalOversized=" + result.IsPhysicalDimensionOversized +
                ", BodyLmm=" + FormatMm(result.PhysicalBodyLengthMm) +
                ", BodyWmm=" + FormatMm(result.PhysicalBodyWidthMm) +
                ", RequiredLmm=" + FormatMm(result.RequiredLengthMm) +
                ", RequiredWmm=" + FormatMm(result.RequiredWidthMm) +
                ", AvailableLmm=" + FormatMm(result.AvailableLengthMm) +
                ", AvailableWmm=" + FormatMm(result.AvailableWidthMm) +
                ", LengthExceedsMm=" + FormatMm(result.LengthExceedsMm) +
                ", WidthExceedsMm=" + FormatMm(result.WidthExceedsMm) +
                ", SideExceedsMm=[L:" + FormatMm(result.LeftExceedsMm) +
                ",R:" + FormatMm(result.RightExceedsMm) +
                ",T:" + FormatMm(result.TopExceedsMm) +
                ",B:" + FormatMm(result.BottomExceedsMm) + "]" +
                ", MaintenanceMm=[L:" + FormatMm(result.MaintenanceLeftMm) +
                ",R:" + FormatMm(result.MaintenanceRightMm) +
                ",T:" + FormatMm(result.MaintenanceTopMm) +
                ",B:" + FormatMm(result.MaintenanceBottomMm) + "]" +
                ", RestrictedViolation=" + result.HasRestrictedAreaViolation +
                ", RestrictedCount=" + result.RestrictedAreaConflicts.Count.ToString(CultureInfo.InvariantCulture) +
                ", RestrictedOverlapM2=" + FormatAreaM2(result.TotalRestrictedOverlapAreaM2) +
                ", Status=" + result.StatusCode +
                ", Mode=" + (mode ?? string.Empty) +
                ", Error=" + (result.Error ?? string.Empty));

            foreach (RestrictedAreaConflict conflict in result.RestrictedAreaConflicts
                .Where(x => x != null)
                .OrderByDescending(x => x.OverlapAreaM2))
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementViolation.Restricted] Zone=" +
                    (conflict.Name ?? string.Empty) +
                    ", ElementId=" +
                    conflict.ElementIdValue.ToString(CultureInfo.InvariantCulture) +
                    ", OverlapAreaM2=" +
                    FormatAreaM2(conflict.OverlapAreaM2));
            }
        }
    }
}
