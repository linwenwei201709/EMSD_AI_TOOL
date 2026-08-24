using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models.Rooms;
using CadToRevit.Services.Rooms;
using CadToRevit.UI.Dockable;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PickRoomAtPointCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || uiDoc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                // Pick a single seed point and run conservative 2D room probing on the resolved level.
                XYZ pickPoint = uiDoc.Selection.PickPoint(Loc.T(LocalizedKeys.RoomProbe.PickPointPrompt));
                RoomPointProbeResult result = RoomPointProbeService.Probe(doc, pickPoint, doc.ActiveView);
                RoomRecognitionPaneRuntime.ApplyProbeResult(doc, uiDoc, result);

                if (!result.Success)
                {
                    TaskDialog.Show(
                        Loc.T(LocalizedKeys.RoomProbe.DialogTitle),
                        Loc.T(LocalizedKeys.RoomProbe.NoRoomFound, result.Message ?? string.Empty));
                    return Result.Succeeded;
                }

                RoomRecognitionPaneRuntime.TryHidePreviewPane(uiApp);
                RoomRecognitionPaneRuntime.ShowRoomAndLiftPane(uiApp);
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
        }
    }
}
