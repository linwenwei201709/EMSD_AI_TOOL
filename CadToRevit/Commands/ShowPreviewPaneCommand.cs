using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowPreviewPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            RoomRecognitionPaneRuntime.HidePanes(uiApp);
            DockablePane pane = uiApp.GetDockablePane(PreviewPaneRuntime.PaneId);
            pane.Show();
            _ = PreviewPaneRuntime.ViewModel.RefreshPaneStateAsync();
            return Result.Succeeded;
        }
    }
}
