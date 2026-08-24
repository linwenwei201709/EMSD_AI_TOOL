using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyzeCadSegmentsCommand : IExternalCommand
    {
        private static readonly HashSet<string> DefaultLayerFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WALL",
            "DOOR",
            "WINDOW",
            "GRID"
        };

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            ImportInstance importInstance = GetSelectedImportInstance(uiDoc)
                ?? GetFirstImportInstance(doc);

            if (importInstance == null)
            {
                UiMessageService.Warning("Command.AnalyzeCadSegments.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            CadSegmentBuildResult result = CadSegmentBuilder.BuildSegments(doc, importInstance, DefaultLayerFilter);
            if (result.Segments.Count == 0)
            {
                UiMessageService.Info("Command.AnalyzeCadSegments.Title", "Dialog.NoSegmentsExtracted.Message");
                return Result.Succeeded;
            }

            string output = BuildOutput(importInstance, result);
            UiMessageService.ShowTaskDialogText("Command.AnalyzeCadSegments.Title", output);
            return Result.Succeeded;
        }

        private static ImportInstance GetSelectedImportInstance(UIDocument uiDoc)
        {
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                return null;
            }

            foreach (ElementId id in selectedIds)
            {
                ImportInstance instance = uiDoc.Document.GetElement(id) as ImportInstance;
                if (instance != null)
                {
                    return instance;
                }
            }

            return null;
        }

        private static ImportInstance GetFirstImportInstance(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .FirstOrDefault();
        }

        private static string BuildOutput(ImportInstance importInstance, CadSegmentBuildResult result)
        {
            StringBuilder sb = new StringBuilder();
            string importName = importInstance.Name ?? "(Unnamed)";

            sb.AppendLine("CAD Link: " + importName);
            sb.AppendLine("ElementId: " + importInstance.Id.IntegerValue);
            sb.AppendLine();

            sb.AppendLine("Segment Count By Layer:");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "WALL");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "DOOR");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "WINDOW");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "GRID");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "0");
            AppendLayerCount(sb, result.Diagnostics.SegmentCountByLayer, "UNKNOWN");
            AppendOtherLayerCounts(sb, result.Diagnostics.SegmentCountByLayer);
            sb.AppendLine();

            sb.AppendLine("Segment Type Counts:");
            AppendTypeCount(sb, result.Diagnostics.SegmentCountBySourceType, CadCurveSourceType.NativeLine);
            AppendTypeCount(sb, result.Diagnostics.SegmentCountBySourceType, CadCurveSourceType.PolyLineSegment);
            AppendTypeCount(sb, result.Diagnostics.SegmentCountBySourceType, CadCurveSourceType.Other);
            sb.AppendLine("IgnoredGeometry: " + result.Diagnostics.IgnoredGeometryCount);
            sb.AppendLine("TinySegmentsSkipped: " + result.Diagnostics.TinySegmentSkippedCount);
            sb.AppendLine();

            sb.AppendLine("WALL Segment Samples (first 10, mm):");
            List<CadSegment> wallSamples = result.Segments
                .Where(x => string.Equals(x.SemanticLayer, "WALL", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();
            if (wallSamples.Count == 0)
            {
                sb.AppendLine("- none");
            }
            else
            {
                foreach (CadSegment segment in wallSamples)
                {
                    sb.AppendLine("- " + FormatPointMm(segment.P0) + " -> " + FormatPointMm(segment.P1));
                }
            }

            sb.AppendLine();
            sb.AppendLine("Raw Layer Samples (first 10):");
            foreach (string sample in result.Diagnostics.RawLayerSamples.Take(10))
            {
                sb.AppendLine("- " + sample);
            }

            return sb.ToString();
        }

        private static void AppendLayerCount(StringBuilder sb, Dictionary<string, int> map, string layer)
        {
            int count = map.ContainsKey(layer) ? map[layer] : 0;
            sb.AppendLine(layer + ": " + count);
        }

        private static void AppendTypeCount(
            StringBuilder sb,
            Dictionary<CadCurveSourceType, int> map,
            CadCurveSourceType sourceType)
        {
            int count = map.ContainsKey(sourceType) ? map[sourceType] : 0;
            sb.AppendLine(sourceType + ": " + count);
        }

        private static void AppendOtherLayerCounts(StringBuilder sb, Dictionary<string, int> map)
        {
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "WALL",
                "DOOR",
                "WINDOW",
                "GRID",
                "0",
                "UNKNOWN"
            };

            List<KeyValuePair<string, int>> others = map
                .Where(x => !known.Contains(x.Key))
                .OrderBy(x => x.Key)
                .ToList();
            foreach (KeyValuePair<string, int> item in others)
            {
                sb.AppendLine(item.Key + ": " + item.Value);
            }
        }

        private static string FormatPointMm(XYZ point)
        {
            if (point == null)
            {
                return "(null)";
            }

            double x = UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters);
            double z = UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters);
            return "(" + x.ToString("F1") + ", " + y.ToString("F1") + ", " + z.ToString("F1") + ")";
        }
    }
}
