using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Path;
using CadToRevit.Services.Diagnostics;
using CadToRevit.UI.Dockable;
using System;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewOrchestrator
    {
        internal static void Run(UIApplication uiApp, Document sourceDoc)
        {
            if (uiApp == null || sourceDoc == null)
            {
                throw new InvalidOperationException("UIApplication or source document is null.");
            }

            PreviewPaneRuntime.TryHidePane(uiApp);

            using (PreviewGenerationProgressWindow progressWindow = new PreviewGenerationProgressWindow())
            using (PathPreviewExecutionScope scope = new PathPreviewExecutionScope(uiApp))
            {
                progressWindow.Show();
                DiagnosticRecorder.AppendDebug("[PathPreview] Start");
                UpdateProgress(progressWindow, "Path Preview", 0, 10, "Initializing preview session");

                PathPreviewTempFileService.PathPreviewSession session = PathPreviewTempFileService.CreateSession();
                DiagnosticRecorder.AppendDebug("[PathPreview] TempFolder=" + session.SessionFolder);

                scope.UpdateStage("ExportIfc");
                DiagnosticRecorder.AppendDebug("[PathPreview] ExportIfc.Start");
                UpdateProgress(progressWindow, "Export IFC", 1, 10, "Exporting temporary IFC from source RVT");
                PathPreviewIfcExportService.ExportResult exportResult = PathPreviewIfcExportService.ExportToTempIfc(sourceDoc, session.TempIfcPath);
                if (!exportResult.Success)
                {
                    throw new InvalidOperationException(exportResult.Error ?? "Temporary IFC export failed.");
                }

                DiagnosticRecorder.AppendDebug("[PathPreview] ExportIfc.Success path=" + exportResult.ExportPath);
                Document ifcDoc = null;
                try
                {
                    scope.UpdateStage("OpenIFCDocument");
                    DiagnosticRecorder.AppendDebug("[PathPreview] OpenIfc.Start path=" + session.TempIfcPath);
                    UpdateProgress(progressWindow, "Open IFC", 2, 10, "Opening temporary IFC document");
                    ifcDoc = PathPreviewIfcOpenService.Open(uiApp.Application, session.TempIfcPath);
                    DiagnosticRecorder.AppendDebug("[PathPreview] OpenIfc.Success title=" + (ifcDoc.Title ?? string.Empty));

                    scope.UpdateStage("NormalizeLinkedRvt");
                    DiagnosticRecorder.AppendDebug("[PathPreview] NormalizeLinkedRvt.Start");
                    UpdateProgress(progressWindow, "Normalize Linked RVT", 3, 10, "Converting linked preview model to gray massing");
                    PathPreviewLinkedRvtNormalizationService.NormalizeLinkedRvtDocument(ifcDoc);

                    scope.UpdateStage("SaveAsLinkedRvt");
                    DiagnosticRecorder.AppendDebug("[PathPreview] SaveLinkedRvt.Start path=" + session.LinkedModelRvtPath);
                    UpdateProgress(progressWindow, "Save Linked RVT", 4, 10, "Saving normalized linked preview model");
                    PathPreviewIfcOpenService.SaveAsLinkedRvt(ifcDoc, session.LinkedModelRvtPath);
                    DiagnosticRecorder.AppendDebug("[PathPreview] SaveLinkedRvt.Success path=" + session.LinkedModelRvtPath);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PathPreview] OpenIfcNormalizeOrSaveLinkedRvt.Failed error=" + ex);
                    throw;
                }
                finally
                {
                    if (ifcDoc != null)
                    {
                        PathPreviewIfcOpenService.Close(ifcDoc);
                    }
                }

                scope.UpdateStage("OpenCreatePreviewHost");
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Create.Start path=" + session.PreviewProjectPath);
                UpdateProgress(progressWindow, "Open Preview Host", 5, 10, "Creating and opening preview host document");
                UIDocument previewUiDoc = PathPreviewPreviewDocumentService.CreateOpenAndActivate(uiApp, session.PreviewProjectPath);
                if (previewUiDoc == null || previewUiDoc.Document == null)
                {
                    throw new InvalidOperationException("Preview project document could not be opened.");
                }

                Document previewDoc = previewUiDoc.Document;
                DiagnosticRecorder.AppendDebug("[PathPreview] PreviewProject.Opened title=" + (previewDoc.Title ?? string.Empty));

                View3D previewView = null;
                Path3DVisualizationService.DrawResult drawResult = null;
                PathPreviewModelAnchorService.ModelAnchorInfo anchor = null;

                using (Transaction tx = new Transaction(previewDoc, "Build Path Preview"))
                {
                    tx.Start();

                    scope.UpdateStage("LinkRvt");
                    DiagnosticRecorder.AppendDebug("[PathPreview] LinkRvt.Start path=" + session.LinkedModelRvtPath);
                    UpdateProgress(progressWindow, "Link RVT", 6, 10, "Linking normalized preview model into host");
                    RevitLinkInstance linkInstance = PathPreviewLinkedModelService.LinkRvt(previewDoc, session.LinkedModelRvtPath);
                    DiagnosticRecorder.AppendDebug("[PathPreview] LinkRvt.Success instanceId=" + linkInstance.Id.IntegerValue);

                    scope.UpdateStage("CreateGetPreviewView");
                    DiagnosticRecorder.AppendDebug("[PathPreview] View.CreateOrGet.Start");
                    UpdateProgress(progressWindow, "Prepare 3D View", 7, 10, "Creating or reusing AI_PATH_PREVIEW_3D");
                    previewView = PathPreviewViewService.GetOrCreate(previewDoc);
                    DiagnosticRecorder.AppendDebug("[PathPreview] View.CreateOrGet.Success name=" + (previewView == null ? string.Empty : previewView.Name));

                    scope.UpdateStage("ResolveAnchor");
                    UpdateProgress(progressWindow, "Resolve Anchor", 8, 10, "Calculating path anchor from linked model");
                    anchor = PathPreviewModelAnchorService.Resolve(previewView, linkInstance);
                    DiagnosticRecorder.AppendDebug("[PathPreview] Anchor.ModelMin=" + PathPreviewModelAnchorService.FormatPoint(anchor.ModelMin));
                    DiagnosticRecorder.AppendDebug("[PathPreview] Anchor.ModelMax=" + PathPreviewModelAnchorService.FormatPoint(anchor.ModelMax));
                    DiagnosticRecorder.AppendDebug("[PathPreview] Anchor.BasePoint=" + PathPreviewModelAnchorService.FormatPoint(anchor.SuggestedPathBasePoint));

                    PathPolyline path = DemoPathDataService.BuildDemoPath(anchor);

                    scope.UpdateStage("DrawPath");
                    DiagnosticRecorder.AppendDebug("[PathPreview] Path.Draw.Start pathId=" + (path.PathId ?? string.Empty));
                    UpdateProgress(progressWindow, "Draw Path", 9, 10, "Drawing preview path geometry");
                    Path3DVisualizationService.Clear(previewDoc);
                    drawResult = Path3DVisualizationService.Draw(previewDoc, previewView, path);

                    scope.UpdateStage("FitActivateView");
                    UpdateProgress(progressWindow, "Fit And Activate View", 10, 10, "Fitting preview view and activating it");
                    PathPreviewViewService.FitToModelAndPath(previewView, linkInstance);
                    DiagnosticRecorder.AppendDebug("[PathPreview] View.Fit.Success");

                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug(
                    "[PathPreview] Path.Draw.Success segmentCount=" + (drawResult == null ? 0 : drawResult.SegmentCount) +
                    " arrowCount=" + (drawResult == null ? 0 : drawResult.ArrowCount));
                DiagnosticRecorder.AppendDebug("[PathPreview] View.Ready name=" + (previewView == null ? string.Empty : previewView.Name));

                scope.UpdateStage("FitActivateView");
                previewUiDoc.RequestViewChange(previewView);
                PathPreviewPreviewDocumentService.CleanupOpenedTabs(previewUiDoc);
                UpdateProgress(progressWindow, "Completed", 10, 10, "Preview is ready");
                DiagnosticRecorder.AppendDebug("[PathPreview] Completed");
            }
        }

        private static void UpdateProgress(PreviewGenerationProgressWindow progressWindow, string stage, int current, int total, string detail)
        {
            if (progressWindow == null)
            {
                return;
            }

            progressWindow.UpdateProgress(stage, current, total, detail);
        }
    }
}
