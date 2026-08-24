using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.UI;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowGlobalSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp != null && uiApp.ActiveUIDocument != null ? uiApp.ActiveUIDocument.Document : null;
            if (doc == null)
            {
                message = Loc.T(LocalizedKeys.GlobalSettings.NoActiveDocument);
                return Result.Failed;
            }

            GlobalSettingsWindow window = new GlobalSettingsWindow(doc);
            bool? saved = window.ShowDialog();
            if (saved == true)
            {
                _ = PreviewPaneRuntime.ViewModel.RefreshPaneStateAsync();
            }

            return Result.Succeeded;
        }
    }
}
