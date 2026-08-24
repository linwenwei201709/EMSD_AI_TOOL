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
    public static class RoomRangeVisualizationService
    {
        // Draw matched room boundaries in active view for quick visual verification.
        public static Dictionary<string, List<ElementId>> DrawMatchedRoomRanges(Document doc, RoomSemanticRunResult runResult)
        {
            Dictionary<string, List<ElementId>> result = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || runResult == null || doc.ActiveView == null)
            {
                return result;
            }
            if (doc.ActiveView is View3D)
            {
                // Boundary helper lines are 2D visual aids and are intentionally disabled in 3D views.
                return result;
            }

            List<RoomSemanticRecord> matchedRooms = (runResult.Rooms ?? new List<RoomSemanticRecord>())
                .Where(x => x != null &&
                            (x.Status ?? string.Empty).StartsWith("Matched", StringComparison.OrdinalIgnoreCase) &&
                            x.LoopPoints != null &&
                            x.LoopPoints.Count >= 2)
                .ToList();
            if (matchedRooms.Count == 0)
            {
                return result;
            }

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(96, 42, 149)); // Deep purple.
            ogs.SetProjectionLineWeight(8);

            using (Transaction tx = new Transaction(doc, "Draw Target Room Ranges"))
            {
                tx.Start();
                foreach (RoomSemanticRecord room in matchedRooms)
                {
                    string roomKey = room.Key ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(roomKey) && !result.ContainsKey(roomKey))
                    {
                        result[roomKey] = new List<ElementId>();
                    }

                    List<XYZ> points = room.LoopPoints;
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        TryCreateRangeLine(doc, doc.ActiveView, points[i], points[i + 1], ogs, result, roomKey);
                    }

                    // Ensure closed polygon display when loop is not explicitly closed.
                    XYZ first = points.FirstOrDefault();
                    XYZ last = points.LastOrDefault();
                    if (first != null && last != null && first.DistanceTo(last) > 1e-6)
                    {
                        TryCreateRangeLine(doc, doc.ActiveView, last, first, ogs, result, roomKey);
                    }
                }

                tx.Commit();
            }

            return result;
        }

        public static void FilterSummaryByCreatedRanges(
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<ElementId>> roomElementMap)
        {
            if (summary == null || summary.RunResult == null || summary.RunResult.Rooms == null)
            {
                return;
            }

            List<RoomSemanticRecord> kept = new List<RoomSemanticRecord>();
            int failed = 0;
            foreach (RoomSemanticRecord room in summary.RunResult.Rooms ?? new List<RoomSemanticRecord>())
            {
                string key = room != null ? (room.Key ?? string.Empty) : string.Empty;
                bool created = !string.IsNullOrWhiteSpace(key) &&
                               roomElementMap != null &&
                               roomElementMap.TryGetValue(key, out List<ElementId> ids) &&
                               ids != null &&
                               ids.Count > 0;
                if (created)
                {
                    kept.Add(room);
                    continue;
                }

                failed++;
                string reason = "RegionCreated=0";
                if (summary.Errors != null)
                {
                    summary.Errors.Add((room != null ? (room.RoomName ?? string.Empty) : string.Empty) + ": " + reason);
                }

                DiagnosticRecorder.AppendDebug("[RoomRangeVis] Visualization failed, RoomName=" +
                    (room != null ? (room.RoomName ?? string.Empty) : string.Empty) +
                    ", RoomKey=" + key +
                    ", LoopPoints=" + ((room != null && room.LoopPoints != null) ? room.LoopPoints.Count.ToString(CultureInfo.InvariantCulture) : "0") +
                    ", Stage=RoomRangeVisualizationService" +
                    ", Reason=" + reason +
                    ", RemovedFromResults=True");
            }

            ApplyFilteredRooms(summary, kept, failed, "RoomRangeVis");
        }

        internal static void ApplyFilteredRooms(
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            List<RoomSemanticRecord> kept,
            int failed,
            string source)
        {
            if (summary == null || summary.RunResult == null)
            {
                return;
            }

            int visualized = kept != null ? kept.Count : 0;
            summary.RunResult.Rooms = kept ?? new List<RoomSemanticRecord>();
            summary.RunResult.Matched = visualized;
            summary.RunResult.Total = visualized;
            summary.RunResult.UnmatchedLabel = 0;
            summary.RunResult.NeedsFix += failed;
            summary.Matched = visualized;
            summary.Failed += failed;
            DiagnosticRecorder.AppendDebug("[" + (source ?? "RoomVisualization") + "] VisualizedRooms=" +
                visualized.ToString(CultureInfo.InvariantCulture) +
                ", VisualizationFailed=" + failed.ToString(CultureInfo.InvariantCulture) +
                ", RemovedFromResults=" + failed.ToString(CultureInfo.InvariantCulture));
        }

        private static void TryCreateRangeLine(
            Document doc,
            View view,
            XYZ a,
            XYZ b,
            OverrideGraphicSettings ogs,
            Dictionary<string, List<ElementId>> roomElementMap,
            string roomKey)
        {
            if (doc == null || view == null || a == null || b == null || a.DistanceTo(b) < 1e-6)
            {
                return;
            }

            try
            {
                DetailCurve curve = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                if (curve != null)
                {
                    view.SetElementOverrides(curve.Id, ogs);
                    if (!string.IsNullOrWhiteSpace(roomKey) && roomElementMap != null && roomElementMap.TryGetValue(roomKey, out List<ElementId> ids))
                    {
                        ids.Add(curve.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[TargetRoomModelRecognitionCommand] TryCreateRangeLine failed. "
                    + "ViewType=" + (view != null ? view.ViewType.ToString() : "null")
                    + ", RoomKey=" + (roomKey ?? string.Empty)
                    + ", Error=" + ex.Message);
            }
        }
    }
}
