using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateWindowsCommand : IExternalCommand
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
                UiMessageService.Warning("Command.CreateWindows.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            WindowCreateSettings settings = new WindowCreateSettings();
            VerticalDimensionSettings vertical = VerticalDimensionStoreService.Load(doc);
            List<WindowCandidate> candidates = WindowCandidateBuilder.Build(doc, importInstance, settings);
            WindowCreateResult createResult = WindowCreatorService.Create(doc, candidates, settings, vertical, null, null, true);
            WindowLoggerService.Write(createResult, candidates);

            UiMessageService.ShowTaskDialogText("Command.CreateWindows.Title", BuildSummary(createResult));
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
                .FirstElement() as ImportInstance;
        }

        private static string BuildSummary(WindowCreateResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TotalCandidates: " + result.TotalCandidates);
            sb.AppendLine("Created: " + result.CreatedCount);
            sb.AppendLine("Skipped: " + result.SkippedCount);
            sb.AppendLine("WidthSetSuccess: " + result.WidthSetSuccessCount);
            sb.AppendLine("WidthSetFailed: " + result.WidthSetFailedCount);
            sb.AppendLine("WindowSymbol: " + (result.WindowSymbolName ?? "(none)"));

            if (result.SkipByReason.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skip Reasons:");
                foreach (KeyValuePair<string, int> item in result.SkipByReason)
                {
                    sb.AppendLine("- " + item.Key + ": " + item.Value);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Logs:");
            sb.AppendLine("- JSON: " + result.JsonLogPath);
            sb.AppendLine("- CSV: " + result.CsvLogPath);
            return sb.ToString();
        }
    }
}
