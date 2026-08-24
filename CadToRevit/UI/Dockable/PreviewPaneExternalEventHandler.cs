using Autodesk.Revit.UI;
using CadToRevit.Commands;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using CadToRevit.Services.Common;
using System;
using System.Collections.Concurrent;

namespace CadToRevit.UI.Dockable
{
    public sealed class PreviewPaneExternalEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<PreviewPaneRequest> _queue = new ConcurrentQueue<PreviewPaneRequest>();
        private readonly PreviewPaneDataService _service = new PreviewPaneDataService();

        public void Enqueue(PreviewPaneRequest request)
        {
            if (request == null)
            {
                return;
            }

            _queue.Enqueue(request);
        }

        public void Execute(UIApplication app)
        {
            PreviewPaneRuntime.SetUiApplication(app);
            PreviewPaneRequest request;
            while (_queue.TryDequeue(out request))
            {
                try
                {
                    PreviewPaneResponse response = new PreviewPaneResponse();
                    switch (request.Type)
                    {
                        case PreviewPaneRequestType.RefreshState:
                            response.State = _service.BuildState(app);
                            break;
                        case PreviewPaneRequestType.LoadLayerMappings:
                            response.LayerMappings = _service.LoadLayerMappings(app);
                            break;
                        case PreviewPaneRequestType.CaptureAnalyzeSnapshot:
                            response.Snapshot = _service.CaptureAnalyzeSnapshot(app);
                            break;
                        case PreviewPaneRequestType.SaveLayerMappings:
                            response = _service.SaveLayerMappings(app, request.LayerMappings, request.RoomRecognitionSettings, request.GlobalGenerationSettings);
                            break;
                        case PreviewPaneRequestType.TestExternalEvent:
                            response.State = _service.BuildState(app);
                            TaskDialog.Show("DockablePane Preview", "ExternalEvent OK\nDocument: " + (response.State != null ? response.State.DocumentTitle : "(No Document)"));
                            break;
                        case PreviewPaneRequestType.Preview:
                            response = _service.ExecutePreview(app);
                            break;
                        case PreviewPaneRequestType.ClearPreview:
                            response = _service.ExecuteClearPreview(app);
                            break;
                        case PreviewPaneRequestType.CreateWalls:
                            response = _service.ExecuteCreateWalls(app);
                            break;
                        case PreviewPaneRequestType.CreateDoors:
                            response = _service.ExecuteCreateDoors(app);
                            break;
                        case PreviewPaneRequestType.CreateFloors:
                            response = _service.ExecuteCreateFloors(app);
                            break;
                        case PreviewPaneRequestType.CreateGroundFloor:
                            Result groundResult = CreateGroundFloorCommand.ExecuteForUiApplication(app);
                            response.Message = groundResult == Result.Succeeded
                                ? "Ground floor operation completed."
                                : groundResult == Result.Cancelled
                                    ? "Ground floor operation cancelled."
                                    : "Ground floor operation failed.";
                            break;
                        case PreviewPaneRequestType.CreateElements:
                            response = _service.ExecuteCreateElements(app);
                            break;
                        case PreviewPaneRequestType.RegenerateAll:
                            response = _service.ExecuteRegenerateAll(app);
                            break;
                        case PreviewPaneRequestType.GenerateSingleLayer:
                            response = _service.ExecuteGenerateSingleLayer(
                                app,
                                request.SelectedLayerItem);
                            break;
                        case PreviewPaneRequestType.DeleteSingleLayer:
                            response = _service.ExecuteDeleteSingleLayer(
                                app,
                                request.SelectedRawLayerName,
                                request.SelectedCategory);
                            break;
                        case PreviewPaneRequestType.DeleteSelectedLayers:
                            response = _service.ExecuteDeleteSelectedLayers(app);
                            break;
                        case PreviewPaneRequestType.HighlightGeneratedElementsForSelectedLayer:
                            response = _service.ExecuteHighlightGeneratedElementsForSelectedLayer(
                                app,
                                request.SelectedRawLayerName,
                                request.SelectedCategory);
                            break;
                        case PreviewPaneRequestType.ClearGeneratedElementHighlight:
                            response = _service.ExecuteClearGeneratedElementHighlight(app);
                            break;
                        case PreviewPaneRequestType.ToggleCadVisibility:
                            response = _service.ExecuteToggleCadVisibility(app);
                            break;
                        case PreviewPaneRequestType.ToggleBuildingElementsVisibility:
                            response = _service.ExecuteToggleBuildingElementsVisibility(app);
                            break;
                        case PreviewPaneRequestType.ToggleGeneratedElementsVisibilityForLayer:
                            response = _service.ExecuteToggleGeneratedElementsVisibilityForLayer(
                                app,
                                request.SelectedRawLayerName,
                                request.SelectedCategory);
                            break;
                        case PreviewPaneRequestType.DetachSelectedElements:
                            response = _service.ExecuteDetachSelectedElements(app);
                            break;
                        case PreviewPaneRequestType.RestoreSelectedBindings:
                            response = _service.ExecuteRestoreSelectedBindings(app);
                            break;
                        case PreviewPaneRequestType.UndoLastDetachBatch:
                            response = _service.ExecuteUndoLastDetachBatch(app);
                            break;
                    }

                    if (IsFineDetailLevelRequest(request.Type))
                    {
                        ViewDisplayHelper.EnsureFineDetailLevel(app != null && app.ActiveUIDocument != null
                            ? app.ActiveUIDocument.Document
                            : null);
                    }

                    request.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    request.TrySetException(ex);
                }
            }
        }

        public string GetName()
        {
            return "CadToRevit PreviewPane ExternalEvent";
        }

        private static bool IsFineDetailLevelRequest(PreviewPaneRequestType type)
        {
            return type == PreviewPaneRequestType.CreateElements ||
                type == PreviewPaneRequestType.RegenerateAll ||
                type == PreviewPaneRequestType.GenerateSingleLayer;
        }
    }

    public sealed class PreviewPaneRequest
    {
        public PreviewPaneRequestType Type { get; set; }

        public System.Threading.Tasks.TaskCompletionSource<PreviewPaneResponse> Completion { get; set; }

        public System.Collections.Generic.IList<PreviewPaneLayerItem> LayerMappings { get; set; }

        public RoomRecognitionSettings RoomRecognitionSettings { get; set; }

        public GlobalGenerationSettings GlobalGenerationSettings { get; set; }

        public string SelectedRawLayerName { get; set; }

        public MapCategory? SelectedCategory { get; set; }

        public PreviewPaneLayerItem SelectedLayerItem { get; set; }

        public void TrySetResult(PreviewPaneResponse response)
        {
            if (Completion != null)
            {
                Completion.TrySetResult(response);
            }
        }

        public void TrySetException(Exception ex)
        {
            if (Completion != null)
            {
                Completion.TrySetException(ex);
            }
        }
    }
}
