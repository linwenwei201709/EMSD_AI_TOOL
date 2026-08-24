using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CadToRevit.Services.Rooms
{
    public static class Room3DVisualizationIfcCleanupService
    {
        public sealed class CleanupResult
        {
            public int DeletedRegionCount { get; set; }
            public int DeletedMarkerCount { get; set; }
            public int DeletedTextCount { get; set; }
            public int DeletedTagCount { get; set; }
            public int DeletedTotal { get; set; }
        }

        public static CleanupResult DeleteVisualizationElementsForIfcExport(Document doc)
        {
            CleanupResult result = new CleanupResult();
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] Started");

            if (doc == null)
            {
                LogResult(result);
                return result;
            }

            List<CleanupCandidate> candidates = new List<CleanupCandidate>();
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (element == null || element.Id == null || !TryClassify(element.Name, out VisualizationKind kind))
                {
                    continue;
                }

                candidates.Add(new CleanupCandidate { ElementId = element.Id, Kind = kind });
            }

            if (candidates.Count > 0)
            {
                using (Transaction tx = new Transaction(doc, "Delete Room 3D Visualization Before IFC Export"))
                {
                    tx.Start();
                    foreach (CleanupCandidate candidate in candidates)
                    {
                        try
                        {
                            ICollection<ElementId> deleted = doc.Delete(candidate.ElementId);
                            if (deleted != null && deleted.Count > 0)
                            {
                                Increment(result, candidate.Kind);
                            }
                        }
                        catch (Exception ex)
                        {
                            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeleteSkipped ElementId=" +
                                (candidate.ElementId == null ? "-" : candidate.ElementId.IntegerValue.ToString(CultureInfo.InvariantCulture)) +
                                ", Error=" + ex.Message);
                        }
                    }
                    tx.Commit();
                }
            }

            result.DeletedTotal = result.DeletedRegionCount +
                                  result.DeletedMarkerCount +
                                  result.DeletedTextCount +
                                  result.DeletedTagCount;
            LogResult(result);
            return result;
        }

        private static bool TryClassify(string name, out VisualizationKind kind)
        {
            kind = VisualizationKind.Unknown;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (name.StartsWith(Room3DVisualizationConstants.RegionNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisualizationKind.Region;
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.MarkerNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisualizationKind.Marker;
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.AhuPlacementPointMarkerNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisualizationKind.Marker;
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.TextNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisualizationKind.Text;
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.LegacyTagNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = VisualizationKind.Tag;
                return true;
            }

            return false;
        }

        private static void Increment(CleanupResult result, VisualizationKind kind)
        {
            if (result == null)
            {
                return;
            }

            if (kind == VisualizationKind.Region)
            {
                result.DeletedRegionCount++;
            }
            else if (kind == VisualizationKind.Marker)
            {
                result.DeletedMarkerCount++;
            }
            else if (kind == VisualizationKind.Text)
            {
                result.DeletedTextCount++;
            }
            else if (kind == VisualizationKind.Tag)
            {
                result.DeletedTagCount++;
            }
        }

        private static void LogResult(CleanupResult result)
        {
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeletedRegionCount=" + result.DeletedRegionCount.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeletedMarkerCount=" + result.DeletedMarkerCount.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeletedTextCount=" + result.DeletedTextCount.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeletedTagCount=" + result.DeletedTagCount.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] DeletedTotal=" + result.DeletedTotal.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[Room3DVisIfcCleanup] Finished");
        }

        private enum VisualizationKind
        {
            Unknown,
            Region,
            Marker,
            Text,
            Tag
        }

        private sealed class CleanupCandidate
        {
            public ElementId ElementId { get; set; }
            public VisualizationKind Kind { get; set; }
        }
    }
}
