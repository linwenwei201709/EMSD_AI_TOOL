using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.ExportDrawing;
using CadToRevit.UI.Dialogs;
using System;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ExportDrawingCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication app = commandData != null ? commandData.Application : null;
                UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
                Document doc = uiDoc != null ? uiDoc.Document : null;
                if (doc == null)
                {
                    message = "No active Revit document.";
                    TaskDialog.Show("Export Drawing", message);
                    return Result.Cancelled;
                }

                string tempDirectory = ExportDrawingPdfService.GetTempDirectory();
                ExportDrawingImageResult imageResult = ExportDrawingImageService.ExportFiveViews(doc, tempDirectory);
                ExportDrawingPdfResult pdfResult = ExportDrawingPdfService.ExportTemporary(
                    doc.Title,
                    imageResult.Views);

                DiagnosticRecorder.AppendDebug("[ExportDrawing] PDF=" + (pdfResult.PdfPath ?? string.Empty));

                ExportDrawingPdfPreviewWindow window = new ExportDrawingPdfPreviewWindow(pdfResult);
                window.SetRevitOwner();
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ExportDrawing] Failed: " + ex);
                message = ex.Message;
                TaskDialog.Show("Export Drawing", ex.Message);
                return Result.Failed;
            }
        }
    }
}
