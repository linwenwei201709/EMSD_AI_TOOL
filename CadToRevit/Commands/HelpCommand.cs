using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.UI.Dialogs;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class HelpCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            HelpCenterWindow.ShowOrActivate(commandData.Application);
            return Result.Succeeded;
        }
    }
}
