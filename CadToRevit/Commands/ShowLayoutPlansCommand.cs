using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowLayoutPlansCommand : IExternalCommand
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

            RoomRecognitionPaneRuntime.TryHidePreviewPane(uiApp);
            RoomRecognitionPaneRuntime.PrepareLayoutPlansOverview(doc);
            RoomRecognitionPaneRuntime.ShowLayoutPlansPane(uiApp);

            return Result.Succeeded;
        }
    }
}
