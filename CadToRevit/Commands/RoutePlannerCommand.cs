using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.UI.Part3;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RoutePlannerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            if (uiApp == null)
            {
                return Result.Failed;
            }

            Part3MessageWindow.ShowMessage(uiApp, "Route Planner will be available after RVT model import.");
            return Result.Succeeded;
        }
    }
}
