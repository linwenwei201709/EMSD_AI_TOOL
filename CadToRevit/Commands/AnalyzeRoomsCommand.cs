using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Workflow;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyzeRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            bool allowModelFallback = ProjectWorkflowModeStoreService.GetMode(doc) == ProjectWorkflowMode.RvtModelImportMode;
            AnalyzeRoomsCommandRunner.RunAnalyzeRoomsForActiveModel(
                uiApp,
                "Analyze Rooms found no candidate rooms.",
                allowModelFallback);
            return Result.Succeeded;
        }
    }
}
