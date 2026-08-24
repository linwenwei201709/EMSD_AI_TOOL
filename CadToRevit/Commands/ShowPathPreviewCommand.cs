using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathPreview;
using System;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowPathPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            Document sourceDoc = uiApp?.ActiveUIDocument?.Document;
            if (sourceDoc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                // Keep the command as a thin entry point and delegate the full workflow.
                PathPreviewOrchestrator.Run(uiApp, sourceDoc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                DiagnosticRecorder.AppendDebug("[PathPreview] Execute failed=" + ex);
                return Result.Failed;
            }
        }
    }
}
