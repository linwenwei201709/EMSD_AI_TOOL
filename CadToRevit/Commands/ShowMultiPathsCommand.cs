using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Path;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathPreview;
using System;
using System.Collections.Generic;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowMultiPathsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            UIDocument uiDoc = uiApp?.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                View3D previewView;
                using (Transaction tx = new Transaction(doc, "Show Multiple Demo Paths"))
                {
                    tx.Start();

                    previewView = PathPreviewViewService.GetOrCreate(doc);
                    PathPreviewViewService.PrepareForSourceDocPreview(previewView);
                    Path3DVisualizationService.Clear(doc);

                    IList<PathPolyline> paths = MultiPathDemoDataService.BuildDemoPaths();
                    Path3DVisualizationService.DrawMany(doc, previewView, paths, false);

                    tx.Commit();
                }

                if (previewView != null)
                {
                    uiDoc.RequestViewChange(previewView);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                DiagnosticRecorder.AppendDebug("[PathPreview] ShowMultiPaths failed=" + ex);
                return Result.Failed;
            }
        }
    }
}
