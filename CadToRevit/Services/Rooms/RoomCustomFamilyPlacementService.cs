using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
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
                // Legacy fallback only for flows that do not have an API orientation.
                // Keep this code for backward compatibility (saved-plan/detail restore,
                // API failure, etc.). It is NOT used by the current Select -> room-fit API
                // insertion path when orientation_deg is present.
                if (!TryOrientPlacedEquipmentTowardRoomDoor(
                        doc,
                        room,
                        option,
                        result.CreatedElementId,
                        result.PlacementPoint,
                        result.LevelId,
                        out orientError,
                        out maintenanceSpaceCheck) &&
                    !string.IsNullOrWhiteSpace(orientError))
                {
                    DiagnosticRecorder.AppendDebug(
                        "[RoomCustomFamily] Legacy door-facing orientation skipped. RoomKey=" + room.Key +
                        ", FamilyKey=" + option.Key +
                        ", ElementId=" + (result.CreatedElementId != null ? result.CreatedElementId.IntegerValue.ToString() : string.Empty) +
                        ", Error=" + orientError);
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
            if (box == null || box.Min == null || box.Max == null)
            {
                return corners;
            }

            double z = (box.Min.Z + box.Max.Z) * 0.5;
            corners.Add(new XYZ(box.Min.X, box.Min.Y, z));
            corners.Add(new XYZ(box.Min.X, box.Max.Y, z));
            corners.Add(new XYZ(box.Max.X, box.Max.Y, z));
            corners.Add(new XYZ(box.Max.X, box.Min.Y, z));
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

        private static void AddUniquePointXY(List<XYZ> points, XYZ point)
        {
            if (points == null || point == null)
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
}
