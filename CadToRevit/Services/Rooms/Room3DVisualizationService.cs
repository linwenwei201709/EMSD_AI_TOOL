using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Common;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class Room3DVisualizationService
    {
        private const double RoomRegionVisualOffsetMm = 80.0;
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, RoomSemanticRecord> LastRoomsByKey = new Dictionary<string, RoomSemanticRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MarkerPointInfo> LastMarkerByRoomKey = new Dictionary<string, MarkerPointInfo>(StringComparer.OrdinalIgnoreCase);
        private static string LastHighlightedRoomKey;

        public static void Refresh(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (doc == null || !(doc.ActiveView is View3D))
            {
                return;
            }

            List<RoomSemanticRecord> validRooms = GetValidMatchedRooms(summary);
            Dictionary<string, Level> levelByRoomKey = BuildLevelByRoomKey(doc, summary);
            Dictionary<string, MarkerPointInfo> markerPoints = BuildMarkerPoints(doc, validRooms);
            Dictionary<string, RoomSemanticRecord> visualRooms = new Dictionary<string, RoomSemanticRecord>(StringComparer.OrdinalIgnoreCase);
            DiagnosticRecorder.AppendDebug("[Room3DVis] RegionThicknessMm=" + Room3DVisualizationConstants.RegionThicknessMm.ToString("F0", CultureInfo.InvariantCulture));

            using (Transaction tx = new Transaction(doc, "Refresh Room 3D Visualization"))
            {
                tx.Start();
                View3D view3D = doc.ActiveView as View3D;
                try
                {
                    // Keep room visualization robust even if template blocks display-style change.
                    ViewDisplayStyleHelper.Ensure3DViewShaded(view3D);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[Room3DVis] Ensure3DViewShaded skipped. Error=" + ex.Message);
                }
                ClearInternal(doc);
                HideManagedMarkersInView(view3D);
                MaterialContext materials = BuildMaterials(doc);
                RefreshStats stats = new RefreshStats();

                foreach (RoomSemanticRecord room in validRooms)
                {
                    try
                    {
                        RoomVisualCreateResult roomResult = CreateRoomShapes(doc, room, markerPoints, materials, false, ResolveRoomLevel(levelByRoomKey, room));
                        stats.Accumulate(roomResult);
                        AppendRoomLog(room, roomResult);
                        if (roomResult != null && roomResult.VisualRoom != null && !string.IsNullOrWhiteSpace(roomResult.VisualRoom.Key))
                        {
                            visualRooms[roomResult.VisualRoom.Key] = roomResult.VisualRoom;
                        }
                    }
                    catch (Exception ex)
                    {
                        stats.Rooms++;
                        stats.RegionSkipped++;
                        stats.MarkerSkipped++;
                        stats.TextSkipped++;
                        DiagnosticRecorder.AppendDebug("[Room3DVis] Room=" + (room?.Key ?? string.Empty) + ", Region=Skipped(Exception), Marker=Skipped(Exception), Text=Skipped(Exception), Error=" + ex.Message);
                    }
                }

                tx.Commit();
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis] Refresh done, Rooms=" + stats.Rooms +
                    ", RegionCreated=" + stats.RegionCreated +
                    ", RegionSkipped=" + stats.RegionSkipped +
                    ", MarkerCreated=" + stats.MarkerCreated +
                    ", MarkerSkipped=" + stats.MarkerSkipped +
                    ", TextCreated=" + stats.TextCreated +
                    ", TextSkipped=" + stats.TextSkipped);
            }

            lock (SyncRoot)
            {
                LastRoomsByKey.Clear();
                LastMarkerByRoomKey.Clear();
                foreach (RoomSemanticRecord room in validRooms)
                {
                    if (room == null || string.IsNullOrWhiteSpace(room.Key))
                    {
                        continue;
                    }

                    LastRoomsByKey[room.Key] = visualRooms.TryGetValue(room.Key, out RoomSemanticRecord visualRoom) ? visualRoom : room;
                    if (markerPoints.TryGetValue(room.Key, out MarkerPointInfo info))
                    {
                        LastMarkerByRoomKey[room.Key] = info;
                    }
                }

                LastHighlightedRoomKey = string.Empty;
            }
        }

        public static void RefreshAndFilterResults(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (doc == null || !(doc.ActiveView is View3D))
            {
                return;
            }

            List<RoomSemanticRecord> validRooms = GetValidMatchedRooms(summary);
            Dictionary<string, Level> levelByRoomKey = BuildLevelByRoomKey(doc, summary);
            Dictionary<string, MarkerPointInfo> markerPoints = BuildMarkerPoints(doc, validRooms);
            Dictionary<string, RoomSemanticRecord> visualRooms = new Dictionary<string, RoomSemanticRecord>(StringComparer.OrdinalIgnoreCase);
            List<RoomSemanticRecord> kept = new List<RoomSemanticRecord>();
            int failed = 0;
            DiagnosticRecorder.AppendDebug("[Room3DVis] RegionThicknessMm=" + Room3DVisualizationConstants.RegionThicknessMm.ToString("F0", CultureInfo.InvariantCulture));

            using (Transaction tx = new Transaction(doc, "Refresh Room 3D Visualization"))
            {
                tx.Start();
                View3D view3D = doc.ActiveView as View3D;
                try
                {
                    ViewDisplayStyleHelper.Ensure3DViewShaded(view3D);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[Room3DVis] Ensure3DViewShaded skipped. Error=" + ex.Message);
                }

                ClearInternal(doc);
                HideManagedMarkersInView(view3D);
                MaterialContext materials = BuildMaterials(doc);
                RefreshStats stats = new RefreshStats();

                foreach (RoomSemanticRecord room in validRooms)
                {
                    RoomVisualCreateResult roomResult = null;
                    try
                    {
                        roomResult = CreateRoomShapes(doc, room, markerPoints, materials, false, ResolveRoomLevel(levelByRoomKey, room));
                        stats.Accumulate(roomResult);
                        AppendRoomLog(room, roomResult);
                        if (roomResult != null && roomResult.VisualRoom != null && !string.IsNullOrWhiteSpace(roomResult.VisualRoom.Key))
                        {
                            visualRooms[roomResult.VisualRoom.Key] = roomResult.VisualRoom;
                        }
                    }
                    catch (Exception ex)
                    {
                        stats.Rooms++;
                        stats.RegionSkipped++;
                        stats.MarkerSkipped++;
                        stats.TextSkipped++;
                        roomResult = new RoomVisualCreateResult
                        {
                            RoomKey = room != null ? (room.Key ?? string.Empty) : string.Empty,
                            RoomName = room != null ? (room.RoomName ?? string.Empty) : string.Empty,
                            RegionCreated = false,
                            RegionReason = "Skipped(Exception:" + ex.Message + ")"
                        };
                    }

                    if (roomResult != null && roomResult.RegionCreated)
                    {
                        kept.Add(room);
                        continue;
                    }

                    failed++;
                    string reason = roomResult != null ? (roomResult.RegionReason ?? "RegionCreated=0") : "RegionCreated=0";
                    if (summary != null && summary.Errors != null)
                    {
                        summary.Errors.Add((room != null ? (room.RoomName ?? string.Empty) : string.Empty) + ": " + reason);
                    }

                    DiagnosticRecorder.AppendDebug("[Room3DVis] Visualization failed, RoomName=" +
                        (room != null ? (room.RoomName ?? string.Empty) : string.Empty) +
                        ", RoomKey=" + (room != null ? (room.Key ?? string.Empty) : string.Empty) +
                        ", LoopPoints=" + ((room != null && room.LoopPoints != null) ? room.LoopPoints.Count.ToString(CultureInfo.InvariantCulture) : "0") +
                        ", Stage=Room3DVisualizationService" +
                        ", Reason=" + reason +
                        ", RemovedFromResults=True");
                }

                tx.Commit();
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis] Refresh done, Rooms=" + stats.Rooms +
                    ", RegionCreated=" + stats.RegionCreated +
                    ", RegionSkipped=" + stats.RegionSkipped +
                    ", MarkerCreated=" + stats.MarkerCreated +
                    ", MarkerSkipped=" + stats.MarkerSkipped +
                    ", TextCreated=" + stats.TextCreated +
                    ", TextSkipped=" + stats.TextSkipped);
            }

            RoomRangeVisualizationService.ApplyFilteredRooms(summary, kept, failed, "Room3DVis");
            lock (SyncRoot)
            {
                LastRoomsByKey.Clear();
                LastMarkerByRoomKey.Clear();
                foreach (RoomSemanticRecord room in kept)
                {
                    if (room == null || string.IsNullOrWhiteSpace(room.Key))
                    {
                        continue;
                    }

                    LastRoomsByKey[room.Key] = visualRooms.TryGetValue(room.Key, out RoomSemanticRecord visualRoom) ? visualRoom : room;
                    if (markerPoints.TryGetValue(room.Key, out MarkerPointInfo info))
                    {
                        LastMarkerByRoomKey[room.Key] = info;
                    }
                }

                LastHighlightedRoomKey = string.Empty;
            }
        }

        public static void HighlightRoom(Document doc, string roomKey)
        {
            if (doc == null || string.IsNullOrWhiteSpace(roomKey) || !(doc.ActiveView is View3D))
            {
                return;
            }

            List<RoomSemanticRecord> rooms;
            Dictionary<string, MarkerPointInfo> markerPoints;
            lock (SyncRoot)
            {
                rooms = LastRoomsByKey.Values.ToList();
                markerPoints = LastMarkerByRoomKey.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            }

            if (rooms.Count == 0)
            {
                return;
            }

            using (Transaction tx = new Transaction(doc, "Highlight Room 3D Visualization"))
            {
                tx.Start();
                View3D view3D = doc.ActiveView as View3D;
                try
                {
                    // Keep room visualization robust even if template blocks display-style change.
                    ViewDisplayStyleHelper.Ensure3DViewShaded(view3D);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[Room3DVis] Ensure3DViewShaded skipped. Error=" + ex.Message);
                }
                ClearInternal(doc);
                HideManagedMarkersInView(view3D);
                MaterialContext materials = BuildMaterials(doc);
                RefreshStats stats = new RefreshStats();

                foreach (RoomSemanticRecord room in rooms)
                {
                    bool highlight = string.Equals(room.Key, roomKey, StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        RoomVisualCreateResult roomResult = CreateRoomShapes(doc, room, markerPoints, materials, highlight, null);
                        stats.Accumulate(roomResult);
                    }
                    catch (Exception ex)
                    {
                        stats.Rooms++;
                        stats.RegionSkipped++;
                        stats.MarkerSkipped++;
                        stats.TextSkipped++;
                        DiagnosticRecorder.AppendDebug("[Room3DVis] Highlight room failed, RoomKey=" + (room?.Key ?? string.Empty) + ", Error=" + ex.Message);
                    }
                }

                tx.Commit();
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis] Highlight done, RoomKey=" + roomKey +
                    ", Rooms=" + stats.Rooms +
                    ", RegionCreated=" + stats.RegionCreated +
                    ", MarkerCreated=" + stats.MarkerCreated +
                    ", TextCreated=" + stats.TextCreated);
            }

            lock (SyncRoot)
            {
                LastHighlightedRoomKey = roomKey;
            }
        }

        public static void Clear(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            using (Transaction tx = new Transaction(doc, "Clear Room 3D Visualization"))
            {
                tx.Start();
                int count = ClearInternal(doc);
                HideManagedMarkersInView(doc.ActiveView as View3D);
                tx.Commit();
                DiagnosticRecorder.AppendDebug("[Room3DVis] Clear done, Deleted=" + count + ".");
            }

            lock (SyncRoot)
            {
                LastRoomsByKey.Clear();
                LastMarkerByRoomKey.Clear();
                LastHighlightedRoomKey = string.Empty;
            }
        }

        private static List<RoomSemanticRecord> GetValidMatchedRooms(TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            return (summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                .Where(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.Key) &&
                    IsVisualRoomStatus(x.Status) &&
                    x.LoopPoints != null &&
                    x.LoopPoints.Count >= 4 &&
                    x.AreaM2 > 0.0)
                .ToList();
        }

        private static bool IsVisualRoomStatus(string status)
        {
            string value = status ?? string.Empty;
            return value.IndexOf("Matched", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "UserDefined", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, Level> BuildLevelByRoomKey(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            Dictionary<string, Level> result = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || summary == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, int> item in summary.SeedLevelIdByKey)
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value <= 0)
                {
                    continue;
                }

                Level level = doc.GetElement(new ElementId(item.Value)) as Level;
                if (level != null)
                {
                    result[item.Key] = level;
                }
            }

            return result;
        }

        private static Level ResolveRoomLevel(Dictionary<string, Level> levelByRoomKey, RoomSemanticRecord room)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.Key) || levelByRoomKey == null)
            {
                return null;
            }

            return levelByRoomKey.TryGetValue(room.Key, out Level level) ? level : null;
        }

        private static Dictionary<string, MarkerPointInfo> BuildMarkerPoints(Document doc, List<RoomSemanticRecord> rooms)
        {
            Dictionary<string, MarkerPointInfo> result = new Dictionary<string, MarkerPointInfo>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || rooms == null || rooms.Count == 0)
            {
                return result;
            }

            List<TargetRoomSeed> seeds = TargetRoomSeedStorageService.LoadSeeds(doc);
            foreach (RoomSemanticRecord room in rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.Key))
                {
                    continue;
                }

                RoomMarkerPlacementInfo placement = ResolveMarkerPlacement(room, seeds);
                MarkerPointInfo info = placement != null
                    ? new MarkerPointInfo
                    {
                        Position = placement.Position,
                        Source = placement.Source
                    }
                    : null;
                if (info != null && info.Position != null)
                {
                    result[room.Key] = info;
                }
            }

            return result;
        }

        internal static RoomMarkerPlacementInfo ResolveMarkerPlacement(Document doc, RoomSemanticRecord room)
        {
            return ResolveMarkerPlacement(room, TargetRoomSeedStorageService.LoadSeeds(doc));
        }

        internal static RoomMarkerPlacementInfo ResolveMarkerPlacement(RoomSemanticRecord room, List<TargetRoomSeed> seeds)
        {
            if (room == null)
            {
                return null;
            }

            TargetRoomSeed seedByKey = (seeds ?? new List<TargetRoomSeed>())
                .FirstOrDefault(x =>
                    x != null &&
                    x.Position != null &&
                    !string.IsNullOrWhiteSpace(room.Key) &&
                    string.Equals(x.Key, room.Key, StringComparison.OrdinalIgnoreCase));

            TargetRoomSeed seedFallback = seedByKey ?? (seeds ?? new List<TargetRoomSeed>())
                .FirstOrDefault(x =>
                    x != null &&
                    x.Position != null &&
                    string.Equals((x.RoomName ?? string.Empty).Trim(), (room.RoomName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((x.TargetRoomType ?? string.Empty).Trim(), (room.TargetRoomType ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

            XYZ basePoint = seedFallback?.Position ?? room.Centroid;
            string source = seedFallback != null ? "Seed" : "Centroid";
            if (basePoint == null)
            {
                return null;
            }

            double baseZ = room.BBox?.Min?.Z ?? room.Centroid?.Z ?? 0.0;
            double z = baseZ + Room3DVisualizationConstants.MarkerOffsetMm * Room3DVisualizationConstants.MmToFeet;
            return new RoomMarkerPlacementInfo
            {
                Position = new XYZ(basePoint.X, basePoint.Y, z),
                Source = source,
                LevelId = seedFallback != null ? seedFallback.LevelId : ElementId.InvalidElementId
            };
        }

        private static RoomVisualCreateResult CreateRoomShapes(
            Document doc,
            RoomSemanticRecord room,
            Dictionary<string, MarkerPointInfo> markerPoints,
            MaterialContext materials,
            bool highlight,
            Level resolvedLevel)
        {
            RoomVisualCreateResult result = new RoomVisualCreateResult
            {
                RoomKey = room?.Key ?? string.Empty,
                RoomName = room?.RoomName ?? string.Empty
            };

            if (doc == null || room == null || string.IsNullOrWhiteSpace(room.Key))
            {
                result.RegionReason = "RoomOrDocInvalid";
                result.MarkerReason = "RoomOrDocInvalid";
                return result;
            }

            ElementId regionMat = highlight ? materials.RegionHighlightId : materials.RegionNormalId;
            View3D view3D = doc.ActiveView as View3D;
            RoomSemanticRecord visualRoom = BuildVisualizationRoom(doc, room, resolvedLevel);
            result.VisualRoom = visualRoom;

            Solid regionSolid = Room3DVisualizationGeometryBuilder.BuildRoomRegionSolid(
                visualRoom,
                regionMat,
                out string regionFailStage,
                out string regionFailReason);
            if (regionSolid != null && regionSolid.Faces != null && regionSolid.Faces.Size > 0)
            {
                DirectShape regionShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                Room3DVisualizationMetadataService.ApplyMetadata(
                    regionShape,
                    Room3DVisualizationMetadataService.BuildRegionName(visualRoom.Key),
                    Room3DVisualizationMetadataService.BuildRegionDataId(visualRoom.Key));
                regionShape.SetShape(new List<GeometryObject> { regionSolid });
                ApplyRegionOverride(view3D, regionShape.Id, highlight);
                result.RegionCreated = true;
                result.RegionReason = "Created";
            }
            else
            {
                result.RegionCreated = false;
                result.RegionReason = "Skipped(" + (string.IsNullOrWhiteSpace(regionFailStage) ? "Unknown" : regionFailStage) + ":" + (regionFailReason ?? string.Empty) + ")";
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis] Region failed, RoomKey=" + room.Key +
                    ", RoomName=" + (room.RoomName ?? string.Empty) +
                    ", LoopPoints=" + ((room.LoopPoints != null) ? room.LoopPoints.Count.ToString(CultureInfo.InvariantCulture) : "0") +
                    ", Stage=" + (regionFailStage ?? string.Empty) +
                    ", Reason=" + (regionFailReason ?? string.Empty));
            }

            result.MarkerCreated = false;
            result.MarkerReason = "Disabled";
            result.TextCreated = false;
            result.TextReason = "Disabled";

            return result;
        }

        private static string FormatPoint(XYZ p)
        {
            if (p == null)
            {
                return "-";
            }

            return p.X.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   p.Y.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   p.Z.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static RoomSemanticRecord BuildVisualizationRoom(Document doc, RoomSemanticRecord room, Level resolvedLevel)
        {
            if (room == null)
            {
                return null;
            }

            double visualizationZ = ResolveRoomVisualizationZ(doc, room, resolvedLevel, out RoomVisualizationZInfo zInfo);
            List<XYZ> points = (room.LoopPoints ?? new List<XYZ>())
                .Where(x => x != null)
                .Select(x => new XYZ(x.X, x.Y, visualizationZ))
                .ToList();
            BoundingBoxXYZ box = room.BBox;
            BoundingBoxXYZ visualBox = box != null && box.Min != null && box.Max != null
                ? new BoundingBoxXYZ
                {
                    Min = new XYZ(box.Min.X, box.Min.Y, visualizationZ),
                    Max = new XYZ(box.Max.X, box.Max.Y, visualizationZ)
                }
                : null;
            XYZ centroid = room.Centroid != null
                ? new XYZ(room.Centroid.X, room.Centroid.Y, visualizationZ)
                : null;

            DiagnosticRecorder.AppendDebug("[Room3DVis] Room=" + (room.RoomName ?? room.Key ?? string.Empty) +
                ", ResolvedLevel=" + (resolvedLevel != null ? (resolvedLevel.Name ?? string.Empty) : "-") +
                ", ResolvedLevelElevation=" + zInfo.ResolvedLevelElevation.ToString("F4", CultureInfo.InvariantCulture) +
                ", FloorFound=" + zInfo.FloorFound.ToString(CultureInfo.InvariantCulture) +
                ", FloorTopZ=" + zInfo.FloorTopZText +
                ", LoopAverageZ=" + zInfo.LoopAverageZText +
                ", VisualizationZ=" + visualizationZ.ToString("F4", CultureInfo.InvariantCulture) +
                ", VisualOffsetMm=" + RoomRegionVisualOffsetMm.ToString("F0", CultureInfo.InvariantCulture) +
                ", VisualizationZSource=" + zInfo.Source +
                ", ElevationMismatchMm=" + zInfo.ElevationMismatchMmText);

            return new RoomSemanticRecord
            {
                Key = room.Key,
                RoomName = room.RoomName,
                RoomNumber = room.RoomNumber,
                TargetRoomType = room.TargetRoomType,
                Status = room.Status,
                AreaM2 = room.AreaM2,
                CloseGapMm = room.CloseGapMm,
                BoundaryLayers = room.BoundaryLayers,
                Centroid = centroid,
                BBox = visualBox,
                LoopPoints = points,
                BoundaryWalls = room.BoundaryWalls
            };
        }

        private static double ResolveRoomVisualizationZ(
            Document doc,
            RoomSemanticRecord room,
            Level resolvedLevel,
            out RoomVisualizationZInfo info)
        {
            info = new RoomVisualizationZInfo();
            double offsetFt = UnitUtils.ConvertToInternalUnits(RoomRegionVisualOffsetMm, UnitTypeId.Millimeters);
            double levelZ = resolvedLevel != null ? resolvedLevel.Elevation : 0.0;
            info.ResolvedLevelElevation = levelZ;
            double loopAverageZ = ResolveLoopAverageZ(room);
            if (!double.IsNaN(loopAverageZ))
            {
                info.LoopAverageZText = loopAverageZ.ToString("F4", CultureInfo.InvariantCulture);
            }

            if (TryResolveFloorTopZ(doc, room, resolvedLevel, loopAverageZ, out double floorTopZ))
            {
                info.FloorFound = true;
                info.FloorTopZText = floorTopZ.ToString("F4", CultureInfo.InvariantCulture);
                info.Source = "FloorTopZ+Offset";
                if (!double.IsNaN(loopAverageZ))
                {
                    info.ElevationMismatchMmText = UnitUtils.ConvertFromInternalUnits(Math.Abs(floorTopZ - loopAverageZ), UnitTypeId.Millimeters).ToString("F0", CultureInfo.InvariantCulture);
                }

                return floorTopZ + offsetFt;
            }

            if (!double.IsNaN(loopAverageZ))
            {
                info.Source = "LoopAverageZ+Offset";
                return loopAverageZ + offsetFt;
            }

            info.Source = "LevelElevation+Offset";
            return levelZ + offsetFt;
        }

        private static double ResolveLoopAverageZ(RoomSemanticRecord room)
        {
            List<double> values = (room != null ? room.LoopPoints : null) != null
                ? room.LoopPoints.Where(x => x != null).Select(x => x.Z).ToList()
                : new List<double>();
            if (HasValidZValues(values))
            {
                return values.Average();
            }

            BoundingBoxXYZ box = room != null ? room.BBox : null;
            if (box != null && box.Min != null && HasValidZValues(new List<double> { box.Min.Z }))
            {
                return box.Min.Z;
            }

            if (room != null && room.Centroid != null && HasValidZValues(new List<double> { room.Centroid.Z }))
            {
                return room.Centroid.Z;
            }

            return double.NaN;
        }

        private static bool TryResolveFloorTopZ(Document doc, RoomSemanticRecord room, Level resolvedLevel, double loopAverageZ, out double floorTopZ)
        {
            floorTopZ = 0.0;
            BoundingBoxXYZ roomBox = room != null ? room.BBox : null;
            XYZ centroid = room != null ? room.Centroid : null;
            if (doc == null || roomBox == null || roomBox.Min == null || roomBox.Max == null)
            {
                return false;
            }

            List<Tuple<Floor, BoundingBoxXYZ, double>> candidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .Select(x => Tuple.Create(x, x.get_BoundingBox(null), 0.0))
                .Where(x => x.Item1 != null && x.Item2 != null && x.Item2.Min != null && x.Item2.Max != null)
                .Where(x => FloorMatchesRoom(x.Item1, x.Item2, roomBox, centroid, resolvedLevel))
                .Select(x => Tuple.Create(x.Item1, x.Item2, ResolveFloorScore(x.Item1, x.Item2, resolvedLevel, loopAverageZ)))
                .OrderBy(x => x.Item3)
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            floorTopZ = candidates[0].Item2.Max.Z;
            return true;
        }

        private static bool FloorMatchesRoom(Floor floor, BoundingBoxXYZ floorBox, BoundingBoxXYZ roomBox, XYZ centroid, Level resolvedLevel)
        {
            bool xyOverlap = HasPlanOverlap(floorBox, roomBox);
            bool containsCentroid = centroid != null &&
                                    centroid.X >= Math.Min(floorBox.Min.X, floorBox.Max.X) &&
                                    centroid.X <= Math.Max(floorBox.Min.X, floorBox.Max.X) &&
                                    centroid.Y >= Math.Min(floorBox.Min.Y, floorBox.Max.Y) &&
                                    centroid.Y <= Math.Max(floorBox.Min.Y, floorBox.Max.Y);
            if (!xyOverlap && !containsCentroid)
            {
                return false;
            }

            if (resolvedLevel == null || floor.LevelId == null || floor.LevelId == ElementId.InvalidElementId)
            {
                return true;
            }

            if (floor.LevelId.IntegerValue == resolvedLevel.Id.IntegerValue)
            {
                return true;
            }

            double toleranceFt = UnitUtils.ConvertToInternalUnits(1500.0, UnitTypeId.Millimeters);
            return Math.Abs(floorBox.Max.Z - resolvedLevel.Elevation) <= toleranceFt ||
                   Math.Abs(floorBox.Min.Z - resolvedLevel.Elevation) <= toleranceFt;
        }

        private static double ResolveFloorScore(Floor floor, BoundingBoxXYZ floorBox, Level resolvedLevel, double loopAverageZ)
        {
            double score = 0.0;
            if (resolvedLevel != null && floor != null && floor.LevelId != null && floor.LevelId != ElementId.InvalidElementId &&
                floor.LevelId.IntegerValue != resolvedLevel.Id.IntegerValue)
            {
                score += 1000000.0;
            }

            if (!double.IsNaN(loopAverageZ))
            {
                score += Math.Abs(floorBox.Max.Z - loopAverageZ);
            }
            else if (resolvedLevel != null)
            {
                score += Math.Abs(floorBox.Max.Z - resolvedLevel.Elevation);
            }

            return score;
        }

        private static bool HasPlanOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            double ax0 = Math.Min(a.Min.X, a.Max.X);
            double ay0 = Math.Min(a.Min.Y, a.Max.Y);
            double ax1 = Math.Max(a.Min.X, a.Max.X);
            double ay1 = Math.Max(a.Min.Y, a.Max.Y);
            double bx0 = Math.Min(b.Min.X, b.Max.X);
            double by0 = Math.Min(b.Min.Y, b.Max.Y);
            double bx1 = Math.Max(b.Min.X, b.Max.X);
            double by1 = Math.Max(b.Min.Y, b.Max.Y);
            return Math.Min(ax1, bx1) > Math.Max(ax0, bx0) &&
                   Math.Min(ay1, by1) > Math.Max(ay0, by0);
        }

        private static bool HasValidZValues(List<double> zValues)
        {
            if (zValues == null || zValues.Count == 0)
            {
                return false;
            }

            double min = zValues.Min();
            double max = zValues.Max();
            return !double.IsNaN(min) &&
                   !double.IsNaN(max) &&
                   !double.IsInfinity(min) &&
                   !double.IsInfinity(max) &&
                   !(Math.Abs(min) < 1e-6 && Math.Abs(max) < 1e-6);
        }

        private static void AppendRoomLog(RoomSemanticRecord room, RoomVisualCreateResult roomResult)
        {
            if (room == null || roomResult == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[Room3DVis] Room=" + (room.Key ?? string.Empty) +
                ", Region=" + (roomResult.RegionCreated ? "Created" : roomResult.RegionReason) +
                ", Marker=" + (roomResult.MarkerCreated ? "Created" : roomResult.MarkerReason) +
                ", Text=" + (roomResult.TextCreated ? "Created" : roomResult.TextReason) +
                ", MarkerSource=" + (roomResult.MarkerSource ?? "-"));
        }

        private static MaterialContext BuildMaterials(Document doc)
        {
            return new MaterialContext
            {
                RegionNormalId = Room3DVisualizationMaterialService.GetOrCreateNormalMaterialId(doc),
                RegionHighlightId = Room3DVisualizationMaterialService.GetOrCreateHighlightMaterialId(doc),
                MarkerNormalId = Room3DVisualizationMaterialService.GetOrCreateMarkerNormalMaterialId(doc),
                MarkerHighlightId = Room3DVisualizationMaterialService.GetOrCreateMarkerHighlightMaterialId(doc)
            };
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            // Resolve by API semantic flag to avoid localization-dependent name matching.
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);

            return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
        }

        private static void ApplyRegionOverride(View3D view, ElementId elementId, bool highlight)
        {
            if (view == null || elementId == ElementId.InvalidElementId)
            {
                return;
            }

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ElementId solidFillId = GetSolidFillPatternId(view.Document);
            Color color = highlight
                ? Room3DVisualizationConstants.RegionHighlightColor
                : Room3DVisualizationConstants.RegionNormalColor;
            int transparency = highlight
                ? Room3DVisualizationConstants.RegionHighlightTransparency
                : Room3DVisualizationConstants.RegionNormalTransparency;

            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternColor(color);
            }

            ogs.SetSurfaceTransparency(transparency);
            view.SetElementOverrides(elementId, ogs);
        }

        private static int ClearInternal(Document doc)
        {
            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => Room3DVisualizationMetadataService.IsManagedName(x.Name))
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            List<FamilyInstance> textInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(IsManagedTextInstance)
                .ToList();

            ids.AddRange(textInstances.Select(x => x.Id));
            ids = ids.Distinct().ToList();

            if (ids.Count == 0)
            {
                return 0;
            }

            doc.Delete(ids);
            return ids.Count;
        }

        private static void HideManagedMarkersInView(View3D view)
        {
            if (view == null)
            {
                return;
            }

            List<ElementId> ids = new FilteredElementCollector(view.Document)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => Room3DVisualizationMetadataService.IsManagedMarkerName(x.Name))
                .Select(x => x.Id)
                .Where(x => x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return;
            }

            try
            {
                view.HideElements(ids);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Room3DVis] Hide marker elements failed: " + ex.Message);
            }
        }

        private static bool IsManagedTextInstance(FamilyInstance instance)
        {
            if (instance == null)
            {
                return false;
            }

            string familyName = instance.Symbol != null ? (instance.Symbol.FamilyName ?? string.Empty) : string.Empty;
            if (familyName.IndexOf(Room3DVisualizationConstants.TextFamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return Room3DVisualizationMetadataService.IsManagedTextElement(instance);
        }

        private sealed class MaterialContext
        {
            public ElementId RegionNormalId { get; set; } = ElementId.InvalidElementId;
            public ElementId RegionHighlightId { get; set; } = ElementId.InvalidElementId;
            public ElementId MarkerNormalId { get; set; } = ElementId.InvalidElementId;
            public ElementId MarkerHighlightId { get; set; } = ElementId.InvalidElementId;
        }

        private sealed class MarkerPointInfo
        {
            public XYZ Position { get; set; }
            public string Source { get; set; }
        }

        internal sealed class RoomMarkerPlacementInfo
        {
            public XYZ Position { get; set; }
            public string Source { get; set; }
            public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        }

        private sealed class RoomVisualCreateResult
        {
            public string RoomKey { get; set; }
            public string RoomName { get; set; }
            public RoomSemanticRecord VisualRoom { get; set; }
            public bool RegionCreated { get; set; }
            public bool MarkerCreated { get; set; }
            public bool TextCreated { get; set; }
            public string RegionReason { get; set; }
            public string MarkerReason { get; set; }
            public string TextReason { get; set; }
            public string MarkerSource { get; set; }
            public XYZ MarkerPosition { get; set; }
            public XYZ TextPosition { get; set; }
        }

        private sealed class RoomVisualizationZInfo
        {
            public bool FloorFound { get; set; }
            public double ResolvedLevelElevation { get; set; }
            public string FloorTopZText { get; set; } = "-";
            public string LoopAverageZText { get; set; } = "-";
            public string ElevationMismatchMmText { get; set; } = "-";
            public string Source { get; set; } = "LevelElevation+Offset";
        }

        private sealed class RefreshStats
        {
            public int Rooms { get; set; }
            public int RegionCreated { get; set; }
            public int RegionSkipped { get; set; }
            public int MarkerCreated { get; set; }
            public int MarkerSkipped { get; set; }
            public int TextCreated { get; set; }
            public int TextSkipped { get; set; }

            public void Accumulate(RoomVisualCreateResult result)
            {
                Rooms++;
                if (result != null && result.RegionCreated)
                {
                    RegionCreated++;
                }
                else
                {
                    RegionSkipped++;
                }

                if (result != null && result.MarkerCreated)
                {
                    MarkerCreated++;
                }
                else
                {
                    MarkerSkipped++;
                }

                if (result != null && result.TextCreated)
                {
                    TextCreated++;
                }
                else
                {
                    TextSkipped++;
                }
            }
        }
    }
}
