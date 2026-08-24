using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class TargetRoomSeedDebugVisualizerService
    {
        private const double HalfSizeMm = 150.0;

        private static readonly Dictionary<string, List<ElementId>> LastMarkerIdsByDoc =
            new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

        public static void Draw(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (doc == null || doc.ActiveView == null || doc.ActiveView is View3D)
            {
                return;
            }

            List<TargetRoomSeed> seeds = TargetRoomSeedStorageService.LoadSeeds(doc);
            if (seeds == null || seeds.Count == 0)
            {
                ClearLast(doc);
                return;
            }

            HashSet<string> matchedKeys = new HashSet<string>(
                (summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                    .Select(x => x.Key),
                StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "Draw Target Room Seed Debug Marks"))
            {
                tx.Start();

                ClearLastInternal(doc);

                OverrideGraphicSettings matchedOgs = BuildGraphicSettings(new Color(32, 178, 84));
                OverrideGraphicSettings failedOgs = BuildGraphicSettings(new Color(220, 50, 47));
                List<ElementId> createdIds = new List<ElementId>();

                foreach (TargetRoomSeed seed in seeds)
                {
                    if (seed == null || seed.Position == null)
                    {
                        continue;
                    }

                    bool matched = !string.IsNullOrWhiteSpace(seed.Key) && matchedKeys.Contains(seed.Key);
                    OverrideGraphicSettings ogs = matched ? matchedOgs : failedOgs;
                    createdIds.AddRange(DrawX(doc, doc.ActiveView, seed.Position, HalfSizeMm, ogs));

                    DiagnosticRecorder.AppendDebug(
                        "[TargetSeedDebug] Key=" + (seed.Key ?? string.Empty) +
                        ", RawText=" + (seed.RawText ?? string.Empty) +
                        ", Layer=" + (seed.SourceLayer ?? string.Empty) +
                        ", XYZ=(" + FormatPoint(seed.Position) + ")" +
                        ", Result=" + (matched ? "Matched" : "Failed"));
                }

                SaveLastIds(doc, createdIds);
                tx.Commit();
            }
        }

        private static void ClearLast(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            using (Transaction tx = new Transaction(doc, "Clear Target Room Seed Debug Marks"))
            {
                tx.Start();
                ClearLastInternal(doc);
                tx.Commit();
            }
        }

        private static void ClearLastInternal(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            string docKey = BuildDocumentKey(doc);
            if (!LastMarkerIdsByDoc.TryGetValue(docKey, out List<ElementId> ids) || ids == null || ids.Count == 0)
            {
                return;
            }

            List<ElementId> deleteIds = ids
                .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                .Distinct()
                .ToList();
            if (deleteIds.Count > 0)
            {
                doc.Delete(deleteIds);
            }

            LastMarkerIdsByDoc.Remove(docKey);
        }

        private static void SaveLastIds(Document doc, List<ElementId> ids)
        {
            if (doc == null)
            {
                return;
            }

            string docKey = BuildDocumentKey(doc);
            LastMarkerIdsByDoc[docKey] = (ids ?? new List<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();
        }

        private static List<ElementId> DrawX(
            Document doc,
            View view,
            XYZ center,
            double halfSizeMm,
            OverrideGraphicSettings ogs)
        {
            List<ElementId> result = new List<ElementId>();
            if (doc == null || view == null || center == null)
            {
                return result;
            }

            double halfFt = halfSizeMm / 304.8;
            XYZ a0 = new XYZ(center.X - halfFt, center.Y - halfFt, center.Z);
            XYZ a1 = new XYZ(center.X + halfFt, center.Y + halfFt, center.Z);
            XYZ b0 = new XYZ(center.X - halfFt, center.Y + halfFt, center.Z);
            XYZ b1 = new XYZ(center.X + halfFt, center.Y - halfFt, center.Z);

            TryCreateLine(doc, view, a0, a1, ogs, result);
            TryCreateLine(doc, view, b0, b1, ogs, result);
            return result;
        }

        private static void TryCreateLine(
            Document doc,
            View view,
            XYZ start,
            XYZ end,
            OverrideGraphicSettings ogs,
            List<ElementId> result)
        {
            if (doc == null || view == null || start == null || end == null || start.DistanceTo(end) < 1e-6)
            {
                return;
            }

            try
            {
                DetailCurve curve = doc.Create.NewDetailCurve(view, Line.CreateBound(start, end));
                if (curve != null)
                {
                    view.SetElementOverrides(curve.Id, ogs);
                    result?.Add(curve.Id);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TargetSeedDebug] TryCreateLine failed: " + ex.Message);
                DiagnosticRecorder.AppendDebug("[TargetSeedDebug] TryCreateLine failed: " + ex.Message);
            }
        }

        private static OverrideGraphicSettings BuildGraphicSettings(Color color)
        {
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetProjectionLineWeight(7);
            return ogs;
        }

        private static string BuildDocumentKey(Document doc)
        {
            if (doc == null)
            {
                return string.Empty;
            }

            string path = doc.PathName ?? string.Empty;
            string title = doc.Title ?? string.Empty;
            return path + "|" + title + "|" + doc.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return string.Empty;
            }

            return point.X.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
