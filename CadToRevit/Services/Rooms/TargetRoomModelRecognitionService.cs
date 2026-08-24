using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms.Lifts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class TargetRoomModelRecognitionService
    {
        public sealed class RecognitionSummary
        {
            public int TotalSeeds { get; set; }
            public int Matched { get; set; }
            public int Failed { get; set; }
            public string Message { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public RoomSemanticRunResult RunResult { get; set; } = new RoomSemanticRunResult();
            public Dictionary<string, int> SeedLevelIdByKey { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<LiftRecognitionRecord> Lifts { get; set; } = new List<LiftRecognitionRecord>();
        }

        public static RecognitionSummary Run(Document doc, double windowSizeMm = 0.0)
        {
            RecognitionSummary summary = new RecognitionSummary();
            if (doc == null)
            {
                summary.Message = "No active document.";
                return summary;
            }

            if (windowSizeMm <= 0.0)
            {
                windowSizeMm = ModelRoomSeedRecognitionService.ResolveRecognitionWindowSizeMm(doc);
            }

            List<TargetRoomSeed> seeds = TargetRoomSeedStorageService.LoadSeeds(doc);
            summary.Lifts = LiftRecognitionStorageService.Load(doc);
            summary.TotalSeeds = seeds.Count;
            if (seeds.Count == 0)
            {
                summary.Message = "No target room seeds found. Lifts=" + summary.Lifts.Count;
                return summary;
            }

            RoomSemanticRunResult run = new RoomSemanticRunResult();
            RoomRecognitionSettings settings = ModelRoomSeedRecognitionService.ResolveRoomRecognitionSettings(doc);
            List<string> targetKeywords = settings.GetConfiguredTargetKeywords();
            if (targetKeywords.Count == 0)
            {
                targetKeywords = new List<string> { "A/C", "AHU", "PAU" };
            }
            RoomSemanticConfig recognitionConfig = ModelRoomSeedRecognitionService.BuildRecognitionConfig(settings, targetKeywords);

            foreach (TargetRoomSeed seed in seeds)
            {
                if (seed == null || seed.Position == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(seed.Key))
                {
                    int levelIdValue = seed.LevelId != null ? seed.LevelId.IntegerValue : -1;
                    summary.SeedLevelIdByKey[seed.Key] = levelIdValue;
                }

                try
                {
                    ModelRoomSeedRecognitionResult seedResult = ModelRoomSeedRecognitionService.RecognizeSeed(
                        doc,
                        seed,
                        windowSizeMm,
                        recognitionConfig);
                    if (!seedResult.Success || seedResult.Record == null)
                    {
                        summary.Failed++;
                        run.UnmatchedLabels.Add(new RoomLabel
                        {
                            RawText = seed.RawText ?? string.Empty,
                            RoomName = seed.RoomName ?? string.Empty,
                            TargetRoomType = seed.TargetRoomType ?? string.Empty,
                            SourceLayer = seed.SourceLayer ?? string.Empty,
                            Position = seed.Position
                        });
                        string failReason = string.IsNullOrWhiteSpace(seedResult.FailureReason)
                            ? "NoContainingClosedLoop"
                            : seedResult.FailureReason;
                        summary.Errors.Add((seed.RoomName ?? seed.Key ?? "-") + ": " + failReason);
                        continue;
                    }

                    run.Rooms.Add(seedResult.Record);
                    summary.Matched++;
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    summary.Errors.Add((seed.RoomName ?? seed.Key ?? "-") + ": " + ex.Message);
                }
            }

            run.TargetLabels = seeds.Select(x => new RoomLabel
            {
                RawText = x.RawText ?? string.Empty,
                RoomName = x.RoomName ?? string.Empty,
                RoomNumber = string.Empty,
                TargetRoomType = x.TargetRoomType ?? string.Empty,
                SourceLayer = x.SourceLayer ?? string.Empty,
                Position = x.Position
            }).ToList();
            run.Total = summary.TotalSeeds;
            run.Matched = summary.Matched;
            run.UnmatchedLabel = summary.Failed;
            run.NoLabel = 0;
            run.NeedsFix = summary.Failed;
            run.OpenRoom = 0;
            summary.RunResult = run;

            using (Transaction tx = new Transaction(doc, "Save Model Room Semantics"))
            {
                tx.Start();
                FailureHandlingOptions options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new NonCriticalWarningsPreprocessor("ModelRoomSemanticSave"));
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                TargetRoomSeedStorageService.SaveSeeds(doc, seeds);
                LiftRecognitionStorageService.Save(doc, summary.Lifts);
                RoomSemanticStorageService.Save(doc, run, new RoomSemanticStorageMeta
                {
                    DwgImportId = -1,
                    LevelId = -1,
                    RoomNameLayer = "(ALL_TEXTS)",
                    WallLayers = new List<string>
                    {
                        ModelBoundarySegmentBuilder.WallBoundaryLayerName,
                        ModelBoundarySegmentBuilder.RoomSeparatorLayerName,
                        ModelBoundarySegmentBuilder.DoorClosureLayerName
                    },
                    Config = new RoomSemanticConfig
                    {
                        TargetKeywords = targetKeywords,
                        CloseTolMm = recognitionConfig.CloseTolMm,
                        MaxPatchMm = recognitionConfig.MaxPatchMm,
                        MinAreaM2 = recognitionConfig.MinAreaM2,
                        DoorGapMaxMm = recognitionConfig.DoorGapMaxMm,
                        SmallGapPatchMaxMm = recognitionConfig.SmallGapPatchMaxMm
                    }
                });
                tx.Commit();
            }

            summary.Message = "Model room recognition done. Seeds=" + summary.TotalSeeds +
                              ", Matched=" + summary.Matched +
                              ", Failed=" + summary.Failed +
                              ", Lifts=" + summary.Lifts.Count;
            DiagnosticRecorder.AppendDebug("[ModelRoomRecognition] " + summary.Message);
            return summary;
        }

    }
}
