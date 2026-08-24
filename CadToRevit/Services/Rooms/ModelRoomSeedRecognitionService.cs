using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal sealed class ModelRoomSeedRecognitionResult
    {
        public bool Success { get; set; }

        public string Status { get; set; }

        public string FailureReason { get; set; }

        public RoomSemanticRecord Record { get; set; }
    }

    internal static class ModelRoomSeedRecognitionService
    {
        // Probe Room uses a dedicated fixed local window and does not reuse the project-scoped
        // room-recognition window setting from the auto-generated element flow.
        internal const double ProbeRecognitionWindowSizeM = 40.0;
        internal const double ProbeRecognitionWindowSizeMm = ProbeRecognitionWindowSizeM * 1000.0;

        internal static RoomRecognitionSettings ResolveRoomRecognitionSettings(Document doc)
        {
            try
            {
                LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
                return RoomRecognitionSettings.Clone(store != null ? store.RoomRecognitionSettings : null);
            }
            catch
            {
                return RoomRecognitionSettings.CreateDefault();
            }
        }

        // Resolve the project-scoped local recognition window for seed-based room detection.
        internal static double ResolveRecognitionWindowSizeMm(Document doc)
        {
            double windowM = RoomRecognitionSettings.DefaultModelRecognitionWindowSizeM;
            try
            {
                LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
                RoomRecognitionSettings settings = RoomRecognitionSettings.Clone(store != null ? store.RoomRecognitionSettings : null);
                windowM = RoomRecognitionSettings.NormalizeModelRecognitionWindowSizeM(settings.ModelRecognitionWindowSizeM);
            }
            catch
            {
                windowM = RoomRecognitionSettings.DefaultModelRecognitionWindowSizeM;
            }

            return windowM * 1000.0;
        }

        // Probe Room uses a larger dedicated window so that wide rooms and off-center pick points
        // are not constrained by the smaller global recognition window.
        internal static double ResolveProbeRecognitionWindowSizeMm()
        {
            return ProbeRecognitionWindowSizeMm;
        }

        internal static RoomSemanticConfig BuildRecognitionConfig(Document doc)
        {
            RoomRecognitionSettings settings = ResolveRoomRecognitionSettings(doc);
            List<string> targetKeywords = settings.GetConfiguredTargetKeywords();
            if (targetKeywords.Count == 0)
            {
                targetKeywords = new List<string> { "A/C", "AHU", "PAU" };
            }

            return BuildRecognitionConfig(settings, targetKeywords);
        }

        internal static RoomSemanticConfig BuildRecognitionConfig(RoomRecognitionSettings settings, List<string> targetKeywords)
        {
            RoomSemanticConfig config = new RoomSemanticConfig
            {
                TargetKeywords = (targetKeywords ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            if (settings != null)
            {
                config.DoorGapMaxMm = settings.DoorGapMaxMm > 0.0 ? settings.DoorGapMaxMm : config.DoorGapMaxMm;
                config.SmallGapPatchMaxMm = settings.SmallGapPatchMaxMm > 0.0 ? settings.SmallGapPatchMaxMm : config.SmallGapPatchMaxMm;
            }

            return config;
        }

        internal static ModelRoomSeedRecognitionResult RecognizeProbeSeed(
            Document doc,
            TargetRoomSeed seed)
        {
            DiagnosticRecorder.AppendDebug(
                "[ProbeRoom] DedicatedRecognitionWindowMm=" + ProbeRecognitionWindowSizeMm.ToString("F0"));
            return RecognizeSeed(
                doc,
                seed,
                ResolveProbeRecognitionWindowSizeMm(),
                BuildRecognitionConfig(doc));
        }

        internal static ModelRoomSeedRecognitionResult RecognizeSeed(
            Document doc,
            TargetRoomSeed seed,
            double windowSizeMm,
            RoomSemanticConfig config)
        {
            ModelRoomSeedRecognitionResult result = new ModelRoomSeedRecognitionResult();
            if (doc == null || seed == null || seed.Position == null || seed.LevelId == null || seed.LevelId == ElementId.InvalidElementId)
            {
                result.FailureReason = "NoValidAnalysisLevel";
                return result;
            }

            DiagnosticRecorder.AppendDebug(
                "[ModelRoomRecognition] Seed=" + BuildSeedDisplay(seed) +
                ", WindowMm=" + windowSizeMm.ToString("F0"));

            ModelBoundaryDatasetBuildResult build = ModelBoundarySegmentBuilder.BuildLocalDataset(
                doc,
                seed.LevelId,
                seed.Position,
                windowSizeMm);
            DiagnosticRecorder.AppendDebug(
                "[ModelRoomRecognition] BoundaryDataset: WallSegments=" + build.WallSegments +
                ", SeparatorSegments=" + build.SeparatorSegments +
                ", DoorClosureSegments=" + build.DoorClosureSegments +
                ", Total=" + ((build.Dataset?.Segments?.Count) ?? 0) +
                ", SkippedCurvedWalls=" + build.SkippedCurvedWalls);
            DiagnosticRecorder.AppendDebug(
                "[ModelRoomRecognition] BoundaryNormalization=Disabled, Seed=" + BuildSeedDisplay(seed) +
                ", Segments=" + ((build.Dataset?.Segments?.Count) ?? 0));

            RoomCandidate matchedLoop = TryMatchLoop(build.Dataset, seed, config, out int totalLoops, out int validLoops, out int containsSeed);
            DiagnosticRecorder.AppendDebug(
                "[ModelRoomRecognition] LoopDetect: TotalLoops=" + totalLoops +
                ", ValidLoops=" + validLoops +
                ", ContainsSeed=" + containsSeed);

            if (matchedLoop != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[ModelRoomRecognition] SelectedLoopAreaM2=" + matchedLoop.AreaM2.ToString("F3"));
                result.Success = true;
                result.Status = "Matched-ModelLoop";
                result.Record = BuildLoopRecord(doc, seed, matchedLoop);
                DiagnosticRecorder.AppendDebug("[ModelRoomRecognition] Result=Matched-ModelLoop");
                return result;
            }

            ModelFloodFillService.FloodFillResult fill = RunFloodFillFallback(doc, seed, windowSizeMm);
            if (!fill.Success)
            {
                result.FailureReason = !string.IsNullOrWhiteSpace(fill.Reason)
                    ? "FloodFillFallbackFailed:" + fill.Reason
                    : "NoContainingClosedLoop";
                DiagnosticRecorder.AppendDebug("[ModelRoomRecognition] Result=Failed, Reason=" + result.FailureReason);
                return result;
            }

            result.Success = true;
            result.Status = "Matched-ModelFloodFillFallback";
            result.Record = BuildFloodFillFallbackRecord(doc, seed, fill);
            DiagnosticRecorder.AppendDebug("[ModelRoomRecognition] Result=Matched-ModelFloodFillFallback");
            return result;
        }

        private static RoomCandidate TryMatchLoop(
            Models.Cad.CadDataset dataset,
            TargetRoomSeed seed,
            RoomSemanticConfig config,
            out int totalLoops,
            out int validLoops,
            out int containsSeed)
        {
            totalLoops = 0;
            validLoops = 0;
            containsSeed = 0;
            if (dataset == null || seed == null || seed.Position == null)
            {
                return null;
            }

            HashSet<string> boundaryLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModelBoundarySegmentBuilder.WallBoundaryLayerName,
                ModelBoundarySegmentBuilder.RoomSeparatorLayerName,
                ModelBoundarySegmentBuilder.DoorClosureLayerName
            };

            List<RoomCandidate> loops = RoomBoundaryLoopService.DetectMulti(
                dataset,
                boundaryLayers,
                config != null ? config.CloseTolMm : 10.0,
                config != null ? config.MaxPatchMm : 300.0,
                config != null ? config.MinAreaM2 : 1.0,
                config != null ? config.DoorGapMaxMm : 1200.0,
                config != null ? config.SmallGapPatchMaxMm : 350.0,
                false,
                false);
            totalLoops = loops.Count;

            List<RoomCandidate> valid = loops
                .Where(x => IsValidLoop(x, config))
                .ToList();
            validLoops = valid.Count;

            List<RoomCandidate> containing = valid
                .Where(x => x != null &&
                            x.LoopPoints != null &&
                            PointInPolygon.ContainsPointXY(x.LoopPoints, seed.Position))
                .OrderBy(x => x.AreaM2)
                .ToList();
            containsSeed = containing.Count;
            return containing.FirstOrDefault();
        }

        private static bool IsValidLoop(RoomCandidate loop, RoomSemanticConfig config)
        {
            if (loop == null || loop.LoopPoints == null || loop.LoopPoints.Count < 4)
            {
                return false;
            }

            if (loop.Status == RoomBoundaryStatus.NeedsFix)
            {
                return false;
            }

            double minAreaM2 = config != null ? Math.Max(0.1, config.MinAreaM2) : 1.0;
            if (loop.AreaM2 < minAreaM2)
            {
                return false;
            }

            return loop.BBox != null && loop.BBox.Min != null && loop.BBox.Max != null;
        }

        private static ModelFloodFillService.FloodFillResult RunFloodFillFallback(
            Document doc,
            TargetRoomSeed seed,
            double windowSizeMm)
        {
            List<Line> boundaries = ModelBoundaryCollector.CollectBoundaryLines(doc, seed.LevelId, seed.Position, windowSizeMm);
            List<Line> doorClosures = DoorClosureBuilder.BuildDoorClosureLines(doc, seed.LevelId, seed.Position, windowSizeMm);
            boundaries.AddRange(doorClosures);
            return ModelFloodFillService.DetectRoomPolygon(
                seed.Position,
                boundaries,
                windowSizeMm,
                150.0);
        }

        internal static RoomSemanticRecord BuildLoopRecord(Document doc, TargetRoomSeed seed, RoomCandidate loop)
        {
            return new RoomSemanticRecord
            {
                Key = seed.Key ?? Guid.NewGuid().ToString("N"),
                RoomName = seed.RoomName ?? string.Empty,
                RoomNumber = string.Empty,
                TargetRoomType = seed.TargetRoomType ?? string.Empty,
                Status = "Matched-ModelLoop",
                AreaM2 = loop != null ? loop.AreaM2 : 0.0,
                CloseGapMm = loop != null ? loop.CloseGapMm : 0.0,
                BoundaryLayers = "MODEL_WALL_BOUNDARY+ROOM_SEPARATION+DOOR_CLOSURE",
                Centroid = loop != null ? loop.Centroid : null,
                BBox = loop != null ? loop.BBox : null,
                LoopPoints = loop != null ? (loop.LoopPoints ?? new List<XYZ>()) : new List<XYZ>(),
                BoundaryWalls = RoomBoundaryWallResolver.Resolve(
                    doc,
                    seed != null ? seed.LevelId : ElementId.InvalidElementId,
                    loop != null ? (loop.LoopPoints ?? new List<XYZ>()) : new List<XYZ>())
            };
        }

        internal static RoomSemanticRecord BuildFloodFillFallbackRecord(Document doc, TargetRoomSeed seed, ModelFloodFillService.FloodFillResult fill)
        {
            return new RoomSemanticRecord
            {
                Key = seed.Key ?? Guid.NewGuid().ToString("N"),
                RoomName = seed.RoomName ?? string.Empty,
                RoomNumber = string.Empty,
                TargetRoomType = seed.TargetRoomType ?? string.Empty,
                Status = "Matched-ModelFloodFillFallback",
                AreaM2 = fill != null ? fill.AreaM2 : 0.0,
                CloseGapMm = 0.0,
                BoundaryLayers = "MODEL_WALL_CENTERLINE+DOOR_CLOSURE",
                Centroid = fill != null ? fill.Centroid : null,
                BBox = fill != null ? fill.BBox : null,
                LoopPoints = fill != null ? (fill.Polygon ?? new List<XYZ>()) : new List<XYZ>(),
                BoundaryWalls = RoomBoundaryWallResolver.Resolve(
                    doc,
                    seed != null ? seed.LevelId : ElementId.InvalidElementId,
                    fill != null ? (fill.Polygon ?? new List<XYZ>()) : new List<XYZ>())
            };
        }

        private static string BuildSeedDisplay(TargetRoomSeed seed)
        {
            if (seed == null)
            {
                return string.Empty;
            }

            string key = seed.Key ?? string.Empty;
            string roomName = seed.RoomName ?? string.Empty;
            return string.IsNullOrWhiteSpace(roomName) ? key : key + "/" + roomName;
        }
    }
}
