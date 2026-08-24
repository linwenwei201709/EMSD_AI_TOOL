using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewPreviewDocumentService
    {
        internal static UIDocument CreateOpenAndActivate(UIApplication uiApp, string previewProjectPath)
        {
            if (uiApp == null || string.IsNullOrWhiteSpace(previewProjectPath))
            {
                throw new InvalidOperationException("Preview document arguments are invalid.");
            }

            PathPreviewTempFileService.EnsureDirectory(Path.GetDirectoryName(previewProjectPath));

            Application app = uiApp.Application;
            Document tempDoc = app.NewProjectDocument(UnitSystem.Metric);
            if (tempDoc == null)
            {
                throw new InvalidOperationException("Preview project document could not be created.");
            }

            try
            {
                using (Transaction tx = new Transaction(tempDoc, "Prepare Preview Host View"))
                {
                    tx.Start();
                    View3D previewView = PathPreviewViewService.GetOrCreate(tempDoc);
                    ConfigureStartingView(tempDoc, previewView);
                    tx.Commit();
                }

                if (File.Exists(previewProjectPath))
                {
                    File.Delete(previewProjectPath);
                }

                SaveAsOptions saveOptions = new SaveAsOptions
                {
                    OverwriteExistingFile = true
                };

                tempDoc.SaveAs(previewProjectPath, saveOptions);
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Create path=" + previewProjectPath);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Create.Failed error=" + ex);
                throw new InvalidOperationException("Preview host project creation failed: " + ex.Message, ex);
            }
            finally
            {
                // Close the seed document handle before opening the saved preview file in the UI.
                tempDoc.Close(false);
            }

            try
            {
                UIDocument previewUiDoc = uiApp.OpenAndActivateDocument(previewProjectPath);
                CleanupOpenedTabs(previewUiDoc);
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Open.Success path=" + previewProjectPath);
                return previewUiDoc;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Open.Failed error=" + ex);
                throw new InvalidOperationException("Preview host project could not be opened: " + ex.Message, ex);
            }
        }

        private static void ConfigureStartingView(Document doc, View3D previewView)
        {
            if (doc == null || previewView == null)
            {
                return;
            }

            StartingViewSettings settings = StartingViewSettings.GetStartingViewSettings(doc);
            if (settings == null || !settings.IsAcceptableStartingView(previewView.Id))
            {
                return;
            }

            settings.ViewId = previewView.Id;
            DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.StartingView set=" + previewView.Name);
        }

        internal static void CleanupOpenedTabs(UIDocument previewUiDoc)
        {
            if (previewUiDoc == null || previewUiDoc.Document == null)
            {
                return;
            }

            View previewView = new FilteredElementCollector(previewUiDoc.Document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, PathPreviewConstants.PreviewViewName, StringComparison.OrdinalIgnoreCase));
            if (previewView == null)
            {
                return;
            }

            try
            {
                previewUiDoc.RequestViewChange(previewView);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.RequestPreviewView failed=" + ex.Message);
            }

            var openViews = previewUiDoc.GetOpenUIViews();
            if (openViews == null || openViews.Count <= 1 || !openViews.Any(x => x != null && x.ViewId == previewView.Id))
            {
                return;
            }

            foreach (UIView uiView in openViews.Where(x => x != null && x.ViewId != previewView.Id).ToList())
            {
                try
                {
                    uiView.Close();
                    DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.CloseResidualView viewId=" + uiView.ViewId.IntegerValue);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.CloseResidualView failed=" + ex.Message);
                }
            }
        }
    }
}
