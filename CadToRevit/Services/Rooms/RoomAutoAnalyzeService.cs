using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms.Lifts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class RoomAutoAnalyzeService
    {
        private const double MinCandidateRoomAreaM2 = 8.0;
        private const double MaximumRoomAreaM2 = 100.0;
        private const double MinimumRoomWidthM = 2.0;
        private const double MaximumAspectRatio = 8.0;
        private const double AnalysisWindowMarginMm = 2000.0;
        private const int MaximumCandidateCount = 100;
        private const int MaximumElapsedMs = 30000;
        private const double DuplicateAreaToleranceRatio = 0.05;
        private const double DuplicateCentroidToleranceM = 1.0;
        private const string ComplexModelStopMessage = "Analyze Rooms stopped because the model is too complex. Please narrow the view range or adjust room filters.";

        public static TargetRoomModelRecognitionService.RecognitionSummary Run(Document doc, View activeView)
        {
            AnalyzeRoomsLevelResolveResult levelResult = AnalyzeRoomsLevelResolver.Resolve(null, activeView, null, false);
            return Run(doc, activeView, levelResult);
        }

        public static TargetRoomModelRecognitionService.RecognitionSummary Run(Document doc, View activeView, AnalyzeRoomsLevelResolveResult levelResult)
        {
            Stopwatch totalWatch = Stopwatch.StartNew();
            TargetRoomModelRecognitionService.RecognitionSummary summary = new TargetRoomModelRecognitionService.RecognitionSummary();
            if (doc == null)
            {
                summary.Message = "Analyze Rooms failed: no active document.";
                return summary;
            }

            Level level = levelResult != null ? levelResult.Level : null;
            if (level == null)
            {
                summary.Message = levelResult != null && !string.IsNullOrWhiteSpace(levelResult.Message)
                    ? levelResult.Message
                    : "Analyze Rooms failed: no analysis level was found.";
                return summary;
            }

            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] Started. Level=" + (level.Name ?? string.Empty));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] AnalyzeLevelResolved=" + (level.Name ?? string.Empty) +
                ", AnalyzeLevelResolveReason=" + (levelResult != null ? (levelResult.Reason ?? string.Empty) : string.Empty));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] BoundarySource=WallLocationCurveAndWallFootprint");
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] WallOpeningsIgnoredForRoomClosure=True");

            RoomSemanticRunResult run = new RoomSemanticRunResult();
            RoomSemanticConfig config = ModelRoomSeedRecognitionService.BuildRecognitionConfig(doc);
            List<RoomSemanticRecord> accepted = new List<RoomSemanticRecord>();
            List<string> errors = new List<string>();
            FilterStats filterStats = new FilterStats();
            bool stoppedByLimit = false;

            List<Room> nativeRooms = CollectNativeRooms(doc, level);
            foreach (Room room in nativeRooms)
            {
                RoomSemanticRecord record = BuildNativeRoomRecord(doc, room, level);
                NormalizeRecordElevation(record, level);
                if (!PassesCandidateFilters(record, out string reason, filterStats))
                {
                    errors.Add((room.Name ?? "Native room") + ": " + reason);
                    continue;
                }

                if (!AddCandidate(accepted, record, true))
                {
                    filterStats.Duplicate++;
                }
            }

            BoundingBoxXYZ analysisBox = ResolveAnalysisBox(doc, activeView, level);
            if (analysisBox == null || analysisBox.Min == null || analysisBox.Max == null)
            {
                DiagnosticRecorder.AppendDebug("[AnalyzeRooms] AnalysisBox=null");
                errors.Add(BuildNoAnalysisBoxMessage(doc, level));
            }
            else if (totalWatch.ElapsedMilliseconds <= MaximumElapsedMs)
            {
                DiagnosticRecorder.AppendDebug("[AnalyzeRooms] AnalysisBox=created");
                AnalysisDatasetBuildInfo buildInfo = BuildAnalysisDatasetOnce(doc, level, analysisBox);
                if (totalWatch.ElapsedMilliseconds > MaximumElapsedMs)
                {
                    stoppedByLimit = true;
                }
                else
                {
                    List<RoomCandidate> loops = DetectModelLoopsOnce(buildInfo.BuildResult, config, out int totalLoops, out long detectElapsedMs);
                    int validLoops = 0;
                    int modelIndex = 1;
                    foreach (RoomCandidate loop in loops)
                    {
                        if (totalWatch.ElapsedMilliseconds > MaximumElapsedMs)
                        {
                            stoppedByLimit = true;
                            break;
                        }

                        if (accepted.Count >= MaximumCandidateCount)
                        {
                            stoppedByLimit = true;
                            break;
                        }

                    if (!IsValidModelLoop(loop))
                        {
                            filterStats.NeedsFix++;
                            continue;
                        }

                    validLoops++;
                    RoomSemanticRecord record = BuildModelLoopCandidateRecord(doc, level, loop, modelIndex++);
                    NormalizeRecordElevation(record, level);
                    if (!PassesCandidateFilters(record, out string reason, filterStats))
                    {
                        if (string.Equals(reason, "Area too small", StringComparison.OrdinalIgnoreCase))
                        {
                            AppendTooSmallLoopLog(record, loop, modelIndex - 1, reason);
                        }

                        if (!string.Equals(reason, "Area too small", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add((record.RoomName ?? record.Key ?? "Model candidate") + ": " + reason);
                        }

                        continue;
                        }

                        if (!AddCandidate(accepted, record, false))
                        {
                            filterStats.Duplicate++;
                        }
                    }

                    DiagnosticRecorder.AppendDebug("[AnalyzeRooms] DetectMultiFinished: TotalLoops=" +
                        totalLoops.ToString(CultureInfo.InvariantCulture) +
                        ", ValidLoops=" + validLoops.ToString(CultureInfo.InvariantCulture) +
                        ", ElapsedMs=" + detectElapsedMs.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                stoppedByLimit = true;
            }

            if (stoppedByLimit)
            {
                errors.Add(ComplexModelStopMessage);
            }

            FilterNestedWallShellDuplicates(accepted, filterStats);
            foreach (RoomSemanticRecord record in accepted)
            {
                NormalizeRecordElevation(record, level);
            }

            AssignCandidateNames(accepted);

            List<RoomSemanticRecord> orderedRooms = accepted
                .OrderByDescending(IsNativeCandidate)
                .ThenByDescending(x => x.AreaM2)
                .ToList();
            if (orderedRooms.Count > MaximumCandidateCount)
            {
                stoppedByLimit = true;
                orderedRooms = orderedRooms.Take(MaximumCandidateCount).ToList();
                if (!errors.Contains(ComplexModelStopMessage))
                {
                    errors.Add(ComplexModelStopMessage);
                }
            }

            run.Rooms = orderedRooms;
            run.Total = nativeRooms.Count + run.Rooms.Count + errors.Count;
            run.Matched = run.Rooms.Count;
            run.UnmatchedLabel = Math.Max(0, run.Total - run.Matched);
            run.NeedsFix = errors.Count;

            summary.RunResult = run;
            summary.Matched = run.Matched;
            summary.Failed = errors.Count;
            summary.Errors = errors;
            summary.Lifts = LiftRecognitionStorageService.Load(doc);
            foreach (RoomSemanticRecord room in run.Rooms)
            {
                if (room != null && !string.IsNullOrWhiteSpace(room.Key))
                {
                    summary.SeedLevelIdByKey[room.Key] = level.Id.IntegerValue;
                }
            }

            summary.Message = "Analyze Rooms done. Level=" + (level.Name ?? string.Empty) +
                              ", NativeRooms=" + nativeRooms.Count +
                              ", Candidates=" + summary.Matched +
                              ", Filtered=" + summary.Failed;
            if (stoppedByLimit)
            {
                summary.Message = ComplexModelStopMessage;
            }

            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] Filtered: TooSmall=" +
                filterStats.TooSmall.ToString(CultureInfo.InvariantCulture) +
                ", TooLarge=" + filterStats.TooLarge.ToString(CultureInfo.InvariantCulture) +
                ", TooNarrow=" + filterStats.TooNarrow.ToString(CultureInfo.InvariantCulture) +
                ", AspectTooHigh=" + filterStats.AspectTooHigh.ToString(CultureInfo.InvariantCulture) +
                ", Duplicate=" + filterStats.Duplicate.ToString(CultureInfo.InvariantCulture) +
                ", NestedWallShellDuplicate=" + filterStats.NestedWallShellDuplicate.ToString(CultureInfo.InvariantCulture) +
                ", NeedsFix=" + filterStats.NeedsFix.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] Candidates=" + summary.Matched.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] Finished. ElapsedMs=" + totalWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] " + summary.Message);
            return summary;
        }

        private static Level ResolveAnalysisLevel(Document doc, View activeView)
        {
            if (doc == null)
            {
                return null;
            }

            if (activeView != null && !(activeView is View3D) && activeView.GenLevel != null)
            {
                Level viewLevel = doc.GetElement(activeView.GenLevel.Id) as Level;
                if (viewLevel != null)
                {
                    return viewLevel;
                }
            }

            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x != null)
                .OrderBy(x => x.Elevation)
                .ToList();
            if (levels.Count == 0)
            {
                return null;
            }

            Level l1 = levels.FirstOrDefault(x =>
                string.Equals((x.Name ?? string.Empty).Trim(), "L1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((x.Name ?? string.Empty).Trim(), "Level 1", StringComparison.OrdinalIgnoreCase));
            return l1 ?? levels.FirstOrDefault();
        }

        private static List<Room> CollectNativeRooms(Document doc, Level level)
        {
            if (doc == null || level == null)
            {
                return new List<Room>();
            }

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(x => x != null &&
                            x.LevelId == level.Id &&
                            x.Area > 0.0 &&
                            x.Location != null)
                .ToList();
        }

        private static RoomSemanticRecord BuildNativeRoomRecord(Document doc, Room room, Level level)
        {
            List<XYZ> loopPoints = ExtractNativeRoomLoopPoints(room);
            string key = RoomPointProbeService.BuildStableRoomKey(
                level != null ? level.Id : ElementId.InvalidElementId,
                loopPoints,
                true,
                room != null ? room.Id : ElementId.InvalidElementId);
            double areaM2 = room != null ? UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters) : 0.0;

            return new RoomSemanticRecord
            {
                Key = key,
                RoomName = BuildNativeCandidateName(room),
                RoomNumber = room != null ? (room.Number ?? string.Empty) : string.Empty,
                TargetRoomType = "Candidate room",
                Status = "Matched-CandidateRoom-NativeRoom",
                AreaM2 = areaM2,
                CloseGapMm = 0.0,
                BoundaryLayers = "NativeRoom",
                Centroid = ResolveRoomPoint(room, loopPoints),
                BBox = BuildBoundingBox(loopPoints, level != null ? level.Elevation : 0.0),
                LoopPoints = loopPoints,
                BoundaryWalls = RoomBoundaryWallResolver.Resolve(
                    doc,
                    level != null ? level.Id : ElementId.InvalidElementId,
                    loopPoints)
            };
        }

        private static string BuildNativeCandidateName(Room room)
        {
            string name = room != null ? (room.Name ?? string.Empty).Trim() : string.Empty;
            string number = room != null ? (room.Number ?? string.Empty).Trim() : string.Empty;
            string combined = string.Join(" ", new[] { name, number }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(combined) ? "Room Candidate" : "Candidate room - " + combined;
        }

        private static List<XYZ> ExtractNativeRoomLoopPoints(Room room)
        {
            List<XYZ> bestLoop = new List<XYZ>();
            if (room == null)
            {
                return bestLoop;
            }

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);
            foreach (IList<BoundarySegment> loop in loops ?? new List<IList<BoundarySegment>>())
            {
                List<XYZ> points = new List<XYZ>();
                foreach (BoundarySegment segment in loop ?? new List<BoundarySegment>())
                {
                    Curve curve = segment != null ? segment.GetCurve() : null;
                    if (curve == null)
                    {
                        continue;
                    }

                    XYZ start = curve.GetEndPoint(0);
                    if (start != null)
                    {
                        points.Add(start);
                    }
                }

                points = NormalizeLoopPoints(points);
                if (points.Count >= 3 && (bestLoop.Count == 0 || ComputePolygonAreaM2(points) > ComputePolygonAreaM2(bestLoop)))
                {
                    bestLoop = points;
                }
            }

            return bestLoop;
        }

        private static BoundingBoxXYZ ResolveAnalysisBox(Document doc, View activeView, Level level)
        {
            BoundingBoxXYZ modelBox = CollectLevelModelBox(doc, level);
            BoundingBoxXYZ viewBox = TryGetViewCropBox(activeView, level);
            if (modelBox == null)
            {
                return viewBox;
            }

            if (viewBox == null)
            {
                return modelBox;
            }

            return IntersectBoxes(modelBox, viewBox, level != null ? level.Elevation : 0.0) ?? modelBox;
        }

        private static BoundingBoxXYZ CollectLevelModelBox(Document doc, Level level)
        {
            if (doc == null || level == null)
            {
                return null;
            }

            BoundingBoxXYZ total = null;
            List<Element> elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(x => IsBoundaryElementOnLevel(x, level))
                .ToList();
            foreach (Element element in elements)
            {
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null || box.Min == null || box.Max == null)
                {
                    continue;
                }

                total = UnionBoxes(total, box);
            }

            return total;
        }

        private static bool IsBoundaryElementOnLevel(Element element, Level level)
        {
            if (element == null || level == null || element.Category == null)
            {
                return false;
            }

            int categoryId = element.Category.Id.IntegerValue;
            bool boundaryCategory =
                categoryId == (int)BuiltInCategory.OST_Walls ||
                categoryId == (int)BuiltInCategory.OST_Columns ||
                categoryId == (int)BuiltInCategory.OST_StructuralColumns ||
                categoryId == (int)BuiltInCategory.OST_RoomSeparationLines;
            if (!boundaryCategory)
            {
                return false;
            }

            if (element is Wall wall)
            {
                Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                ElementId wallLevelId = parameter != null ? parameter.AsElementId() : ElementId.InvalidElementId;
                return wallLevelId != null && wallLevelId.IntegerValue == level.Id.IntegerValue;
            }

            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
            {
                return element.LevelId.IntegerValue == level.Id.IntegerValue;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            double toleranceFt = UnitUtils.ConvertToInternalUnits(500.0, UnitTypeId.Millimeters);
            double minZ = Math.Min(box.Min.Z, box.Max.Z) - toleranceFt;
            double maxZ = Math.Max(box.Min.Z, box.Max.Z) + toleranceFt;
            return level.Elevation >= minZ && level.Elevation <= maxZ;
        }

        private static BoundingBoxXYZ TryGetViewCropBox(View activeView, Level level)
        {
            if (activeView == null || !activeView.CropBoxActive || activeView.CropBox == null)
            {
                return null;
            }

            BoundingBoxXYZ source = activeView.CropBox;
            double z = level != null ? level.Elevation : 0.0;
            return new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(source.Min.X, source.Max.X), Math.Min(source.Min.Y, source.Max.Y), z),
                Max = new XYZ(Math.Max(source.Min.X, source.Max.X), Math.Max(source.Min.Y, source.Max.Y), z)
            };
        }

        private static string BuildNoAnalysisBoxMessage(Document doc, Level level)
        {
            Dictionary<ElementId, int> distribution = new Dictionary<ElementId, int>();
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(x => x != null))
            {
                Level wallLevel = AnalyzeRoomsLevelResolver.ResolveWallBaseLevel(doc, wall);
                if (wallLevel == null)
                {
                    continue;
                }

                if (!distribution.ContainsKey(wallLevel.Id))
                {
                    distribution[wallLevel.Id] = 0;
                }

                distribution[wallLevel.Id]++;
            }

            string distributionText = string.Join(", ", distribution
                .OrderByDescending(x => x.Value)
                .Select(x => ((doc.GetElement(x.Key) as Level)?.Name ?? "?") + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] WallBaseConstraintLevelDistribution=" + (string.IsNullOrWhiteSpace(distributionText) ? "(empty)" : distributionText));
            KeyValuePair<ElementId, int> best = distribution.OrderByDescending(x => x.Value).FirstOrDefault();
            Level bestLevel = best.Key != null ? doc.GetElement(best.Key) as Level : null;
            if (level != null && bestLevel != null && best.Value > 0 && bestLevel.Id.IntegerValue != level.Id.IntegerValue)
            {
                return "No usable walls found on " + (level.Name ?? string.Empty) +
                       ". Walls were found on " + (bestLevel.Name ?? string.Empty) +
                       ". Please select the target level or switch to a floor plan view.";
            }

            return "Analysis box not found.";
        }

        private static AnalysisDatasetBuildInfo BuildAnalysisDatasetOnce(Document doc, Level level, BoundingBoxXYZ analysisBox)
        {
            AnalysisDatasetBuildInfo info = new AnalysisDatasetBuildInfo();
            if (doc == null || level == null || analysisBox == null || analysisBox.Min == null || analysisBox.Max == null)
            {
                return info;
            }

            double widthFt = Math.Abs(analysisBox.Max.X - analysisBox.Min.X);
            double heightFt = Math.Abs(analysisBox.Max.Y - analysisBox.Min.Y);
            info.WidthM = UnitUtils.ConvertFromInternalUnits(widthFt, UnitTypeId.Meters);
            info.HeightM = UnitUtils.ConvertFromInternalUnits(heightFt, UnitTypeId.Meters);
            info.WindowMm = UnitUtils.ConvertFromInternalUnits(Math.Max(widthFt, heightFt), UnitTypeId.Millimeters) + AnalysisWindowMarginMm;
            XYZ center = new XYZ(
                (analysisBox.Min.X + analysisBox.Max.X) * 0.5,
                (analysisBox.Min.Y + analysisBox.Max.Y) * 0.5,
                level.Elevation);

            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] AnalysisBox: width=" +
                info.WidthM.ToString("F2", CultureInfo.InvariantCulture) +
                "m, height=" + info.HeightM.ToString("F2", CultureInfo.InvariantCulture) +
                "m, windowMm=" + info.WindowMm.ToString("F0", CultureInfo.InvariantCulture));

            Stopwatch watch = Stopwatch.StartNew();
            info.BuildResult = ModelBoundarySegmentBuilder.BuildLocalDataset(doc, level.Id, center, info.WindowMm);
            watch.Stop();
            ModelBoundaryDatasetBuildResult build = info.BuildResult ?? new ModelBoundaryDatasetBuildResult();
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] BoundaryDatasetBuiltOnce: WallSegments=" +
                build.WallSegments.ToString(CultureInfo.InvariantCulture) +
                ", ColumnSegments=" + build.ColumnSegments.ToString(CultureInfo.InvariantCulture) +
                ", SeparatorSegments=" + build.SeparatorSegments.ToString(CultureInfo.InvariantCulture) +
                ", DoorClosureSegments=" + build.DoorClosureSegments.ToString(CultureInfo.InvariantCulture) +
                ", DirectWallCount=" + build.DirectWallCount.ToString(CultureInfo.InvariantCulture) +
                ", GroupWallCount=" + build.GroupWallCount.ToString(CultureInfo.InvariantCulture) +
                ", WallLocationCurveNull=" + build.WallLocationCurveNull.ToString(CultureInfo.InvariantCulture) +
                ", WallLocationCurveNotLine=" + build.WallLocationCurveNotLine.ToString(CultureInfo.InvariantCulture) +
                ", Total=" + (build.Dataset != null ? build.Dataset.Segments.Count : 0).ToString(CultureInfo.InvariantCulture) +
                ", ElapsedMs=" + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            return info;
        }

        private static List<RoomCandidate> DetectModelLoopsOnce(
            ModelBoundaryDatasetBuildResult build,
            RoomSemanticConfig config,
            out int totalLoops,
            out long detectElapsedMs)
        {
            totalLoops = 0;
            detectElapsedMs = 0;
            if (build == null || build.Dataset == null)
            {
                return new List<RoomCandidate>();
            }

            HashSet<string> boundaryLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModelBoundarySegmentBuilder.WallBoundaryLayerName,
                ModelBoundarySegmentBuilder.RoomSeparatorLayerName,
                ModelBoundarySegmentBuilder.DoorClosureLayerName
            };

            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] DetectMultiStarted");
            Stopwatch watch = Stopwatch.StartNew();
            List<RoomCandidate> loops = RoomBoundaryLoopService.DetectMulti(
                build.Dataset,
                boundaryLayers,
                config != null ? config.CloseTolMm : 10.0,
                config != null ? config.MaxPatchMm : 300.0,
                config != null ? config.MinAreaM2 : 1.0,
                config != null ? config.DoorGapMaxMm : 1200.0,
                config != null ? config.SmallGapPatchMaxMm : 350.0,
                false,
                false);
            watch.Stop();
            detectElapsedMs = watch.ElapsedMilliseconds;
            totalLoops = loops != null ? loops.Count : 0;
            return loops ?? new List<RoomCandidate>();
        }

        private static bool IsValidModelLoop(RoomCandidate loop)
        {
            return loop != null &&
                   loop.Status != RoomBoundaryStatus.NeedsFix &&
                   loop.LoopPoints != null &&
                   loop.LoopPoints.Count >= 4 &&
                   loop.BBox != null &&
                   loop.BBox.Min != null &&
                   loop.BBox.Max != null;
        }

        private static RoomSemanticRecord BuildModelLoopCandidateRecord(Document doc, Level level, RoomCandidate loop, int index)
        {
            List<XYZ> loopPoints = loop != null ? (loop.LoopPoints ?? new List<XYZ>()) : new List<XYZ>();
            ElementId levelId = level != null ? level.Id : ElementId.InvalidElementId;
            return new RoomSemanticRecord
            {
                Key = RoomPointProbeService.BuildStableRoomKey(levelId, loopPoints, false, ElementId.InvalidElementId),
                RoomName = "Room Candidate " + index.ToString("000", CultureInfo.InvariantCulture),
                RoomNumber = string.Empty,
                TargetRoomType = "Candidate room",
                Status = "Matched-CandidateRoom-ModelLoop",
                AreaM2 = loop != null ? loop.AreaM2 : 0.0,
                CloseGapMm = loop != null ? loop.CloseGapMm : 0.0,
                BoundaryLayers = !string.IsNullOrWhiteSpace(loop != null ? loop.SourceLayer : null)
                    ? loop.SourceLayer
                    : "MODEL_WALL_BOUNDARY+ROOM_SEPARATION+DOOR_CLOSURE",
                Centroid = loop != null ? loop.Centroid : null,
                BBox = loop != null ? loop.BBox : null,
                LoopPoints = loopPoints,
                BoundaryWalls = RoomBoundaryWallResolver.Resolve(doc, levelId, loopPoints)
            };
        }

        private static void NormalizeRecordElevation(RoomSemanticRecord record, Level level)
        {
            if (record == null || level == null)
            {
                return;
            }

            List<XYZ> source = NormalizeLoopPoints(record.LoopPoints);
            double levelZ = level.Elevation;
            double visualizationZ = ResolveVisualizationZ(record, source, levelZ, out string zSource);
            double averageZ = source.Count > 0 ? source.Average(x => x.Z) : visualizationZ;
            double bboxMinZ = record.BBox != null && record.BBox.Min != null ? record.BBox.Min.Z : visualizationZ;
            double mismatchMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(averageZ - levelZ), UnitTypeId.Millimeters);
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] AnalyzeLevelResolved=" + (level.Name ?? string.Empty) +
                ", ResolvedLevelElevation=" + levelZ.ToString("F4", CultureInfo.InvariantCulture) +
                ", LoopAverageZ=" + averageZ.ToString("F4", CultureInfo.InvariantCulture) +
                ", LoopBBoxMinZ=" + bboxMinZ.ToString("F4", CultureInfo.InvariantCulture) +
                ", VisualizationZ=" + visualizationZ.ToString("F4", CultureInfo.InvariantCulture) +
                ", ElevationMismatchMm=" + mismatchMm.ToString("F0", CultureInfo.InvariantCulture) +
                ", VisualizationZSource=" + zSource);

            record.LoopPoints = source.Count > 0
                ? source.Select(p => new XYZ(p.X, p.Y, visualizationZ)).ToList()
                : new List<XYZ>();
            if (record.Centroid != null)
            {
                record.Centroid = new XYZ(record.Centroid.X, record.Centroid.Y, visualizationZ);
            }

            BoundingBoxXYZ box = record.BBox;
            record.BBox = box != null && box.Min != null && box.Max != null
                ? new BoundingBoxXYZ
                {
                    Min = new XYZ(box.Min.X, box.Min.Y, visualizationZ),
                    Max = new XYZ(box.Max.X, box.Max.Y, visualizationZ)
                }
                : BuildBoundingBox(record.LoopPoints, visualizationZ);
        }

        private static double ResolveVisualizationZ(RoomSemanticRecord record, List<XYZ> loopPoints, double fallbackLevelZ, out string source)
        {
            source = "LevelElevationFallback";
            List<double> zValues = (loopPoints ?? new List<XYZ>())
                .Where(x => x != null)
                .Select(x => x.Z)
                .ToList();
            if (HasValidZValues(zValues))
            {
                source = "LoopGeometryZ";
                return zValues.Average();
            }

            BoundingBoxXYZ box = record != null ? record.BBox : null;
            if (box != null && box.Min != null && HasValidZValues(new List<double> { box.Min.Z }))
            {
                source = "LoopGeometryZ";
                return box.Min.Z;
            }

            if (record != null && record.Centroid != null && HasValidZValues(new List<double> { record.Centroid.Z }))
            {
                source = "LoopGeometryZ";
                return record.Centroid.Z;
            }

            return fallbackLevelZ;
        }

        private static bool HasValidZValues(List<double> zValues)
        {
            if (zValues == null || zValues.Count == 0)
            {
                return false;
            }

            double min = zValues.Min();
            double max = zValues.Max();
            if (Math.Abs(min) < 1e-6 && Math.Abs(max) < 1e-6)
            {
                return false;
            }

            return !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max);
        }

        private static bool PassesCandidateFilters(RoomSemanticRecord record, out string reason, FilterStats stats)
        {
            reason = string.Empty;
            if (record == null)
            {
                reason = "Null candidate";
                if (stats != null)
                {
                    stats.NeedsFix++;
                }

                return false;
            }

            if (record.LoopPoints == null || record.LoopPoints.Count < 3)
            {
                reason = "Not closed";
                if (stats != null)
                {
                    stats.NeedsFix++;
                }

                return false;
            }

            if (record.AreaM2 < MinCandidateRoomAreaM2)
            {
                reason = "Area too small";
                if (stats != null)
                {
                    stats.TooSmall++;
                }

                return false;
            }

            if (record.AreaM2 > MaximumRoomAreaM2)
            {
                reason = "Area too large";
                if (stats != null)
                {
                    stats.TooLarge++;
                }

                return false;
            }

            double minWidthM = ComputeMinimumBoxWidthM(record);
            if (minWidthM < MinimumRoomWidthM)
            {
                reason = "Minimum width too small";
                if (stats != null)
                {
                    stats.TooNarrow++;
                }

                return false;
            }

            double aspectRatio = ComputeAspectRatio(record);
            if (aspectRatio > MaximumAspectRatio)
            {
                reason = "Aspect ratio too high";
                if (stats != null)
                {
                    stats.AspectTooHigh++;
                }

                return false;
            }

            return true;
        }

        private static void AppendTooSmallLoopLog(RoomSemanticRecord record, RoomCandidate loop, int loopId, string reason)
        {
            double area = record != null ? record.AreaM2 : loop != null ? loop.AreaM2 : 0.0;
            if (area < 5.0 || area > 15.0)
            {
                return;
            }

            BoundingBoxXYZ box = record != null ? record.BBox : loop != null ? loop.BBox : null;
            double widthM = 0.0;
            double heightM = 0.0;
            if (box != null && box.Min != null && box.Max != null)
            {
                widthM = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.X - box.Min.X), UnitTypeId.Meters);
                heightM = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Y - box.Min.Y), UnitTypeId.Meters);
            }

            XYZ center = ResolveRecordCentroid(record);
            DiagnosticRecorder.AppendDebug("[AnalyzeRooms] TooSmallLoopDetail: LoopId=" +
                loopId.ToString(CultureInfo.InvariantCulture) +
                ", AreaM2=" + area.ToString("F2", CultureInfo.InvariantCulture) +
                ", BBoxWidth=" + widthM.ToString("F2", CultureInfo.InvariantCulture) +
                ", BBoxHeight=" + heightM.ToString("F2", CultureInfo.InvariantCulture) +
                ", FilterReason=" + (reason ?? string.Empty) +
                ", CenterPoint=" + FormatPoint(center) +
                ", MinCandidateRoomAreaM2=" + MinCandidateRoomAreaM2.ToString("F1", CultureInfo.InvariantCulture));
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return "-";
            }

            return point.X.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static bool AddCandidate(List<RoomSemanticRecord> accepted, RoomSemanticRecord candidate, bool preferCandidate)
        {
            if (accepted == null || candidate == null)
            {
                return false;
            }

            for (int i = 0; i < accepted.Count; i++)
            {
                RoomSemanticRecord existing = accepted[i];
                if (!IsDuplicate(existing, candidate))
                {
                    continue;
                }

                if (preferCandidate || (!IsNativeCandidate(existing) && IsNativeCandidate(candidate)))
                {
                    accepted[i] = candidate;
                }

                return false;
            }

            accepted.Add(candidate);
            return true;
        }

        private static bool IsDuplicate(RoomSemanticRecord a, RoomSemanticRecord b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(a.Key) &&
                string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (a.AreaM2 <= 0.0 || b.AreaM2 <= 0.0)
            {
                return false;
            }

            double areaRatio = Math.Abs(a.AreaM2 - b.AreaM2) / Math.Max(a.AreaM2, b.AreaM2);
            XYZ ca = ResolveRecordCentroid(a);
            XYZ cb = ResolveRecordCentroid(b);
            if (ca == null || cb == null)
            {
                return false;
            }

            double distanceM = UnitUtils.ConvertFromInternalUnits(ca.DistanceTo(cb), UnitTypeId.Meters);
            return areaRatio <= DuplicateAreaToleranceRatio && distanceM <= DuplicateCentroidToleranceM;
        }

        private static void FilterNestedWallShellDuplicates(List<RoomSemanticRecord> records, FilterStats stats)
        {
            if (records == null || records.Count < 2)
            {
                return;
            }

            HashSet<RoomSemanticRecord> drop = new HashSet<RoomSemanticRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                RoomSemanticRecord a = records[i];
                if (a == null || drop.Contains(a))
                {
                    continue;
                }

                for (int j = i + 1; j < records.Count; j++)
                {
                    RoomSemanticRecord b = records[j];
                    if (b == null || drop.Contains(b))
                    {
                        continue;
                    }

                    if (!TryResolveNestedWallShellDuplicate(a, b, out RoomSemanticRecord keep, out RoomSemanticRecord remove, out double distanceM, out bool containsSmallerCentroid))
                    {
                        continue;
                    }

                    drop.Add(remove);
                    if (stats != null)
                    {
                        stats.NestedWallShellDuplicate++;
                    }

                    DiagnosticRecorder.AppendDebug("[AnalyzeRooms] NestedWallShellDuplicate: drop outer loop, keep inner loop. DropArea=" +
                        remove.AreaM2.ToString("F2", CultureInfo.InvariantCulture) +
                        ", KeepArea=" + keep.AreaM2.ToString("F2", CultureInfo.InvariantCulture) +
                        ", CentroidDistanceM=" + distanceM.ToString("F2", CultureInfo.InvariantCulture) +
                        ", ContainsSmallerCentroid=" + containsSmallerCentroid.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (drop.Count == 0)
            {
                return;
            }

            records.RemoveAll(x => x != null && drop.Contains(x));
        }

        private static bool TryResolveNestedWallShellDuplicate(
            RoomSemanticRecord a,
            RoomSemanticRecord b,
            out RoomSemanticRecord keep,
            out RoomSemanticRecord remove,
            out double distanceM,
            out bool containsSmallerCentroid)
        {
            keep = null;
            remove = null;
            distanceM = double.MaxValue;
            containsSmallerCentroid = false;
            if (!CanCompareNestedShell(a) || !CanCompareNestedShell(b))
            {
                return false;
            }

            XYZ ca = ResolveRecordCentroid(a);
            XYZ cb = ResolveRecordCentroid(b);
            if (ca == null || cb == null)
            {
                return false;
            }

            distanceM = UnitUtils.ConvertFromInternalUnits(ca.DistanceTo(cb), UnitTypeId.Meters);
            if (distanceM >= DuplicateCentroidToleranceM)
            {
                return false;
            }

            RoomSemanticRecord smaller = a.AreaM2 <= b.AreaM2 ? a : b;
            RoomSemanticRecord larger = smaller == a ? b : a;
            if (smaller.AreaM2 < MinCandidateRoomAreaM2 || larger.AreaM2 <= smaller.AreaM2 * 1.05)
            {
                return false;
            }

            XYZ smallerCentroid = ResolveRecordCentroid(smaller);
            containsSmallerCentroid = smallerCentroid != null &&
                                      larger.LoopPoints != null &&
                                      PointInPolygon.ContainsPointXY(larger.LoopPoints, smallerCentroid);
            if (!containsSmallerCentroid || !HasHighPlanOverlap(a.BBox, b.BBox))
            {
                return false;
            }

            bool aNative = IsNativeCandidate(a);
            bool bNative = IsNativeCandidate(b);
            if (aNative || bNative)
            {
                keep = aNative ? a : b;
                remove = keep == a ? b : a;
                return true;
            }

            keep = smaller;
            remove = larger;
            return true;
        }

        private static bool CanCompareNestedShell(RoomSemanticRecord record)
        {
            return record != null &&
                   record.AreaM2 >= MinCandidateRoomAreaM2 &&
                   record.LoopPoints != null &&
                   record.LoopPoints.Count >= 4 &&
                   record.BBox != null &&
                   record.BBox.Min != null &&
                   record.BBox.Max != null;
        }

        private static bool HasHighPlanOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null || a.Min == null || a.Max == null || b.Min == null || b.Max == null)
            {
                return false;
            }

            double ax0 = Math.Min(a.Min.X, a.Max.X);
            double ay0 = Math.Min(a.Min.Y, a.Max.Y);
            double ax1 = Math.Max(a.Min.X, a.Max.X);
            double ay1 = Math.Max(a.Min.Y, a.Max.Y);
            double bx0 = Math.Min(b.Min.X, b.Max.X);
            double by0 = Math.Min(b.Min.Y, b.Max.Y);
            double bx1 = Math.Max(b.Min.X, b.Max.X);
            double by1 = Math.Max(b.Min.Y, b.Max.Y);
            double ix = Math.Max(0.0, Math.Min(ax1, bx1) - Math.Max(ax0, bx0));
            double iy = Math.Max(0.0, Math.Min(ay1, by1) - Math.Max(ay0, by0));
            double intersection = ix * iy;
            double areaA = Math.Max(0.0, (ax1 - ax0) * (ay1 - ay0));
            double areaB = Math.Max(0.0, (bx1 - bx0) * (by1 - by0));
            double smaller = Math.Min(areaA, areaB);
            return smaller > 1e-9 && intersection / smaller >= 0.75;
        }

        private static void AssignCandidateNames(List<RoomSemanticRecord> records)
        {
            int modelIndex = 1;
            foreach (RoomSemanticRecord record in records ?? new List<RoomSemanticRecord>())
            {
                if (record == null)
                {
                    continue;
                }

                if (IsNativeCandidate(record) && !string.IsNullOrWhiteSpace(record.RoomName))
                {
                    continue;
                }

                record.RoomName = "Room Candidate " + modelIndex.ToString("000");
                modelIndex++;
            }
        }

        private static bool IsNativeCandidate(RoomSemanticRecord record)
        {
            return record != null &&
                   (record.Status ?? string.Empty).IndexOf("NativeRoom", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double ComputeMinimumBoxWidthM(RoomSemanticRecord record)
        {
            BoundingBoxXYZ box = record != null ? record.BBox : null;
            if ((box == null || box.Min == null || box.Max == null) && record != null)
            {
                box = BuildBoundingBox(record.LoopPoints, 0.0);
            }

            if (box == null || box.Min == null || box.Max == null)
            {
                return 0.0;
            }

            double widthFt = Math.Abs(box.Max.X - box.Min.X);
            double heightFt = Math.Abs(box.Max.Y - box.Min.Y);
            return UnitUtils.ConvertFromInternalUnits(Math.Min(widthFt, heightFt), UnitTypeId.Meters);
        }

        private static double ComputeAspectRatio(RoomSemanticRecord record)
        {
            BoundingBoxXYZ box = record != null ? record.BBox : null;
            if ((box == null || box.Min == null || box.Max == null) && record != null)
            {
                box = BuildBoundingBox(record.LoopPoints, 0.0);
            }

            if (box == null || box.Min == null || box.Max == null)
            {
                return double.MaxValue;
            }

            double width = Math.Abs(box.Max.X - box.Min.X);
            double height = Math.Abs(box.Max.Y - box.Min.Y);
            double min = Math.Min(width, height);
            double max = Math.Max(width, height);
            return min <= 1e-9 ? double.MaxValue : max / min;
        }

        private static XYZ ResolveRecordCentroid(RoomSemanticRecord record)
        {
            if (record == null)
            {
                return null;
            }

            if (record.Centroid != null)
            {
                return record.Centroid;
            }

            return ResolveRoomPoint(null, record.LoopPoints);
        }

        private static XYZ ResolveRoomPoint(Room room, List<XYZ> loopPoints)
        {
            LocationPoint locationPoint = room != null ? room.Location as LocationPoint : null;
            if (locationPoint != null && locationPoint.Point != null)
            {
                return locationPoint.Point;
            }

            List<XYZ> points = NormalizeLoopPoints(loopPoints);
            if (points.Count == 0)
            {
                return null;
            }

            return new XYZ(points.Average(x => x.X), points.Average(x => x.Y), points.Average(x => x.Z));
        }

        private static BoundingBoxXYZ BuildBoundingBox(List<XYZ> points, double z)
        {
            List<XYZ> normalized = NormalizeLoopPoints(points);
            if (normalized.Count == 0)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(normalized.Min(x => x.X), normalized.Min(x => x.Y), z),
                Max = new XYZ(normalized.Max(x => x.X), normalized.Max(x => x.Y), z)
            };
        }

        private static BoundingBoxXYZ UnionBoxes(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (b == null || b.Min == null || b.Max == null)
            {
                return a;
            }

            if (a == null || a.Min == null || a.Max == null)
            {
                return new BoundingBoxXYZ { Min = b.Min, Max = b.Max };
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
                Max = new XYZ(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z))
            };
        }

        private static BoundingBoxXYZ IntersectBoxes(BoundingBoxXYZ a, BoundingBoxXYZ b, double z)
        {
            if (a == null || b == null || a.Min == null || a.Max == null || b.Min == null || b.Max == null)
            {
                return null;
            }

            double minX = Math.Max(Math.Min(a.Min.X, a.Max.X), Math.Min(b.Min.X, b.Max.X));
            double minY = Math.Max(Math.Min(a.Min.Y, a.Max.Y), Math.Min(b.Min.Y, b.Max.Y));
            double maxX = Math.Min(Math.Max(a.Min.X, a.Max.X), Math.Max(b.Min.X, b.Max.X));
            double maxY = Math.Min(Math.Max(a.Min.Y, a.Max.Y), Math.Max(b.Min.Y, b.Max.Y));
            if (maxX <= minX || maxY <= minY)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, z),
                Max = new XYZ(maxX, maxY, z)
            };
        }

        private static List<XYZ> NormalizeLoopPoints(List<XYZ> loopPoints)
        {
            List<XYZ> normalized = new List<XYZ>();
            foreach (XYZ point in loopPoints ?? new List<XYZ>())
            {
                if (point != null)
                {
                    normalized.Add(point);
                }
            }

            if (normalized.Count > 1 && normalized[0].DistanceTo(normalized[normalized.Count - 1]) <= 1e-6)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            return normalized;
        }

        private static double ComputePolygonAreaM2(List<XYZ> points)
        {
            List<XYZ> normalized = NormalizeLoopPoints(points);
            if (normalized.Count < 3)
            {
                return 0.0;
            }

            double signedAreaFt2 = 0.0;
            for (int i = 0; i < normalized.Count; i++)
            {
                XYZ a = normalized[i];
                XYZ b = normalized[(i + 1) % normalized.Count];
                signedAreaFt2 += (a.X * b.Y) - (b.X * a.Y);
            }

            return UnitUtils.ConvertFromInternalUnits(Math.Abs(signedAreaFt2) * 0.5, UnitTypeId.SquareMeters);
        }

        private sealed class AnalysisDatasetBuildInfo
        {
            public ModelBoundaryDatasetBuildResult BuildResult { get; set; } = new ModelBoundaryDatasetBuildResult();
            public double WidthM { get; set; }
            public double HeightM { get; set; }
            public double WindowMm { get; set; }
        }

        private sealed class FilterStats
        {
            public int TooSmall { get; set; }
            public int TooLarge { get; set; }
            public int TooNarrow { get; set; }
            public int AspectTooHigh { get; set; }
            public int Duplicate { get; set; }
            public int NestedWallShellDuplicate { get; set; }
            public int NeedsFix { get; set; }
        }
    }
}
