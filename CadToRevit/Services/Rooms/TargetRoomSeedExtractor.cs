using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class TargetRoomSeedExtractor
    {
        private static readonly string[] DefaultKeywords = { "A/C", "AHU", "PAU" };

        public static List<TargetRoomSeed> ExtractFromDataset(
            CadDataset dataset,
            string roomNameLayer,
            IList<string> targetKeywords,
            ElementId levelId)
        {
            List<TargetRoomSeed> result = new List<TargetRoomSeed>();
            List<string> keys = (targetKeywords == null || targetKeywords.Count == 0)
                ? DefaultKeywords.ToList()
                : targetKeywords.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (dataset == null || dataset.Texts == null || dataset.Texts.Count == 0 || keys.Count == 0)
            {
                return result;
            }

            IEnumerable<CadText> source = dataset.Texts.Where(x => x != null && x.Position != null);
            if (!string.IsNullOrWhiteSpace(roomNameLayer))
            {
                source = source.Where(x => string.Equals(x.RawLayerName, roomNameLayer, StringComparison.OrdinalIgnoreCase));
            }

            foreach (CadText text in source)
            {
                string raw = text.Text ?? string.Empty;
                if (raw.Trim().Length > 120)
                {
                    continue;
                }

                string normalized = Normalize(raw);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                string targetType = ResolveTargetType(normalized, keys);
                if (string.IsNullOrWhiteSpace(targetType))
                {
                    continue;
                }

                TargetRoomSeed seed = new TargetRoomSeed
                {
                    RoomName = raw.Trim(),
                    TargetRoomType = targetType,
                    Position = text.Position,
                    LevelId = levelId ?? ElementId.InvalidElementId,
                    SourceLayer = text.RawLayerName ?? string.Empty,
                    RawText = raw
                };
                seed.Key = BuildKey(seed);
                result.Add(seed);
            }

            return result
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        // Build deterministic seed key for dedupe and storage lookup.
        private static string BuildKey(TargetRoomSeed seed)
        {
            if (seed == null || seed.Position == null)
            {
                return string.Empty;
            }

            int level = seed.LevelId != null ? seed.LevelId.IntegerValue : -1;
            double xMm = UnitUtils.ConvertFromInternalUnits(seed.Position.X, UnitTypeId.Millimeters);
            double yMm = UnitUtils.ConvertFromInternalUnits(seed.Position.Y, UnitTypeId.Millimeters);
            string room = (seed.RoomName ?? string.Empty).Trim();
            return level + "|" + room + "|" + Math.Round(xMm, 0) + "|" + Math.Round(yMm, 0);
        }

        private static string ResolveTargetType(string normalizedText, IList<string> keywords)
        {
            foreach (string keyword in keywords ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (normalizedText.Contains(Normalize(keyword)))
                {
                    return keyword.Trim().ToUpperInvariant();
                }
            }

            return string.Empty;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string s = text.Trim().ToUpperInvariant();
            s = s.Replace("／", "/");
            s = s.Replace("　", string.Empty);
            s = s.Replace(" ", string.Empty);
            s = s.Replace("\t", string.Empty);
            s = s.Replace("\r", string.Empty);
            s = s.Replace("\n", string.Empty);
            return s;
        }
    }
}
