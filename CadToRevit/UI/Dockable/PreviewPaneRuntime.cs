using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CadToRevit.UI.Dockable
{
    public static class PreviewPaneRuntime
    {
        private static readonly DockablePaneId _paneId = new DockablePaneId(new Guid("DA9F5C96-D29F-4CF2-8A7A-8A237F806B1D"));
        private static PreviewPaneExternalEventHandler _handler;
        private static ExternalEvent _externalEvent;

        public static UIApplication UiApplication { get; private set; }

        public static DockablePaneId PaneId
        {
            get { return _paneId; }
        }

        public static PreviewPaneViewModel ViewModel { get; } = new PreviewPaneViewModel();

        public static void InitializeExternalEvent()
        {
            if (_externalEvent != null)
            {
                return;
            }

            _handler = new PreviewPaneExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static Task<PreviewPaneResponse> RequestAsync(
            PreviewPaneRequestType type,
            System.Collections.Generic.IList<PreviewPaneLayerItem> layerMappings = null,
            RoomRecognitionSettings roomRecognitionSettings = null,
            GlobalGenerationSettings globalGenerationSettings = null)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = type,
                Completion = tcs,
                LayerMappings = layerMappings,
                RoomRecognitionSettings = roomRecognitionSettings,
                GlobalGenerationSettings = globalGenerationSettings
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<PreviewPaneResponse> RequestGenerateSingleLayerAsync(
            PreviewPaneLayerItem layerItem)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.GenerateSingleLayer,
                Completion = tcs,
                SelectedLayerItem = layerItem,
                SelectedRawLayerName = layerItem != null ? layerItem.RawLayerName : null,
                SelectedCategory = layerItem != null ? layerItem.Category : null
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<PreviewPaneResponse> RequestDeleteSingleLayerAsync(
            string rawLayerName,
            MapCategory? category)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.DeleteSingleLayer,
                Completion = tcs,
                SelectedRawLayerName = rawLayerName,
                SelectedCategory = category
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<PreviewPaneResponse> RequestDeleteSelectedLayersAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.DeleteSelectedLayers,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<PreviewPaneResponse> RequestHighlightSelectedLayerAsync(
            string rawLayerName,
            MapCategory? category)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.HighlightGeneratedElementsForSelectedLayer,
                Completion = tcs,
                SelectedRawLayerName = rawLayerName,
                SelectedCategory = category
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<PreviewPaneResponse> RequestToggleLayerGeneratedElementsVisibilityAsync(
            string rawLayerName,
            MapCategory? category)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.ToggleGeneratedElementsVisibilityForLayer,
                Completion = tcs,
                SelectedRawLayerName = rawLayerName,
                SelectedCategory = category
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<PreviewPaneResponse> RequestClearGeneratedElementHighlightAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult<PreviewPaneResponse>(null);
            }

            TaskCompletionSource<PreviewPaneResponse> tcs = new TaskCompletionSource<PreviewPaneResponse>();
            _handler.Enqueue(new PreviewPaneRequest
            {
                Type = PreviewPaneRequestType.ClearGeneratedElementHighlight,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static void TryHidePane(UIApplication uiApp)
        {
            try
            {
                DockablePane pane = uiApp == null ? null : uiApp.GetDockablePane(PaneId);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Hide pane failed: " + ex.Message);
            }
        }

        public static void SetUiApplication(UIApplication uiApp)
        {
            UiApplication = uiApp;
        }

        public static void UpdateRevitSelectionCount(UIApplication uiApp)
        {
            try
            {
                UiApplication = uiApp ?? UiApplication;
                UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
                int detachableCount = GeneratedElementBindingRestoreService.CountDetachableSelectedElements(uiDoc);
                int restorableCount = GeneratedElementBindingRestoreService.CountRestorableSelectedBindings(uiDoc);
                int undoCount = DetachUndoStackService.GetLatestRestorableCount(uiDoc != null ? uiDoc.Document : null);
                ViewModel.UpdateRevitSelectionCounts(detachableCount, restorableCount, undoCount);
            }
            catch
            {
                ViewModel.UpdateRevitSelectionCount(0);
            }
        }

        public static async Task RaiseTestExternalEventAsync()
        {
            await RequestAsync(PreviewPaneRequestType.TestExternalEvent);
        }

        public static void ApplyState(PreviewPaneState state)
        {
            ViewModel.DocumentTitle = state != null ? state.DocumentTitle : "(No Document)";
            ViewModel.LevelName = state != null ? state.LevelName : "-";
            ViewModel.IsCadVisible = state == null || state.IsCadVisible;
            ViewModel.IsBuildingElementsVisible = state == null || state.IsBuildingElementsVisible;
            RoomRecognitionSettings settings = state != null ? state.RoomRecognitionSettings : null;
            settings = RoomRecognitionSettings.Clone(settings);
            GlobalGenerationSettings global = state != null ? state.GlobalGenerationSettings : null;
            global = GlobalGenerationSettings.Clone(global);
            ViewModel.RoomTextLayerNames = settings.RoomTextLayerNames;
            ViewModel.RoomDoorGapMaxMm = settings.DoorGapMaxMm;
            ViewModel.RoomSmallGapPatchMaxMm = settings.SmallGapPatchMaxMm;
            ViewModel.RoomTargetKeywordsText = settings.TargetKeywordsText;
            ViewModel.RoomRecognitionWindowSizeM = settings.ModelRecognitionWindowSizeM;
            ViewModel.SafeModeEnabled = global.SafeModeEnabled;
            ViewModel.AutoJoinWallsAfterCreate = global.AutoJoinWallsAfterCreate;
            ViewModel.HeadRoomMm = global.HeadRoomMm;
            ViewModel.UseGlobalWallHeightOverride = global.UseGlobalWallHeightOverride;
            ViewModel.GlobalWallHeightMm = global.GlobalWallHeightMm;
            ViewModel.UseGlobalDoorHeightOverride = global.UseGlobalDoorHeightOverride;
            ViewModel.GlobalDoorHeightMm = global.GlobalDoorHeightMm;
            ViewModel.UseGlobalDoorSillHeightOverride = global.UseGlobalDoorSillHeightOverride;
            ViewModel.GlobalDoorSillHeightMm = global.GlobalDoorSillHeightMm;
        }
    }
}
