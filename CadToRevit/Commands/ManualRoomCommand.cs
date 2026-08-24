using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ManualRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            return ManualRoomCommandRunner.Run(uiApp, out message);
        }
    }
}
