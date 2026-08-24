using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Common;
using CadToRevit.Services.Part3;
using CadToRevit.Services.Rooms;
using CadToRevit.UI.Common;
using CadToRevit.UI.Dockable;
using CadToRevit.UI.Part3;
using System.Collections.Generic;

namespace CadToRevit.Commands
{
    internal static class AnalyzeRoomsCommandRunner
    {
        public static TargetRoomModelRecognitionService.RecognitionSummary RunAnalyzeRoomsForActiveModel(
            UIApplication uiApp,
            string emptyResultMessage,
            bool allowModelLevelFallback = false,
            IEnumerable<ElementId> contextElementIds = null,
            bool preserveSolutionEditor = false)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return null;
            }

            BusyProgressWindow progress = null;
            try
            {
                AnalyzeRoomsLevelResolveResult levelResult = AnalyzeRoomsLevelResolver.Resolve(
                    uiDoc,
                    doc.ActiveView,
                    contextElementIds,
                    allowModelLevelFallback);
                if (levelResult == null || !levelResult.Success)
                {
                    TargetRoomModelRecognitionService.RecognitionSummary failed = new TargetRoomModelRecognitionService.RecognitionSummary
                    {
                        Message = levelResult != null ? levelResult.Message : "Analyze Rooms failed: no analysis level was found."
                    };
                    Part3MessageWindow.ShowMessage(uiApp, failed.Message);
                    return failed;
                }

                progress = BusyProgressWindow.Show(uiApp, "Analyze Rooms", "Analyzing room candidates, please wait...");
                TargetRoomModelRecognitionService.RecognitionSummary summary = RoomAutoAnalyzeService.Run(doc, doc.ActiveView, levelResult);
                Dictionary<string, List<ElementId>> roomRangeElementIds = new Dictionary<string, List<ElementId>>();
                if (doc.ActiveView is View3D)
                {
                    Room3DVisualizationService.RefreshAndFilterResults(doc, summary);
                }
                else
                {
                    roomRangeElementIds = RoomRangeVisualizationService.DrawMatchedRoomRanges(
                        doc,
                        summary != null ? summary.RunResult : null);
                    RoomRangeVisualizationService.FilterSummaryByCreatedRanges(summary, roomRangeElementIds);
                }

                if (summary != null)
                {
                    summary.Lifts = LiftRoomDetectionService.Detect(doc, summary);
                }

                RoomRecognitionPaneRuntime.ApplyRecognitionResult(
                    doc,
                    uiDoc,
                    summary,
                    roomRangeElementIds,
                    preserveSolutionEditor);
                ViewDisplayHelper.EnsureFineDetailLevel(doc);
                RoomRecognitionPaneRuntime.TryHidePreviewPane(uiApp);
                RoomRecognitionPaneRuntime.ShowRoomAndLiftPane(uiApp);

                bool stoppedByLimit = summary != null &&
                                      !string.IsNullOrWhiteSpace(summary.Message) &&
                                      summary.Message.IndexOf("model is too complex", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (summary == null || summary.Matched == 0 || stoppedByLimit)
                {
                    Part3MessageWindow.ShowMessage(uiApp, stoppedByLimit && summary != null ? summary.Message : ResolveEmptyMessage(summary, emptyResultMessage));
                }

                return summary;
            }
            finally
            {
                if (progress != null)
                {
                    progress.Dispose();
                }
            }
        }

        private static string ResolveEmptyMessage(TargetRoomModelRecognitionService.RecognitionSummary summary, string fallback)
        {
            if (summary != null && !string.IsNullOrWhiteSpace(summary.Message) &&
                summary.Message.IndexOf("Analyze Rooms done", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return summary.Message;
            }

            return fallback;
        }
    }
}
