using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyzeCadLinkCommand : IExternalCommand
    {
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
                UiMessageService.Warning("Command.AnalyzeCadLink.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            List<CadGeometryData> geometryItems = CadGeometryReader.ReadGeometryItems(doc, importInstance).ToList();
            if (geometryItems.Count == 0)
            {
                UiMessageService.Info("Command.AnalyzeCadLink.Title", "Dialog.NoSupportedGeometry.Message");
                return Result.Succeeded;
            }

            Dictionary<string, int> layerCounts = geometryItems
                .GroupBy(x => x.LayerName)
                .ToDictionary(g => g.Key, g => g.Count());
            Dictionary<string, int> typeCounts = geometryItems
                .GroupBy(x => x.GeometryType)
                .ToDictionary(g => g.Key, g => g.Count());
            List<string> rawLayerSamples = geometryItems
                .Select(x => x.RawLayerName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(10)
                .ToList();

            string importName = importInstance.Name ?? "(Unnamed)";
            string output = BuildOutput(importName, layerCounts, typeCounts, rawLayerSamples);
            UiMessageService.ShowTaskDialogText("Command.AnalyzeCadLink.Title", output);
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

        private static string BuildOutput(
            string importName,
            Dictionary<string, int> layerCounts,
            Dictionary<string, int> typeCounts,
            List<string> rawLayerSamples)
        {
            string[] orderedLayers = { "WALL", "DOOR", "GRID", "0", "UNKNOWN" };
            string[] orderedTypes = { "Line", "PolyLine", "Arc", "OtherCurve" };
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CAD Link: " + importName);
            sb.AppendLine("Curve Count By Layer:");

            foreach (string layer in orderedLayers)
            {
                int value = layerCounts.ContainsKey(layer) ? layerCounts[layer] : 0;
                sb.AppendLine(layer + ": " + value);
            }

            IEnumerable<KeyValuePair<string, int>> otherLayers = layerCounts
                .Where(x => !orderedLayers.Contains(x.Key))
                .OrderBy(x => x.Key);
            foreach (KeyValuePair<string, int> item in otherLayers)
            {
                sb.AppendLine(item.Key + ": " + item.Value);
            }

            sb.AppendLine();
            sb.AppendLine("Curve Type Counts:");
            foreach (string type in orderedTypes)
            {
                int value = typeCounts.ContainsKey(type) ? typeCounts[type] : 0;
                sb.AppendLine(type + ": " + value);
            }

            IEnumerable<KeyValuePair<string, int>> otherTypes = typeCounts
                .Where(x => !orderedTypes.Contains(x.Key))
                .OrderBy(x => x.Key);
            foreach (KeyValuePair<string, int> item in otherTypes)
            {
                sb.AppendLine(item.Key + ": " + item.Value);
            }

            sb.AppendLine();
            sb.AppendLine("Raw Layer Samples:");
            foreach (string sample in rawLayerSamples)
            {
                sb.AppendLine("- " + sample);
            }

            return sb.ToString();
        }
    }
}
