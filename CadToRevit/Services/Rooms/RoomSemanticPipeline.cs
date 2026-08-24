using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public sealed class RoomSemanticConfig
    {
        public List<string> TargetKeywords { get; set; } = new List<string> { "A/C", "AHU", "PAU" };

        public double CloseTolMm { get; set; } = 10.0;

        public double MaxPatchMm { get; set; } = 300.0;

        public double MinAreaM2 { get; set; } = 1.0;

        public double DoorGapMaxMm { get; set; } = 1200.0;

        public double SmallGapPatchMaxMm { get; set; } = 350.0;
    }

    public sealed class RoomSemanticRunResult
    {
        public List<RoomSemanticRecord> Rooms { get; set; } = new List<RoomSemanticRecord>();

        public List<RoomSemanticRecord> DebugCandidates { get; set; } = new List<RoomSemanticRecord>();

        public List<RoomLabel> TargetLabels { get; set; } = new List<RoomLabel>();

        public List<RoomLabel> UnmatchedLabels { get; set; } = new List<RoomLabel>();

        public int Total { get; set; }

        public int Matched { get; set; }

        public int NoLabel { get; set; }

        public int NeedsFix { get; set; }

        public int OpenRoom { get; set; }

        public int UnmatchedLabel { get; set; }
    }

    public static class RoomSemanticPipeline
    {
        public static RoomSemanticRunResult Run(
            CadDataset runDataset,
            HashSet<string> wallLayers,
            string roomNameLayer,
            RoomSemanticConfig cfg)
        {
            RoomSemanticRunResult result = new RoomSemanticRunResult();
            RoomSemanticConfig useCfg = cfg ?? new RoomSemanticConfig();
            HashSet<string> layers = wallLayers ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (runDataset == null || layers.Count == 0)
            {
                return result;
            }

            List<RoomLabel> labels = ExtractLabels(runDataset, roomNameLayer);
            List<RoomLabel> targetLabels = FilterTargetRoomLabels(labels, useCfg.TargetKeywords);
            result.TargetLabels = targetLabels;
            DetectLocalRooms(runResult: result, dataset: runDataset, wallLayers: layers, cfg: useCfg);
            FillStats(result);
            return result;
        }

        private static List<RoomLabel> ExtractLabels(CadDataset dataset, string roomNameLayer)
        {
            // Room name labels are optional; fallback naming is used when nothing is found.
            List<CadText> allTexts = dataset != null ? (dataset.Texts ?? new List<CadText>()) : new List<CadText>();
            IEnumerable<CadText> selected = allTexts;
            if (!string.IsNullOrWhiteSpace(roomNameLayer))
            {
                selected = allTexts.Where(x => x != null && string.Equals(x.RawLayerName, roomNameLayer, StringComparison.OrdinalIgnoreCase));
            }

            List<RoomLabel> labels = new List<RoomLabel>();
            foreach (CadText text in selected)
            {
                RoomLabel label = RoomLabelParser.Parse(text);
                if (label != null && label.Position != null)
                {
                    labels.Add(label);
                }
            }

            return MergeRoomLabels(labels, roomNameLayer);
        }

        private static List<RoomLabel> MergeRoomLabels(List<RoomLabel> labels, string roomNameLayer)
        {
            List<RoomLabel> source = labels ?? new List<RoomLabel>();
            if (source.Count <= 1)
            {
                return source;
            }

            double xTolFt = 500.0 / 304.8;
            double yTolFt = 700.0 / 304.8;
            double distTolFt = 800.0 / 304.8;
            List<RoomLabel> sorted = source
                .Where(x => x != null && x.Position != null)
                .OrderBy(x => x.Position.X)
                .ThenByDescending(x => x.Position.Y)
                .ToList();
            bool[] used = new bool[sorted.Count];
            List<RoomLabel> merged = new List<RoomLabel>();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                RoomLabel seed = sorted[i];
                string layer = seed.SourceLayer ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(roomNameLayer) &&
                    !string.Equals(layer, roomNameLayer, StringComparison.OrdinalIgnoreCase))
                {
                    used[i] = true;
                    merged.Add(seed);
                    continue;
                }

                List<RoomLabel> group = new List<RoomLabel> { seed };
                used[i] = true;
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    RoomLabel cur = sorted[j];
                    if (!string.Equals(cur.SourceLayer, layer, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool closeToGroup = group.Any(g =>
                        Math.Abs(g.Position.X - cur.Position.X) <= xTolFt &&
                        Math.Abs(g.Position.Y - cur.Position.Y) <= yTolFt &&
                        g.Position.DistanceTo(cur.Position) <= distTolFt);
                    if (!closeToGroup)
                    {
                        continue;
                    }

                    used[j] = true;
                    group.Add(cur);
                }

                if (group.Count == 1)
                {
                    merged.Add(seed);
                    continue;
                }

                List<RoomLabel> ordered = group
                    .OrderByDescending(x => x.Position.Y)
                    .ThenBy(x => x.Position.X)
                    .ToList();
                string roomName = string.Join(" ",
                    ordered
                        .Select(x => x.RoomName)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    .Trim();
                string roomNumber = ordered
                    .Select(x => x.RoomNumber)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
                double minX = ordered.Min(x => x.Position.X);
                double maxX = ordered.Max(x => x.Position.X);
                double minY = ordered.Min(x => x.Position.Y);
                double maxY = ordered.Max(x => x.Position.Y);
                merged.Add(new RoomLabel
                {
                    RawText = string.Join(" | ", ordered.Select(x => x.RawText).Where(x => !string.IsNullOrWhiteSpace(x))),
                    RoomName = roomName,
                    RoomNumber = roomNumber,
                    SourceLayer = layer,
                    Position = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0.0)
                });
            }

            return merged;
        }

        private static List<RoomLabel> FilterTargetRoomLabels(List<RoomLabel> labels, List<string> keywords)
        {
            List<RoomLabel> source = labels ?? new List<RoomLabel>();
            List<string> keys = (keywords ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => NormalizeForKeyword(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
            {
                return new List<RoomLabel>();
            }

            List<RoomLabel> result = new List<RoomLabel>();
            foreach (RoomLabel label in source)
            {
                if (label == null)
                {
                    continue;
                }

                string text = NormalizeForKeyword((label.RoomName ?? string.Empty) + " " + (label.RoomNumber ?? string.Empty));
                string type = ResolveTargetRoomType(text, keys);
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                label.TargetRoomType = type;
                result.Add(label);
            }

            return result;
        }

        private static string ResolveTargetRoomType(string normalizedText, List<string> normalizedKeys)
        {
            string text = normalizedText ?? string.Empty;
            if (text.IndexOf("A/C", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "AC";
            }

            if (text.IndexOf("AHU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "AHU";
            }

            if (text.IndexOf("PAU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PAU";
            }

            foreach (string key in normalizedKeys ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (text.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return key;
                }
            }

            return string.Empty;
        }

        private static string NormalizeForKeyword(string value)
        {
            string text = (value ?? string.Empty).Trim().ToUpperInvariant();
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            text = text.Replace("／", "/");
            text = text.Replace("A / C", "A/C");
            text = text.Replace("A/ C", "A/C");
            text = text.Replace("A /C", "A/C");
            return text;
        }

        private static void DetectLocalRooms(
            RoomSemanticRunResult runResult,
            CadDataset dataset,
            HashSet<string> wallLayers,
            RoomSemanticConfig cfg)
        {
            if (runResult == null || runResult.TargetLabels == null || runResult.TargetLabels.Count == 0)
            {
                return;
            }

            int seq = 1;
            foreach (RoomLabel label in runResult.TargetLabels ?? new List<RoomLabel>())
            {
                TargetRoomLocalDetectResult local = TargetRoomLocalDetector.DetectLocalRoomForLabel(dataset, wallLayers, label, cfg);
                if (local == null || local.MatchedLoop == null)
                {
                    if (local != null && local.DebugSegments != null && local.DebugSegments.Count > 0)
                    {
                        int debugSeq = 0;
                        foreach (Services.CadSegment segment in local.DebugSegments.Where(x => x != null && x.P0 != null && x.P1 != null))
                        {
                            runResult.DebugCandidates.Add(new RoomSemanticRecord
                            {
                                Key = "debug_" + seq.ToString("0000") + "_" + debugSeq.ToString("000"),
                                RoomName = string.IsNullOrWhiteSpace(label.RoomName) ? ("ROOM-" + seq.ToString("000")) : label.RoomName,
                                RoomNumber = string.IsNullOrWhiteSpace(label.RoomNumber) ? seq.ToString("000") : label.RoomNumber,
                                TargetRoomType = label.TargetRoomType ?? string.Empty,
                                Status = "DebugSegment",
                                BoundaryLayers = segment.RawLayerName ?? string.Empty,
                                Centroid = new XYZ((segment.P0.X + segment.P1.X) * 0.5, (segment.P0.Y + segment.P1.Y) * 0.5, 0.0),
                                BBox = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(Math.Min(segment.P0.X, segment.P1.X), Math.Min(segment.P0.Y, segment.P1.Y), 0.0),
                                    Max = new XYZ(Math.Max(segment.P0.X, segment.P1.X), Math.Max(segment.P0.Y, segment.P1.Y), 0.0)
                                },
                                LoopPoints = new List<XYZ> { segment.P0, segment.P1 }
                            });
                            debugSeq++;
                        }
                    }

                    runResult.UnmatchedLabels.Add(label);
                    continue;
                }

                RoomCandidate loop = local.MatchedLoop;
                RoomSemanticRecord record = new RoomSemanticRecord
                {
                    Key = string.IsNullOrWhiteSpace(loop.Key) ? ("target_room_" + seq.ToString("0000")) : loop.Key,
                    RoomName = string.IsNullOrWhiteSpace(label.RoomName) ? ("ROOM-" + seq.ToString("000")) : label.RoomName,
                    RoomNumber = string.IsNullOrWhiteSpace(label.RoomNumber) ? seq.ToString("000") : label.RoomNumber,
                    TargetRoomType = label.TargetRoomType ?? string.Empty,
                    Status = loop.CloseGapMm > 0.0 ? "MatchedPatched" : "MatchedClosed",
                    AreaM2 = loop.AreaM2,
                    CloseGapMm = loop.CloseGapMm,
                    BoundaryLayers = loop.SourceLayer ?? string.Empty,
                    Centroid = loop.Centroid ?? XYZ.Zero,
                    BBox = loop.BBox ?? new BoundingBoxXYZ(),
                    LoopPoints = loop.LoopPoints ?? new List<XYZ>()
                };
                runResult.Rooms.Add(record);
                seq++;
            }
        }

        private static void FillStats(RoomSemanticRunResult result)
        {
            result.Total = result.TargetLabels != null ? result.TargetLabels.Count : 0;
            result.Matched = result.Rooms.Count(x => x != null && x.Status != null && x.Status.StartsWith("Matched", StringComparison.OrdinalIgnoreCase));
            result.NoLabel = result.Rooms.Count(x => string.Equals(x.Status, "NoLabel", StringComparison.OrdinalIgnoreCase));
            result.NeedsFix = result.Rooms.Count(x => string.Equals(x.Status, "NeedsFix", StringComparison.OrdinalIgnoreCase));
            result.OpenRoom = result.Rooms.Count(x => string.Equals(x.Status, "OpenRoom", StringComparison.OrdinalIgnoreCase));
            result.UnmatchedLabel = result.UnmatchedLabels.Count;
        }
    }
}
