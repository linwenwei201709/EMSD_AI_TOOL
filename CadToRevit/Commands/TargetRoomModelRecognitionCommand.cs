using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Workflow;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class TargetRoomModelRecognitionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc == null ? null : uiDoc.Document;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            RoomRecognitionPaneRuntime.OpenRoomManagement(doc, uiDoc);

            ProjectWorkflowMode mode = ProjectWorkflowModeStoreService.GetMode(doc);
            if (mode == ProjectWorkflowMode.RvtModelImportMode)
            {
                // RVT import mode must use the model-based room/lift recognition flow.
                RoomRecognitionPaneRuntime.ExecuteAutoDetectRooms(uiApp);
            }
            else
            {
                // Preserve the existing DWG workflow.
                RoomRecognitionPaneRuntime.ExecuteInitialAutoDetectRoomsAndLifts(uiApp);
            }

            RoomRecognitionPaneRuntime.TryHidePreviewPane(uiApp);
            RoomRecognitionPaneRuntime.ShowRoomAndLiftPane(uiApp);
            return Result.Succeeded;
        }
    }
}
