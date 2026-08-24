using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using CadToRevit.UI.RouteApi;
using System;
using System.Diagnostics;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RouteApiConsoleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RouteApiConsoleWindow.ShowOrActivate(commandData.Application);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class OpenLogsFolderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string logFolder = DiagnosticRecorder.GetLogDirectory();
                Process.Start(new ProcessStartInfo
                {
                    FileName = logFolder,
                    UseShellExecute = true
                });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(
                    "EMSD AI Tool",
                    "Unable to open the log folder." + Environment.NewLine + Environment.NewLine + ex.Message);
                return Result.Failed;
            }
        }
    }

}
