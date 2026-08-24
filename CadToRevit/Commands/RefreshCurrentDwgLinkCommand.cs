using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Common;
using CadToRevit.Services.Dwg;
using CadToRevit.Services.PathPreview;
using CadToRevit.UI;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RefreshCurrentDwgLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc != null ? uiDoc.Document : null;

            DwgRefreshResult refreshResult = DwgRefreshService.RefreshCurrentLink(doc);
            DwgRefreshStatusWindow.ShowMessage(refreshResult.Message);

            if (refreshResult.Success && refreshResult.ReloadExecuted)
            {
                RoutePlannerSessionCacheService.MarkDirty(doc, "DWG refresh reloaded the linked file.");
                ViewDisplayHelper.EnsureFineDetailLevel(doc);
                uiDoc?.RefreshActiveView();
                _ = PreviewPaneRuntime.ViewModel.RefreshAndAnalyzeAsync();
            }

            return refreshResult.Success ? Result.Succeeded : Result.Cancelled;
        }
    }
}
