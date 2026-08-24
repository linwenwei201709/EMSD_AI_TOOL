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
    public class CreateDoorsCommand : IExternalCommand
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
                UiMessageService.Warning("Command.CreateDoors.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            DoorDetectResult detect = DoorCandidateDetector.Detect(doc, importInstance, new DoorDetectSettings());
            DoorCreateResult create = DoorCreatorService.CreateDoors(doc, detect.Candidates);

            UiMessageService.ShowTaskDialogText("Command.CreateDoors.Title", BuildOutput(create));
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

        private static string BuildOutput(DoorCreateResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DoorCandidates: " + result.DoorCandidates);
            sb.AppendLine("CreatedDoors: " + result.CreatedDoors);
            sb.AppendLine("Skipped: " + result.SkippedDoors);
            sb.AppendLine("DoorSymbol: " + (result.DoorSymbolName ?? "(none)"));

            if (result.SkipReasons.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skip Samples:");
                foreach (string reason in result.SkipReasons)
                {
                    sb.AppendLine("- " + reason);
                }
            }

            return sb.ToString();
        }
    }
}
