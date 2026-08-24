using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.UI.PathObstacles;
using System;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PathObstacleManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData != null ? commandData.Application : null;
                if (uiApp == null || uiApp.ActiveUIDocument == null)
                {
                    return Result.Cancelled;
                }

                PathObstacleRuntime.ShowPane(uiApp);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
