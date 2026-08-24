using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.IO;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewIfcOpenService
    {
        internal static Document Open(Application app, string ifcPath)
        {
            if (app == null)
            {
                throw new InvalidOperationException("OpenIFCDocument failed: application is null.");
            }

            if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
            {
                throw new InvalidOperationException("OpenIFCDocument failed: IFC file does not exist: " + (ifcPath ?? string.Empty));
            }

            try
            {
                Document ifcDoc = app.OpenIFCDocument(ifcPath);
                if (ifcDoc == null)
                {
                    throw new InvalidOperationException("OpenIFCDocument failed: returned document is null.");
                }

                DiagnosticRecorder.AppendDebug("[PathPreview] OpenIFCDocument.Success path=" + ifcPath);
                return ifcDoc;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] OpenIFCDocument.Failed error=" + ex);
                throw new InvalidOperationException("OpenIFCDocument failed: " + ex.Message, ex);
            }
        }

        internal static void SaveAsLinkedRvt(Document ifcDoc, string linkedModelRvtPath)
        {
            if (ifcDoc == null)
            {
                throw new InvalidOperationException("Save linked RVT failed: IFC document is null.");
            }

            if (string.IsNullOrWhiteSpace(linkedModelRvtPath))
            {
                throw new InvalidOperationException("Save linked RVT failed: target path is empty.");
            }

            try
            {
                PathPreviewTempFileService.EnsureDirectory(Path.GetDirectoryName(linkedModelRvtPath));
                SaveAsOptions saveOptions = new SaveAsOptions
                {
                    OverwriteExistingFile = true
                };

                ifcDoc.SaveAs(linkedModelRvtPath, saveOptions);
                DiagnosticRecorder.AppendDebug("[PathPreview] SaveLinkedRvt.Success path=" + linkedModelRvtPath);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] SaveLinkedRvt.Failed error=" + ex);
                throw new InvalidOperationException("Save linked RVT failed: " + ex.Message, ex);
            }
        }

        internal static void Close(Document ifcDoc)
        {
            if (ifcDoc == null)
            {
                return;
            }

            try
            {
                ifcDoc.Close(false);
                DiagnosticRecorder.AppendDebug("[PathPreview] OpenIFCDocument.Close.Success");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] OpenIFCDocument.Close.Failed error=" + ex);
                throw new InvalidOperationException("Close IFC document failed: " + ex.Message, ex);
            }
        }
    }
}
