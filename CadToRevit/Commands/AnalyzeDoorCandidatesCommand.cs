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
    public class AnalyzeDoorCandidatesCommand : IExternalCommand
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
                UiMessageService.Warning("Command.AnalyzeDoorCandidates.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            DoorDetectResult detect = DoorCandidateDetector.Detect(doc, importInstance, new DoorDetectSettings());
            DoorCandidateLogWriter.Write(detect);

            UiMessageService.ShowTaskDialogText("Command.AnalyzeDoorCandidates.Title", BuildSummary(detect));
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

        private static string BuildSummary(DoorDetectResult detect)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Door Segments Total: " + detect.DoorSegmentsTotal);
            sb.AppendLine("Candidates (R1): " + detect.Rule1Count);
            sb.AppendLine("Candidates (R2): " + detect.Rule2Count);
            sb.AppendLine("Merged Candidates: " + detect.MergedCandidateCount);
            sb.AppendLine("Matched to Wall: " + detect.MatchedCount);
            sb.AppendLine("Unmatched: " + detect.UnmatchedCount);
            sb.AppendLine();
            sb.AppendLine("Width Stats (mm):");
            sb.AppendLine("- [650-800]: " + detect.WidthRange650To800);
            sb.AppendLine("- [800-1000]: " + detect.WidthRange800To1000);
            sb.AppendLine("- [1000-1200]: " + detect.WidthRange1000To1200);
            sb.AppendLine();
            sb.AppendLine("Logs:");
            sb.AppendLine("- JSON: " + detect.JsonLogPath);
            sb.AppendLine("- CSV: " + detect.CsvLogPath);
            return sb.ToString();
        }
    }
}
