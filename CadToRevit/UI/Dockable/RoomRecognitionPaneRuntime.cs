using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CadToRevit.Commands;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models.Path;
using CadToRevit.Models.Rooms.DeliveryRoutes;
using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Part3;
using CadToRevit.Services.PathObstacles;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.DeliveryRoutes;
using CadToRevit.Services.Rooms.LayoutPlanReports;
using CadToRevit.Services.Rooms.LayoutPlans;
using CadToRevit.Services.Rooms.Lifts;
using CadToRevit.Services.Rooms.Manual;
using CadToRevit.Services.Workflow;
using CadToRevit.UI.Dialogs;
using CadToRevit.UI.PathObstacles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Globalization;

namespace CadToRevit.UI.Dockable
{
    public static class RoomRecognitionPaneRuntime
    {
        private static readonly DockablePaneId _leftPaneId = new DockablePaneId(new Guid("CF771D6D-E9F2-40C5-B8D1-D6E1F553A0AB"));
        private static readonly DockablePaneId _rightPaneId = new DockablePaneId(new Guid("889AB101-5B50-4E56-9850-2AE35270DA41"));
        private static readonly object SyncRoot = new object();

        private static RoomRecognitionExternalEventHandler _handler;
        private static ExternalEvent _externalEvent;
        private static RoomRecognitionPaneState _state = new RoomRecognitionPaneState();
        private static UIDocument _uiDoc;
        private static Document _doc;
        private static string _selectedRoomKey;
        private static string _selectedLiftKey;
        private static XYZ _lastDeliveryRouteRequestStartPoint;
        private static XYZ _lastDeliveryRouteRequestGoalPoint;
        private static readonly List<DeliveryRouteRecordDto> _deliveryRouteRecordsCache =
            new List<DeliveryRouteRecordDto>();
        private static ManualRoomSelectionSession _manualRoomSelectionSession;
        private static UIApplication _manualRoomSelectionApp;
        private static DeliveryRouteStartPointSelectionSession _deliveryRouteStartPointSelectionSession;
        private const string TempExportRouteMarker = "EMSD_TEMP_EXPORT_ROUTE";
        private const string DeliveryRouteStartPointMarkerNamePrefix = "EMSD_DELIVERY_ROUTE_START_POINT__";
        private const double DeliveryRouteStartPointStemRadiusMm = 45.0;
        private const double DeliveryRouteStartPointStemHeightMm = 650.0;
        private const double DeliveryRouteStartPointCapRadiusMm = 120.0;
        private const double DeliveryRouteStartPointCapThicknessMm = 65.0;

        private enum ManualBoundarySelectionMode
        {
            Room,
            Lift
        }

        private sealed class ManualRoomSelectionSession
        {
            public bool IsActive { get; set; }

            public ManualBoundarySelectionMode Mode { get; set; } = ManualBoundarySelectionMode.Room;

            public HashSet<ElementId> SelectedIds { get; } = new HashSet<ElementId>();

            public ManualRoomSelectionBarWindow BarWindow { get; set; }
        }

        private sealed class DeliveryRouteStartPointSelectionSession
        {
            public bool IsActive { get; set; }
            public bool IsPicking { get; set; }
            public bool ConfirmRequested { get; set; }
            public bool CancelRequested { get; set; }
            public bool HasNewPoint { get; set; }
            public UIApplication App { get; set; }
            public XYZ OriginalPoint { get; set; }
            public string OriginalName { get; set; } = string.Empty;
            public XYZ PickedPoint { get; set; }
            public DeliveryRouteStartPointSelectionBarWindow BarWindow { get; set; }
        }

        public static DockablePaneId LeftPaneId => _leftPaneId;

        public static DockablePaneId RightPaneId => _rightPaneId;

        public static RoomListPaneViewModel ListViewModel { get; } = new RoomListPaneViewModel();

        public static RoomDetailPaneViewModel DetailViewModel { get; } = new RoomDetailPaneViewModel();

        public static List<EditorRoomOptionViewModel> GetDeliveryRouteRoomOptionsSnapshot()
        {
            RoomRecognitionPaneState snapshot;
            lock (SyncRoot)
            {
                snapshot = _state ?? new RoomRecognitionPaneState();
            }

            HashSet<string> liftRoomKeys = BuildAnalyzeRoomsLiftRoomKeys(snapshot.Summary);
            return (snapshot.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                .Where(IsMatchedRoom)
                .Where(x => x == null || !liftRoomKeys.Contains(x.Key ?? string.Empty))
                .OrderByDescending(x => x.AreaM2)
                .Select(room =>
                {
                    string key = room.Key ?? string.Empty;
                    string roomName = ResolveRoomDisplayName(snapshot, key, room.RoomName);
                    string levelName = snapshot.LevelNameByRoomKey.TryGetValue(key, out string resolvedLevel)
                        ? resolvedLevel
                        : Loc.T("Common.NA");
                    string areaText = FormatArea(room.AreaM2);
                    string statusText = string.IsNullOrWhiteSpace(room.Status) ? "-" : room.Status;
                    ElementId levelId = ResolveRoomLevelId(snapshot, key);
                    RoomCardMetricDisplay metrics = BuildRoomCardMetricDisplay(room, areaText, levelId);

                    return new EditorRoomOptionViewModel
                    {
                        Key = key,
                        RoomName = roomName,
                        TargetType = string.IsNullOrWhiteSpace(room.TargetRoomType) ? "-" : room.TargetRoomType,
                        AreaText = areaText,
                        LevelText = levelName,
                        StatusText = statusText,
                        RoomLengthText = metrics.RoomLengthText,
                        RoomWidthText = metrics.RoomWidthText,
                        RoomHeightText = metrics.RoomHeightText,
                        DoorWidthText = metrics.DoorWidthText,
                        DoorHeightText = metrics.DoorHeightText,
                        AvailableUsableAreaText = metrics.AvailableUsableAreaText,
                        DisplayName = roomName,
                        WallOptions = BuildEditorWallOptions(room)
                    };
                })
                .ToList();
        }

        public static List<EditorLiftOptionViewModel> GetDeliveryRouteLiftOptionsSnapshot()
        {
            RoomRecognitionPaneState snapshot;
            lock (SyncRoot)
            {
                snapshot = _state ?? new RoomRecognitionPaneState();
            }

            return snapshot.LiftByKey.Values
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .OrderBy(x => x.LiftName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(lift => new EditorLiftOptionViewModel
                {
                    Key = lift.Key,
                    DisplayName = ResolveLiftDisplayName(lift),
                    LiftKind = lift.LiftKind ?? string.Empty
                })
                .ToList();
        }

        public static void InitializeExternalEvent()
        {
            if (_externalEvent != null)
            {
                return;
            }

            _handler = new RoomRecognitionExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void OpenRoomManagement(Document doc, UIDocument uiDoc)
        {
            RoomPointProbeService.ClearProbePreview(doc);
            Room3DVisualizationService.Clear(doc);
            Lift3DVisualizationService.Clear(doc);

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                _state = new RoomRecognitionPaneState
                {
                    Mode = RoomRecognitionPaneMode.Detect,
                    Summary = new TargetRoomModelRecognitionService.RecognitionSummary(),
                    RoomRangeElementIds = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase)
                };
                _selectedRoomKey = null;
                _selectedLiftKey = null;
            }

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
                ListViewModel.Rooms.Clear();
                ListViewModel.Lifts.Clear();
                ListViewModel.SetSelectedRoomSilently(null);
                ListViewModel.SetSelectedLiftSilently(null);
                DetailViewModel.SetEditorRoomOptions(new List<EditorRoomOptionViewModel>());
                DetailViewModel.SetEditorLiftOptionItems(new List<EditorLiftOptionViewModel>());
                DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();
            });
            SetDetailEmpty();
            RefreshLayoutPlansFromDocument(doc);
        }
        public static void ApplyRecognitionResult(
            Document doc,
            UIDocument uiDoc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<ElementId>> roomRangeElementIds)
        {
            ApplyRecognitionResult(
                doc,
                uiDoc,
                summary,
                roomRangeElementIds,
                preserveSolutionEditor: false);
        }

        internal static void ApplyRecognitionResult(
            Document doc,
            UIDocument uiDoc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<ElementId>> roomRangeElementIds,
            bool preserveSolutionEditor)
        {
            RoomPointProbeService.ClearProbePreview(doc);

            string previousSelectedRoomKey = null;
            string previousSelectedLiftKey = null;
            bool keepSolutionEditor =
                preserveSolutionEditor &&
                DetailViewModel.CurrentPageMode == RoomDetailPageMode.SolutionEditor;

            lock (SyncRoot)
            {
                if (keepSolutionEditor)
                {
                    previousSelectedRoomKey = _selectedRoomKey;
                    previousSelectedLiftKey = _selectedLiftKey;
                }

                _doc = doc;
                _uiDoc = uiDoc;
                _state = BuildState(doc, summary, roomRangeElementIds);

                _selectedRoomKey =
                    keepSolutionEditor &&
                    !string.IsNullOrWhiteSpace(previousSelectedRoomKey) &&
                    _state.RoomByKey.ContainsKey(previousSelectedRoomKey)
                        ? previousSelectedRoomKey
                        : null;

                _selectedLiftKey =
                    keepSolutionEditor &&
                    !string.IsNullOrWhiteSpace(previousSelectedLiftKey) &&
                    _state.LiftByKey.ContainsKey(previousSelectedLiftKey)
                        ? previousSelectedLiftKey
                        : null;

                _state.SelectedLiftKey = _selectedLiftKey;
            }

            RefreshRoomVisualizationIfNeeded(doc, summary);
            RefreshSelectionState(keepSolutionEditor);
            RefreshLayoutPlansFromDocument(doc);
        }

        private static void ApplyRoomRecognitionResultOnly(
            Document doc,
            UIDocument uiDoc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<ElementId>> roomRangeElementIds)
        {
            List<LiftRecognitionRecord> existingLifts;
            lock (SyncRoot)
            {
                existingLifts = (_state?.Summary?.Lifts ?? new List<LiftRecognitionRecord>())
                    .Where(x => x != null)
                    .ToList();
            }

            if (summary == null)
            {
                summary = new TargetRoomModelRecognitionService.RecognitionSummary();
            }

            summary.Lifts = existingLifts;

            // The auto-detect button refreshes the room dataset, but it should not
            // throw the user out of an active Equipment Planner session. The editor
            // keeps its current values while SetEditorRoomOptions refreshes the
            // target-room dropdown against the newly detected rooms.
            ApplyRecognitionResult(
                doc,
                uiDoc,
                summary,
                roomRangeElementIds,
                preserveSolutionEditor: true);
        }

        private static void ApplyLiftRecognitionResultOnly(
            Document doc,
            UIDocument uiDoc,
            List<LiftRecognitionRecord> lifts)
        {
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                if (_state == null || _state.Summary == null)
                {
                    _state = BuildState(
                        doc,
                        new TargetRoomModelRecognitionService.RecognitionSummary(),
                        new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase));
                }

                EnsureSummaryReady(_state.Summary);
                _state.Summary.Lifts = lifts ?? new List<LiftRecognitionRecord>();
                _state.LiftByKey.Clear();
                foreach (LiftRecognitionRecord lift in _state.Summary.Lifts)
                {
                    if (lift == null || string.IsNullOrWhiteSpace(lift.Key))
                    {
                        continue;
                    }

                    _state.LiftByKey[lift.Key] = lift;
                }

                ApplyNameOverrides(doc, _state);
                ApplyLiftDisplayOverrides(doc, _state);
                _selectedLiftKey = null;
                _state.SelectedLiftKey = null;
            }

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
                ListViewModel.Lifts.Clear();
                foreach (LiftRecognitionRecord lift in lifts ?? new List<LiftRecognitionRecord>())
                {
                    if (lift == null || string.IsNullOrWhiteSpace(lift.Key))
                    {
                        continue;
                    }

                    ListViewModel.Lifts.Add(BuildLiftListItem(lift, null));
                }

                DetailViewModel.SetEditorLiftOptionItems(BuildEditorLiftOptions(lifts));
                DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();
                ListViewModel.SetSelectedLiftSilently(null);
            });

            RefreshLayoutPlansFromDocument(doc);
        }

        public static void AddManualRoomAndRefresh(Document doc, UIDocument uiDoc, ManualRoomRecord manualRoom)
        {
            if (doc == null || manualRoom == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                if (_state == null || _state.Mode != RoomRecognitionPaneMode.Detect)
                {
                    _state = BuildState(
                        doc,
                        new TargetRoomModelRecognitionService.RecognitionSummary(),
                        new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase));
                }

                EnsureSummaryReady(_state.Summary);
                UpsertManualRoom(_state.Summary, manualRoom);
                IndexManualRoom(_state, manualRoom);
                AssignRoomDisplayNames(_state);
                _selectedRoomKey = manualRoom.Key;
                _selectedLiftKey = null;
                _state.SelectedLiftKey = null;
            }

            RefreshRoomVisualizationIfNeeded(doc, _state != null ? _state.Summary : null);
            RefreshSelectionState();
            RefreshLayoutPlansFromDocument(doc);
        }

        public static void AddManualLiftAndRefresh(Document doc, UIDocument uiDoc, LiftRecognitionRecord lift)
        {
            if (doc == null || lift == null || string.IsNullOrWhiteSpace(lift.Key))
            {
                return;
            }

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                if (_state == null || _state.Mode != RoomRecognitionPaneMode.Detect)
                {
                    _state = BuildState(
                        doc,
                        new TargetRoomModelRecognitionService.RecognitionSummary(),
                        new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase));
                }

                EnsureSummaryReady(_state.Summary);
                _state.Summary.Lifts.RemoveAll(x => x == null || string.Equals(x.Key, lift.Key, StringComparison.OrdinalIgnoreCase));
                _state.Summary.Lifts.Add(lift);
                _state.LiftByKey[lift.Key] = lift;
                ApplyNameOverrides(doc, _state);
                ApplyLiftDisplayOverrides(doc, _state);
                _selectedLiftKey = lift.Key;
                _state.SelectedLiftKey = lift.Key;
                _selectedRoomKey = null;
            }

            RefreshSelectionState();
            RefreshLayoutPlansFromDocument(doc);
        }

        public static void ApplyProbeResult(Document doc, UIDocument uiDoc, RoomPointProbeResult probeResult)
        {
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state == null || _state.Mode != RoomRecognitionPaneMode.Probe)
                {
                    _state = new RoomRecognitionPaneState
                    {
                        Mode = RoomRecognitionPaneMode.Probe
                    };
                    _selectedRoomKey = null;
                    _selectedLiftKey = null;
                }

                if (probeResult != null && probeResult.Success && probeResult.SemanticRecord != null)
                {
                    MergeProbeCard(doc, probeResult);
                }
                else if (!string.IsNullOrWhiteSpace(_state.SelectedProbeRoomStableKey))
                {
                    // Keep existing history cards on probe failure while the latest highlight has already been cleared.
                }
            }

            RefreshSelectionState();
            RefreshLayoutPlansFromDocument(doc);
        }

        public static void RefreshFamilyCatalog()
        {
            RoomCustomFamilyCatalogService.Reload();
            ExecuteOnUiThread(() =>
            {
                string highlightedFamilyKey = DetailViewModel.HighlightedFamilyKey;
                DetailViewModel.LoadFamilyOptions();
                DetailViewModel.HighlightedFamilyKey = highlightedFamilyKey;
            });
        }

        public static List<ManualRoomValidationRoomInfo> GetRoomValidationSnapshot()
        {
            RoomRecognitionPaneState snapshot;
            lock (SyncRoot)
            {
                snapshot = _state;
            }

            List<ManualRoomValidationRoomInfo> result = new List<ManualRoomValidationRoomInfo>();
            foreach (RoomSemanticRecord room in snapshot?.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
            {
                if (room == null || string.IsNullOrWhiteSpace(room.Key))
                {
                    continue;
                }

                ElementId levelId = ResolveRoomLevelId(snapshot, room.Key);
                result.Add(new ManualRoomValidationRoomInfo
                {
                    Room = room,
                    LevelIdValue = levelId != null ? levelId.IntegerValue : -1,
                    SourceType = room.Status ?? string.Empty
                });
            }

            return result;
        }

        public static void RefreshSelectionState()
        {
            RefreshSelectionState(preserveSolutionEditor: false);
        }

        private static void RefreshSelectionState(bool preserveSolutionEditor)
        {
            RoomRecognitionPaneState snapshot;
            string selectedKey;
            string selectedLiftKey;
            lock (SyncRoot)
            {
                snapshot = _state ?? new RoomRecognitionPaneState();
                selectedKey = _selectedRoomKey;
                selectedLiftKey = _selectedLiftKey;
            }

            if (snapshot.Mode == RoomRecognitionPaneMode.Probe)
            {
                RefreshProbeSelectionState(snapshot);
                return;
            }

            HashSet<string> liftRoomKeys = BuildAnalyzeRoomsLiftRoomKeys(snapshot.Summary);
            List<RoomSemanticRecord> matchedRooms = (snapshot.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                .Where(IsMatchedRoom)
                .Where(x => x == null || !liftRoomKeys.Contains(x.Key ?? string.Empty))
                .OrderByDescending(x => x.AreaM2)
                .ToList();
            List<LiftRecognitionRecord> lifts = snapshot.LiftByKey.Values
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .OrderBy(x => x.LiftName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<EditorRoomOptionViewModel> editorRoomOptions = matchedRooms.Select(room =>
            {
                string key = room.Key ?? string.Empty;
                string roomName = ResolveRoomDisplayName(snapshot, key, room.RoomName);
                string roomType = string.IsNullOrWhiteSpace(room.TargetRoomType) ? "-" : room.TargetRoomType;
                string levelName = snapshot.LevelNameByRoomKey.TryGetValue(key, out string resolvedLevel)
                    ? resolvedLevel
                    : Loc.T("Common.NA");
                string areaText = FormatArea(room.AreaM2);
                string statusText = string.IsNullOrWhiteSpace(room.Status) ? "-" : room.Status;
                ElementId levelId = ResolveRoomLevelId(snapshot, key);
                RoomCardMetricDisplay metrics = BuildRoomCardMetricDisplay(room, areaText, levelId);

                return new EditorRoomOptionViewModel
                {
                    Key = key,
                    RoomName = roomName,
                    TargetType = roomType,
                    AreaText = areaText,
                    LevelText = levelName,
                    StatusText = statusText,
                    RoomLengthText = metrics.RoomLengthText,
                    RoomWidthText = metrics.RoomWidthText,
                    RoomHeightText = metrics.RoomHeightText,
                    DoorWidthText = metrics.DoorWidthText,
                    DoorHeightText = metrics.DoorHeightText,
                    AvailableUsableAreaText = metrics.AvailableUsableAreaText,
                    DisplayName = roomName,
                    WallOptions = BuildEditorWallOptions(room)
                };
            }).ToList();

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                int foundCount = matchedRooms.Count;
                ListViewModel.SummaryText = string.Empty;
                ListViewModel.Rooms.Clear();
                ListViewModel.Lifts.Clear();
                DetailViewModel.SetEditorRoomOptions(editorRoomOptions);
                DetailViewModel.SetEditorLiftOptionItems(BuildEditorLiftOptions(lifts));
                DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();

                foreach (LiftRecognitionRecord lift in lifts)
                {
                    ListViewModel.Lifts.Add(BuildLiftListItem(lift, selectedLiftKey));
                }

                if (foundCount == 0)
                {
                    ListViewModel.SetSelectedRoomSilently(null);
                    LiftListItemViewModel selectedLiftItem = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, selectedLiftKey, StringComparison.OrdinalIgnoreCase));
                    ListViewModel.SetSelectedLiftSilently(selectedLiftItem);
                    return;
                }

                foreach (RoomSemanticRecord room in matchedRooms)
                {
                    RoomListItemViewModel item = BuildListItem(snapshot, room, selectedKey);
                    ListViewModel.Rooms.Add(item);
                }
            });

            if (preserveSolutionEditor)
            {
                // Auto-detect is a data refresh, not a navigation command. Keep the
                // right-side Equipment Planner on its current page and only refresh
                // the left-side selection highlight plus the room/lift option lists.
                ExecuteOnUiThread(() =>
                {
                    RoomListItemViewModel selectedRoomItem = ListViewModel.Rooms.FirstOrDefault(x =>
                        string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
                    LiftListItemViewModel selectedLiftItem = ListViewModel.Lifts.FirstOrDefault(x =>
                        string.Equals(x.Key, selectedLiftKey, StringComparison.OrdinalIgnoreCase));

                    ListViewModel.SetSelectedRoomSilently(selectedRoomItem);
                    ListViewModel.SetSelectedLiftSilently(selectedLiftItem);
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedKey) || !matchedRooms.Any(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(selectedLiftKey) && lifts.Any(x => string.Equals(x.Key, selectedLiftKey, StringComparison.OrdinalIgnoreCase)))
                {
                    SetDetailEmpty();
                    ExecuteOnUiThread(() =>
                    {
                        LiftListItemViewModel selectedLiftItem = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, selectedLiftKey, StringComparison.OrdinalIgnoreCase));
                        ListViewModel.SetSelectedLiftSilently(selectedLiftItem);
                    });
                    return;
                }

                SetDetailEmpty();
                return;
            }

            RoomSemanticRecord selectedRoom = matchedRooms.FirstOrDefault(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            ApplyDetail(selectedRoom);
            ExecuteOnUiThread(() =>
            {
                RoomListItemViewModel selectedItem = ListViewModel.Rooms.FirstOrDefault(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(selectedItem);
                ListViewModel.SetSelectedLiftSilently(null);
            });
        }

        public static void OnListRoomSelected(RoomListItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsProbeRoomCard)
            {
                SelectProbeRoomCardForFocusOnly(item.StableRoomKey);
                _ = RequestRestoreProbePreviewAsync(item.StableRoomKey);
                return;
            }

            if (string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            ToggleRoomSelectionForLeftOnly(item.Key);
        }

        public static void OnListLiftSelected(LiftListItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            ToggleLiftSelectionForLeftOnly(item.Key);
        }

        public static void SelectRoomByKey(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                SetDetailEmpty();
                return;
            }

            RoomSemanticRecord room;
            lock (SyncRoot)
            {
                _selectedRoomKey = roomKey;
                _selectedLiftKey = null;
                _state.SelectedLiftKey = null;
                _state.RoomByKey.TryGetValue(roomKey, out room);
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = string.Equals(item.Key, roomKey, StringComparison.OrdinalIgnoreCase);
                }

                RoomListItemViewModel selectedItem = ListViewModel.Rooms.FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(selectedItem);
                foreach (LiftListItemViewModel liftItem in ListViewModel.Lifts)
                {
                    liftItem.IsSelected = false;
                }
                ListViewModel.SetSelectedLiftSilently(null);
            });

            ApplyDetail(room);
        }

        public static void SelectLiftByKey(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                SetDetailEmpty();
                return;
            }

            lock (SyncRoot)
            {
                _selectedRoomKey = null;
                _selectedLiftKey = liftKey;
                _state.SelectedLiftKey = liftKey;
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = false;
                }

                foreach (LiftListItemViewModel item in ListViewModel.Lifts)
                {
                    item.IsSelected = string.Equals(item.Key, liftKey, StringComparison.OrdinalIgnoreCase);
                }

                LiftListItemViewModel selectedItem = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(null);
                ListViewModel.SetSelectedLiftSilently(selectedItem);
            });

            SetDetailEmpty();
        }

        public static bool TryGetSelectedRoom(out RoomSemanticRecord room)
        {
            lock (SyncRoot)
            {
                room = null;
                if (_state == null || string.IsNullOrWhiteSpace(_selectedRoomKey))
                {
                    return false;
                }

                return _state.RoomByKey.TryGetValue(_selectedRoomKey, out room) && room != null;
            }
        }

        public static void SelectFirstMatchedRoomAndFocus()
        {
            string firstKey = null;
            lock (SyncRoot)
            {
                firstKey = (_state?.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                    .Where(IsMatchedRoom)
                    .Where(x => !BuildAnalyzeRoomsLiftRoomKeys(_state?.Summary).Contains(x.Key ?? string.Empty))
                    .OrderByDescending(x => x.AreaM2)
                    .Select(x => x.Key)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }

            if (string.IsNullOrWhiteSpace(firstKey))
            {
                SetDetailEmpty();
                return;
            }

            SelectRoomByKey(firstKey);
            _ = RequestFocusRoomAsync(firstKey);
        }

        public static void SelectFirstMatchedRoom()
        {
            string firstKey = null;
            lock (SyncRoot)
            {
                firstKey = (_state?.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                    .Where(IsMatchedRoom)
                    .Where(x => !BuildAnalyzeRoomsLiftRoomKeys(_state?.Summary).Contains(x.Key ?? string.Empty))
                    .OrderByDescending(x => x.AreaM2)
                    .Select(x => x.Key)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }

            if (string.IsNullOrWhiteSpace(firstKey))
            {
                SetDetailEmpty();
                return;
            }

            SelectRoomByKey(firstKey);
        }

        public static Task<bool> RequestAutoDetectRoomsAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.AutoDetectRooms,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestAutoDetectLiftsAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.AutoDetectLifts,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestCreateManualRoomAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreateManualRoom,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestFinishManualRoomSelectionAsync()
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.FinishManualRoomSelection, null, null, null);
        }

        public static Task<bool> RequestCancelManualRoomSelectionAsync()
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.CancelManualRoomSelection, null, null, null);
        }

        public static Task<bool> RequestCreateManualLiftAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreateManualLift,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestRenameRoomAsync(string roomKey, string newName)
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.RenameRoom, roomKey, null, newName);
        }

        public static Task<bool> RequestRenameLiftAsync(string liftKey, string newName)
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.RenameLift, null, liftKey, newName);
        }

        public static Task<bool> RequestSaveLiftDisplayInfoAsync(string liftKey, string newName, LiftDisplayOverride displayOverride)
        {
            if (string.IsNullOrWhiteSpace(liftKey) ||
                string.IsNullOrWhiteSpace(newName) ||
                displayOverride == null ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.SaveLiftDisplayInfo,
                LiftKey = liftKey,
                NewName = newName,
                LiftDisplayOverride = displayOverride,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestDeleteRoomAsync(string roomKey)
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.DeleteRoom, roomKey, null, null);
        }

        public static Task<bool> RequestDeleteLiftAsync(string liftKey)
        {
            return EnqueueSimpleRequest(RoomRecognitionPaneRequestType.DeleteLift, null, liftKey, null);
        }

        private static Task<bool> EnqueueSimpleRequest(RoomRecognitionPaneRequestType type, string roomKey, string liftKey, string newName)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = type,
                RoomKey = roomKey,
                LiftKey = liftKey,
                NewName = newName,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static async void EditRoomNameFromUi(RoomListItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            RoomRecognitionNameEditWindow window = new RoomRecognitionNameEditWindow("Room Name", item.Title);
            if (window.ShowDialog() == true)
            {
                await RequestRenameRoomAsync(item.Key, window.EditedName);
            }
        }

        public static async void EditLiftNameFromUi(LiftListItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            LiftRecognitionRecord lift = null;
            lock (SyncRoot)
            {
                if (_state != null && _state.LiftByKey != null)
                {
                    _state.LiftByKey.TryGetValue(item.Key, out lift);
                }
            }

            LiftEditWindow window = new LiftEditWindow(item.Key, item.Title, ResolveLiftDisplayInfo(lift));
            window.SetRevitOwner();
            if (window.ShowDialog() == true)
            {
                await RequestSaveLiftDisplayInfoAsync(item.Key, window.EditedName, window.DisplayOverride);
            }
        }

        public static async void DeleteRoomFromUi(RoomListItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            RoomRecognitionDeleteConfirmWindow window = new RoomRecognitionDeleteConfirmWindow(
                "Are you sure you want to delete this room from the current list?");
            if (window.ShowDialog() == true)
            {
                await RequestDeleteRoomAsync(item.Key);
            }
        }

        public static async void DeleteLiftFromUi(LiftListItemViewModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key))
            {
                return;
            }

            RoomRecognitionDeleteConfirmWindow window = new RoomRecognitionDeleteConfirmWindow(
                "Are you sure you want to delete this lift from the current list?");
            if (window.ShowDialog() == true)
            {
                await RequestDeleteLiftAsync(item.Key);
            }
        }

        public static Task<bool> RequestFocusRoomAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.FocusRoom,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestFocusLiftAsync(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.FocusLift,
                LiftKey = liftKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestFocusLiftPreserveViewAsync(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.FocusLiftPreserveView,
                LiftKey = liftKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestClearRoomFocusAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearRoomFocus,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestHighlightRoomOnlyAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.HighlightRoomOnly,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestHighlightLiftOnlyAsync(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.HighlightLiftOnly,
                LiftKey = liftKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestClearLeftSelectionHighlightAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearLeftSelectionHighlight,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<CalculatePathExecutionResult> RequestGenerateDeliveryRouteAsync(string startLiftKey, string targetRoomKey)
        {
            if (string.IsNullOrWhiteSpace(startLiftKey) ||
                string.IsNullOrWhiteSpace(targetRoomKey) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(new CalculatePathExecutionResult
                {
                    Success = false,
                    Drawn = false,
                    Message = "Failed to generate delivery route."
                });
            }

            TaskCompletionSource<CalculatePathExecutionResult> tcs = new TaskCompletionSource<CalculatePathExecutionResult>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.GenerateDeliveryRoute,
                StartLiftKey = startLiftKey,
                TargetRoomKey = targetRoomKey,
                PathExecutionCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<DeliveryRoutePreparationResult> RequestPrepareDeliveryRouteAsync(string startLiftKey, string targetRoomKey)
        {
            return RequestPrepareDeliveryRouteAsync(
                "Lift",
                startLiftKey,
                null,
                null,
                null,
                targetRoomKey);
        }

        public static Task<DeliveryRoutePreparationResult> RequestPrepareDeliveryRouteAsync(
            string startLocationType,
            string startLiftKey,
            double? startPointXmm,
            double? startPointYmm,
            double? startPointZmm,
            string targetRoomKey)
        {
            bool isPoint = string.Equals(startLocationType, "Point", StringComparison.OrdinalIgnoreCase);
            bool validStart = isPoint
                ? startPointXmm.HasValue && startPointYmm.HasValue
                : !string.IsNullOrWhiteSpace(startLiftKey);

            if (!validStart ||
                string.IsNullOrWhiteSpace(targetRoomKey) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(new DeliveryRoutePreparationResult
                {
                    Success = false,
                    Message = "Failed to generate delivery route."
                });
            }

            TaskCompletionSource<DeliveryRoutePreparationResult> tcs = new TaskCompletionSource<DeliveryRoutePreparationResult>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.PrepareDeliveryRoute,
                StartLocationType = isPoint ? "Point" : "Lift",
                StartLiftKey = startLiftKey,
                StartPointXmm = startPointXmm,
                StartPointYmm = startPointYmm,
                StartPointZmm = startPointZmm,
                TargetRoomKey = targetRoomKey,
                PreparationCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestBeginDeliveryRouteStartPointSelectionAsync(
            string originalName,
            double? originalXmm,
            double? originalYmm,
            double? originalZmm)
        {
            if (_externalEvent == null || _handler == null)
            {
                InitializeExternalEvent();
            }
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.BeginDeliveryRouteStartPointSelection,
                NewName = originalName ?? string.Empty,
                StartPointXmm = originalXmm,
                StartPointYmm = originalYmm,
                StartPointZmm = originalZmm,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static void RequestConfirmDeliveryRouteStartPointSelection()
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            if (session == null || !session.IsActive)
            {
                return;
            }

            session.ConfirmRequested = true;
            session.CancelRequested = false;
            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteStartPoint] Confirm requested. IsPicking=" + session.IsPicking);
            if (session.IsPicking)
            {
                InterruptDeliveryRouteStartPointPick(session);
                return;
            }

            EnqueueDeliveryRouteStartPointRequest(RoomRecognitionPaneRequestType.ConfirmDeliveryRouteStartPointSelection);
        }

        public static void RequestCancelDeliveryRouteStartPointSelection()
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            if (session == null || !session.IsActive)
            {
                return;
            }

            session.CancelRequested = true;
            session.ConfirmRequested = false;
            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteStartPoint] Cancel requested. IsPicking=" + session.IsPicking);
            if (session.IsPicking)
            {
                InterruptDeliveryRouteStartPointPick(session);
                return;
            }

            EnqueueDeliveryRouteStartPointRequest(RoomRecognitionPaneRequestType.CancelDeliveryRouteStartPointSelection);
        }

        public static Task<bool> RequestCancelDeliveryRouteStartPointSelectionAsync()
        {
            RequestCancelDeliveryRouteStartPointSelection();
            return Task.FromResult(true);
        }

        public static Task<bool> RequestFocusDeliveryRouteStartPointAsync(
            double startPointXmm,
            double startPointYmm,
            double startPointZmm)
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.FocusDeliveryRouteStartPoint,
                StartPointXmm = startPointXmm,
                StartPointYmm = startPointYmm,
                StartPointZmm = startPointZmm,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestClearDeliveryRouteStartPointMarkerAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                InitializeExternalEvent();
            }
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearDeliveryRouteStartPointMarker,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<AhuPlacementValidationPreparationResult> RequestPrepareAhuPlacementValidationAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(new AhuPlacementValidationPreparationResult
                {
                    Success = false,
                    Message = "Failed to prepare AHU room fit validation."
                });
            }

            TaskCompletionSource<AhuPlacementValidationPreparationResult> tcs =
                new TaskCompletionSource<AhuPlacementValidationPreparationResult>();

            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.PrepareAhuPlacementValidation,
                RoomKey = roomKey,
                AhuPlacementPreparationCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestClearAhuPlacementPointMarkerAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearAhuPlacementPointMarker,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<CalculatePathExecutionResult> RequestDrawDeliveryRoutePathAsync(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(new CalculatePathExecutionResult
                {
                    Success = false,
                    Drawn = false,
                    Message = "Failed to generate delivery route."
                });
            }

            TaskCompletionSource<CalculatePathExecutionResult> tcs = new TaskCompletionSource<CalculatePathExecutionResult>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.DrawDeliveryRoutePath,
                ResponseBody = responseBody,
                PathExecutionCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestClearDeliveryRoutePathAsync()
        {
            if (_externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearDeliveryRoutePath,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<CalculatePathExecutionResult> RequestDrawDeliveryRouteComparisonAsync(IList<string> routeIds)
        {
            if (routeIds == null || routeIds.Count == 0 || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(new CalculatePathExecutionResult
                {
                    Success = false,
                    Drawn = false,
                    Message = "Please select at least one delivery route to compare."
                });
            }

            TaskCompletionSource<CalculatePathExecutionResult> tcs =
                new TaskCompletionSource<CalculatePathExecutionResult>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.DrawDeliveryRouteComparison,
                RouteIds = routeIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                PathExecutionCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<CalculatePathExecutionResult> RequestDrawLayoutPlanRouteComparisonAsync(IList<string> layoutIds)
        {
            if (layoutIds == null || layoutIds.Count == 0 || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(new CalculatePathExecutionResult
                {
                    Success = false,
                    Drawn = false,
                    Message = "Please select at least one layout plan to compare."
                });
            }

            TaskCompletionSource<CalculatePathExecutionResult> tcs = new TaskCompletionSource<CalculatePathExecutionResult>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.DrawLayoutPlanRouteComparison,
                LayoutIds = layoutIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                PathExecutionCompletion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestSetRoomCustomFamilyAsync(string roomKey, string familyKey)
        {
            return RequestSetRoomCustomFamilyAsync(roomKey, familyKey, false, 0, 0, false, 0);
        }

        public static Task<bool> RequestSetRoomCustomFamilyAsync(
            string roomKey,
            string familyKey,
            double placementXmm,
            double placementYmm)
        {
            return RequestSetRoomCustomFamilyAsync(
                roomKey,
                familyKey,
                true,
                placementXmm,
                placementYmm,
                false,
                0);
        }

        public static Task<bool> RequestSetRoomCustomFamilyAsync(
            string roomKey,
            string familyKey,
            double placementXmm,
            double placementYmm,
            double? orientationDeg)
        {
            return RequestSetRoomCustomFamilyAsync(
                roomKey,
                familyKey,
                true,
                placementXmm,
                placementYmm,
                orientationDeg.HasValue,
                orientationDeg.GetValueOrDefault());
        }

        private static Task<bool> RequestSetRoomCustomFamilyAsync(
            string roomKey,
            string familyKey,
            bool useCustomPlacementPoint,
            double placementXmm,
            double placementYmm,
            bool useCustomOrientation,
            double orientationDeg)
        {
            if (string.IsNullOrWhiteSpace(roomKey) ||
                string.IsNullOrWhiteSpace(familyKey) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            RoomCustomFamilyOption option = RoomCustomFamilyCatalogService.GetOption(familyKey);
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.SetRoomCustomFamily,
                RoomKey = roomKey,
                FamilyKey = familyKey,
                FamilyPath = option != null ? option.FullPath : string.Empty,
                UseCustomFamilyPlacementPoint = useCustomPlacementPoint,
                CustomFamilyPlacementXmm = placementXmm,
                CustomFamilyPlacementYmm = placementYmm,
                UseCustomFamilyOrientation = useCustomOrientation,
                CustomFamilyOrientationDeg = orientationDeg,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestRestoreProbePreviewAsync(string stableRoomKey)
        {
            if (string.IsNullOrWhiteSpace(stableRoomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.RestoreProbePreview,
                StableRoomKey = stableRoomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestPickPipeWallPointAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.PickPipeWallPoint,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestCreatePipeSystemAsync(string roomKey, string pipeDiameterText)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreatePipeSystem,
                RoomKey = roomKey,
                PipeDiameterText = pipeDiameterText ?? string.Empty,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestPickDuctWallPointAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.PickDuctWallPoint,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestCreateDuctSystemAsync(string roomKey, string ductSizeText)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreateDuctSystem,
                RoomKey = roomKey,
                PipeDiameterText = ductSizeText ?? string.Empty,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestCreateDuctWorkAsync(
            string roomKey,
            string sadDuctSizeText,
            int sadWallElementId,
            string radDuctSizeText,
            int radWallElementId)
        {
            if (string.IsNullOrWhiteSpace(roomKey) ||
                sadWallElementId <= 0 ||
                radWallElementId <= 0 ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreateDuctWork,
                RoomKey = roomKey,
                SadDuctSizeText = sadDuctSizeText ?? string.Empty,
                SadWallElementId = new ElementId(sadWallElementId),
                RadDuctSizeText = radDuctSizeText ?? string.Empty,
                RadWallElementId = new ElementId(radWallElementId),
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static Task<bool> RequestCreatePipeWorkAsync(
            string roomKey,
            string chwsPipeSizeText,
            int chwsWallElementId,
            string chwrPipeSizeText,
            int chwrWallElementId)
        {
            if (string.IsNullOrWhiteSpace(roomKey) ||
                chwsWallElementId <= 0 ||
                chwrWallElementId <= 0 ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.CreatePipeWork,
                RoomKey = roomKey,
                ChwsPipeSizeText = chwsPipeSizeText ?? string.Empty,
                ChwsWallElementId = new ElementId(chwsWallElementId),
                ChwrPipeSizeText = chwrPipeSizeText ?? string.Empty,
                ChwrWallElementId = new ElementId(chwrWallElementId),
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestAddCustomDuctSizeOptionAsync(double lengthMm, double widthMm)
        {
            if (lengthMm <= 0.0 ||
                widthMm <= 0.0 ||
                double.IsNaN(lengthMm) ||
                double.IsNaN(widthMm) ||
                double.IsInfinity(lengthMm) ||
                double.IsInfinity(widthMm) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.AddCustomDuctSizeOption,
                DuctLengthMm = lengthMm,
                DuctWidthMm = widthMm,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestAddCustomPipeSizeOptionAsync(double diameterMm)
        {
            if (diameterMm <= 0.0 ||
                double.IsNaN(diameterMm) ||
                double.IsInfinity(diameterMm) ||
                _externalEvent == null ||
                _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.AddCustomPipeSizeOption,
                PipeSizeMm = diameterMm,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestRemoveDuctWorkAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.RemoveDuctWork,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestRemovePipeWorkAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.RemovePipeWork,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }
        public static Task<bool> RequestSelectBoundaryWallAsync(int wallElementId)
        {
            if (wallElementId <= 0 || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.SelectBoundaryWall,
                BoundaryWallElementId = new ElementId(wallElementId),
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static string GetPlacedRoomCustomFamilyKey(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return string.Empty;
            }

            Document activeDocument;
            lock (SyncRoot)
            {
                if (_state != null &&
                    _state.RoomCustomFamilyKeyByRoomKey.TryGetValue(roomKey, out string cachedFamilyKey) &&
                    !string.IsNullOrWhiteSpace(cachedFamilyKey))
                {
                    return cachedFamilyKey;
                }

                activeDocument = _doc ?? (_uiDoc != null ? _uiDoc.Document : null);
            }

            // The Layout Plans pane can be opened independently from Room & Lift. In that case
            // the in-memory room state may not yet contain the AHU that is already placed in the
            // RVT document. Resolve it from the managed family metadata so Cancel can restore the
            // last submitted AHU instead of treating the room as originally empty.
            if (activeDocument != null)
            {
                try
                {
                    if (RoomCustomFamilyPlacementService.TryGetPlacedFamilyKey(
                            activeDocument,
                            roomKey,
                            out string resolvedFamilyKey) &&
                        !string.IsNullOrWhiteSpace(resolvedFamilyKey))
                    {
                        RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(
                            activeDocument,
                            roomKey,
                            out ElementId resolvedInstanceId);

                        lock (SyncRoot)
                        {
                            if (_state == null)
                            {
                                _state = new RoomRecognitionPaneState();
                            }

                            _state.RoomCustomFamilyKeyByRoomKey[roomKey] = resolvedFamilyKey;
                            _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] =
                                resolvedInstanceId ?? ElementId.InvalidElementId;
                        }

                        return resolvedFamilyKey;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanCancel] Failed to resolve the existing room AHU. RoomKey=" +
                        roomKey + ", Error=" + ex.Message);
                }
            }

            return string.Empty;
        }

        public static DeliveryRouteEquipmentInfo GetDeliveryRouteEquipmentInfo(string roomKey)
        {
            DeliveryRouteEquipmentInfo empty = new DeliveryRouteEquipmentInfo
            {
                Found = false,
                RoomKey = roomKey ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return empty;
            }

            string familyKey = string.Empty;
            ElementId instanceId = ElementId.InvalidElementId;
            Document activeDocument;
            lock (SyncRoot)
            {
                activeDocument = _doc ?? (_uiDoc != null ? _uiDoc.Document : null);
                if (_state != null)
                {
                    _state.RoomCustomFamilyKeyByRoomKey.TryGetValue(roomKey, out familyKey);
                    _state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out instanceId);
                }
            }

            bool cachedInstanceExists =
                !string.IsNullOrWhiteSpace(familyKey) &&
                activeDocument != null &&
                instanceId != null &&
                instanceId != ElementId.InvalidElementId &&
                activeDocument.GetElement(instanceId) != null;

            if (!cachedInstanceExists && activeDocument != null)
            {
                try
                {
                    string resolvedFamilyKey;
                    ElementId resolvedInstanceId;
                    bool hasFamily = RoomCustomFamilyPlacementService.TryGetPlacedFamilyKey(
                        activeDocument,
                        roomKey,
                        out resolvedFamilyKey);
                    bool hasInstance = RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(
                        activeDocument,
                        roomKey,
                        out resolvedInstanceId);

                    if (hasFamily && !string.IsNullOrWhiteSpace(resolvedFamilyKey))
                    {
                        familyKey = resolvedFamilyKey;
                        instanceId = hasInstance && resolvedInstanceId != null
                            ? resolvedInstanceId
                            : ElementId.InvalidElementId;
                        lock (SyncRoot)
                        {
                            if (_state == null)
                            {
                                _state = new RoomRecognitionPaneState();
                            }

                            _state.RoomCustomFamilyKeyByRoomKey[roomKey] = familyKey;
                            _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = instanceId;
                        }
                    }
                    else
                    {
                        lock (SyncRoot)
                        {
                            if (_state != null)
                            {
                                _state.RoomCustomFamilyKeyByRoomKey.Remove(roomKey);
                                _state.RoomCustomFamilyInstanceIdByRoomKey.Remove(roomKey);
                            }
                        }

                        return empty;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DeliveryRouteEquipment] Resolve failed. RoomKey=" +
                        roomKey +
                        ", Error=" +
                        ex.Message);
                    return empty;
                }
            }

            int originalModelId;
            if (string.IsNullOrWhiteSpace(familyKey) ||
                !TryParseDeliveryRouteOriginalModelId(familyKey, out originalModelId))
            {
                return empty;
            }

            RoomCustomFamilyOption option = RoomCustomFamilyCatalogService.GetOption(familyKey);
            return new DeliveryRouteEquipmentInfo
            {
                Found = true,
                RoomKey = roomKey,
                FamilyKey = familyKey,
                OriginalModelId = originalModelId,
                RevitElementId = instanceId != null && instanceId != ElementId.InvalidElementId
                    ? instanceId.IntegerValue
                    : 0,
                DisplayName = option != null && !string.IsNullOrWhiteSpace(option.DisplayName)
                    ? option.DisplayName
                    : familyKey,
                AirflowM3s = option != null ? option.AirflowM3s : 0,
                TotalLengthMm = option != null ? option.TotalLengthMm : 0,
                WidthMm = option != null ? option.WidthMm : 0,
                HeightMm = option != null ? option.HeightMm : 0
            };
        }

        private static bool TryParseDeliveryRouteOriginalModelId(string familyKey, out int originalModelId)
        {
            originalModelId = 0;
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return false;
            }

            const string prefix = "ahu_";
            if (!familyKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string numberText = familyKey.Substring(prefix.Length);
            int value;
            if (!int.TryParse(numberText, out value) || value < 1 || value > 10)
            {
                return false;
            }

            originalModelId = value;
            return true;
        }

        public static Task<bool> RequestClearRoomEquipmentLayoutAsync(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ClearRoomEquipmentLayout,
                RoomKey = roomKey,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestSaveLayoutPlanAsync(
            RoomLayoutPlanDto plan,
            bool submitLayoutPlan = false,
            bool applyActiveState = true)
        {
            if (plan == null || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.SaveLayoutPlan,
                LayoutPlan = plan,
                SubmitLayoutPlan = submitLayoutPlan,
                ApplyLayoutPlanActiveState = applyActiveState,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }


        public static void RefreshDeliveryRouteRecordsSnapshotFromDocument(UIApplication app)
        {
            Document doc = app?.ActiveUIDocument?.Document ?? _doc ?? _uiDoc?.Document;
            if (doc == null)
            {
                lock (SyncRoot)
                {
                    _deliveryRouteRecordsCache.Clear();
                }
                return;
            }

            try
            {
                DeliveryRouteStorePayload payload = DeliveryRouteStorageService.Load(doc);
                lock (SyncRoot)
                {
                    _deliveryRouteRecordsCache.Clear();
                    _deliveryRouteRecordsCache.AddRange(
                        (payload?.Routes ?? new List<DeliveryRouteRecordDto>())
                            .Where(x => x != null));
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoute] Refresh saved route cache failed: " + ex.Message);
            }
        }

        public static IList<DeliveryRouteRecordDto> GetDeliveryRouteRecordsSnapshot()
        {
            lock (SyncRoot)
            {
                return _deliveryRouteRecordsCache
                    .Where(x => x != null)
                    .ToList();
            }
        }

        public static Task<bool> RequestSaveDeliveryRouteAsync(DeliveryRouteRecordDto route)
        {
            if (route == null || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.SaveDeliveryRoute,
                DeliveryRoute = route,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestDeleteDeliveryRouteAsync(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.DeleteDeliveryRoute,
                RouteId = routeId,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestExportDeliveryRouteAsync(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ExportDeliveryRoute,
                RouteId = routeId,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestDeleteLayoutPlanAsync(string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.DeleteLayoutPlan,
                LayoutId = layoutId,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestExportLayoutPlanAsync(string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ExportLayoutPlan,
                LayoutId = layoutId,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static Task<bool> RequestActivateLayoutPlanAsync(string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId) || _externalEvent == null || _handler == null)
            {
                return Task.FromResult(false);
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            _handler.Enqueue(new RoomRecognitionPaneRequest
            {
                Type = RoomRecognitionPaneRequestType.ActivateLayoutPlan,
                LayoutId = layoutId,
                Completion = tcs
            });
            _externalEvent.Raise();
            return tcs.Task;
        }

        public static bool ExecuteSelectBoundaryWall(UIApplication app, ElementId wallElementId)
        {
            if (wallElementId == null || wallElementId == ElementId.InvalidElementId)
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            Element wall = doc.GetElement(wallElementId);
            if (wall == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
            }

            List<ElementId> ids = new List<ElementId> { wallElementId };
            uiDoc.Selection.SetElementIds(ids);

            // Do not call UIDocument.ShowElements here.
            // In Revit 2025 English, ShowElements can activate another open view
            // such as the L1 architectural plan when a wall is selected from the
            // dockable pane. The wall dropdown should only select/highlight the
            // boundary wall and keep the user's current 3D view active.
            try
            {
                uiDoc.RefreshActiveView();
            }
            catch
            {
                // Refresh is best-effort only; selection already succeeded.
            }

            return true;
        }

        public static bool ExecuteAutoDetectRooms(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return false;
            }

            if (ProjectWorkflowModeStoreService.GetMode(doc) == ProjectWorkflowMode.RvtModelImportMode)
            {
                return ExecuteRvtAutoDetectRoomsAndLifts(app);
            }

            TargetRoomModelRecognitionService.RecognitionSummary summary = TargetRoomModelRecognitionService.Run(doc);
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

            TargetRoomSeedDebugVisualizerService.Draw(doc, summary);
            ApplyRoomRecognitionResultOnly(doc, uiDoc, summary, roomRangeElementIds);
            TryHidePreviewPane(app);
            ShowRoomAndLiftPane(app);

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
            });
            return true;
        }

        public static bool ExecuteInitialAutoDetectRoomsAndLifts(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return false;
            }

            TargetRoomModelRecognitionService.RecognitionSummary summary = TargetRoomModelRecognitionService.Run(doc);
            Dictionary<string, List<ElementId>> roomRangeElementIds = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
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

            TargetRoomSeedDebugVisualizerService.Draw(doc, summary);
            ApplyRecognitionResult(doc, uiDoc, summary, roomRangeElementIds);

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
            });
            return true;
        }

        public static bool ExecuteAutoDetectLifts(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return false;
            }

            if (ProjectWorkflowModeStoreService.GetMode(doc) == ProjectWorkflowMode.RvtModelImportMode)
            {
                return ExecuteRvtAutoDetectLiftsOnly(app);
            }

            ApplyLiftRecognitionResultOnly(doc, uiDoc, LiftRecognitionStorageService.Load(doc));
            TryHidePreviewPane(app);
            ShowRoomAndLiftPane(app);

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
            });
            return true;
        }

        private static bool ExecuteRvtAutoDetectRoomsAndLifts(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return false;
            }

            TargetRoomModelRecognitionService.RecognitionSummary summary = AnalyzeRoomsCommandRunner.RunAnalyzeRoomsForActiveModel(
                app,
                "Analyze Rooms found no candidate rooms.",
                true,
                contextElementIds: null,
                preserveSolutionEditor: true);
            if (summary != null)
            {
                List<LiftRecognitionRecord> lifts = MergeRvtDetectedAndManualLifts(doc, summary.Lifts);
                summary.Lifts = lifts;
                ApplyLiftRecognitionResultOnly(doc, uiDoc, lifts);
            }

            return summary != null;
        }

        private static bool ExecuteRvtAutoDetectLiftsOnly(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return false;
            }

            TargetRoomModelRecognitionService.RecognitionSummary currentSummary;
            lock (SyncRoot)
            {
                currentSummary = _state != null ? _state.Summary : null;
            }

            if (currentSummary == null ||
                currentSummary.RunResult == null ||
                currentSummary.RunResult.Rooms == null ||
                currentSummary.RunResult.Rooms.Count == 0)
            {
                return ExecuteRvtAutoDetectRoomsAndLifts(app);
            }

            List<LiftRecognitionRecord> lifts = LiftRoomDetectionService.Detect(doc, currentSummary);
            lifts = MergeRvtDetectedAndManualLifts(doc, lifts);

            ApplyLiftRecognitionResultOnly(doc, uiDoc, lifts);
            TryHidePreviewPane(app);
            ShowRoomAndLiftPane(app);

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
            });
            return true;
        }

        private static List<LiftRecognitionRecord> MergeRvtDetectedAndManualLifts(
            Document doc,
            List<LiftRecognitionRecord> detectedLifts)
        {
            List<LiftRecognitionRecord> result = (detectedLifts ?? new List<LiftRecognitionRecord>())
                .Where(x => x != null)
                .ToList();

            List<LiftRecognitionRecord> manualLifts = LiftRecognitionStorageService.Load(doc)
                .Where(IsManualLiftRecord)
                .ToList();

            foreach (LiftRecognitionRecord lift in manualLifts)
            {
                if (lift == null)
                {
                    continue;
                }

                result.RemoveAll(x => string.Equals(x != null ? x.Key : null, lift.Key, StringComparison.OrdinalIgnoreCase));
                result.Add(lift);
            }

            return result;
        }

        private static bool IsManualLiftRecord(LiftRecognitionRecord lift)
        {
            return lift != null &&
                (string.Equals(lift.GeometrySourceLayer, "Manual", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(lift.LiftType, "Manual", StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(lift.Key) &&
                  lift.Key.StartsWith("manual_lift_", StringComparison.OrdinalIgnoreCase)));
        }

        public static bool ExecuteCreateManualRoom(UIApplication app)
        {
            return ExecuteBeginManualBoundarySelection(app, ManualBoundarySelectionMode.Room);
        }

        public static bool ExecuteBeginManualRoomSelection(UIApplication app)
        {
            return ExecuteBeginManualBoundarySelection(app, ManualBoundarySelectionMode.Room);
        }

        private static bool ExecuteBeginManualBoundarySelection(UIApplication app, ManualBoundarySelectionMode mode)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (app == null || uiDoc == null || doc == null)
            {
                return false;
            }

            EndManualRoomSelection(app, false);

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                _manualRoomSelectionSession = new ManualRoomSelectionSession { IsActive = true, Mode = mode };
                _manualRoomSelectionApp = app;
            }

            try
            {
                uiDoc.Selection.SetElementIds(new List<ElementId>());
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ManualRoom] Clear selection before mode failed: " + ex.Message);
            }

            app.Idling -= OnManualRoomSelectionIdling;
            app.Idling += OnManualRoomSelectionIdling;

            ExecuteOnUiThread(() =>
            {
                ManualRoomSelectionSession session = _manualRoomSelectionSession;
                if (session == null || !session.IsActive)
                {
                    return;
                }

                ManualRoomSelectionBarWindow window = mode == ManualBoundarySelectionMode.Lift
                    ? new ManualRoomSelectionBarWindow(
                        "Lift Creation Mode",
                        "Lift Creation Mode:",
                        "Please click to select the wall / column elements that form the lift shaft.")
                    : new ManualRoomSelectionBarWindow();
                if (app.MainWindowHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(window).Owner = app.MainWindowHandle;
                }

                session.BarWindow = window;
                window.Show();
            });

            return true;
        }

        public static bool ExecuteFinishManualRoomSelection(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : _uiDoc;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            if (app == null || uiDoc == null || doc == null)
            {
                return false;
            }

            UpdateManualRoomSelectionFromCurrentSelection(app);

            List<ElementId> selectedIds;
            ManualBoundarySelectionMode mode;
            lock (SyncRoot)
            {
                if (_manualRoomSelectionSession == null || !_manualRoomSelectionSession.IsActive)
                {
                    return false;
                }

                selectedIds = _manualRoomSelectionSession.SelectedIds.ToList();
                mode = _manualRoomSelectionSession.Mode;
            }

            List<Element> boundaryElements = selectedIds
                .Select(id => doc.GetElement(id))
                .Where(ManualRoomBoundaryBuilder.IsSupportedBoundaryElement)
                .ToList();

            ManualRoomBoundaryBuildResult buildResult = ManualRoomBoundaryBuilder.Build(doc, doc.ActiveView, boundaryElements);

            if (mode == ManualBoundarySelectionMode.Lift)
            {
                // Preserve the original rule first:
                // a fully closed shaft must contain a real Revit door.
                if (buildResult != null && buildResult.Success && buildResult.Record != null)
                {
                    if (!ManualRoomDoorValidator.HasDoor(doc, doc.ActiveView, buildResult.Record, boundaryElements))
                    {
                        ShowManualRoomMissingDoorWindow(app);
                        RestoreManualRoomSelection(uiDoc);
                        return false;
                    }

                    DiagnosticRecorder.AppendDebug(
                        "[ManualLiftBoundary] Rule=ClosedLoopWithDoor, Result=Accepted.");
                    return ExecuteFinishManualLiftSelection(app, uiDoc, doc, buildResult);
                }

                // New lift-only fallback:
                // accept an otherwise open four-sided shaft when one and only
                // one opening is between 1500 mm and 3000 mm. The opening is
                // persisted as a virtual lift door.
                ManualRoomBoundaryBuildResult openGapResult =
                    ManualRoomBoundaryBuilder.BuildOpenGapLiftBoundary(
                        doc,
                        doc.ActiveView,
                        boundaryElements);

                if (openGapResult != null &&
                    openGapResult.Success &&
                    openGapResult.Record != null)
                {
                    return ExecuteFinishManualLiftSelection(app, uiDoc, doc, openGapResult);
                }

                string message = openGapResult != null && !string.IsNullOrWhiteSpace(openGapResult.Message)
                    ? openGapResult.Message
                    : "The selected lift boundary must either form a closed loop containing a door, " +
                      "or form a four-sided lift shaft with one opening between 1500 mm and 3000 mm.";

                LocalizedDialogService.Warning(app, message, "EMSD AI Tool");
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            if (buildResult == null || !buildResult.Success || buildResult.Record == null)
            {
                ShowManualRoomUnclosedWindow(app);
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            if (!ManualRoomDoorValidator.HasDoor(doc, doc.ActiveView, buildResult.Record, boundaryElements))
            {
                ShowManualRoomMissingDoorWindow(app);
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            ManualRoomDuplicateValidationResult duplicateResult = ManualRoomDuplicateValidator.Validate(
                doc,
                buildResult.Record,
                GetRoomValidationSnapshot());
            if (duplicateResult != null && duplicateResult.IsDuplicate)
            {
                LocalizedDialogService.Warning(
                    app,
                    duplicateResult.Message ?? "A room already exists in this area. Please delete the existing room first if you need to recreate it.",
                    "EMSD AI Tool");
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            string defaultName = BuildDefaultManualRoomName(doc);
            RoomRecognitionNameEditWindow nameWindow = new RoomRecognitionNameEditWindow("Room Name", defaultName);
            if (app.MainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(nameWindow).Owner = app.MainWindowHandle;
            }

            bool? dialogResult = nameWindow.ShowDialog();
            if (dialogResult != true)
            {
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            ManualRoomRecord record = buildResult.Record;
            record.Key = "manual_room_" + Guid.NewGuid().ToString("N");
            record.RoomName = nameWindow.EditedName;
            record.RoomNumber = string.Empty;
            record.RoomType = "Manual";
            record.SourceType = "Manual";
            record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using (Transaction tx = new Transaction(doc, "Save Manual Room"))
            {
                tx.Start();
                ManualRoomStorageService.Upsert(doc, record);
                tx.Commit();
            }

            RoomRecognitionPaneRuntime.AddManualRoomAndRefresh(doc, uiDoc, record);
            EndManualRoomSelection(app, true);
            LocalizedDialogService.Success(app, "Manual room saved successfully.", "EMSD AI Tool");
            return true;
        }

        private static bool ExecuteFinishManualLiftSelection(
            UIApplication app,
            UIDocument uiDoc,
            Document doc,
            ManualRoomBoundaryBuildResult buildResult)
        {
            string defaultName = BuildDefaultManualLiftName(doc);
            RoomRecognitionNameEditWindow nameWindow = new RoomRecognitionNameEditWindow("Lift Name", defaultName);
            if (app.MainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(nameWindow).Owner = app.MainWindowHandle;
            }

            bool? dialogResult = nameWindow.ShowDialog();
            if (dialogResult != true)
            {
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            LiftRecognitionRecord lift = ManualLiftRecordBuilder.Build(doc, buildResult, nameWindow.EditedName);
            if (lift == null)
            {
                RestoreManualRoomSelection(uiDoc);
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Save Manual Lift"))
            {
                tx.Start();
                LiftRecognitionStorageService.Upsert(doc, lift);
                tx.Commit();
            }

            AddManualLiftAndRefresh(doc, uiDoc, lift);
            EndManualRoomSelection(app, true);
            LocalizedDialogService.Success(app, "Manual lift saved successfully.", "EMSD AI Tool");
            return true;
        }

        public static bool ExecuteCancelManualRoomSelection(UIApplication app)
        {
            EndManualRoomSelection(app, true);
            return true;
        }

        private static void OnManualRoomSelectionIdling(object sender, IdlingEventArgs e)
        {
            UIApplication app = sender as UIApplication ?? _manualRoomSelectionApp;
            if (app == null)
            {
                return;
            }

            UpdateManualRoomSelectionFromCurrentSelection(app);
        }

        private static void UpdateManualRoomSelectionFromCurrentSelection(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : _uiDoc;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            ManualRoomSelectionSession session = _manualRoomSelectionSession;
            if (uiDoc == null || doc == null || session == null || !session.IsActive)
            {
                return;
            }

            ICollection<ElementId> currentIds;
            try
            {
                currentIds = uiDoc.Selection.GetElementIds();
            }
            catch
            {
                return;
            }

            bool changed = false;
            foreach (ElementId id in currentIds ?? new List<ElementId>())
            {
                Element element = doc.GetElement(id);
                if (!ManualRoomBoundaryBuilder.IsSupportedBoundaryElement(element))
                {
                    changed = true;
                    continue;
                }

                if (session.SelectedIds.Add(id))
                {
                    changed = true;
                }
            }

            if (changed || !IsSameElementIdSet(currentIds, session.SelectedIds))
            {
                RestoreManualRoomSelection(uiDoc);
            }
        }

        private static void RestoreManualRoomSelection(UIDocument uiDoc)
        {
            ManualRoomSelectionSession session = _manualRoomSelectionSession;
            if (uiDoc == null || session == null || !session.IsActive)
            {
                return;
            }

            try
            {
                uiDoc.Selection.SetElementIds(session.SelectedIds.ToList());
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ManualRoom] Restore selection failed: " + ex.Message);
            }
        }

        private static bool IsSameElementIdSet(ICollection<ElementId> currentIds, HashSet<ElementId> selectedIds)
        {
            if (currentIds == null)
            {
                return selectedIds == null || selectedIds.Count == 0;
            }

            if (selectedIds == null || currentIds.Count != selectedIds.Count)
            {
                return false;
            }

            foreach (ElementId id in currentIds)
            {
                if (!selectedIds.Contains(id))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EndManualRoomSelection(UIApplication app, bool clearSelection)
        {
            UIApplication eventApp = app ?? _manualRoomSelectionApp;
            if (eventApp != null)
            {
                eventApp.Idling -= OnManualRoomSelectionIdling;
            }

            ManualRoomSelectionSession session = _manualRoomSelectionSession;
            _manualRoomSelectionSession = null;
            _manualRoomSelectionApp = null;

            ExecuteOnUiThread(() =>
            {
                try
                {
                    if (session != null && session.BarWindow != null)
                    {
                        session.BarWindow.Close();
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[ManualRoom] Close selection bar failed: " + ex.Message);
                }
            });

            if (session != null)
            {
                session.IsActive = false;
                session.SelectedIds.Clear();
            }

            if (clearSelection)
            {
                UIDocument uiDoc = eventApp != null ? eventApp.ActiveUIDocument : _uiDoc;
                try
                {
                    uiDoc?.Selection.SetElementIds(new List<ElementId>());
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[ManualRoom] Clear selection after mode failed: " + ex.Message);
                }
            }
        }

        private static void ShowManualRoomUnclosedWindow(UIApplication app)
        {
            ManualRoomMessageWindow window = new ManualRoomMessageWindow(
                "Unclosed Space",
                "The selected walls do not form a closed loop. Please select additional walls.");
            if (app != null && app.MainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = app.MainWindowHandle;
            }

            window.ShowDialog();
        }

        private static void ShowManualRoomMissingDoorWindow(UIApplication app)
        {
            ManualRoomMessageWindow window = new ManualRoomMessageWindow(
                "Missing Door",
                "The selected walls do not contain a door element. Please check your selection.");
            if (app != null && app.MainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = app.MainWindowHandle;
            }

            window.ShowDialog();
        }

        private static string BuildDefaultManualRoomName(Document doc)
        {
            HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManualRoomRecord room in ManualRoomStorageService.Load(doc))
            {
                if (!string.IsNullOrWhiteSpace(room.RoomName))
                {
                    existingNames.Add(room.RoomName.Trim());
                }
            }

            lock (SyncRoot)
            {
                foreach (RoomSemanticRecord room in _state?.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(room.RoomName))
                    {
                        existingNames.Add(room.RoomName.Trim());
                    }
                }
            }

            for (int i = 1; i < 10000; i++)
            {
                string candidate = "ROOM " + i.ToString("000");
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            return "ROOM " + (existingNames.Count + 1).ToString("000");
        }

        public static bool ExecuteCreateManualLift(UIApplication app)
        {
            return ExecuteBeginManualBoundarySelection(app, ManualBoundarySelectionMode.Lift);
        }

        private static string BuildDefaultManualLiftName(Document doc)
        {
            HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiftRecognitionRecord lift in LiftRecognitionStorageService.Load(doc))
            {
                if (!string.IsNullOrWhiteSpace(lift.LiftName))
                {
                    existingNames.Add(lift.LiftName.Trim());
                }
            }

            lock (SyncRoot)
            {
                foreach (LiftRecognitionRecord lift in _state?.Summary?.Lifts ?? new List<LiftRecognitionRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(lift.LiftName))
                    {
                        existingNames.Add(lift.LiftName.Trim());
                    }
                }
            }

            for (int i = 1; i < 10000; i++)
            {
                string candidate = "LIFT " + i.ToString("000");
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            return "LIFT " + (existingNames.Count + 1).ToString("000");
        }

        public static bool ExecuteRenameRoom(UIApplication app, string roomKey, string newName)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            string trimmed = (newName ?? string.Empty).Trim();
            if (doc == null || string.IsNullOrWhiteSpace(roomKey) || string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Update Room Display Name"))
            {
                tx.Start();
                RoomRecognitionNameOverrideStorageService.UpsertRoomName(doc, roomKey, trimmed);
                tx.Commit();
            }

            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.RoomDisplayNameByKey[roomKey] = trimmed;
                }
            }

            ExecuteOnUiThread(() =>
            {
                RoomListItemViewModel item = ListViewModel.Rooms.FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.Title = trimmed;
                    item.RaiseTitleChanged();
                }
            });
            return true;
        }

        public static bool ExecuteRenameLift(UIApplication app, string liftKey, string newName)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            string trimmed = (newName ?? string.Empty).Trim();
            if (doc == null || string.IsNullOrWhiteSpace(liftKey) || string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Update Lift Display Name"))
            {
                tx.Start();
                RoomRecognitionNameOverrideStorageService.UpsertLiftName(doc, liftKey, trimmed);
                tx.Commit();
            }

            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.LiftDisplayNameByKey[liftKey] = trimmed;
                }
            }

            ExecuteOnUiThread(() =>
            {
                LiftListItemViewModel item = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.Title = trimmed;
                    item.RaiseTitleChanged();
                }
            });
            return true;
        }

        public static bool ExecuteSaveLiftDisplayInfo(
            UIApplication app,
            string liftKey,
            string newName,
            LiftDisplayOverride displayOverride)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            string trimmed = (newName ?? string.Empty).Trim();
            if (doc == null ||
                string.IsNullOrWhiteSpace(liftKey) ||
                string.IsNullOrWhiteSpace(trimmed) ||
                displayOverride == null)
            {
                return false;
            }

            displayOverride.LiftKey = liftKey;
            using (Transaction tx = new Transaction(doc, "Update Lift Display Info"))
            {
                tx.Start();
                RoomRecognitionNameOverrideStorageService.UpsertLiftName(doc, liftKey, trimmed);
                LiftDisplayOverrideStorageService.Upsert(doc, displayOverride);
                tx.Commit();
            }

            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.LiftDisplayNameByKey[liftKey] = trimmed;
                    _state.LiftDisplayOverrideByKey[liftKey] = displayOverride;
                }
            }

            RefreshSelectionState();
            DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();
            return true;
        }

        public static bool ExecuteDeleteRoomFromCurrentList(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.Summary?.RunResult?.Rooms?.RemoveAll(x => x == null || string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                    _state.RoomByKey.Remove(roomKey);
                    _state.RoomRangeElementIds.Remove(roomKey);
                    _state.RoomDisplayNameByKey.Remove(roomKey);
                    if (string.Equals(_selectedRoomKey, roomKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedRoomKey = null;
                    }
                }
            }

            ExecuteOnUiThread(() =>
            {
                RoomListItemViewModel item = ListViewModel.Rooms.FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    ListViewModel.Rooms.Remove(item);
                }

                ListViewModel.SetSelectedRoomSilently(null);
            });
            SetDetailEmpty();
            return true;
        }

        public static bool ExecuteDeleteLiftFromCurrentList(UIApplication app, string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return false;
            }

            List<LiftRecognitionRecord> remainingLifts = new List<LiftRecognitionRecord>();
            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.Summary?.Lifts?.RemoveAll(x => x == null || string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                    _state.LiftByKey.Remove(liftKey);
                    _state.LiftDisplayNameByKey.Remove(liftKey);
                    _state.LiftDisplayOverrideByKey.Remove(liftKey);
                    if (string.Equals(_selectedLiftKey, liftKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedLiftKey = null;
                    }

                    remainingLifts = (_state.Summary?.Lifts ?? new List<LiftRecognitionRecord>())
                        .Where(x => x != null)
                        .ToList();
                }
            }

            ExecuteOnUiThread(() =>
            {
                LiftListItemViewModel item = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    ListViewModel.Lifts.Remove(item);
                }

                ListViewModel.SetSelectedLiftSilently(null);
                DetailViewModel.SetEditorLiftOptionItems(BuildEditorLiftOptions(remainingLifts));
                DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();
            });
            SetDetailEmpty();
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            if (doc != null)
            {
                using (Transaction tx = new Transaction(doc, "Delete Lift Display Overrides"))
                {
                    tx.Start();
                    RoomRecognitionNameOverrideStorageService.DeleteLiftNameOverride(doc, liftKey);
                    LiftDisplayOverrideStorageService.Delete(doc, liftKey);
                    tx.Commit();
                }
            }
            return true;
        }

        public static bool ExecuteFocusRoom(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            RoomSemanticRecord room;
            List<ElementId> roomRangeIds = null;
            lock (SyncRoot)
            {
                if (!_state.RoomByKey.TryGetValue(roomKey, out room) || room == null)
                {
                    return false;
                }

                _state.RoomRangeElementIds.TryGetValue(roomKey, out roomRangeIds);
            }

            View activeView = doc.ActiveView;
            if (!(activeView is View3D))
            {
                List<ElementId> validIds = (roomRangeIds ?? new List<ElementId>())
                    .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                    .Distinct()
                    .ToList();
                if (validIds.Count > 0)
                {
                    uiDoc.Selection.SetElementIds(validIds);
                    uiDoc.ShowElements(validIds);
                    Lift3DVisualizationService.Clear(doc);
                    Room3DVisualizationService.HighlightRoom(doc, roomKey);
                    return true;
                }
            }

            RevitRoomSemanticFocusService.Focus(uiDoc, room);
            Lift3DVisualizationService.Clear(doc);
            Room3DVisualizationService.HighlightRoom(doc, roomKey);
            return true;
        }

        public static bool ExecuteClearRoomFocus(UIApplication app)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            try
            {
                uiDoc.Selection.SetElementIds(new List<ElementId>());
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomManagement] Clear room selection skipped. Error=" + ex.Message);
            }

            TargetRoomModelRecognitionService.RecognitionSummary summary = null;
            lock (SyncRoot)
            {
                summary = _state != null ? _state.Summary : null;
            }

            if (doc.ActiveView is View3D && summary != null)
            {
                Room3DVisualizationService.Refresh(doc, summary);
            }

            return true;
        }

        public static bool ExecuteHighlightRoomOnly(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            List<ElementId> roomRangeIds = null;
            lock (SyncRoot)
            {
                if (_state == null || !_state.RoomByKey.ContainsKey(roomKey))
                {
                    return false;
                }

                _state.RoomRangeElementIds.TryGetValue(roomKey, out roomRangeIds);
            }

            Lift3DVisualizationService.Clear(doc);
            if (doc.ActiveView is View3D)
            {
                Room3DVisualizationService.HighlightRoom(doc, roomKey);
                return true;
            }

            List<ElementId> validIds = (roomRangeIds ?? new List<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                .Distinct()
                .ToList();
            if (validIds.Count > 0)
            {
                uiDoc.Selection.SetElementIds(validIds);
            }

            return true;
        }

        public static bool ExecuteHighlightLiftOnly(UIApplication app, string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            LiftRecognitionRecord lift;
            TargetRoomModelRecognitionService.RecognitionSummary summary = null;
            lock (SyncRoot)
            {
                if (_state == null || !_state.LiftByKey.TryGetValue(liftKey, out lift) || lift == null)
                {
                    return false;
                }

                summary = _state.Summary;
            }

            if (doc.ActiveView is View3D && summary != null)
            {
                Room3DVisualizationService.Refresh(doc, summary);
            }
            else
            {
                try
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId>());
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[RoomManagement] Clear selection before lift highlight skipped. Error=" + ex.Message);
                }
            }

            return Lift3DVisualizationService.Highlight(doc, lift);
        }

        public static bool ExecuteClearLeftSelectionHighlight(UIApplication app)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            try
            {
                uiDoc.Selection.SetElementIds(new List<ElementId>());
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomManagement] Clear left selection highlight skipped. Error=" + ex.Message);
            }

            TargetRoomModelRecognitionService.RecognitionSummary summary = null;
            lock (SyncRoot)
            {
                summary = _state != null ? _state.Summary : null;
            }

            if (doc.ActiveView is View3D && summary != null)
            {
                Room3DVisualizationService.Refresh(doc, summary);
            }

            Lift3DVisualizationService.Clear(doc);
            return true;
        }

        public static bool ExecuteFocusLift(UIApplication app, string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            LiftRecognitionRecord lift;
            RoomSemanticRecord matchedRoom = null;
            string matchedRoomKey = string.Empty;
            lock (SyncRoot)
            {
                if (!_state.LiftByKey.TryGetValue(liftKey, out lift) || lift == null)
                {
                    return false;
                }

                if (IsAnalyzeRoomsPostProcessLift(lift) &&
                    TryGetAnalyzeRoomsLiftRoomKey(lift, out matchedRoomKey))
                {
                    _state.RoomByKey.TryGetValue(matchedRoomKey, out matchedRoom);
                }

                _doc = doc;
                _uiDoc = uiDoc;
            }

            if (matchedRoom != null && !string.IsNullOrWhiteSpace(matchedRoomKey) && uiDoc != null)
            {
                Lift3DVisualizationService.Clear(doc);
                RevitRoomSemanticFocusService.Focus(uiDoc, matchedRoom);
                Room3DVisualizationService.HighlightRoom(doc, matchedRoomKey);
                return true;
            }

            return Lift3DVisualizationService.Focus(uiDoc, lift);
        }

        public static bool ExecuteFocusLiftPreserveView(UIApplication app, string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            LiftRecognitionRecord lift;
            RoomSemanticRecord matchedRoom = null;
            string matchedRoomKey = string.Empty;
            lock (SyncRoot)
            {
                if (_state == null || !_state.LiftByKey.TryGetValue(liftKey, out lift) || lift == null)
                {
                    return false;
                }

                if (IsAnalyzeRoomsPostProcessLift(lift) &&
                    TryGetAnalyzeRoomsLiftRoomKey(lift, out matchedRoomKey))
                {
                    _state.RoomByKey.TryGetValue(matchedRoomKey, out matchedRoom);
                }

                _doc = doc;
                _uiDoc = uiDoc;
            }

            if (matchedRoom != null && !string.IsNullOrWhiteSpace(matchedRoomKey))
            {
                Lift3DVisualizationService.Clear(doc);
                RevitRoomSemanticFocusService.Focus(uiDoc, matchedRoom);
                Room3DVisualizationService.HighlightRoom(doc, matchedRoomKey);
                return true;
            }

            return Lift3DVisualizationService.FocusPreserveView(uiDoc, lift);
        }

        public static CalculatePathExecutionResult ExecuteGenerateDeliveryRoute(
            UIApplication app,
            string startLiftKey,
            string targetRoomKey)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to generate delivery route.", null, null);
            }

            LiftRecognitionRecord lift = null;
            RoomSemanticRecord room = null;
            lock (SyncRoot)
            {
                if (_state == null)
                {
                    return ShowDeliveryRouteFailure(app, "Failed to generate delivery route.", null, null);
                }

                _state.LiftByKey.TryGetValue(startLiftKey ?? string.Empty, out lift);
                _state.RoomByKey.TryGetValue(targetRoomKey ?? string.Empty, out room);
            }

            if (lift == null || lift.Position == null || room == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to generate delivery route.", null, null);
            }

            XYZ goalPoint = ResolveRoomRoutePoint(room);
            if (goalPoint == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to generate delivery route.", null, null);
            }

            RoutePlannerInitResult initResult = RoutePlannerAutoInitService.EnsureInitialized(doc, uiDoc);
            if (initResult == null || !initResult.Success || string.IsNullOrWhiteSpace(initResult.SessionId))
            {
                string initMessage = initResult != null && !string.IsNullOrWhiteSpace(initResult.Message)
                    ? initResult.Message
                    : "Failed to generate delivery route.";
                DiagnosticRecorder.AppendDebug("[DeliveryRoute] Auto init failed. Message=" + initMessage);
                if (initResult != null && initResult.ApiUnavailable)
                {
                    LocalizedDialogService.Warning(app, initMessage, "EMSD AI Tool");
                }
                else
                {
                    LocalizedDialogService.Error(app, initMessage, "EMSD AI Tool");
                }

                return new CalculatePathExecutionResult
                {
                    Success = false,
                    Drawn = false,
                    Message = initMessage
                };
            }

            CalculatePathExecutionResult result = CalculatePathApiService.CalculateAndDraw(
                doc,
                uiDoc,
                initResult.SessionId,
                lift.Position,
                goalPoint);
            DiagnosticRecorder.AppendDebug("[DeliveryRoute] response=" + (result == null ? string.Empty : result.ResponseBody ?? string.Empty));

            if (result == null || !result.Success || !result.Drawn)
            {
                string failureMessage = result != null && !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : "Failed to generate delivery route.";
                string responseBody = result != null && result.ResponseBody != null && result.Message != null &&
                    result.Message.StartsWith("Failed to generate delivery route.", StringComparison.OrdinalIgnoreCase)
                    ? result.ResponseBody
                    : null;
                double? pathLength = result == null ? null : result.PathLengthMeters;
                return ShowDeliveryRouteFailure(app, failureMessage, responseBody, pathLength);
            }

            return result;
        }

        public static bool ExecuteBeginDeliveryRouteStartPointSelection(
            UIApplication app,
            string originalName,
            double? originalXmm,
            double? originalYmm,
            double? originalZmm)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            if (uiDoc == null || uiDoc.Document == null)
            {
                return false;
            }

            if (_deliveryRouteStartPointSelectionSession != null &&
                _deliveryRouteStartPointSelectionSession.IsActive)
            {
                EndDeliveryRouteStartPointSelection(app, false, false);
            }

            XYZ originalPoint = null;
            if (originalXmm.HasValue && originalYmm.HasValue)
            {
                originalPoint = new XYZ(
                    originalXmm.Value / 304.8,
                    originalYmm.Value / 304.8,
                    (originalZmm ?? 0.0) / 304.8);
            }

            // IMPORTANT: use the same non-blocking-friendly work-plane setup as
            // Restricted Area drawing. PickObject(PointOnElement) only accepts
            // existing Revit geometry, so blank floor areas could never be picked.
            // A horizontal work plane lets PickPoint() accept any visible location
            // in the model view while still supporting normal Revit object snaps.
            try
            {
                DrawPathObstacleCommand.PrepareDrawing(uiDoc.Document);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRouteStartPoint] Prepare work plane skipped: " + ex.Message);
            }

            DeliveryRouteStartPointSelectionSession session = new DeliveryRouteStartPointSelectionSession
            {
                IsActive = true,
                App = app,
                OriginalPoint = originalPoint,
                OriginalName = originalName ?? string.Empty
            };
            _deliveryRouteStartPointSelectionSession = session;

            DeliveryRouteStartPointSelectionBarWindow bar = new DeliveryRouteStartPointSelectionBarWindow();
            bar.AttachToRevit(app);
            bar.Show();
            session.BarWindow = bar;
            ScheduleDeliveryRouteStartPointPick();
            return true;
        }

        public static bool ExecutePickDeliveryRouteStartPoint(UIApplication app)
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            if (session == null || !session.IsActive || uiDoc == null || session.CancelRequested || session.ConfirmRequested)
            {
                return false;
            }

            try
            {
                session.IsPicking = true;

                // Do NOT use PickObject(PointOnElement) here. That API blocks the
                // user from selecting empty areas and is also more prone to leaving
                // Revit in a modal selection state when the modeless Confirm/Cancel
                // bar is clicked. This mirrors the already-proven Restricted Area
                // workflow: PickPoint on a prepared work plane accepts blank space.
                Autodesk.Revit.UI.Selection.ObjectSnapTypes snapTypes =
                    Autodesk.Revit.UI.Selection.ObjectSnapTypes.Endpoints |
                    Autodesk.Revit.UI.Selection.ObjectSnapTypes.Intersections |
                    Autodesk.Revit.UI.Selection.ObjectSnapTypes.Midpoints |
                    Autodesk.Revit.UI.Selection.ObjectSnapTypes.Nearest;

                XYZ point = uiDoc.Selection.PickPoint(
                    snapTypes,
                    "Click anywhere in the view to set the delivery route start point.");
                if (point != null)
                {
                    ClearDeliveryRouteStartPointMarker(uiDoc.Document);
                    DrawDeliveryRouteStartPointMarker(uiDoc.Document, uiDoc.ActiveView, point);
                    session.PickedPoint = point;
                    session.HasNewPoint = true;

                    try
                    {
                        uiDoc.RefreshActiveView();
                    }
                    catch
                    {
                    }

                    DiagnosticRecorder.AppendDebug(
                        "[DeliveryRouteStartPoint] Picked mm=[" +
                        (point.X * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "," +
                        (point.Y * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "," +
                        (point.Z * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "]");
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                if (!session.ConfirmRequested && !session.CancelRequested)
                {
                    session.CancelRequested = true;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRouteStartPoint] Pick failed: " + ex.Message);
                session.CancelRequested = true;
            }
            finally
            {
                session.IsPicking = false;
            }

            if (session.CancelRequested)
            {
                return ExecuteCancelDeliveryRouteStartPointSelection(app);
            }

            if (session.ConfirmRequested)
            {
                return ExecuteConfirmDeliveryRouteStartPointSelection(app);
            }

            ScheduleDeliveryRouteStartPointPick();
            return true;
        }

        public static bool ExecuteConfirmDeliveryRouteStartPointSelection(UIApplication app)
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            if (session == null || !session.IsActive || session.IsPicking)
            {
                return false;
            }

            XYZ point = session.PickedPoint ?? session.OriginalPoint;
            if (point == null)
            {
                session.ConfirmRequested = false;
                ScheduleDeliveryRouteStartPointPick();
                return false;
            }

            CloseDeliveryRouteStartPointBar(session);
            string defaultName = !string.IsNullOrWhiteSpace(session.OriginalName)
                ? session.OriginalName
                : DeliveryRoutePaneRuntime.ViewModel.GetDefaultStartPointName();

            RoomRecognitionNameEditWindow dialog = new RoomRecognitionNameEditWindow(
                "Name Location",
                defaultName,
                "Save");
            try
            {
                if (app != null && app.MainWindowHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(dialog).Owner = app.MainWindowHandle;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                // Best effort only; the dialog can still be shown without owner.
            }

            if (dialog.ShowDialog() != true)
            {
                return ExecuteCancelDeliveryRouteStartPointSelection(app);
            }

            string name = string.IsNullOrWhiteSpace(dialog.EditedName) ? defaultName : dialog.EditedName.Trim();
            DeliveryRoutePaneRuntime.ViewModel.ApplySelectedStartPoint(
                name,
                point.X * 304.8,
                point.Y * 304.8,
                point.Z * 304.8);

            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteStartPoint] Confirmed Name=" + name +
                ", mm=[" +
                (point.X * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "," +
                (point.Y * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "," +
                (point.Z * 304.8).ToString("0.###", CultureInfo.InvariantCulture) + "]");

            _deliveryRouteStartPointSelectionSession = null;
            return true;
        }

        public static bool ExecuteCancelDeliveryRouteStartPointSelection(UIApplication app)
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            if (session == null)
            {
                return true;
            }

            UIDocument uiDoc = app != null ? app.ActiveUIDocument : session.App != null ? session.App.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (session.HasNewPoint && doc != null)
            {
                ClearDeliveryRouteStartPointMarker(doc);
                if (session.OriginalPoint != null)
                {
                    DrawDeliveryRouteStartPointMarker(doc, uiDoc != null ? uiDoc.ActiveView : null, session.OriginalPoint);
                }
            }

            CloseDeliveryRouteStartPointBar(session);
            _deliveryRouteStartPointSelectionSession = null;
            return true;
        }

        public static bool ExecuteFocusDeliveryRouteStartPoint(
            UIApplication app,
            double? startPointXmm,
            double? startPointYmm,
            double? startPointZmm)
        {
            if (!startPointXmm.HasValue || !startPointYmm.HasValue)
            {
                return false;
            }

            UIDocument uiDoc = app != null ? app.ActiveUIDocument : _uiDoc;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            XYZ point = new XYZ(
                startPointXmm.Value / 304.8,
                startPointYmm.Value / 304.8,
                (startPointZmm ?? 0.0) / 304.8);

            // "Define Start Location" is a locate/confirm action only.  Never
            // start PickPoint here.  Recreate the marker from the already saved
            // coordinate so the user can visually confirm the defined start.
            ClearDeliveryRouteStartPointMarker(doc);
            ElementId markerId = DrawDeliveryRouteStartPointMarker(doc, uiDoc.ActiveView, point);
            if (markerId == null || markerId == ElementId.InvalidElementId)
            {
                return false;
            }

            try
            {
                uiDoc.RefreshActiveView();
            }
            catch
            {
            }

            // Keep the current 3D orientation but center the view around the saved
            // point, similar to Define Start Lift.  Use a modest window so the
            // surrounding room context remains visible.
            try
            {
                UIView uiView = uiDoc.GetOpenUIViews()
                    .FirstOrDefault(x => x != null && x.ViewId == uiDoc.ActiveView.Id);
                if (uiView != null)
                {
                    double halfSpanFt = UnitUtils.ConvertToInternalUnits(1800.0, UnitTypeId.Millimeters);
                    XYZ min = new XYZ(point.X - halfSpanFt, point.Y - halfSpanFt, point.Z - halfSpanFt * 0.25);
                    XYZ max = new XYZ(point.X + halfSpanFt, point.Y + halfSpanFt, point.Z + halfSpanFt * 0.25);
                    uiView.ZoomAndCenterRectangle(min, max);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRouteStartPoint] Focus zoom skipped: " + ex.Message);
            }

            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteStartPoint] Focus saved location mm=[" +
                startPointXmm.Value.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                startPointYmm.Value.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                (startPointZmm ?? 0.0).ToString("0.###", CultureInfo.InvariantCulture) + "]");
            return true;
        }

        public static bool ExecuteClearDeliveryRouteStartPointMarker(UIApplication app)
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            if (session != null && session.IsActive)
            {
                EndDeliveryRouteStartPointSelection(app, true, false);
            }

            UIDocument uiDoc = app != null ? app.ActiveUIDocument : _uiDoc;
            Document doc = uiDoc != null ? uiDoc.Document : _doc;
            return ClearDeliveryRouteStartPointMarker(doc) >= 0;
        }

        private static void EndDeliveryRouteStartPointSelection(
            UIApplication app,
            bool clearMarker,
            bool restoreOriginal)
        {
            DeliveryRouteStartPointSelectionSession session = _deliveryRouteStartPointSelectionSession;
            _deliveryRouteStartPointSelectionSession = null;
            if (session == null)
            {
                return;
            }

            UIDocument uiDoc = app != null ? app.ActiveUIDocument : session.App != null ? session.App.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (clearMarker && doc != null)
            {
                ClearDeliveryRouteStartPointMarker(doc);
            }
            if (restoreOriginal && doc != null && session.OriginalPoint != null)
            {
                DrawDeliveryRouteStartPointMarker(doc, uiDoc != null ? uiDoc.ActiveView : null, session.OriginalPoint);
            }
            CloseDeliveryRouteStartPointBar(session);
        }

        private static void CloseDeliveryRouteStartPointBar(DeliveryRouteStartPointSelectionSession session)
        {
            DeliveryRouteStartPointSelectionBarWindow bar = session != null ? session.BarWindow : null;
            if (bar == null)
            {
                return;
            }

            try
            {
                // Same close pattern as Restricted Area.  Confirm/Cancel is
                // resolved only after PickPoint has returned, then the modeless
                // bar is closed on its dispatcher without fighting the active
                // Revit selection call.
                bar.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (bar.IsVisible)
                    {
                        bar.Close();
                    }
                }));
            }
            catch
            {
            }
        }

        private static void ScheduleDeliveryRouteStartPointPick()
        {
            Application application = Application.Current;
            if (application != null && application.Dispatcher != null)
            {
                application.Dispatcher.BeginInvoke(new Action(delegate
                {
                    EnqueueDeliveryRouteStartPointRequest(RoomRecognitionPaneRequestType.PickDeliveryRouteStartPoint);
                }));
                return;
            }

            EnqueueDeliveryRouteStartPointRequest(RoomRecognitionPaneRequestType.PickDeliveryRouteStartPoint);
        }

        private static void EnqueueDeliveryRouteStartPointRequest(RoomRecognitionPaneRequestType type)
        {
            if (_externalEvent == null || _handler == null)
            {
                InitializeExternalEvent();
            }
            if (_externalEvent == null || _handler == null)
            {
                return;
            }

            _handler.Enqueue(new RoomRecognitionPaneRequest { Type = type });
            _externalEvent.Raise();
        }

        // Keep the Delivery Route start-point picker on exactly the same
        // interruption mechanism as Restricted Area drawing.  PickPoint() blocks
        // the current Revit ExternalEvent, so Confirm/Cancel only need to set a
        // session flag and post ESC to Revit's main window.  Once PickPoint throws
        // OperationCanceledException, the SAME ExternalEvent continues and consumes
        // ConfirmRequested/CancelRequested immediately.
        private const int DeliveryRouteWmKeyDown = 0x0100;
        private const int DeliveryRouteWmKeyUp = 0x0101;
        private const int DeliveryRouteVkEscape = 0x1B;

        // IMPORTANT: keep the native entry-point names identical to the proven
        // Restricted Area implementation.  The previous declarations were named
        // DeliveryRoutePostMessage / DeliveryRouteSetForegroundWindow without an
        // EntryPoint override, so P/Invoke tried to resolve those non-existent
        // exports from user32.dll.  The exception was caught below, which made the
        // UI look as if ESC had been posted even though PickPoint was never
        // interrupted.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void InterruptDeliveryRouteStartPointPick(DeliveryRouteStartPointSelectionSession session)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                IntPtr mainWindowHandle = session.App != null
                    ? session.App.MainWindowHandle
                    : IntPtr.Zero;

                if (mainWindowHandle == IntPtr.Zero)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DeliveryRouteStartPoint] Cannot interrupt PickPoint: Revit MainWindowHandle is zero.");
                    return;
                }

                // IMPORTANT: this intentionally mirrors
                // PathObstacleRuntime.InterruptActivePick().  Do not use Task.Run,
                // delayed keybd_event, WS_EX_NOACTIVATE, or a second ExternalEvent
                // here; those paths were the source of the delayed Confirm/Cancel
                // behaviour seen in testing.
                bool foregroundOk = SetForegroundWindow(mainWindowHandle);
                bool keyDownOk = PostMessage(
                    mainWindowHandle,
                    DeliveryRouteWmKeyDown,
                    new IntPtr(DeliveryRouteVkEscape),
                    IntPtr.Zero);
                bool keyUpOk = PostMessage(
                    mainWindowHandle,
                    DeliveryRouteWmKeyUp,
                    new IntPtr(DeliveryRouteVkEscape),
                    IntPtr.Zero);

                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRouteStartPoint] PickPoint interrupt posted using Restricted Area mechanism. " +
                    "ForegroundOk=" + foregroundOk +
                    ", KeyDownOk=" + keyDownOk +
                    ", KeyUpOk=" + keyUpOk +
                    ", ConfirmRequested=" + session.ConfirmRequested +
                    ", CancelRequested=" + session.CancelRequested);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRouteStartPoint] Failed to interrupt PickPoint: " + ex.Message);
            }
        }

        private static int ClearDeliveryRouteStartPointMarker(Document doc)
        {
            if (doc == null)
            {
                return 0;
            }

            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name) &&
                            x.Name.StartsWith(DeliveryRouteStartPointMarkerNamePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .ToList();
            if (ids.Count == 0)
            {
                return 0;
            }

            using (Transaction tx = new Transaction(doc, "Clear Delivery Route Start Point Marker"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }
            return ids.Count;
        }

        private static ElementId DrawDeliveryRouteStartPointMarker(Document doc, View activeView, XYZ point)
        {
            if (doc == null || point == null)
            {
                return ElementId.InvalidElementId;
            }

            ElementId createdId = ElementId.InvalidElementId;
            using (Transaction tx = new Transaction(doc, "Draw Delivery Route Start Point Marker"))
            {
                tx.Start();
                ElementId materialId = GetOrCreateAhuPlacementPointMaterialId(doc);
                Solid stem = BuildAhuPlacementPointCylinder(
                    point,
                    DeliveryRouteStartPointStemRadiusMm,
                    DeliveryRouteStartPointStemHeightMm,
                    materialId);
                XYZ capOrigin = new XYZ(
                    point.X,
                    point.Y,
                    point.Z + DeliveryRouteStartPointStemHeightMm / 304.8);
                Solid cap = BuildAhuPlacementPointCylinder(
                    capOrigin,
                    DeliveryRouteStartPointCapRadiusMm,
                    DeliveryRouteStartPointCapThicknessMm,
                    materialId);

                List<GeometryObject> geometry = new List<GeometryObject>();
                if (stem != null) geometry.Add(stem);
                if (cap != null) geometry.Add(cap);
                if (geometry.Count == 0)
                {
                    tx.RollBack();
                    return ElementId.InvalidElementId;
                }

                DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.ApplicationId = "CadToRevit.DeliveryRouteStartPoint";
                shape.ApplicationDataId = "DELIVERY_ROUTE_START_POINT";
                shape.Name = DeliveryRouteStartPointMarkerNamePrefix + "CURRENT";
                shape.SetShape(geometry);
                createdId = shape.Id;
                ApplyAhuPlacementPointViewOverride(activeView, createdId);
                tx.Commit();
            }
            return createdId;
        }

        public static DeliveryRoutePreparationResult ExecutePrepareDeliveryRoute(
            UIApplication app,
            string startLocationType,
            string startLiftKey,
            double? startPointXmm,
            double? startPointYmm,
            double? startPointZmm,
            string targetRoomKey)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return CreateDeliveryRoutePreparationFailure("Failed to generate delivery route.");
            }

            bool isPointStart = string.Equals(startLocationType, "Point", StringComparison.OrdinalIgnoreCase);
            LiftRecognitionRecord lift = null;
            RoomSemanticRecord room = null;
            lock (SyncRoot)
            {
                if (_state == null)
                {
                    return CreateDeliveryRoutePreparationFailure("Failed to generate delivery route.");
                }

                if (!isPointStart)
                {
                    _state.LiftByKey.TryGetValue(startLiftKey ?? string.Empty, out lift);
                }
                _state.RoomByKey.TryGetValue(targetRoomKey ?? string.Empty, out room);
            }

            XYZ startPoint = null;
            if (isPointStart)
            {
                if (!startPointXmm.HasValue || !startPointYmm.HasValue)
                {
                    return CreateDeliveryRoutePreparationFailure("Please set a start location.");
                }

                startPoint = new XYZ(
                    startPointXmm.Value / 304.8,
                    startPointYmm.Value / 304.8,
                    (startPointZmm ?? 0.0) / 304.8);
            }
            else
            {
                startPoint = lift != null ? lift.Position : null;
            }

            if (startPoint == null || room == null)
            {
                return CreateDeliveryRoutePreparationFailure("Failed to generate delivery route.");
            }

            XYZ goalPoint = ResolveRoomRoutePoint(room);
            if (goalPoint == null)
            {
                return CreateDeliveryRoutePreparationFailure("Failed to generate delivery route.");
            }

            // The red point marker is only a UI aid. Remove it before route-planner
            // initialization so it can never be exported as an obstacle.
            ClearDeliveryRouteStartPointMarker(doc);

            RoutePlannerInitResult initResult = RoutePlannerAutoInitService.EnsureInitialized(doc, uiDoc);
            if (initResult == null || !initResult.Success || string.IsNullOrWhiteSpace(initResult.SessionId))
            {
                string initMessage = initResult != null && !string.IsNullOrWhiteSpace(initResult.Message)
                    ? initResult.Message
                    : "Failed to generate delivery route.";
                DiagnosticRecorder.AppendDebug("[DeliveryRoute] Auto init failed. Message=" + initMessage);
                return CreateDeliveryRoutePreparationFailure(initMessage);
            }

            lock (SyncRoot)
            {
                _lastDeliveryRouteRequestStartPoint = startPoint;
                _lastDeliveryRouteRequestGoalPoint = goalPoint;
            }

            DiagnosticRecorder.AppendDebug(
                "[DeliveryRoute] requestStartPointMm=[" +
                (startPoint.X * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                (startPoint.Y * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "], requestGoalPointMm=[" +
                (goalPoint.X * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                (goalPoint.Y * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "]");

            using (Transaction tx = new Transaction(doc, "Clear Delivery Route Path"))
            {
                tx.Start();
                Path3DVisualizationService.Clear(doc);
                View3D activeView = uiDoc.ActiveView as View3D;
                if (activeView != null)
                {
                    Path3DVisualizationService.DrawRequestPointMarkers(doc, activeView, startPoint, goalPoint);
                }
                tx.Commit();
            }

            List<RestrictedAreaRequestItem> restrictedAreas =
                BuildDeliveryRouteRestrictedAreas(doc);

            return new DeliveryRoutePreparationResult
            {
                Success = true,
                Message = string.Empty,
                SessionId = initResult.SessionId,
                StartXmm = startPoint.X * 304.8,
                StartYmm = startPoint.Y * 304.8,
                GoalXmm = goalPoint.X * 304.8,
                GoalYmm = goalPoint.Y * 304.8,
                RestrictedAreas = restrictedAreas
            };
        }

        private static List<RestrictedAreaRequestItem> BuildDeliveryRouteRestrictedAreas(Document doc)
        {
            List<RestrictedAreaRequestItem> result = new List<RestrictedAreaRequestItem>();
            if (doc == null)
            {
                return result;
            }

            IList<CadToRevit.Models.PathObstacleRecord> records =
                PathObstacleStoreService.Load(doc);

            foreach (CadToRevit.Models.PathObstacleRecord record in
                records ?? new List<CadToRevit.Models.PathObstacleRecord>())
            {
                Element element = PathObstacleStoreService.FindElement(doc, record);
                BoundingBoxXYZ box = element != null ? element.get_BoundingBox(null) : null;
                if (box == null || box.Min == null || box.Max == null)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DeliveryRouteRestrictedArea] skipped name=" +
                        (record != null ? record.Name ?? string.Empty : string.Empty) +
                        ", reason=BoundingBoxUnavailable");
                    continue;
                }

                Transform transform = box.Transform ?? Transform.Identity;
                XYZ[] corners =
                {
                    transform.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
                    transform.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
                    transform.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
                    transform.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z))
                };

                double minXmm = corners.Min(point => point.X) * 304.8;
                double minYmm = corners.Min(point => point.Y) * 304.8;
                double maxXmm = corners.Max(point => point.X) * 304.8;
                double maxYmm = corners.Max(point => point.Y) * 304.8;

                string name = record != null && !string.IsNullOrWhiteSpace(record.Name)
                    ? record.Name.Trim()
                    : "Restricted Area";

                result.Add(new RestrictedAreaRequestItem
                {
                    Name = name,
                    Bounds = new[] { minXmm, minYmm, maxXmm, maxYmm }
                });

                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRouteRestrictedArea] name=" + name +
                    ", boundsMm=[" +
                    minXmm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    minYmm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    maxXmm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    maxYmm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "]");
            }

            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteRestrictedArea] totalCount=" +
                result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return result;
        }

        private const string AhuPlacementPointMaterialName = "EMSD_AHU_PLACEMENT_POINT_MAT";
        private const double AhuPlacementPointBaseOffsetMm = 100.0;
        private const double AhuPlacementPointStemRadiusMm = 90.0;
        private const double AhuPlacementPointStemHeightMm = 2600.0;
        private const double AhuPlacementPointCapRadiusMm = 260.0;
        private const double AhuPlacementPointCapThicknessMm = 80.0;

        private static readonly Color AhuPlacementPointColor = new Color(255, 45, 45);

        private static int ClearAhuPlacementPointMarker(Document doc)
        {
            if (doc == null)
            {
                return 0;
            }

            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    x.Name.StartsWith(
                        Room3DVisualizationConstants.AhuPlacementPointMarkerNamePrefix,
                        StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return 0;
            }

            using (Transaction tx = new Transaction(doc, "Clear AHU Placement Point Marker"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug(
                "[AhuPlacementPointMarker] Cleared count=" +
                ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return ids.Count;
        }

        private static ElementId DrawAhuPlacementPointMarker(
            Document doc,
            View activeView,
            RoomSemanticRecord room,
            XYZ placementPoint)
        {
            if (doc == null || placementPoint == null)
            {
                return ElementId.InvalidElementId;
            }

            ElementId createdId = ElementId.InvalidElementId;

            using (Transaction tx = new Transaction(doc, "Draw AHU Placement Point Marker"))
            {
                tx.Start();

                ElementId materialId = GetOrCreateAhuPlacementPointMaterialId(doc);
                double baseZ = ResolveAhuPlacementPointBaseZ(room, placementPoint) +
                               (AhuPlacementPointBaseOffsetMm / 304.8);
                XYZ baseCenter = new XYZ(placementPoint.X, placementPoint.Y, baseZ);

                Solid stem = BuildAhuPlacementPointCylinder(
                    baseCenter,
                    AhuPlacementPointStemRadiusMm,
                    AhuPlacementPointStemHeightMm,
                    materialId);

                XYZ capCenter = new XYZ(
                    placementPoint.X,
                    placementPoint.Y,
                    baseZ + (AhuPlacementPointStemHeightMm / 304.8));

                Solid cap = BuildAhuPlacementPointCylinder(
                    capCenter,
                    AhuPlacementPointCapRadiusMm,
                    AhuPlacementPointCapThicknessMm,
                    materialId);

                List<GeometryObject> geometry = new List<GeometryObject>();
                if (stem != null && stem.Faces != null && stem.Faces.Size > 0)
                {
                    geometry.Add(stem);
                }

                if (cap != null && cap.Faces != null && cap.Faces.Size > 0)
                {
                    geometry.Add(cap);
                }

                if (geometry.Count == 0)
                {
                    tx.RollBack();
                    DiagnosticRecorder.AppendDebug(
                        "[AhuPlacementPointMarker] Draw skipped. No marker geometry was created.");
                    return ElementId.InvalidElementId;
                }

                DirectShape shape = DirectShape.CreateElement(
                    doc,
                    new ElementId(BuiltInCategory.OST_GenericModel));

                string roomKey = room != null ? (room.Key ?? string.Empty) : string.Empty;
                shape.ApplicationId = "CadToRevit.AhuPlacementPoint";
                shape.ApplicationDataId = "AHU_PLACEMENT_POINT::" + roomKey;
                shape.Name =
                    Room3DVisualizationConstants.AhuPlacementPointMarkerNamePrefix +
                    SanitizeAhuPlacementPointMarkerName(roomKey);
                shape.SetShape(geometry);
                createdId = shape.Id;

                ApplyAhuPlacementPointViewOverride(activeView, shape.Id);

                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug(
                "[AhuPlacementPointMarker] Drawn ElementId=" +
                (createdId == null
                    ? "-"
                    : createdId.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
                ", RoomKey=" + (room != null ? (room.Key ?? string.Empty) : string.Empty) +
                ", PlacementFt=[" +
                placementPoint.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                placementPoint.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                placementPoint.Z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "], PlacementMm=[" +
                (placementPoint.X * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                (placementPoint.Y * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "]");

            return createdId;
        }

        private static ElementId GetOrCreateAhuPlacementPointMaterialId(Document doc)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x =>
                    x != null &&
                    string.Equals(
                        x.Name,
                        AhuPlacementPointMaterialName,
                        StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, AhuPlacementPointMaterialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            material.Color = AhuPlacementPointColor;
            material.Transparency = 0;
            return material.Id;
        }

        private static Solid BuildAhuPlacementPointCylinder(
            XYZ origin,
            double radiusMm,
            double heightMm,
            ElementId materialId)
        {
            if (origin == null || radiusMm <= 0.0 || heightMm <= 0.0)
            {
                return null;
            }

            double radiusFt = radiusMm / 304.8;
            double heightFt = heightMm / 304.8;

            CurveLoop loop = new CurveLoop();
            loop.Append(Arc.Create(
                origin,
                radiusFt,
                0.0,
                Math.PI,
                XYZ.BasisX,
                XYZ.BasisY));
            loop.Append(Arc.Create(
                origin,
                radiusFt,
                Math.PI,
                Math.PI * 2.0,
                XYZ.BasisX,
                XYZ.BasisY));

            SolidOptions options = new SolidOptions(
                materialId,
                ElementId.InvalidElementId);

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                heightFt,
                options);
        }

        private static double ResolveAhuPlacementPointBaseZ(
            RoomSemanticRecord room,
            XYZ placementPoint)
        {
            if (room != null && room.BBox != null && room.BBox.Min != null)
            {
                return room.BBox.Min.Z;
            }

            if (room != null && room.LoopPoints != null)
            {
                List<XYZ> points = room.LoopPoints
                    .Where(x => x != null)
                    .ToList();
                if (points.Count > 0)
                {
                    return points.Min(x => x.Z);
                }
            }

            return placementPoint != null ? placementPoint.Z : 0.0;
        }

        private static void ApplyAhuPlacementPointViewOverride(
            View view,
            ElementId elementId)
        {
            if (view == null ||
                elementId == null ||
                elementId == ElementId.InvalidElementId)
            {
                return;
            }

            try
            {
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(AhuPlacementPointColor);

                FillPatternElement solidFill = new FilteredElementCollector(view.Document)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(x =>
                        x != null &&
                        x.GetFillPattern() != null &&
                        x.GetFillPattern().IsSolidFill);

                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternVisible(true);
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(AhuPlacementPointColor);
                }

                ogs.SetSurfaceTransparency(0);
                view.SetElementOverrides(elementId, ogs);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementPointMarker] View override skipped. Error=" +
                    ex.Message);
            }
        }

        private static string SanitizeAhuPlacementPointMarkerName(string value)
        {
            string result = value ?? string.Empty;
            char[] invalidChars =
            {
                '|', ':', ';', '<', '>', '?',
                '[', ']', '{', '}', '/', '\\'
            };

            foreach (char c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            return result.Trim();
        }

        public static AhuPlacementValidationPreparationResult ExecutePrepareAhuPlacementValidation(
            UIApplication app,
            string roomKey)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return CreateAhuPlacementValidationPreparationFailure(
                    "No active Revit document is available.");
            }

            // The AHU placement marker is no longer shown to users. Clear any
            // DirectShape marker left by an older build before preparing the new
            // /api/check_room_fit request.
            try
            {
                ClearAhuPlacementPointMarker(doc);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementPointMarker] Clear before prepare skipped. Error=" +
                    ex.Message);
            }

            RoomSemanticRecord room = null;
            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.RoomByKey.TryGetValue(roomKey ?? string.Empty, out room);
                }
            }

            if (room == null)
            {
                return CreateAhuPlacementValidationPreparationFailure(
                    "The selected room could not be resolved.");
            }

            XYZ placementPoint;
            string placementSource;
            if (!RoomCustomFamilyPlacementService.TryResolveValidationPlacementPoint(
                    doc,
                    room,
                    out placementPoint,
                    out placementSource) ||
                placementPoint == null)
            {
                return CreateAhuPlacementValidationPreparationFailure(
                    "The selected room placement point could not be resolved.");
            }

            RoutePlannerInitResult initResult = RoutePlannerAutoInitService.EnsureInitialized(doc, uiDoc);
            if (initResult == null ||
                !initResult.Success ||
                string.IsNullOrWhiteSpace(initResult.SessionId))
            {
                string message = initResult != null && !string.IsNullOrWhiteSpace(initResult.Message)
                    ? initResult.Message
                    : "Failed to initialize the Route API session.";
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFit] Auto init failed. Message=" + message);
                return CreateAhuPlacementValidationPreparationFailure(message);
            }

            // Do not draw the AHU placement-point diagnostic marker in the model.
            // The placement point is still resolved and sent to /api/check_room_fit,
            // but the old red 3D DirectShape marker must remain hidden from users.
            // Any marker left by an older build has already been removed above by
            // ClearAhuPlacementPointMarker(doc).
            DiagnosticRecorder.AppendDebug(
                "[AhuPlacementPointMarker] Draw disabled. Placement point is API-only.");

            double xMm = placementPoint.X * 304.8;
            double yMm = placementPoint.Y * 304.8;

            DiagnosticRecorder.AppendDebug(
                "[AhuRoomFit] prepared roomKey=" + (roomKey ?? string.Empty) +
                ", placementSource=" + (placementSource ?? string.Empty) +
                ", pointInRoomMm=[" +
                xMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                yMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "], placementPointMm=[" +
                xMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                yMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "], sessionId=" + initResult.SessionId);

            return new AhuPlacementValidationPreparationResult
            {
                Success = true,
                Message = string.Empty,
                SessionId = initResult.SessionId,
                RoomKey = roomKey ?? string.Empty,
                PlacementXmm = xMm,
                PlacementYmm = yMm
            };
        }

        public static bool ExecuteClearAhuPlacementPointMarker(UIApplication app)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            try
            {
                ClearAhuPlacementPointMarker(doc);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementPointMarker] Clear request failed. Error=" +
                    ex.Message);
                return false;
            }
        }

        public static CalculatePathExecutionResult ExecuteDrawDeliveryRoutePath(
            UIApplication app,
            string responseBody)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to generate delivery route.", responseBody, null);
            }

            CalculatePathExecutionResult result = CalculatePathApiService.DrawPathInActiveViewFromResponse(doc, uiDoc, responseBody);
            if (result == null || !result.Success || !result.Drawn)
            {
                string failureMessage = result != null && !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : "Failed to generate delivery route.";
                string body = result == null ? responseBody : result.ResponseBody;
                double? pathLength = result == null ? null : result.PathLengthMeters;
                return ShowDeliveryRouteFailure(app, failureMessage, body, pathLength);
            }

            DrawLastDeliveryRouteRequestPointMarkers(doc, uiDoc);
            return result;
        }

        private static void DrawLastDeliveryRouteRequestPointMarkers(Document doc, UIDocument uiDoc)
        {
            if (doc == null || uiDoc == null)
            {
                return;
            }

            XYZ startPoint;
            XYZ goalPoint;
            lock (SyncRoot)
            {
                startPoint = _lastDeliveryRouteRequestStartPoint;
                goalPoint = _lastDeliveryRouteRequestGoalPoint;
            }

            if (startPoint == null || goalPoint == null)
            {
                return;
            }

            View3D activeView = uiDoc.ActiveView as View3D;
            if (activeView == null)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRouteRequestPoint] Skip markers after draw: active view is not 3D.");
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Draw Delivery Route Request Points"))
                {
                    tx.Start();
                    Path3DVisualizationService.DrawRequestPointMarkers(doc, activeView, startPoint, goalPoint);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRouteRequestPoint] Failed to draw markers after route draw. " + ex.Message);
            }
        }

        public static bool ExecuteClearDeliveryRoutePath(UIApplication app)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Clear Delivery Route Path"))
                {
                    tx.Start();
                    Path3DVisualizationService.Clear(doc);
                    tx.Commit();
                }

                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoute] Clear path failed: " + ex);
                return false;
            }
        }


        public static CalculatePathExecutionResult ExecuteDrawDeliveryRouteComparison(
            UIApplication app,
            IList<string> routeIds)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to draw route comparison.", null, null);
            }

            List<string> normalizedIds = (routeIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (normalizedIds.Count == 0)
            {
                return ShowDeliveryRouteFailure(
                    app,
                    "Please select at least one delivery route to compare.",
                    null,
                    null);
            }

            DeliveryRouteStorePayload payload = DeliveryRouteStorageService.Load(doc);
            List<string> responseBodies = new List<string>();
            List<string> pathIds = new List<string>();

            foreach (string routeId in normalizedIds)
            {
                DeliveryRouteRecordDto route = payload.Routes.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(x.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
                if (route == null ||
                    !route.IsSuccess ||
                    string.IsNullOrWhiteSpace(route.ResponseBody))
                {
                    continue;
                }

                responseBodies.Add(route.ResponseBody);
                pathIds.Add("DELIVERY_ROUTE_COMPARE_" + SanitizePathId(route.RouteId));
            }

            CalculatePathExecutionResult result =
                CalculatePathApiService.DrawMultipleSavedPathsInActiveViewFromResponses(
                    doc,
                    uiDoc,
                    responseBodies,
                    pathIds);

            if (result == null || !result.Success || !result.Drawn)
            {
                string failureMessage =
                    result != null && !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "Failed to draw route comparison.";
                return ShowDeliveryRouteFailure(
                    app,
                    failureMessage,
                    result != null ? result.ResponseBody : null,
                    result != null ? result.PathLengthMeters : null);
            }

            return result;
        }


        public static CalculatePathExecutionResult ExecuteDrawLayoutPlanRouteComparison(UIApplication app, IList<string> layoutIds)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return ShowDeliveryRouteFailure(app, "Failed to draw route comparison.", null, null);
            }

            List<string> normalizedIds = (layoutIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedIds.Count == 0)
            {
                return ShowDeliveryRouteFailure(app, "Please select at least one layout plan to compare.", null, null);
            }

            RoomLayoutPlanStorePayload payload = RoomLayoutPlanStorageService.Load(doc);
            List<string> responseBodies = new List<string>();
            List<string> pathIds = new List<string>();

            foreach (string layoutId in normalizedIds)
            {
                RoomLayoutPlanDto plan = payload.Plans.FirstOrDefault(x =>
                    x != null && string.Equals(x.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
                if (plan == null || plan.DeliveryRoute == null || !plan.DeliveryRoute.HasRoute ||
                    string.IsNullOrWhiteSpace(plan.DeliveryRoute.ResponseBody))
                {
                    continue;
                }

                responseBodies.Add(plan.DeliveryRoute.ResponseBody);
                pathIds.Add("LAYOUT_ROUTE_COMPARE_" + SanitizePathId(plan.LayoutId));
            }

            CalculatePathExecutionResult result = CalculatePathApiService.DrawMultipleSavedPathsInActiveViewFromResponses(
                doc,
                uiDoc,
                responseBodies,
                pathIds);
            if (result == null || !result.Success || !result.Drawn)
            {
                string failureMessage = result != null && !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : "Failed to draw route comparison.";
                return ShowDeliveryRouteFailure(app, failureMessage, result != null ? result.ResponseBody : null, result != null ? result.PathLengthMeters : null);
            }

            return result;
        }

        private static string SanitizePathId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Guid.NewGuid().ToString("N");
            }

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char ch = chars[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        public static bool ExecuteClearRoomEquipmentLayout(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            List<ElementId> idsToDelete = new List<ElementId>();
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId equipmentId) &&
                    equipmentId != null &&
                    equipmentId != ElementId.InvalidElementId)
                {
                    idsToDelete.Add(equipmentId);
                }

                AppendStoredElementIds(idsToDelete, _state.RoomGeneratedDuctElementIdsByRoomKey, roomKey);
                AppendStoredElementIds(idsToDelete, _state.RoomGeneratedPipeElementIdsByRoomKey, roomKey);
            }

            idsToDelete.AddRange(RoomCustomFamilyPlacementService.FindManagedInstances(doc, roomKey).Select(x => x.Id));
            idsToDelete.AddRange(CollectStoredLayoutGeneratedElementIds(doc, roomKey));

            List<ElementId> validIds = idsToDelete
                .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                .Distinct(new ElementIdValueComparer())
                .ToList();

            if (validIds.Count > 0)
            {
                using (Transaction tx = new Transaction(doc, "Clear Room Equipment Layout"))
                {
                    tx.Start();
                    try
                    {
                        doc.Delete(validIds);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted())
                        {
                            tx.RollBack();
                        }

                        DiagnosticRecorder.AppendDebug("[RoomEquipmentLayout] Clear failed. RoomKey=" + roomKey + ", Error=" + ex);
                        UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", ex.Message);
                        return false;
                    }
                }
            }

            lock (SyncRoot)
            {
                _state.RoomCustomFamilyKeyByRoomKey.Remove(roomKey);
                _state.RoomCustomFamilyInstanceIdByRoomKey.Remove(roomKey);
                _state.RoomGeneratedDuctElementIdsByRoomKey.Remove(roomKey);
                _state.RoomGeneratedPipeElementIdsByRoomKey.Remove(roomKey);
                _state.PipeWallElementIdByRoomKey.Remove(roomKey);
                _state.PipeWallPointByRoomKey.Remove(roomKey);
                _state.PipeWallDisplayNameByRoomKey.Remove(roomKey);
                _state.DuctWallElementIdByRoomKey.Remove(roomKey);
                _state.DuctWallPointByRoomKey.Remove(roomKey);
                _state.DuctWallDisplayNameByRoomKey.Remove(roomKey);
            }

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.HighlightedFamilyKey = string.Empty;
            });

            if (validIds.Count > 0)
            {
                RoutePlannerSessionCacheService.MarkDirty(doc, "Room equipment layout was cleared.");
            }

            DiagnosticRecorder.AppendDebug("[RoomEquipmentLayout] Cleared RoomKey=" + roomKey + ", DeletedCount=" + validIds.Count.ToString());
            return true;
        }


        private static bool ExecuteClearLayoutPlanVisualsBeforeDetail(UIApplication app, RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return false;
            }

            bool pathClearOk = ExecuteClearDeliveryRoutePath(app);
            if (!pathClearOk)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomLayoutPlan] Detail activation path clear failed. LayoutId=" +
                    (plan.LayoutId ?? string.Empty));
            }

            bool equipmentClearOk = ExecuteClearRoomEquipmentLayout(app, plan.RoomKey);
            if (!equipmentClearOk)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomLayoutPlan] Detail activation equipment clear failed. LayoutId=" +
                    (plan.LayoutId ?? string.Empty) +
                    ", RoomKey=" +
                    (plan.RoomKey ?? string.Empty));
                return false;
            }

            return true;
        }

        private static List<ElementId> CollectStoredLayoutGeneratedElementIds(Document doc, string roomKey)
        {
            List<ElementId> ids = new List<ElementId>();
            if (doc == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return ids;
            }

            try
            {
                RoomLayoutPlanStorePayload payload = RoomLayoutPlanStorageService.Load(doc);
                if (payload == null || payload.Plans == null)
                {
                    return ids;
                }

                foreach (RoomLayoutPlanDto plan in payload.Plans)
                {
                    if (plan == null || !string.Equals(plan.RoomKey ?? string.Empty, roomKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppendElementRef(ids, doc, plan.ActiveGeneratedElements != null ? plan.ActiveGeneratedElements.EquipmentInstance : null);

                    if (plan.ActiveGeneratedElements != null && plan.ActiveGeneratedElements.DuctElements != null)
                    {
                        foreach (LayoutElementRefDto elementRef in plan.ActiveGeneratedElements.DuctElements)
                        {
                            AppendElementRef(ids, doc, elementRef);
                        }
                    }

                    if (plan.ActiveGeneratedElements != null && plan.ActiveGeneratedElements.PipeElements != null)
                    {
                        foreach (LayoutElementRefDto elementRef in plan.ActiveGeneratedElements.PipeElements)
                        {
                            AppendElementRef(ids, doc, elementRef);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomEquipmentLayout] Collect stored generated element ids failed. RoomKey=" +
                    (roomKey ?? string.Empty) +
                    ", Error=" +
                    ex);
            }

            return ids;
        }

        private static void AppendElementRef(List<ElementId> ids, Document doc, LayoutElementRefDto elementRef)
        {
            if (ids == null || doc == null || elementRef == null)
            {
                return;
            }

            ElementId id = ElementId.InvalidElementId;
            if (elementRef.ElementId > 0)
            {
                ElementId byIntId = new ElementId(elementRef.ElementId);
                if (doc.GetElement(byIntId) != null)
                {
                    id = byIntId;
                }
            }

            if ((id == null || id == ElementId.InvalidElementId) && !string.IsNullOrWhiteSpace(elementRef.UniqueId))
            {
                Element element = doc.GetElement(elementRef.UniqueId);
                if (element != null)
                {
                    id = element.Id;
                }
            }

            if (id != null && id != ElementId.InvalidElementId)
            {
                ids.Add(id);
            }
        }

        public static void RefreshLayoutPlansFromDocument(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            RoomLayoutPlanStorePayload payload = RoomLayoutPlanStorageService.Load(doc);
            ConnectivitySizeOptionsPayload sizePayload = ConnectivitySizeOptionsStorageService.Load(doc);
            ExecuteOnUiThread(() =>
            {
                DetailViewModel.SetConnectivitySizeOptions(sizePayload);
                DetailViewModel.SetLayoutPlans(payload.Plans, payload.ActiveLayoutIdByRoomKey);
            });
        }

        public static void PrepareLayoutPlansOverview(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            // Keep the active document available even when Layout Plans is opened without first
            // opening Room & Lift. Cancel rollback uses this document to resolve the AHU that was
            // last submitted for the selected room.
            lock (SyncRoot)
            {
                _doc = doc;
            }

            RefreshLayoutPlansFromDocument(doc);

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.PrepareLayoutPlansOverview();
            });
        }

        private static void AppendStoredElementIds(
            List<ElementId> target,
            Dictionary<string, List<ElementId>> source,
            string roomKey)
        {
            if (target == null || source == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            if (!source.TryGetValue(roomKey, out List<ElementId> ids) || ids == null)
            {
                return;
            }

            foreach (ElementId id in ids)
            {
                if (id != null && id != ElementId.InvalidElementId)
                {
                    target.Add(id);
                }
            }
        }

        private static void StoreGeneratedElementIds(
            string roomKey,
            Dictionary<string, List<ElementId>> target,
            IEnumerable<ElementId> ids)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || target == null || ids == null)
            {
                return;
            }

            List<ElementId> validIds = ids
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct(new ElementIdValueComparer())
                .ToList();
            if (validIds.Count == 0)
            {
                return;
            }

            if (!target.TryGetValue(roomKey, out List<ElementId> stored) || stored == null)
            {
                stored = new List<ElementId>();
                target[roomKey] = stored;
            }

            foreach (ElementId id in validIds)
            {
                if (!stored.Any(x => x != null && x.IntegerValue == id.IntegerValue))
                {
                    stored.Add(id);
                }
            }
        }

        private sealed class ElementIdValueComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.IntegerValue == y.IntegerValue;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.IntegerValue.GetHashCode();
            }
        }

        public static bool ExecuteSetRoomCustomFamily(
            UIApplication app,
            string roomKey,
            string familyKey,
            string familyPath)
        {
            return ExecuteSetRoomCustomFamily(
                app,
                roomKey,
                familyKey,
                familyPath,
                false,
                0,
                0,
                false,
                0);
        }

        public static bool ExecuteSetRoomCustomFamily(
            UIApplication app,
            string roomKey,
            string familyKey,
            string familyPath,
            bool useCustomPlacementPoint,
            double placementXmm,
            double placementYmm)
        {
            return ExecuteSetRoomCustomFamily(
                app,
                roomKey,
                familyKey,
                familyPath,
                useCustomPlacementPoint,
                placementXmm,
                placementYmm,
                false,
                0);
        }

        public static bool ExecuteSetRoomCustomFamily(
            UIApplication app,
            string roomKey,
            string familyKey,
            string familyPath,
            bool useCustomPlacementPoint,
            double placementXmm,
            double placementYmm,
            bool useCustomOrientation,
            double orientationDeg,
            bool placeBuiltInPipeAssembly = true)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || string.IsNullOrWhiteSpace(familyKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                DiagnosticRecorder.AppendDebug("[RoomCustomFamily] Failed: active document missing.");
                UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.SetFailed");
                return false;
            }

            RoomSemanticRecord room;
            lock (SyncRoot)
            {
                if (!_state.RoomByKey.TryGetValue(roomKey, out room) || room == null)
                {
                    DiagnosticRecorder.AppendDebug("[RoomCustomFamily] Failed: room not found, RoomKey=" + roomKey);
                    UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.SetFailed");
                    return false;
                }
            }

            RoomCustomFamilyOption option = RoomCustomFamilyCatalogService.GetOption(familyKey);
            if (option == null)
            {
                DiagnosticRecorder.AppendDebug("[RoomCustomFamily] Failed: family option missing, RoomKey=" + roomKey + ", FamilyKey=" + familyKey);
                UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.SetFailed");
                return false;
            }

            string filePath = string.IsNullOrWhiteSpace(familyPath) ? option.FullPath : familyPath;
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomCustomFamily] Failed: family file missing, RoomKey=" + roomKey +
                    ", FamilyKey=" + familyKey +
                    ", Path=" + (filePath ?? string.Empty));
                UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.FamilyFileMissing", option.FileName ?? familyKey);
                return false;
            }

            XYZ placementPointOverride = null;
            if (useCustomPlacementPoint)
            {
                placementPointOverride = new XYZ(
                    placementXmm / 304.8,
                    placementYmm / 304.8,
                    0);
            }

            double? orientationDegOverride = useCustomOrientation
                ? (double?)orientationDeg
                : null;

            RoomCustomFamilyPlacementService.PlacementResult placementResult =
                RoomCustomFamilyPlacementService.PlaceOrReplace(
                    doc,
                    room,
                    option,
                    placementPointOverride,
                    orientationDegOverride);

            if (placementResult != null && placementResult.Succeeded)
            {
                lock (SyncRoot)
                {
                    _state.RoomCustomFamilyKeyByRoomKey[roomKey] = familyKey;
                    _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = placementResult.CreatedElementId ?? ElementId.InvalidElementId;
                }

                ExecuteOnUiThread(() =>
                {
                    DetailViewModel.HighlightedFamilyKey = familyKey;
                    // Legacy local maintenance-space fit validation.
                    // Temporarily disabled because AHU room-placement validation
                    // is now provided by the external validation API.
                    //
                    // KEEP THIS CODE.
                    // It may be re-enabled as fallback if the external API
                    // cannot provide the required result reliably.
                    // DetailViewModel.ApplyEquipmentPlacementFitResult(
                    //     familyKey,
                    //     placementResult.MaintenanceSpaceFitStatus,
                    //     placementResult.MaintenanceSpaceFitWarningMessage);
                });

                // Built-in pipe template validation - one-connector anchor stage:
                // after the AHU has been created AND its final orientation has finished,
                // copy RevitLinkInstance\内置管道.rvt into the current document and
                // translate the whole assembly so ONE top/straight pipe connector coincides
                // with one AHU CHWS/CHWR connector. No rotation or mirroring is performed.
                // ConnectTo is attempted only when the two connector directions already face
                // each other; otherwise the assembly remains physically touching for review.
                if (placeBuiltInPipeAssembly)
                {
                    RoomPipeSystemService.PlaceBuiltInPipeAssemblyResult pipeAssemblyResult =
                        RoomPipeSystemService.PlaceBuiltInPipeAssemblyAtEquipmentCenter(
                            doc,
                            placementResult.CreatedElementId);

                    if (pipeAssemblyResult != null && pipeAssemblyResult.Succeeded)
                    {
                        lock (SyncRoot)
                        {
                            StoreGeneratedElementIds(
                                roomKey,
                                _state.RoomGeneratedPipeElementIdsByRoomKey,
                                pipeAssemblyResult.CreatedElementIds);
                        }

                        DiagnosticRecorder.AppendDebug(
                            "[BuiltInPipeAssembly.AnchorOne] Stored generated elements. RoomKey=" + roomKey +
                            ", CreatedCount=" + pipeAssemblyResult.CreatedElementIds.Count.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[BuiltInPipeAssembly.AnchorOne] AHU placement succeeded, but pipe template placement was skipped/failed. RoomKey=" +
                            roomKey + ", Message=" +
                            (pipeAssemblyResult != null ? pipeAssemblyResult.Message ?? string.Empty : "No result"));
                    }
                }

                RoutePlannerSessionCacheService.MarkDirty(doc, "Room equipment placement changed.");
                return true;
            }

            string failureMessage = placementResult != null ? placementResult.Message : string.Empty;
            DiagnosticRecorder.AppendDebug(
                "[RoomCustomFamily] Failed: set family failed, RoomKey=" + roomKey +
                ", FamilyKey=" + familyKey +
                ", Message=" + (failureMessage ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.SetFailed", option.DisplayName ?? familyKey);
            }
            else
            {
                UiMessageService.Error("DockablePane.RoomDetail.Title", "DockablePane.RoomDetail.CustomFamily.SetFailed", option.DisplayName ?? familyKey);
            }

            return false;
        }

        public static bool ExecuteRestoreProbePreview(UIApplication app, string stableRoomKey)
        {
            if (string.IsNullOrWhiteSpace(stableRoomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            ProbeRoomCardState card;
            lock (SyncRoot)
            {
                if (_state == null ||
                    _state.Mode != RoomRecognitionPaneMode.Probe ||
                    !_state.ProbeRoomByStableKey.TryGetValue(stableRoomKey, out card) ||
                    card == null)
                {
                    return false;
                }
            }

            // Restore probe preview through ExternalEvent so document mutations run on the Revit API thread.
            RoomPointProbeService.RecreatePreviewFromLoopPoints(doc, doc.ActiveView, card.LoopPoints);
            return true;
        }

        public static bool ExecutePickPipeWallPoint(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            RoomPipeSystemService.PipeWallPickResult pickResult = RoomPipeSystemService.PickWallPoint(uiDoc);
            if (pickResult == null)
            {
                return false;
            }

            if (pickResult.Canceled)
            {
                return false;
            }

            if (!pickResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", pickResult.Message ?? "Pick wall point failed.");
                return false;
            }

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                _state.PipeWallElementIdByRoomKey[roomKey] = pickResult.WallElementId ?? ElementId.InvalidElementId;
                _state.PipeWallPointByRoomKey[roomKey] = pickResult.PickPoint;
                _state.PipeWallDisplayNameByRoomKey[roomKey] = pickResult.DisplayName ?? string.Empty;
            }

            UpdateEditorPipeWallDisplay(roomKey);
            return true;
        }

        public static bool ExecuteCreatePipeSystem(UIApplication app, string roomKey, string pipeDiameterText)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            ElementId equipmentInstanceId = ElementId.InvalidElementId;
            ElementId wallElementId = ElementId.InvalidElementId;
            XYZ wallPoint = null;
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId storedEquipmentId))
                {
                    equipmentInstanceId = storedEquipmentId ?? ElementId.InvalidElementId;
                }

                if (_state.PipeWallElementIdByRoomKey.TryGetValue(roomKey, out ElementId storedWallId))
                {
                    wallElementId = storedWallId ?? ElementId.InvalidElementId;
                }

                _state.PipeWallPointByRoomKey.TryGetValue(roomKey, out wallPoint);
            }

            if (equipmentInstanceId == ElementId.InvalidElementId &&
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, roomKey, out ElementId resolvedEquipmentId))
            {
                equipmentInstanceId = resolvedEquipmentId;
                lock (SyncRoot)
                {
                    _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = equipmentInstanceId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please insert equipment first.");
                return false;
            }

            if (wallElementId == ElementId.InvalidElementId || wallPoint == null)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please pick a wall point first.");
                return false;
            }

            RoomPipeSystemService.CreatePipeResult createResult = RoomPipeSystemService.CreateSinglePipe(
                doc,
                equipmentInstanceId,
                wallElementId,
                wallPoint,
                pipeDiameterText);

            if (createResult == null || !createResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText(
                    "DockablePane.RoomDetail.Title",
                    createResult != null && !string.IsNullOrWhiteSpace(createResult.Message)
                        ? createResult.Message
                        : "Create pipe system failed.");
                return false;
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Pipe system geometry changed.");
            return true;
        }


        public static bool ExecuteRemoveDuctWork(UIApplication app, string roomKey)
        {
            return ExecuteRemoveGeneratedWork(
                app,
                roomKey,
                _state.RoomGeneratedDuctElementIdsByRoomKey,
                "Remove Ductwork",
                "[RoomDuctWork] Removed");
        }

        public static bool ExecuteRemovePipeWork(UIApplication app, string roomKey)
        {
            return ExecuteRemoveGeneratedWork(
                app,
                roomKey,
                _state.RoomGeneratedPipeElementIdsByRoomKey,
                "Remove Pipework",
                "[RoomPipeWork] Removed");
        }

        private static bool ExecuteRemoveGeneratedWork(
            UIApplication app,
            string roomKey,
            Dictionary<string, List<ElementId>> generatedIdsByRoomKey,
            string transactionName,
            string logPrefix)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            List<ElementId> idsToDelete = new List<ElementId>();
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                AppendStoredElementIds(idsToDelete, generatedIdsByRoomKey, roomKey);
            }

            List<ElementId> validIds = idsToDelete
                .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                .Distinct(new ElementIdValueComparer())
                .ToList();

            if (validIds.Count > 0)
            {
                using (Transaction tx = new Transaction(doc, transactionName))
                {
                    tx.Start();
                    try
                    {
                        doc.Delete(validIds);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted())
                        {
                            tx.RollBack();
                        }

                        DiagnosticRecorder.AppendDebug(logPrefix + " failed. RoomKey=" + roomKey + ", Error=" + ex);
                        UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", ex.Message);
                        return false;
                    }
                }
            }

            lock (SyncRoot)
            {
                generatedIdsByRoomKey.Remove(roomKey);
            }

            if (validIds.Count > 0)
            {
                RoutePlannerSessionCacheService.MarkDirty(doc, transactionName + " changed geometry.");
            }

            DiagnosticRecorder.AppendDebug(logPrefix + ". RoomKey=" + roomKey + ", DeletedCount=" + validIds.Count.ToString());
            return true;
        }

        public static bool ExecuteAddCustomDuctSizeOption(
            UIApplication app,
            double lengthMm,
            double widthMm)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            bool ok = ConnectivitySizeOptionsStorageService.AddDuctSize(
                doc,
                lengthMm,
                widthMm,
                out ConnectivitySizeOptionsPayload payload,
                out string error);
            if (!ok)
            {
                LocalizedDialogService.Error(app, string.IsNullOrWhiteSpace(error) ? "Failed to save custom duct size." : error, "EMSD AI Tool");
                return false;
            }

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.SetConnectivitySizeOptions(payload);
            });
            return true;
        }

        public static bool ExecuteAddCustomPipeSizeOption(
            UIApplication app,
            double diameterMm)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            bool ok = ConnectivitySizeOptionsStorageService.AddPipeSize(
                doc,
                diameterMm,
                out ConnectivitySizeOptionsPayload payload,
                out string error);
            if (!ok)
            {
                LocalizedDialogService.Error(app, string.IsNullOrWhiteSpace(error) ? "Failed to save custom pipe size." : error, "EMSD AI Tool");
                return false;
            }

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.SetConnectivitySizeOptions(payload);
            });
            return true;
        }

        public static bool ExecuteCreatePipeWork(
            UIApplication app,
            string roomKey,
            string chwsPipeSizeText,
            ElementId chwsWallElementId,
            string chwrPipeSizeText,
            ElementId chwrWallElementId)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            ElementId equipmentInstanceId = ElementId.InvalidElementId;
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId storedEquipmentId))
                {
                    equipmentInstanceId = storedEquipmentId ?? ElementId.InvalidElementId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId &&
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, roomKey, out ElementId resolvedEquipmentId))
            {
                equipmentInstanceId = resolvedEquipmentId;
                lock (SyncRoot)
                {
                    _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = equipmentInstanceId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please insert equipment first.");
                return false;
            }

            if (chwsWallElementId == null || chwsWallElementId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please select a CHWS wall first.");
                return false;
            }

            if (chwrWallElementId == null || chwrWallElementId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please select a CHWR wall first.");
                return false;
            }

            RoomPipeSystemService.CreatePipeWorkResult createResult = RoomPipeSystemService.CreateChilledWaterPipeWork(
                doc,
                equipmentInstanceId,
                chwsWallElementId,
                chwsPipeSizeText,
                chwrWallElementId,
                chwrPipeSizeText,
                new RoomPipeSystemService.PipeWorkOptions());

            if (createResult == null || !createResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText(
                    "DockablePane.RoomDetail.Title",
                    createResult != null && !string.IsNullOrWhiteSpace(createResult.Message)
                        ? createResult.Message
                        : "Create pipe work failed.");
                return false;
            }

            lock (SyncRoot)
            {
                StoreGeneratedElementIds(roomKey, _state.RoomGeneratedPipeElementIdsByRoomKey, createResult.CreatedElementIds);
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Pipework geometry changed.");
            return true;
        }
        public static bool ExecutePickDuctWallPoint(UIApplication app, string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (uiDoc == null || doc == null)
            {
                return false;
            }

            RoomFlexDuctService.DuctWallPickResult pickResult = RoomFlexDuctService.PickWallPoint(uiDoc);
            if (pickResult == null)
            {
                return false;
            }

            if (pickResult.Canceled)
            {
                return false;
            }

            if (!pickResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", pickResult.Message ?? "Pick wall point failed.");
                return false;
            }

            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;
                _state.DuctWallElementIdByRoomKey[roomKey] = pickResult.WallElementId ?? ElementId.InvalidElementId;
                _state.DuctWallPointByRoomKey[roomKey] = pickResult.PickPoint;
                _state.DuctWallDisplayNameByRoomKey[roomKey] = pickResult.DisplayName ?? string.Empty;
            }

            UpdateEditorDuctWallDisplay(roomKey);
            return true;
        }

        public static bool ExecuteCreateDuctSystem(UIApplication app, string roomKey, string ductSizeText)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            ElementId equipmentInstanceId = ElementId.InvalidElementId;
            ElementId wallElementId = ElementId.InvalidElementId;
            XYZ wallPoint = null;
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId storedEquipmentId))
                {
                    equipmentInstanceId = storedEquipmentId ?? ElementId.InvalidElementId;
                }

                if (_state.DuctWallElementIdByRoomKey.TryGetValue(roomKey, out ElementId storedWallId))
                {
                    wallElementId = storedWallId ?? ElementId.InvalidElementId;
                }

                _state.DuctWallPointByRoomKey.TryGetValue(roomKey, out wallPoint);
            }

            if (equipmentInstanceId == ElementId.InvalidElementId &&
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, roomKey, out ElementId resolvedEquipmentId))
            {
                equipmentInstanceId = resolvedEquipmentId;
                lock (SyncRoot)
                {
                    _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = equipmentInstanceId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please insert equipment first.");
                return false;
            }

            if (wallElementId == ElementId.InvalidElementId || wallPoint == null)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please pick a wall point first.");
                return false;
            }

            RoomFlexDuctService.CreateFlexDuctResult createResult = RoomFlexDuctService.CreateFlexDuct(
                doc,
                equipmentInstanceId,
                wallElementId,
                wallPoint,
                ductSizeText);

            if (createResult == null || !createResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText(
                    "DockablePane.RoomDetail.Title",
                    createResult != null && !string.IsNullOrWhiteSpace(createResult.Message)
                        ? createResult.Message
                        : "Create duct system failed.");
                return false;
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Duct system geometry changed.");
            return true;
        }

        public static bool ExecuteCreateDuctWork(
            UIApplication app,
            string roomKey,
            string sadDuctSizeText,
            ElementId sadWallElementId,
            string radDuctSizeText,
            ElementId radWallElementId)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return false;
            }

            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null)
            {
                return false;
            }

            ElementId equipmentInstanceId = ElementId.InvalidElementId;
            lock (SyncRoot)
            {
                _doc = doc;
                _uiDoc = uiDoc;

                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId storedEquipmentId))
                {
                    equipmentInstanceId = storedEquipmentId ?? ElementId.InvalidElementId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId &&
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, roomKey, out ElementId resolvedEquipmentId))
            {
                equipmentInstanceId = resolvedEquipmentId;
                lock (SyncRoot)
                {
                    _state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = equipmentInstanceId;
                }
            }

            if (equipmentInstanceId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please insert equipment first.");
                return false;
            }

            if (sadWallElementId == null || sadWallElementId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please select a SAD wall first.");
                return false;
            }

            if (radWallElementId == null || radWallElementId == ElementId.InvalidElementId)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Please select a RAD wall first.");
                return false;
            }

            RoomRigidDuctService.CreateDuctWorkResult createResult = RoomRigidDuctService.CreateSupplyReturnDuctWork(
                doc,
                equipmentInstanceId,
                sadWallElementId,
                sadDuctSizeText,
                radWallElementId,
                radDuctSizeText,
                new RoomRigidDuctService.RigidDuctOptions());

            if (createResult == null || !createResult.Succeeded)
            {
                UiMessageService.ShowTaskDialogText(
                    "DockablePane.RoomDetail.Title",
                    createResult != null && !string.IsNullOrWhiteSpace(createResult.Message)
                        ? createResult.Message
                        : "Create duct work failed.");
                return false;
            }

            lock (SyncRoot)
            {
                StoreGeneratedElementIds(roomKey, _state.RoomGeneratedDuctElementIdsByRoomKey, createResult.CreatedElementIds);
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Ductwork geometry changed.");
            return true;
        }

        public static bool ExecuteSaveLayoutPlan(
            UIApplication app,
            RoomLayoutPlanDto plan,
            bool submitLayoutPlan,
            bool applyActiveState)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || plan == null)
            {
                return false;
            }

            try
            {
                plan.ActiveGeneratedElements = CaptureGeneratedElementsForRoom(doc, plan.RoomKey);

                using (Transaction tx = new Transaction(doc, "Save Room Layout Plan"))
                {
                    tx.Start();
                    RoomLayoutPlanStorePayload payload = RoomLayoutPlanStorageService.Upsert(doc, plan);
                    if (applyActiveState && !string.IsNullOrWhiteSpace(plan.RoomKey))
                    {
                        if (submitLayoutPlan)
                        {
                            payload.ActiveLayoutIdByRoomKey[plan.RoomKey] = plan.LayoutId;
                            RoomLayoutPlanStorageService.Save(doc, payload);
                        }
                        else if (payload.ActiveLayoutIdByRoomKey.TryGetValue(plan.RoomKey, out string activeLayoutId) &&
                            string.Equals(activeLayoutId, plan.LayoutId, StringComparison.OrdinalIgnoreCase))
                        {
                            payload.ActiveLayoutIdByRoomKey.Remove(plan.RoomKey);
                            RoomLayoutPlanStorageService.Save(doc, payload);
                        }
                    }
                    tx.Commit();
                }

                RefreshLayoutPlansFromDocument(doc);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomLayoutPlan] Save failed: " + ex);
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", ex.Message);
                return false;
            }
        }

        public static bool ExecuteSaveDeliveryRoute(UIApplication app, DeliveryRouteRecordDto route)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || route == null)
            {
                return false;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Save Delivery Route"))
                {
                    tx.Start();
                    DeliveryRouteStorageService.Upsert(doc, route);
                    tx.Commit();
                }

                RefreshDeliveryRouteRecordsSnapshotFromDocument(app);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRouteStorage] Save failed: " + ex);
                UiMessageService.ShowTaskDialogText("Delivery Route", ex.Message);
                return false;
            }
        }


        public static bool ExecuteDeleteDeliveryRoute(UIApplication app, string routeId)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || string.IsNullOrWhiteSpace(routeId))
            {
                return false;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Delete Delivery Route"))
                {
                    tx.Start();
                    DeliveryRouteStorageService.Delete(doc, routeId);
                    tx.Commit();
                }

                RefreshDeliveryRouteRecordsSnapshotFromDocument(app);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRouteStorage] Delete failed: " + ex);
                UiMessageService.ShowTaskDialogText("Delivery Route", ex.Message);
                return false;
            }
        }

        public static bool ExecuteExportDeliveryRoute(UIApplication app, string routeId)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || string.IsNullOrWhiteSpace(routeId))
            {
                return false;
            }

            List<ElementId> tempRouteElementIds = new List<ElementId>();
            ElementId tempRouteViewId = ElementId.InvalidElementId;
            try
            {
                DeliveryRouteStorePayload payload = DeliveryRouteStorageService.Load(doc);
                DeliveryRouteRecordDto route = (payload?.Routes ?? new List<DeliveryRouteRecordDto>())
                    .FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
                if (route == null)
                {
                    UiMessageService.ShowTaskDialogText("Delivery Route", "Delivery route not found.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(route.ResponseBody))
                {
                    LocalizedDialogService.Warning(app, "This delivery route has no saved route response to export.", "EMSD AI Tool");
                    return false;
                }

                RoomLayoutPlanDto exportPlan = BuildDeliveryRouteExportPlan(route);
                PrepareLayoutPlanRouteForExport(doc, uiDoc, exportPlan, tempRouteElementIds, out tempRouteViewId);

                string tempDirectory = RoomLayoutPlanPdfExportService.GetExportTempDirectory();
                RoomLayoutPlanImageExportResult images =
                    RoomLayoutPlanImageExportService.ExportCurrentViews(app, tempDirectory, exportPlan);
                RoomLayoutPlanPdfExportResult exportResult =
                    RoomLayoutPlanPdfExportService.ExportTemporary(new RoomLayoutPlanPdfExportContext
                    {
                        Plan = exportPlan,
                        MainViewImagePath = images.MainViewImagePath,
                        KeyPlanImagePath = images.KeyPlanImagePath
                    });

                ExecuteOnUiThread(() =>
                {
                    LayoutPlanPdfPreviewWindow window = new LayoutPlanPdfPreviewWindow(exportResult);
                    window.SetRevitOwner();
                    window.ShowDialog();
                });

                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoute] Export PDF failed: " + ex);
                UiMessageService.ShowTaskDialogText("Delivery Route", ex.Message);
                return false;
            }
            finally
            {
                CleanupLayoutPlanExportRoute(doc, tempRouteElementIds, tempRouteViewId);
            }
        }

        private static RoomLayoutPlanDto BuildDeliveryRouteExportPlan(DeliveryRouteRecordDto route)
        {
            double length = route != null && route.PathLengthMeters.HasValue
                ? route.PathLengthMeters.Value
                : 0.0;

            string flowRate = route != null && route.OriginalModelId > 0
                ? route.OriginalModelId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " m³/s"
                : string.Empty;

            return new RoomLayoutPlanDto
            {
                LayoutId = route?.RouteId ?? Guid.NewGuid().ToString("N"),
                SolutionName = !string.IsNullOrWhiteSpace(route?.RouteName)
                    ? route.RouteName
                    : "Delivery Route",
                CreatedAt = route?.CreatedAt ?? string.Empty,
                UpdatedAt = route?.UpdatedAt ?? string.Empty,
                RoomKey = route?.TargetRoomKey ?? string.Empty,
                RoomName = route?.TargetRoomName ?? string.Empty,
                FlowRate = flowRate,
                EquipmentFamilyKey = route?.EquipmentFamilyKey ?? string.Empty,
                EquipmentDisplayName = route?.EquipmentDisplayName ?? string.Empty,
                RouteLengthText = route?.RouteLengthText ?? string.Empty,
                DeliveryRoute = new LayoutDeliveryRouteDto
                {
                    HasRoute = route != null && route.IsSuccess,
                    StartLiftKey = route?.StartLiftKey ?? string.Empty,
                    StartLiftName = route?.StartLiftName ?? string.Empty,
                    StartLocationType = route?.StartLocationType ?? string.Empty,
                    StartPointName = route?.StartPointName ?? string.Empty,
                    StartPointXmm = route?.StartPointXmm,
                    StartPointYmm = route?.StartPointYmm,
                    StartPointZmm = route?.StartPointZmm,
                    TargetRoomKey = route?.TargetRoomKey ?? string.Empty,
                    TargetRoomName = route?.TargetRoomName ?? string.Empty,
                    ResponseBody = route?.ResponseBody ?? string.Empty,
                    PathLengthMeters = length,
                    RouteLengthText = route?.RouteLengthText ?? string.Empty,
                    ResultMessage = route?.StatusText ?? string.Empty,
                    GeneratedAt = !string.IsNullOrWhiteSpace(route?.UpdatedAt)
                        ? route.UpdatedAt
                        : route?.CreatedAt ?? string.Empty
                }
            };
        }

        public static bool ExecuteDeleteLayoutPlan(UIApplication app, string layoutId)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || string.IsNullOrWhiteSpace(layoutId))
            {
                return false;
            }

            try
            {
                RoomLayoutPlanStorePayload payload = RoomLayoutPlanStorageService.Load(doc);
                RoomLayoutPlanDto deletedPlan = payload.Plans.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(x.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
                if (deletedPlan == null)
                {
                    UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Layout plan not found.");
                    return false;
                }

                bool isActivePlan =
                    !string.IsNullOrWhiteSpace(deletedPlan.RoomKey) &&
                    payload.ActiveLayoutIdByRoomKey.TryGetValue(deletedPlan.RoomKey, out string activeLayoutId) &&
                    string.Equals(activeLayoutId, deletedPlan.LayoutId, StringComparison.OrdinalIgnoreCase);

                if (isActivePlan)
                {
                    bool routeClearOk = ExecuteClearDeliveryRoutePath(app);
                    bool equipmentClearOk = ExecuteClearRoomEquipmentLayout(app, deletedPlan.RoomKey);
                    if (!routeClearOk || !equipmentClearOk)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[RoomLayoutPlan] Delete active plan cleanup failed. LayoutId=" +
                            (deletedPlan.LayoutId ?? string.Empty) +
                            ", RoomKey=" +
                            (deletedPlan.RoomKey ?? string.Empty));
                        return false;
                    }

                    payload.ActiveLayoutIdByRoomKey.Remove(deletedPlan.RoomKey);
                }

                payload.Plans.RemoveAll(x =>
                    x != null &&
                    string.Equals(x.LayoutId, deletedPlan.LayoutId, StringComparison.OrdinalIgnoreCase));

                using (Transaction tx = new Transaction(doc, "Delete Room Layout Plan"))
                {
                    tx.Start();
                    RoomLayoutPlanStorageService.Save(doc, payload);
                    tx.Commit();
                }

                RefreshLayoutPlansFromDocument(doc);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomLayoutPlan] Delete failed: " + ex);
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", ex.Message);
                return false;
            }
        }

        public static bool ExecuteExportLayoutPlan(UIApplication app, string layoutId)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || string.IsNullOrWhiteSpace(layoutId))
            {
                return false;
            }

            try
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Start. LayoutId=" + layoutId);
                RoomLayoutPlanDto plan = RoomLayoutPlanStorageService.Find(doc, layoutId);
                if (plan == null)
                {
                    UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Layout plan not found.");
                    return false;
                }

                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Plan loaded. Room=" + (plan.RoomName ?? string.Empty) + ", Level=" + (plan.LevelText ?? string.Empty));
                LayoutPlanReportData reportData = LayoutPlanReportDataService.Build(plan);
                string tempDirectory = LayoutPlanReportPdfExportService.GetExportTempDirectory(plan.LayoutId);
                LayoutPlanReportImageExportResult images = LayoutPlanReportImageExportService.Export(app, tempDirectory, plan);

                LayoutPlanReportPdfExportResult exportResult = LayoutPlanReportPdfExportService.ExportTemporary(new LayoutPlanReportPdfContext
                {
                    Plan = plan,
                    ReportData = reportData,
                    Main3DImagePath = images.Main3DImagePath,
                    KeyPlanImagePath = images.KeyPlanImagePath,
                    OverallTopViewImagePath = images.OverallTopViewImagePath
                });
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Completed. Pdf=" + (exportResult.PdfPath ?? string.Empty));

                ExecuteOnUiThread(() =>
                {
                    LayoutPlanReportPdfPreviewWindow window = new LayoutPlanReportPdfPreviewWindow(exportResult);
                    window.SetRevitOwner();
                    window.ShowDialog();
                });

                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Export PDF failed: " + ex);
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", ex.Message);
                return false;
            }
        }

        private static void PrepareLayoutPlanRouteForExport(
            Document doc,
            UIDocument uiDoc,
            RoomLayoutPlanDto plan,
            List<ElementId> createdElementIds,
            out ElementId tempViewId)
        {
            tempViewId = ElementId.InvalidElementId;
            if (doc == null || plan == null || plan.DeliveryRoute == null ||
                string.IsNullOrWhiteSpace(plan.DeliveryRoute.ResponseBody))
            {
                return;
            }

            List<ElementId> existingIds = FindLayoutPlanExportRouteElementIds(doc, plan.LayoutId);
            if (existingIds.Count > 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomLayoutPlan] Export route reuse. LayoutId=" +
                    (plan.LayoutId ?? string.Empty) +
                    ", Count=" +
                    existingIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            string pathId = BuildLayoutPlanExportPathId(plan.LayoutId);
            PathPolyline path = CalculatePathApiService.BuildPathPolylineFromResponse(
                plan.DeliveryRoute.ResponseBody,
                pathId);
            if (path == null || path.Points == null || path.Points.Count < 2)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomLayoutPlan] Export route response has no drawable path. LayoutId=" +
                    (plan.LayoutId ?? string.Empty));
                return;
            }

            using (Transaction tx = new Transaction(doc, "Prepare Layout Plan Export Route"))
            {
                tx.Start();

                View3D drawView = uiDoc != null ? uiDoc.ActiveView as View3D : null;
                if (drawView == null)
                {
                    drawView = CreateTemporaryExportRouteView(doc);
                    if (drawView != null)
                    {
                        tempViewId = drawView.Id;
                    }
                }

                Path3DVisualizationService.DrawResult drawResult =
                    Path3DVisualizationService.Draw(doc, drawView, path, false);
                if (drawResult != null && drawResult.ElementIds != null)
                {
                    foreach (ElementId id in drawResult.ElementIds.Where(x => x != null && x != ElementId.InvalidElementId))
                    {
                        Element element = doc.GetElement(id);
                        MarkLayoutPlanExportRouteElement(element, plan.LayoutId);
                        createdElementIds.Add(id);
                    }
                }

                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug(
                "[RoomLayoutPlan] Export route prepared. LayoutId=" +
                (plan.LayoutId ?? string.Empty) +
                ", Created=" +
                createdElementIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void CleanupLayoutPlanExportRoute(Document doc, List<ElementId> elementIds, ElementId tempViewId)
        {
            if (doc == null)
            {
                return;
            }

            List<ElementId> ids = new List<ElementId>();
            if (elementIds != null)
            {
                ids.AddRange(elementIds.Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null));
            }

            if (tempViewId != null && tempViewId != ElementId.InvalidElementId && doc.GetElement(tempViewId) != null)
            {
                ids.Add(tempViewId);
            }

            ids = ids.Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Cleanup Layout Plan Export Route"))
                {
                    tx.Start();
                    doc.Delete(ids);
                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug(
                    "[RoomLayoutPlan] Export route cleanup deleted=" +
                    ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomLayoutPlan] Export route cleanup failed: " + ex);
            }
        }

        private static List<ElementId> FindLayoutPlanExportRouteElementIds(Document doc, string layoutId)
        {
            if (doc == null || string.IsNullOrWhiteSpace(layoutId))
            {
                return new List<ElementId>();
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => IsLayoutPlanExportRouteElement(x, layoutId))
                .Select(x => x.Id)
                .Distinct()
                .ToList();
        }

        private static bool IsLayoutPlanExportRouteElement(Element element, string layoutId)
        {
            string comments = ReadTextParameterByName(element, "Comments");
            return !string.IsNullOrWhiteSpace(comments) &&
                   comments.IndexOf(TempExportRouteMarker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                   comments.IndexOf("LayoutPlanId=" + (layoutId ?? string.Empty), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void MarkLayoutPlanExportRouteElement(Element element, string layoutId)
        {
            TrySetTextParameterByName(
                element,
                "Comments",
                TempExportRouteMarker + ";LayoutPlanId=" + (layoutId ?? string.Empty));
        }

        private static string BuildLayoutPlanExportPathId(string layoutId)
        {
            return "LAYOUT_EXPORT_" + (layoutId ?? string.Empty);
        }

        private static View3D CreateTemporaryExportRouteView(Document doc)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x != null && x.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                return null;
            }

            View3D view = View3D.CreateIsometric(doc, type.Id);
            view.Name = "EMSD_TEMP_EXPORT_ROUTE_VIEW_" + DateTime.Now.ToString("HHmmssfff");
            return view;
        }

        private static string ReadTextParameterByName(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return string.Empty;
            }

            Parameter parameter = element.LookupParameter(parameterName);
            return parameter != null && parameter.StorageType == StorageType.String
                ? parameter.AsString() ?? string.Empty
                : string.Empty;
        }

        private static void TrySetTextParameterByName(Element element, string parameterName, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                return;
            }

            parameter.Set(value ?? string.Empty);
        }

        private static string ResolveSubmittedRoomFamilyKey(
            Document doc,
            string roomKey)
        {
            if (doc == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return string.Empty;
            }

            try
            {
                if (RoomCustomFamilyPlacementService.TryGetPlacedFamilyKey(
                        doc,
                        roomKey,
                        out string placedFamilyKey) &&
                    !string.IsNullOrWhiteSpace(placedFamilyKey))
                {
                    return placedFamilyKey;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanCancel] Failed to capture placed AHU before Detail. RoomKey=" +
                    roomKey + ", Error=" + ex.Message);
            }

            // Fallback for older projects where the placed instance metadata cannot be
            // resolved: use the active submitted Layout Plan saved for this room.
            try
            {
                RoomLayoutPlanStorePayload payload =
                    RoomLayoutPlanStorageService.Load(doc);

                if (payload != null &&
                    payload.ActiveLayoutIdByRoomKey != null &&
                    payload.ActiveLayoutIdByRoomKey.TryGetValue(
                        roomKey,
                        out string activeLayoutId) &&
                    !string.IsNullOrWhiteSpace(activeLayoutId) &&
                    payload.Plans != null)
                {
                    RoomLayoutPlanDto activePlan = payload.Plans.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(
                            x.LayoutId,
                            activeLayoutId,
                            StringComparison.OrdinalIgnoreCase));

                    if (activePlan != null &&
                        !string.IsNullOrWhiteSpace(activePlan.EquipmentFamilyKey))
                    {
                        return activePlan.EquipmentFamilyKey;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanCancel] Failed to resolve active submitted AHU. RoomKey=" +
                    roomKey + ", Error=" + ex.Message);
            }

            return string.Empty;
        }

        public static bool ExecuteActivateLayoutPlan(UIApplication app, string layoutId)
        {
            UIDocument uiDoc = app?.ActiveUIDocument ?? _uiDoc;
            Document doc = uiDoc?.Document ?? _doc;
            if (doc == null || string.IsNullOrWhiteSpace(layoutId))
            {
                return false;
            }

            RoomLayoutPlanDto plan = RoomLayoutPlanStorageService.Find(doc, layoutId);
            if (plan == null)
            {
                UiMessageService.ShowTaskDialogText("DockablePane.RoomDetail.Title", "Layout plan not found.");
                return false;
            }

            RoomSemanticRecord room = null;
            lock (SyncRoot)
            {
                if (_state != null)
                {
                    _state.RoomByKey.TryGetValue(plan.RoomKey ?? string.Empty, out room);
                }
            }

            if (room == null)
            {
                UiMessageService.ShowTaskDialogText(
                    "DockablePane.RoomDetail.Title",
                    "Please run Detect Rooms before activating this layout plan.");
                ExecuteOnUiThread(() =>
                {
                    DetailViewModel.LoadLayoutPlanIntoEditor(plan, false);
                });
                return false;
            }

            // Capture the AHU that is currently committed for this room before Detail
            // activation clears the model and creates its temporary preview elements.
            // Cancel must remove the preview ductwork / pipework and restore this AHU.
            string originalSubmittedFamilyKey =
                ResolveSubmittedRoomFamilyKey(doc, plan.RoomKey);

            bool clearOk = ExecuteClearLayoutPlanVisualsBeforeDetail(app, plan);
            if (!clearOk)
            {
                return false;
            }

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.LoadLayoutPlanIntoEditor(
                    plan,
                    true,
                    originalSubmittedFamilyKey);
            });

            bool equipmentOk = true;
            if (!string.IsNullOrWhiteSpace(plan.EquipmentFamilyKey))
            {
                equipmentOk = ExecuteSetRoomCustomFamily(
                    app,
                    plan.RoomKey,
                    plan.EquipmentFamilyKey,
                    null,
                    false,
                    0,
                    0,
                    false,
                    0,
                    false);
            }

            if (!equipmentOk)
            {
                return false;
            }

            bool ductOk = true;
            ElementId sadWallId = ResolveWallElementId(doc, plan.SadWall);
            ElementId radWallId = ResolveWallElementId(doc, plan.RadWall);
            if (IsValidWall(sadWallId) && IsValidWall(radWallId) &&
                !string.IsNullOrWhiteSpace(plan.SadSize) &&
                !string.IsNullOrWhiteSpace(plan.RadSize) &&
                !string.Equals(plan.SadSize, "Select", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(plan.RadSize, "Select", StringComparison.OrdinalIgnoreCase))
            {
                ductOk = ExecuteCreateDuctWork(
                    app,
                    plan.RoomKey,
                    plan.SadSize,
                    sadWallId,
                    plan.RadSize,
                    radWallId);
            }

            bool pipeOk = true;
            ElementId chwsWallId = ResolveWallElementId(doc, plan.ChwsWall);
            ElementId chwrWallId = ResolveWallElementId(doc, plan.ChwrWall);
            if (IsValidWall(chwsWallId) && IsValidWall(chwrWallId) &&
                !string.IsNullOrWhiteSpace(plan.ChwsPipeSize) &&
                !string.IsNullOrWhiteSpace(plan.ChwrPipeSize) &&
                !string.Equals(plan.ChwsPipeSize, "Select", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(plan.ChwrPipeSize, "Select", StringComparison.OrdinalIgnoreCase))
            {
                pipeOk = ExecuteCreatePipeWork(
                    app,
                    plan.RoomKey,
                    plan.ChwsPipeSize,
                    chwsWallId,
                    plan.ChwrPipeSize,
                    chwrWallId);
            }

            if (!ductOk || !pipeOk)
            {
                return false;
            }

            TryDrawSavedDeliveryRoute(uiDoc, doc, plan);

            RefreshLayoutPlansFromDocument(doc);
            return true;
        }

        public static void ShowPanes(UIApplication uiApp)
        {
            PathObstacleRuntime.HidePane(uiApp);
            TryHidePropertiesPalette(uiApp);
            TryShowPane(uiApp, LeftPaneId);
            TryShowPane(uiApp, RightPaneId);
        }

        public static void ShowRoomAndLiftPane(UIApplication uiApp)
        {
            // Room Management and Restricted Area share the left side.
            // Always close Restricted Area before showing Room & Lift.
            PathObstacleRuntime.HidePane(uiApp);
            TryHidePropertiesPalette(uiApp);
            TryShowPane(uiApp, LeftPaneId);
        }

        public static void HideRoomAndLiftPane(UIApplication uiApp)
        {
            TryHidePane(uiApp, LeftPaneId);
        }

        public static void ShowLayoutPlansPane(UIApplication uiApp)
        {
            TryHidePropertiesPalette(uiApp);
            DeliveryRoutePaneRuntime.Hide(uiApp);
            TryShowPane(uiApp, RightPaneId);
        }

        public static void HidePanes(UIApplication uiApp)
        {
            TryHidePane(uiApp, LeftPaneId);
            TryHidePane(uiApp, RightPaneId);
            DeliveryRoutePaneRuntime.Hide(uiApp);
        }

        private static void TryHidePropertiesPalette(UIApplication uiApp)
        {
            try
            {
                if (uiApp == null)
                {
                    return;
                }

                DockablePane pane = uiApp.GetDockablePane(
                    Autodesk.Revit.UI.DockablePanes.BuiltInDockablePanes.PropertiesPalette);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomPane] Hide properties palette failed: " + ex.Message);
            }
        }

        public static void TryHidePreviewPane(UIApplication uiApp)
        {
            try
            {
                DockablePane pane = uiApp.GetDockablePane(PreviewPaneRuntime.PaneId);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomPane] Hide preview pane failed: " + ex.Message);
            }
        }

        private static void TryShowPane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                DockablePane pane = uiApp.GetDockablePane(paneId);
                pane?.Show();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomPane] Show pane failed: " + ex.Message);
            }
        }

        private static void TryHidePane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                DockablePane pane = uiApp.GetDockablePane(paneId);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomPane] Hide pane failed: " + ex.Message);
            }
        }

        private static void MergeProbeCard(Document doc, RoomPointProbeResult probeResult)
        {
            if (_state == null)
            {
                _state = new RoomRecognitionPaneState
                {
                    Mode = RoomRecognitionPaneMode.Probe
                };
            }

            string stableRoomKey = probeResult != null ? probeResult.StableRoomKey ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(stableRoomKey) || probeResult.SemanticRecord == null)
            {
                return;
            }

            if (!_state.ProbeRoomByStableKey.TryGetValue(stableRoomKey, out ProbeRoomCardState card) || card == null)
            {
                card = new ProbeRoomCardState
                {
                    StableRoomKey = stableRoomKey
                };
                _state.ProbeRoomByStableKey[stableRoomKey] = card;
                _state.ProbeRoomCardOrder.Add(stableRoomKey);
            }

            card.HitNativeRoom = probeResult.HitNativeRoom;
            card.LevelId = probeResult.LevelId ?? ElementId.InvalidElementId;
            card.LevelName = probeResult.LevelName ?? string.Empty;
            card.RoomName = probeResult.RoomName ?? string.Empty;
            card.RoomNumber = probeResult.RoomNumber ?? string.Empty;
            card.AreaM2 = probeResult.AreaM2;
            card.Status = probeResult.Status ?? string.Empty;
            card.SemanticRecord = probeResult.SemanticRecord;
            card.LoopPoints = CloneLoopPoints(probeResult.LoopPoints);

            _state.SelectedProbeRoomStableKey = stableRoomKey;
            _selectedRoomKey = probeResult.SemanticRecord.Key;
            RebuildProbeIndexes(doc, _state);
        }

        private static void RebuildProbeIndexes(Document doc, RoomRecognitionPaneState state)
        {
            if (state == null)
            {
                return;
            }

            state.Summary = new TargetRoomModelRecognitionService.RecognitionSummary();
            state.RoomRangeElementIds.Clear();
            state.RoomByKey.Clear();
            state.LiftByKey.Clear();
            state.LevelNameByRoomKey.Clear();
            state.RoomCustomFamilyKeyByRoomKey.Clear();
            state.RoomCustomFamilyInstanceIdByRoomKey.Clear();
            state.RoomGeneratedDuctElementIdsByRoomKey.Clear();
            state.RoomGeneratedPipeElementIdsByRoomKey.Clear();

            foreach (string stableRoomKey in state.ProbeRoomCardOrder ?? new List<string>())
            {
                if (!state.ProbeRoomByStableKey.TryGetValue(stableRoomKey ?? string.Empty, out ProbeRoomCardState card) ||
                    card == null ||
                    card.SemanticRecord == null ||
                    string.IsNullOrWhiteSpace(card.SemanticRecord.Key))
                {
                    continue;
                }

                string roomKey = card.SemanticRecord.Key;
                state.RoomByKey[roomKey] = card.SemanticRecord;
                state.LevelNameByRoomKey[roomKey] = card.LevelName ?? string.Empty;
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyKey(doc, roomKey, out string familyKey);
                state.RoomCustomFamilyKeyByRoomKey[roomKey] = familyKey ?? string.Empty;
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, roomKey, out ElementId instanceId);
                state.RoomCustomFamilyInstanceIdByRoomKey[roomKey] = instanceId ?? ElementId.InvalidElementId;
            }
        }

        private static void SelectProbeRoomCard(string stableRoomKey, bool restorePreview)
        {
            if (string.IsNullOrWhiteSpace(stableRoomKey))
            {
                SetDetailEmpty();
                return;
            }

            ProbeRoomCardState card;
            lock (SyncRoot)
            {
                if (_state == null ||
                    _state.Mode != RoomRecognitionPaneMode.Probe ||
                    !_state.ProbeRoomByStableKey.TryGetValue(stableRoomKey, out card) ||
                    card == null)
                {
                    return;
                }

                _state.SelectedProbeRoomStableKey = stableRoomKey;
                _selectedRoomKey = card.SemanticRecord != null ? card.SemanticRecord.Key : null;
            }

            RefreshSelectionState();

            if (restorePreview)
            {
                _ = RequestRestoreProbePreviewAsync(stableRoomKey);
            }
        }

        private static List<XYZ> CloneLoopPoints(List<XYZ> loopPoints)
        {
            List<XYZ> clone = new List<XYZ>();
            foreach (XYZ point in loopPoints ?? new List<XYZ>())
            {
                if (point != null)
                {
                    clone.Add(new XYZ(point.X, point.Y, point.Z));
                }
            }

            return clone;
        }

        private static RoomRecognitionPaneState BuildState(
            Document doc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<ElementId>> roomRangeElementIds)
        {
            RoomRecognitionPaneState state = new RoomRecognitionPaneState
            {
                Mode = RoomRecognitionPaneMode.Detect,
                Summary = summary ?? new TargetRoomModelRecognitionService.RecognitionSummary(),
                RoomRangeElementIds = roomRangeElementIds ?? new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase)
            };

            EnsureSummaryReady(state.Summary);
            MergeManualRoomsIntoSummary(doc, state.Summary);

            foreach (RoomSemanticRecord room in state.Summary.RunResult.Rooms ?? new List<RoomSemanticRecord>())
            {
                if (room == null || string.IsNullOrWhiteSpace(room.Key))
                {
                    continue;
                }

                state.RoomByKey[room.Key] = room;
                state.LevelNameByRoomKey[room.Key] = ResolveLevelName(doc, state.Summary, room.Key);

                RoomCustomFamilyPlacementService.TryGetPlacedFamilyKey(doc, room.Key, out string familyKey);
                state.RoomCustomFamilyKeyByRoomKey[room.Key] = familyKey ?? string.Empty;
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(doc, room.Key, out ElementId instanceId);
                state.RoomCustomFamilyInstanceIdByRoomKey[room.Key] = instanceId ?? ElementId.InvalidElementId;
            }

            foreach (LiftRecognitionRecord lift in state.Summary.Lifts ?? new List<LiftRecognitionRecord>())
            {
                if (lift == null || string.IsNullOrWhiteSpace(lift.Key))
                {
                    continue;
                }

                state.LiftByKey[lift.Key] = lift;
            }

            AssignRoomDisplayNames(state);
            ApplyNameOverrides(doc, state);
            ApplyLiftDisplayOverrides(doc, state);

            return state;
        }

        private static void EnsureSummaryReady(TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (summary == null)
            {
                return;
            }

            if (summary.RunResult == null)
            {
                summary.RunResult = new RoomSemanticRunResult();
            }

            if (summary.RunResult.Rooms == null)
            {
                summary.RunResult.Rooms = new List<RoomSemanticRecord>();
            }

            if (summary.Lifts == null)
            {
                summary.Lifts = new List<LiftRecognitionRecord>();
            }
        }

        private static void MergeManualRoomsIntoSummary(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (summary == null)
            {
                return;
            }

            EnsureSummaryReady(summary);
            foreach (ManualRoomRecord manualRoom in ManualRoomStorageService.Load(doc))
            {
                UpsertManualRoom(summary, manualRoom);
            }
        }

        private static void UpsertManualRoom(TargetRoomModelRecognitionService.RecognitionSummary summary, ManualRoomRecord manualRoom)
        {
            if (summary == null || manualRoom == null || string.IsNullOrWhiteSpace(manualRoom.Key))
            {
                return;
            }

            EnsureSummaryReady(summary);
            summary.RunResult.Rooms.RemoveAll(x => x == null || string.Equals(x.Key, manualRoom.Key, StringComparison.OrdinalIgnoreCase));
            summary.RunResult.Rooms.Add(manualRoom.ToSemanticRecord());
            summary.SeedLevelIdByKey[manualRoom.Key] = manualRoom.LevelIdValue;
        }

        private static void IndexManualRoom(RoomRecognitionPaneState state, ManualRoomRecord manualRoom)
        {
            if (state == null || manualRoom == null || string.IsNullOrWhiteSpace(manualRoom.Key))
            {
                return;
            }

            RoomSemanticRecord room = manualRoom.ToSemanticRecord();
            state.RoomByKey[manualRoom.Key] = room;
            state.LevelNameByRoomKey[manualRoom.Key] = manualRoom.LevelName ?? string.Empty;
            state.RoomCustomFamilyKeyByRoomKey[manualRoom.Key] = string.Empty;
            state.RoomCustomFamilyInstanceIdByRoomKey[manualRoom.Key] = ElementId.InvalidElementId;
        }

        private static RoomListItemViewModel BuildListItem(RoomRecognitionPaneState snapshot, RoomSemanticRecord room, string selectedKey)
        {
            string title = ResolveRoomDisplayName(snapshot, room != null ? room.Key : string.Empty, room != null ? room.RoomName : string.Empty);
            string roomType = string.IsNullOrWhiteSpace(room.TargetRoomType) ? "-" : room.TargetRoomType;
            string areaText = FormatArea(room.AreaM2);
            string levelName = snapshot.LevelNameByRoomKey.TryGetValue(room.Key ?? string.Empty, out string resolvedLevel)
                ? resolvedLevel
                : Loc.T("Common.NA");
            string statusText = string.IsNullOrWhiteSpace(room.Status) ? "-" : room.Status;
            ElementId levelId = ResolveRoomLevelId(snapshot, room != null ? room.Key : string.Empty);
            RoomCardMetricDisplay metrics = BuildRoomCardMetricDisplay(room, areaText, levelId);

            RoomListItemViewModel item = new RoomListItemViewModel
            {
                Key = room.Key,
                Title = title,
                Subtitle = roomType,
                AreaText = areaText,
                AreaLine = Loc.T("DockablePane.RoomList.AreaFormat", areaText),
                LevelText = levelName,
                LevelLine = Loc.T("DockablePane.RoomList.LevelFormat", levelName),
                StatusText = statusText,
                StatusLine = Loc.T("DockablePane.RoomList.StatusFormat", statusText),
                TargetType = roomType,
                RoomSizeLine = BuildRoomSizeLine(metrics),
                DoorSizeLine = BuildRoomDoorSizeLine(metrics),
                AreaSummaryLine = BuildRoomAreaLine(metrics.AvailableUsableAreaText),
                RoomLengthLine = BuildRoomCardLine("Room Length", metrics.RoomLengthText),
                RoomWidthLine = BuildRoomCardLine("Room Width", metrics.RoomWidthText),
                RoomHeightLine = BuildRoomCardLine("Room Height", metrics.RoomHeightText),
                DoorWidthLine = BuildRoomCardLine("Door Width", metrics.DoorWidthText),
                DoorHeightLine = BuildRoomCardLine("Door Height", metrics.DoorHeightText),
                AvailableUsableAreaLine = BuildRoomCardLine("Available / Usable Area", metrics.AvailableUsableAreaText),
                IsSelected = string.Equals(room.Key, selectedKey, StringComparison.OrdinalIgnoreCase)
            };
            item.EditCommand = new RoomListCommand(_ => EditRoomNameFromUi(item));
            item.DeleteCommand = new RoomListCommand(_ => DeleteRoomFromUi(item));
            return item;
        }

        private static void RefreshProbeSelectionState(RoomRecognitionPaneState snapshot)
        {
            List<ProbeRoomCardState> cards = (snapshot.ProbeRoomCardOrder ?? new List<string>())
                .Select(x => snapshot.ProbeRoomByStableKey.TryGetValue(x ?? string.Empty, out ProbeRoomCardState card) ? card : null)
                .Where(x => x != null)
                .ToList();

            ExecuteOnUiThread(() =>
            {
                ListViewModel.HeaderTitle = "Room Management";
                ListViewModel.SummaryText = string.Empty;
                ListViewModel.Rooms.Clear();
                ListViewModel.Lifts.Clear();
                DetailViewModel.SetEditorLiftOptionItems(new List<EditorLiftOptionViewModel>());
                DeliveryRoutePaneRuntime.RefreshOptionsFromRecognitionState();

                if (cards.Count == 0)
                {
                    ListViewModel.SetSelectedRoomSilently(null);
                    ListViewModel.SetSelectedLiftSilently(null);
                    return;
                }

                foreach (ProbeRoomCardState card in cards)
                {
                    ListViewModel.Rooms.Add(BuildProbeListItem(card, snapshot.SelectedProbeRoomStableKey));
                }
            });

            if (string.IsNullOrWhiteSpace(snapshot.SelectedProbeRoomStableKey) ||
                !snapshot.ProbeRoomByStableKey.TryGetValue(snapshot.SelectedProbeRoomStableKey, out ProbeRoomCardState selectedCard) ||
                selectedCard == null)
            {
                SetDetailEmpty();
                return;
            }

            ApplyDetail(selectedCard.SemanticRecord);
            ExecuteOnUiThread(() =>
            {
                RoomListItemViewModel selectedItem = ListViewModel.Rooms
                    .FirstOrDefault(x => string.Equals(x.StableRoomKey, snapshot.SelectedProbeRoomStableKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(selectedItem);
            });
        }

        private static RoomListItemViewModel BuildProbeListItem(ProbeRoomCardState card, string selectedStableRoomKey)
        {
            string roomName = string.IsNullOrWhiteSpace(card.RoomName) ? Loc.T("DockablePane.RoomDetail.UnnamedRoom") : card.RoomName;
            string subtitle = card.HitNativeRoom ? "Probe Room" : (card.SemanticRecord != null ? (card.SemanticRecord.TargetRoomType ?? string.Empty) : string.Empty);
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                subtitle = "-";
            }

            string areaText = FormatArea(card.AreaM2);
            string levelText = string.IsNullOrWhiteSpace(card.LevelName) ? Loc.T("Common.NA") : card.LevelName;
            string statusText = string.IsNullOrWhiteSpace(card.Status) ? "-" : card.Status;
            RoomCardMetricDisplay metrics = BuildRoomCardMetricDisplay(card.SemanticRecord, areaText, card.LevelId, card.LoopPoints);

            return new RoomListItemViewModel
            {
                Key = card.SemanticRecord != null ? card.SemanticRecord.Key : string.Empty,
                StableRoomKey = card.StableRoomKey,
                IsProbeRoomCard = true,
                Title = roomName,
                Subtitle = subtitle,
                AreaText = areaText,
                AreaLine = Loc.T("DockablePane.RoomList.AreaFormat", areaText),
                LevelText = levelText,
                LevelLine = Loc.T("DockablePane.RoomList.LevelFormat", levelText),
                StatusText = statusText,
                StatusLine = Loc.T("DockablePane.RoomList.StatusFormat", statusText),
                TargetType = subtitle,
                RoomSizeLine = BuildRoomSizeLine(metrics),
                DoorSizeLine = BuildRoomDoorSizeLine(metrics),
                AreaSummaryLine = BuildRoomAreaLine(metrics.AvailableUsableAreaText),
                RoomLengthLine = BuildRoomCardLine("Room Length", metrics.RoomLengthText),
                RoomWidthLine = BuildRoomCardLine("Room Width", metrics.RoomWidthText),
                RoomHeightLine = BuildRoomCardLine("Room Height", metrics.RoomHeightText),
                DoorWidthLine = BuildRoomCardLine("Door Width", metrics.DoorWidthText),
                DoorHeightLine = BuildRoomCardLine("Door Height", metrics.DoorHeightText),
                AvailableUsableAreaLine = BuildRoomCardLine("Available / Usable Area", metrics.AvailableUsableAreaText),
                IsSelected = string.Equals(card.StableRoomKey, selectedStableRoomKey, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static LiftListItemViewModel BuildLiftListItem(LiftRecognitionRecord lift, string selectedLiftKey)
        {
            LiftDisplayInfo displayInfo = ResolveLiftDisplayInfo(lift);
            string dimension = FormatLiftDimension(displayInfo);
            string doorSize = FormatLiftDoorSize(displayInfo);
            string capacity = FormatLiftCapacity(displayInfo);
            string title = ResolveLiftDisplayName(lift);

            LiftListItemViewModel item = new LiftListItemViewModel
            {
                Key = lift != null ? (lift.Key ?? string.Empty) : string.Empty,
                Title = title,
                LiftId = lift == null ? string.Empty : (lift.LiftId ?? string.Empty),
                LiftType = lift == null ? string.Empty : (lift.LiftType ?? string.Empty),
                Dimension = dimension,
                DoorSize = doorSize,
                Capacity = capacity,
                LiftInternalLine = BuildLiftInternalLine(dimension),
                DoorSizeLine = BuildLiftDoorSizeLine(doorSize),
                CapacityLine = BuildLiftCapacityLine(capacity),
                IsSelected = lift != null && string.Equals(lift.Key, selectedLiftKey, StringComparison.OrdinalIgnoreCase)
            };
            item.EditCommand = new RoomListCommand(_ => EditLiftNameFromUi(item));
            item.DeleteCommand = new RoomListCommand(_ => DeleteLiftFromUi(item));
            return item;
        }

        private static List<EditorLiftOptionViewModel> BuildEditorLiftOptions(IEnumerable<LiftRecognitionRecord> lifts)
        {
            return (lifts ?? Enumerable.Empty<LiftRecognitionRecord>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .Select(lift => new EditorLiftOptionViewModel
                {
                    Key = lift.Key,
                    DisplayName = ResolveLiftDisplayName(lift),
                    LiftKind = lift.LiftKind ?? string.Empty
                })
                .ToList();
        }

        private static LiftDisplayInfo ResolveLiftDisplayInfo(LiftRecognitionRecord lift)
        {
            LiftDisplayInfo info = new LiftDisplayInfo();
            if (lift == null)
            {
                return info;
            }

            List<string> dimensionParts = SplitMetricParts(lift.Dimension, 3);
            info.InternalLengthMm = ParseMetricNumber(dimensionParts[0]);
            info.InternalWidthMm = ParseMetricNumber(dimensionParts[1]);
            info.InternalHeightMm = ParseMetricNumber(dimensionParts[2]);

            List<string> doorParts = SplitMetricParts(lift.DoorSize, 2);
            info.DoorWidthMm = ParseMetricNumber(doorParts[0]);
            info.DoorHeightMm = ParseMetricNumber(doorParts[1]);
            info.CapacityKg = ParseMetricNumber(lift.Capacity);

            string key = lift.Key ?? string.Empty;
            lock (SyncRoot)
            {
                if (_state != null &&
                    _state.LiftDisplayOverrideByKey != null &&
                    !string.IsNullOrWhiteSpace(key) &&
                    _state.LiftDisplayOverrideByKey.TryGetValue(key, out LiftDisplayOverride displayOverride) &&
                    displayOverride != null)
                {
                    info.InternalLengthMm = displayOverride.InternalLengthMm ?? info.InternalLengthMm;
                    info.InternalWidthMm = displayOverride.InternalWidthMm ?? info.InternalWidthMm;
                    info.InternalHeightMm = displayOverride.InternalHeightMm ?? info.InternalHeightMm;
                    info.DoorWidthMm = displayOverride.DoorWidthMm ?? info.DoorWidthMm;
                    info.DoorHeightMm = displayOverride.DoorHeightMm ?? info.DoorHeightMm;
                    info.CapacityKg = displayOverride.CapacityKg ?? info.CapacityKg;
                }
            }

            return info;
        }

        private static string FormatLiftDimension(LiftDisplayInfo info)
        {
            return FormatMetricNumber(info?.InternalLengthMm) + " mm x " +
                FormatMetricNumber(info?.InternalWidthMm) + " mm x " +
                FormatMetricNumber(info?.InternalHeightMm) + " mm";
        }

        private static string FormatLiftDoorSize(LiftDisplayInfo info)
        {
            return FormatMetricNumber(info?.DoorWidthMm) + " mm x " +
                FormatMetricNumber(info?.DoorHeightMm) + " mm";
        }

        private static string FormatLiftCapacity(LiftDisplayInfo info)
        {
            return FormatMetricNumber(info?.CapacityKg) + " Kg";
        }

        private static string FormatMetricNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-";
        }

        private static double? ParseMetricNumber(string value)
        {
            string text = StripMetricUnit(value);
            if (string.IsNullOrWhiteSpace(text) || text == "-")
            {
                return null;
            }

            if (text.EndsWith("kg", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 2).Trim();
            }

            if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 2).Trim();
            }

            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double valueMm) ||
                double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out valueMm))
            {
                return valueMm;
            }

            return null;
        }

        private static string BuildLiftInternalLine(string dimension)
        {
            List<string> parts = SplitMetricParts(dimension, 3);
            return "Lift Internal(mm) : L:" + parts[0] + " x W:" + parts[1] + " x H:" + parts[2];
        }

        private static string BuildLiftDoorSizeLine(string doorSize)
        {
            List<string> parts = SplitMetricParts(doorSize, 2);
            return "Door Size(mm) : W:" + parts[0] + " x H:" + parts[1];
        }

        private static string BuildLiftCapacityLine(string capacity)
        {
            string value = StripMetricUnit(capacity);
            if (value.EndsWith("kg", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2).Trim();
            }

            return "Capacity(kg) : " + (string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private static List<string> SplitMetricParts(string value, int count)
        {
            List<string> result = new List<string>();
            string[] parts = string.IsNullOrWhiteSpace(value)
                ? new string[0]
                : value.Split(new[] { 'x', 'X' }, StringSplitOptions.None);
            for (int i = 0; i < count; i++)
            {
                result.Add(i < parts.Length ? StripMetricUnit(parts[i]) : "-");
            }

            return result;
        }

        private static string FormatLiftMetricText(string value, int expectedPartCount)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string[] parts = value.Split(new[] { 'x', 'X' }, StringSplitOptions.None);
            if (parts.Length != expectedPartCount)
            {
                return value;
            }

            List<string> formatted = new List<string>();
            foreach (string part in parts)
            {
                string number = (part ?? string.Empty).Trim();
                if (number.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                {
                    number = number.Substring(0, number.Length - 2).Trim();
                }

                if (string.IsNullOrWhiteSpace(number))
                {
                    return value;
                }

                formatted.Add(number + " mm");
            }

            return string.Join(" x ", formatted);
        }

        private static HashSet<string> BuildAnalyzeRoomsLiftRoomKeys(TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiftRecognitionRecord lift in summary?.Lifts ?? new List<LiftRecognitionRecord>())
            {
                if (!TryGetAnalyzeRoomsLiftRoomKey(lift, out string roomKey))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(roomKey))
                {
                    keys.Add(roomKey);
                }
            }

            return keys;
        }

        private static bool IsAnalyzeRoomsPostProcessLift(LiftRecognitionRecord lift)
        {
            return lift != null &&
                   (string.Equals(lift.GeometrySourceLayer, "AnalyzeRoomsPostProcess", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(lift.Key) &&
                     lift.Key.StartsWith("lift_demo_", StringComparison.OrdinalIgnoreCase)));
        }

        private static bool TryGetAnalyzeRoomsLiftRoomKey(LiftRecognitionRecord lift, out string roomKey)
        {
            roomKey = string.Empty;
            if (!IsAnalyzeRoomsPostProcessLift(lift) ||
                string.IsNullOrWhiteSpace(lift.Key) ||
                !lift.Key.StartsWith("lift_demo_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            roomKey = lift.Key.Substring("lift_demo_".Length);
            return !string.IsNullOrWhiteSpace(roomKey);
        }

        private static void ApplyDetail(RoomSemanticRecord room)
        {
            if (room == null)
            {
                SetDetailEmpty();
                return;
            }

            string levelText = Loc.T("Common.NA");
            string displayRoomName = string.Empty;
            lock (SyncRoot)
            {
                _state.LevelNameByRoomKey.TryGetValue(room.Key ?? string.Empty, out levelText);
                displayRoomName = ResolveRoomDisplayName(_state, room.Key, room.RoomName);
            }

            if (string.IsNullOrWhiteSpace(levelText))
            {
                levelText = Loc.T("Common.NA");
            }

            string roomName = string.IsNullOrWhiteSpace(displayRoomName) ? Loc.T("DockablePane.RoomDetail.UnnamedRoom") : displayRoomName;
            string targetType = string.IsNullOrWhiteSpace(room.TargetRoomType) ? "-" : room.TargetRoomType;
            string statusText = string.IsNullOrWhiteSpace(room.Status) ? "-" : room.Status;
            string boundaryText = string.IsNullOrWhiteSpace(room.BoundaryLayers) ? "-" : room.BoundaryLayers;
            string keyText = string.IsNullOrWhiteSpace(room.Key) ? "-" : room.Key;
            string closeGap = room.CloseGapMm > 0 ? room.CloseGapMm.ToString("F0") : "0";

            ExecuteOnUiThread(() =>
            {
                DetailViewModel.HandleSelectedRoomChanged(room.Key, roomName);
                DetailViewModel.HasSelection = true;
                DetailViewModel.SelectedRoomKey = room.Key;
                DetailViewModel.RoomName = roomName;
                DetailViewModel.TargetRoomType = targetType;
                DetailViewModel.AreaText = FormatArea(room.AreaM2);
                DetailViewModel.LevelText = levelText;
                DetailViewModel.StatusText = statusText;
                DetailViewModel.BoundaryLayersText = boundaryText;
                DetailViewModel.RoomKeyText = keyText;
                DetailViewModel.CloseGapText = closeGap;
                DetailViewModel.HighlightedFamilyKey = _state.RoomCustomFamilyKeyByRoomKey.TryGetValue(room.Key ?? string.Empty, out string familyKey)
                    ? familyKey
                    : string.Empty;
                if (DetailViewModel.CurrentEditor != null)
                {
                    DetailViewModel.CurrentEditor.SelectedDuctWall = ResolveDuctWallDisplay(room.Key);
                    DetailViewModel.CurrentEditor.SelectedPipeWall = ResolvePipeWallDisplay(room.Key);
                }
            });
        }

        private static void SetDetailEmpty()
        {
            ExecuteOnUiThread(() =>
            {
                DetailViewModel.ResetForNoSelection();
                DetailViewModel.HasSelection = false;
                DetailViewModel.SelectedRoomKey = null;
                DetailViewModel.RoomName = Loc.T("DockablePane.RoomDetail.SelectHint");
                DetailViewModel.TargetRoomType = "-";
                DetailViewModel.AreaText = "-";
                DetailViewModel.LevelText = "-";
                DetailViewModel.StatusText = "-";
                DetailViewModel.BoundaryLayersText = "-";
                DetailViewModel.RoomKeyText = "-";
                DetailViewModel.CloseGapText = "-";
                DetailViewModel.HighlightedFamilyKey = string.Empty;
                ListViewModel.SetSelectedRoomSilently(null);
                LiftListItemViewModel selectedLiftItem = ListViewModel.Lifts.FirstOrDefault(x => string.Equals(x.Key, _selectedLiftKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedLiftSilently(selectedLiftItem);
            });
        }

        private static string ResolvePipeWallDisplay(string roomKey)
        {
            lock (SyncRoot)
            {
                if (_state != null &&
                    !string.IsNullOrWhiteSpace(roomKey) &&
                    _state.PipeWallDisplayNameByRoomKey.TryGetValue(roomKey, out string displayName) &&
                    !string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            return "No wall selected";
        }

        private static string ResolveDuctWallDisplay(string roomKey)
        {
            lock (SyncRoot)
            {
                if (_state != null &&
                    !string.IsNullOrWhiteSpace(roomKey) &&
                    _state.DuctWallDisplayNameByRoomKey.TryGetValue(roomKey, out string displayName) &&
                    !string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            return "No wall selected";
        }

        private static void UpdateEditorPipeWallDisplay(string roomKey)
        {
            string displayName = ResolvePipeWallDisplay(roomKey);
            ExecuteOnUiThread(() =>
            {
                if (DetailViewModel.CurrentEditor != null &&
                    string.Equals(DetailViewModel.CurrentEditor.RoomKey, roomKey, StringComparison.OrdinalIgnoreCase))
                {
                    DetailViewModel.CurrentEditor.SelectedPipeWall = displayName;
                }
            });
        }

        private static void UpdateEditorDuctWallDisplay(string roomKey)
        {
            string displayName = ResolveDuctWallDisplay(roomKey);
            ExecuteOnUiThread(() =>
            {
                if (DetailViewModel.CurrentEditor != null &&
                    string.Equals(DetailViewModel.CurrentEditor.RoomKey, roomKey, StringComparison.OrdinalIgnoreCase))
                {
                    DetailViewModel.CurrentEditor.SelectedDuctWall = displayName;
                }
            });
        }

        private static bool IsMatchedRoom(RoomSemanticRecord room)
        {
            if (room == null)
            {
                return false;
            }

            string status = room.Status ?? string.Empty;
            return status.StartsWith("Matched", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Manual", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "UserDefined", StringComparison.OrdinalIgnoreCase);
        }

        private static void RefreshRoomVisualizationIfNeeded(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            if (doc == null || !(doc.ActiveView is View3D))
            {
                return;
            }

            Room3DVisualizationService.Refresh(doc, summary);
        }

        private static void SelectRoomByKeyForFocusOnly(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                _selectedRoomKey = roomKey;
                _selectedLiftKey = null;

                if (_state != null)
                {
                    _state.SelectedLiftKey = null;
                }
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = string.Equals(item.Key, roomKey, StringComparison.OrdinalIgnoreCase);
                }

                foreach (LiftListItemViewModel item in ListViewModel.Lifts)
                {
                    item.IsSelected = false;
                }

                RoomListItemViewModel selectedItem = ListViewModel.Rooms
                    .FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(selectedItem);
                ListViewModel.SetSelectedLiftSilently(null);
            });
        }

        internal static void ToggleRoomSelectionForLeftOnly(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            bool isSameSelected;
            lock (SyncRoot)
            {
                isSameSelected = string.Equals(_selectedRoomKey, roomKey, StringComparison.OrdinalIgnoreCase);
                _selectedRoomKey = isSameSelected ? null : roomKey;
                _selectedLiftKey = null;
                if (_state != null)
                {
                    _state.SelectedLiftKey = null;
                }
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = !isSameSelected && string.Equals(item.Key, roomKey, StringComparison.OrdinalIgnoreCase);
                }

                foreach (LiftListItemViewModel item in ListViewModel.Lifts)
                {
                    item.IsSelected = false;
                }

                RoomListItemViewModel selectedItem = null;
                if (!isSameSelected)
                {
                    selectedItem = ListViewModel.Rooms
                        .FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase));
                }

                ListViewModel.SetSelectedRoomSilently(selectedItem);
                ListViewModel.SetSelectedLiftSilently(null);
            });

            if (isSameSelected)
            {
                _ = RequestClearLeftSelectionHighlightAsync();
            }
            else
            {
                // Restore old room focus behavior: select card + focus view to the room.
                // Previous highlight-only behavior kept for possible future reuse.
                // _ = RequestHighlightRoomOnlyAsync(roomKey);
                _ = RequestFocusRoomAsync(roomKey);
            }
        }

        internal static void SyncListSelectionFromEditorRoom(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            SelectRoomByKeyForFocusOnly(roomKey);
            _ = RequestFocusRoomAsync(roomKey);
        }

        internal static void ToggleLiftSelectionForLeftOnly(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return;
            }

            bool isSameSelected;
            lock (SyncRoot)
            {
                isSameSelected = string.Equals(_selectedLiftKey, liftKey, StringComparison.OrdinalIgnoreCase);
                _selectedRoomKey = null;
                _selectedLiftKey = isSameSelected ? null : liftKey;
                if (_state != null)
                {
                    _state.SelectedLiftKey = isSameSelected ? null : liftKey;
                }
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = false;
                }

                foreach (LiftListItemViewModel item in ListViewModel.Lifts)
                {
                    item.IsSelected = !isSameSelected && string.Equals(item.Key, liftKey, StringComparison.OrdinalIgnoreCase);
                }

                LiftListItemViewModel selectedItem = null;
                if (!isSameSelected)
                {
                    selectedItem = ListViewModel.Lifts
                        .FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                }

                ListViewModel.SetSelectedRoomSilently(null);
                ListViewModel.SetSelectedLiftSilently(selectedItem);
            });

            if (isSameSelected)
            {
                _ = RequestClearLeftSelectionHighlightAsync();
            }
            else
            {
                // Restore lift focus behavior, but preserve current 3D view orientation.
                // Old lift focus may switch to TOP view, do not use for left card click.
                // _ = RequestFocusLiftAsync(liftKey);
                // Previous highlight-only behavior kept for possible future reuse.
                // _ = RequestHighlightLiftOnlyAsync(liftKey);
                _ = RequestFocusLiftPreserveViewAsync(liftKey);
            }
        }

        private static void SelectLiftByKeyForFocusOnly(string liftKey)
        {
            if (string.IsNullOrWhiteSpace(liftKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                _selectedRoomKey = null;
                _selectedLiftKey = liftKey;

                if (_state != null)
                {
                    _state.SelectedLiftKey = liftKey;
                }
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = false;
                }

                foreach (LiftListItemViewModel item in ListViewModel.Lifts)
                {
                    item.IsSelected = string.Equals(item.Key, liftKey, StringComparison.OrdinalIgnoreCase);
                }

                LiftListItemViewModel selectedItem = ListViewModel.Lifts
                    .FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(null);
                ListViewModel.SetSelectedLiftSilently(selectedItem);
            });
        }

        private static void SelectProbeRoomCardForFocusOnly(string stableRoomKey)
        {
            if (string.IsNullOrWhiteSpace(stableRoomKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_state == null || _state.Mode != RoomRecognitionPaneMode.Probe)
                {
                    return;
                }

                _state.SelectedProbeRoomStableKey = stableRoomKey;
                _selectedRoomKey = null;
                _selectedLiftKey = null;
            }

            ExecuteOnUiThread(() =>
            {
                foreach (RoomListItemViewModel item in ListViewModel.Rooms)
                {
                    item.IsSelected = string.Equals(item.StableRoomKey, stableRoomKey, StringComparison.OrdinalIgnoreCase);
                }

                RoomListItemViewModel selectedItem = ListViewModel.Rooms
                    .FirstOrDefault(x => string.Equals(x.StableRoomKey, stableRoomKey, StringComparison.OrdinalIgnoreCase));
                ListViewModel.SetSelectedRoomSilently(selectedItem);
                ListViewModel.SetSelectedLiftSilently(null);
            });
        }

        private static XYZ ResolveRoomRoutePoint(RoomSemanticRecord room)
        {
            if (room == null)
            {
                return null;
            }

            if (room.Centroid != null)
            {
                return room.Centroid;
            }

            if (room.BBox != null && room.BBox.Min != null && room.BBox.Max != null)
            {
                return new XYZ(
                    (room.BBox.Min.X + room.BBox.Max.X) * 0.5,
                    (room.BBox.Min.Y + room.BBox.Max.Y) * 0.5,
                    (room.BBox.Min.Z + room.BBox.Max.Z) * 0.5);
            }

            if (room.LoopPoints != null && room.LoopPoints.Count > 0)
            {
                return new XYZ(
                    room.LoopPoints.Average(x => x.X),
                    room.LoopPoints.Average(x => x.Y),
                    room.LoopPoints.Average(x => x.Z));
            }

            return null;
        }

        private static AhuPlacementValidationPreparationResult CreateAhuPlacementValidationPreparationFailure(string message)
        {
            return new AhuPlacementValidationPreparationResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message)
                    ? "Failed to prepare AHU room fit validation."
                    : message,
                SessionId = string.Empty,
                RoomKey = string.Empty,
                PlacementXmm = 0,
                PlacementYmm = 0
            };
        }

        private static DeliveryRoutePreparationResult CreateDeliveryRoutePreparationFailure(string message)
        {
            return new DeliveryRoutePreparationResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Failed to generate delivery route." : message
            };
        }

        private static CalculatePathExecutionResult ShowDeliveryRouteFailure(
            UIApplication app,
            string message,
            string responseBody,
            double? pathLengthMeters)
        {
            string responseMessage = CalculatePathApiService.ExtractResponseMessage(responseBody);
            if (string.IsNullOrWhiteSpace(responseMessage))
            {
                responseMessage = CalculatePathApiService.ExtractResponseMessage(message);
            }
            string finalMessage = !string.IsNullOrWhiteSpace(responseMessage)
                ? responseMessage
                : string.IsNullOrWhiteSpace(message)
                ? "Failed to generate delivery route."
                : message;

            LocalizedDialogService.Error(app, finalMessage);
            return new CalculatePathExecutionResult
            {
                Success = false,
                Drawn = false,
                Message = finalMessage,
                ResponseBody = responseBody,
                PathLengthMeters = pathLengthMeters
            };
        }

        private static void AssignRoomDisplayNames(RoomRecognitionPaneState state)
        {
            if (state == null || state.RoomDisplayNameByKey == null)
            {
                return;
            }

            state.RoomDisplayNameByKey.Clear();

            List<RoomSemanticRecord> rooms = (state.Summary?.RunResult?.Rooms ?? new List<RoomSemanticRecord>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .OrderByDescending(IsMatchedRoom)
                .ThenByDescending(x => x.AreaM2)
                .ThenBy(x => x.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groups = rooms
                .GroupBy(x => NormalizeRoomDisplayBaseName(x.RoomName), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                string baseName = string.IsNullOrWhiteSpace(group.Key)
                    ? Loc.T("DockablePane.RoomDetail.UnnamedRoom")
                    : group.Key;

                List<RoomSemanticRecord> groupRooms = group.ToList();
                bool needsNumber = groupRooms.Count > 1;
                for (int i = 0; i < groupRooms.Count; i++)
                {
                    RoomSemanticRecord room = groupRooms[i];
                    state.RoomDisplayNameByKey[room.Key] = needsNumber
                        ? baseName + (i + 1).ToString()
                        : baseName;
                }
            }
        }

        private static void ApplyNameOverrides(Document doc, RoomRecognitionPaneState state)
        {
            if (doc == null || state == null)
            {
                return;
            }

            RoomRecognitionNameOverrideData overrides = RoomRecognitionNameOverrideStorageService.Load(doc);
            if (overrides == null)
            {
                return;
            }

            if (state.RoomDisplayNameByKey != null && overrides.RoomNames != null)
            {
                foreach (KeyValuePair<string, string> pair in overrides.RoomNames)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        state.RoomDisplayNameByKey[pair.Key] = pair.Value.Trim();
                    }
                }
            }

            if (state.LiftDisplayNameByKey != null)
            {
                state.LiftDisplayNameByKey.Clear();
                if (overrides.LiftNames != null)
                {
                    foreach (KeyValuePair<string, string> pair in overrides.LiftNames)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        {
                            state.LiftDisplayNameByKey[pair.Key] = pair.Value.Trim();
                        }
                    }
                }
            }
        }

        private static void ApplyLiftDisplayOverrides(Document doc, RoomRecognitionPaneState state)
        {
            if (doc == null || state == null || state.LiftDisplayOverrideByKey == null)
            {
                return;
            }

            state.LiftDisplayOverrideByKey.Clear();
            foreach (KeyValuePair<string, LiftDisplayOverride> pair in LiftDisplayOverrideStorageService.Load(doc))
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    state.LiftDisplayOverrideByKey[pair.Key] = pair.Value;
                }
            }
        }

        private static string ResolveRoomDisplayName(RoomRecognitionPaneState snapshot, string roomKey, string fallbackName)
        {
            if (snapshot != null &&
                !string.IsNullOrWhiteSpace(roomKey) &&
                snapshot.RoomDisplayNameByKey != null &&
                snapshot.RoomDisplayNameByKey.TryGetValue(roomKey, out string displayName) &&
                !string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            return NormalizeRoomDisplayBaseName(fallbackName);
        }

        private static string ResolveLiftDisplayName(LiftRecognitionRecord lift)
        {
            if (lift == null)
            {
                return "-";
            }

            string key = lift.Key ?? string.Empty;
            lock (SyncRoot)
            {
                if (_state != null &&
                    _state.LiftDisplayNameByKey != null &&
                    !string.IsNullOrWhiteSpace(key) &&
                    _state.LiftDisplayNameByKey.TryGetValue(key, out string displayName) &&
                    !string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            return string.IsNullOrWhiteSpace(lift.LiftName) ? "-" : lift.LiftName.Trim();
        }

        private static string NormalizeRoomDisplayBaseName(string roomName)
        {
            return string.IsNullOrWhiteSpace(roomName)
                ? Loc.T("DockablePane.RoomDetail.UnnamedRoom")
                : roomName.Trim();
        }

        private static string ResolveLevelName(
            Document doc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            string roomKey)
        {
            if (doc == null || summary == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return Loc.T("Common.NA");
            }

            if (!summary.SeedLevelIdByKey.TryGetValue(roomKey, out int levelIdValue) || levelIdValue <= 0)
            {
                return Loc.T("Common.NA");
            }

            Level level = doc.GetElement(new ElementId(levelIdValue)) as Level;
            return level != null && !string.IsNullOrWhiteSpace(level.Name) ? level.Name : Loc.T("Common.NA");
        }

        private static ElementId ResolveRoomLevelId(RoomRecognitionPaneState snapshot, string roomKey)
        {
            if (snapshot == null || snapshot.Summary == null || string.IsNullOrWhiteSpace(roomKey))
            {
                return ElementId.InvalidElementId;
            }

            return snapshot.Summary.SeedLevelIdByKey.TryGetValue(roomKey, out int levelIdValue) && levelIdValue > 0
                ? new ElementId(levelIdValue)
                : ElementId.InvalidElementId;
        }

        private static RoomCardMetricDisplay BuildRoomCardMetricDisplay(
            RoomSemanticRecord room,
            string availableUsableAreaText,
            ElementId levelId,
            IList<XYZ> fallbackLoopPoints = null)
        {
            BoundingBoxXYZ bbox = room != null ? room.BBox : null;
            IList<XYZ> loopPoints = room != null && room.LoopPoints != null && room.LoopPoints.Count > 0
                ? room.LoopPoints
                : fallbackLoopPoints;

            string lengthText = "-";
            string widthText = "-";
            if (TryGetPlanExtentsFeet(bbox, loopPoints, out double extentXFeet, out double extentYFeet))
            {
                double lengthFeet = Math.Max(extentXFeet, extentYFeet);
                double widthFeet = Math.Min(extentXFeet, extentYFeet);
                lengthText = FormatLengthFeetAsMm(lengthFeet);
                widthText = FormatLengthFeetAsMm(widthFeet);
            }

            string heightText = ResolveRoomBoundaryWallHeightText(_doc, room);
            DoorMetricDisplay doorMetrics = ResolveRoomDoorMetricText(_doc, room, levelId);

            return new RoomCardMetricDisplay
            {
                RoomLengthText = lengthText,
                RoomWidthText = widthText,
                RoomHeightText = heightText,
                DoorWidthText = doorMetrics.WidthText,
                DoorHeightText = doorMetrics.HeightText,
                AvailableUsableAreaText = string.IsNullOrWhiteSpace(availableUsableAreaText) ? Loc.T("Common.NA") : availableUsableAreaText
            };
        }

        private static bool TryGetPlanExtentsFeet(
            BoundingBoxXYZ bbox,
            IList<XYZ> loopPoints,
            out double extentXFeet,
            out double extentYFeet)
        {
            extentXFeet = 0.0;
            extentYFeet = 0.0;

            if (bbox != null && bbox.Min != null && bbox.Max != null)
            {
                double bboxX = Math.Abs(bbox.Max.X - bbox.Min.X);
                double bboxY = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                if (IsPositiveFinite(bboxX) && IsPositiveFinite(bboxY))
                {
                    extentXFeet = bboxX;
                    extentYFeet = bboxY;
                    return true;
                }
            }

            if (loopPoints == null || loopPoints.Count == 0)
            {
                return false;
            }

            bool hasPoint = false;
            double minX = 0.0;
            double maxX = 0.0;
            double minY = 0.0;
            double maxY = 0.0;
            foreach (XYZ point in loopPoints)
            {
                if (point == null)
                {
                    continue;
                }

                if (!hasPoint)
                {
                    minX = maxX = point.X;
                    minY = maxY = point.Y;
                    hasPoint = true;
                    continue;
                }

                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            if (!hasPoint)
            {
                return false;
            }

            double loopX = Math.Abs(maxX - minX);
            double loopY = Math.Abs(maxY - minY);
            if (!IsPositiveFinite(loopX) || !IsPositiveFinite(loopY))
            {
                return false;
            }

            extentXFeet = loopX;
            extentYFeet = loopY;
            return true;
        }

        private static DoorMetricDisplay ResolveRoomDoorMetricText(Document doc, RoomSemanticRecord room, ElementId levelId)
        {
            DoorMetricCandidate best = ResolveRoomDoorMetricCandidate(doc, room, levelId);
            return new DoorMetricDisplay
            {
                WidthText = best != null && IsPositiveFinite(best.WidthMm) ? Math.Round(best.WidthMm).ToString("F0") + " mm" : "-",
                HeightText = best != null && IsPositiveFinite(best.HeightMm) ? Math.Round(best.HeightMm).ToString("F0") + " mm" : "-"
            };
        }

        private static DoorMetricCandidate ResolveRoomDoorMetricCandidate(Document doc, RoomSemanticRecord room, ElementId levelId)
        {
            if (doc == null || room == null)
            {
                return null;
            }

            List<DoorMetricCandidate> candidates = new List<DoorMetricCandidate>();
            AddBoundaryWallInsertDoorMetricCandidates(doc, room, levelId, candidates);
            AddFallbackDoorMetricCandidates(doc, room, levelId, candidates);

            foreach (DoorMetricCandidate candidate in candidates)
            {
                if (candidate == null || candidate.Center == null)
                {
                    continue;
                }

                candidate.BoundaryDistance = DistanceToRoomBoundaryFeet(room, candidate.Center);
            }

            DoorMetricCandidate best = candidates
                .Where(x => x != null && IsPositiveFinite(x.WidthMm) && IsPositiveFinite(x.HeightMm) && IsAcceptableRoomDoorMetricCandidate(room, x))
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.BoundaryDistance)
                .FirstOrDefault();
            if (best != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomDoorMetric] RoomKey=" + (room.Key ?? string.Empty) +
                    ", DoorId=" + (best.ElementId != null ? best.ElementId.IntegerValue.ToString() : "-") +
                    ", WidthMm=" + best.WidthMm.ToString("F1") +
                    ", HeightMm=" + best.HeightMm.ToString("F1") +
                    ", WidthSource=" + (best.WidthSource ?? string.Empty) +
                    ", HeightSource=" + (best.HeightSource ?? string.Empty));
            }

            return best;
        }

        private static void AddBoundaryWallInsertDoorMetricCandidates(
            Document doc,
            RoomSemanticRecord room,
            ElementId levelId,
            List<DoorMetricCandidate> candidates)
        {
            if (doc == null || room == null || room.BoundaryWalls == null || candidates == null)
            {
                return;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (RoomBoundaryWallReference wallRef in room.BoundaryWalls)
            {
                if (wallRef == null || wallRef.ElementId <= 0)
                {
                    continue;
                }

                Wall wall = doc.GetElement(new ElementId(wallRef.ElementId)) as Wall;
                if (wall == null)
                {
                    continue;
                }

                ICollection<ElementId> insertIds;
                try
                {
                    insertIds = wall.FindInserts(true, true, true, true);
                }
                catch
                {
                    insertIds = null;
                }

                if (insertIds == null)
                {
                    continue;
                }

                foreach (ElementId insertId in insertIds)
                {
                    if (insertId == null || insertId == ElementId.InvalidElementId || !seen.Add(insertId.IntegerValue))
                    {
                        continue;
                    }

                    Element insert = doc.GetElement(insertId);
                    if (!IsDoorMetricElement(insert) || !IsElementNearRoomLevel(doc, insert, levelId))
                    {
                        continue;
                    }

                    DoorMetricCandidate candidate;
                    if (TryBuildDoorMetricCandidate(doc, insert, wall, IsMarkedDoorOpening(insert) ? 0 : 10, out candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }
        }

        private static void AddFallbackDoorMetricCandidates(
            Document doc,
            RoomSemanticRecord room,
            ElementId levelId,
            List<DoorMetricCandidate> candidates)
        {
            if (doc == null || room == null || candidates == null)
            {
                return;
            }

            foreach (Opening opening in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
            {
                if (opening == null || !IsElementNearRoomLevel(doc, opening, levelId))
                {
                    continue;
                }

                XYZ center = GetElementBoundingBoxCenter(opening);
                bool marked = IsMarkedDoorOpening(opening);
                if (center == null || (!marked && !IsDoorCenterNearRoomBoundary(room, center, 800.0)))
                {
                    continue;
                }

                Wall hostWall = ResolveNearestBoundaryWall(doc, room, center);
                DoorMetricCandidate candidate;
                if (TryBuildDoorMetricCandidate(doc, opening, hostWall, marked ? 20 : 50, out candidate))
                {
                    candidates.Add(candidate);
                }
            }

            foreach (FamilyInstance door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                if (door == null || !IsElementNearRoomLevel(doc, door, levelId))
                {
                    continue;
                }

                XYZ center = GetElementBoundingBoxCenter(door) ?? ((door.Location as LocationPoint)?.Point);
                if (center == null || !IsDoorCenterNearRoomBoundary(room, center, 800.0))
                {
                    continue;
                }

                Wall hostWall = door.Host as Wall ?? ResolveNearestBoundaryWall(doc, room, center);
                DoorMetricCandidate candidate;
                if (TryBuildDoorMetricCandidate(doc, door, hostWall, 30, out candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        private static bool TryBuildDoorMetricCandidate(
            Document doc,
            Element element,
            Wall hostWall,
            int priority,
            out DoorMetricCandidate candidate)
        {
            candidate = null;
            if (doc == null || element == null)
            {
                return false;
            }

            XYZ center = GetElementBoundingBoxCenter(element);
            if (center == null)
            {
                return false;
            }

            double widthMm = 0.0;
            double heightMm = 0.0;
            string widthSource = string.Empty;
            string heightSource = string.Empty;
            FamilyInstance door = element as FamilyInstance;
            if (door != null)
            {
                widthMm = ResolveDoorFamilyLengthMm(door, true, out widthSource);
                heightMm = ResolveDoorFamilyLengthMm(door, false, out heightSource);
            }

            if (!IsPositiveFinite(widthMm) || !IsPositiveFinite(heightMm))
            {
                ResolveDoorOpeningBoxMetricMm(element, hostWall, out double boxWidthMm, out double boxHeightMm);
                if (!IsPositiveFinite(widthMm))
                {
                    widthMm = boxWidthMm;
                    widthSource = "BoundingBox";
                }

                if (!IsPositiveFinite(heightMm))
                {
                    heightMm = boxHeightMm;
                    heightSource = "BoundingBox";
                }
            }

            if (!IsPositiveFinite(widthMm) || !IsPositiveFinite(heightMm))
            {
                return false;
            }

            candidate = new DoorMetricCandidate
            {
                Center = center,
                WidthMm = widthMm,
                HeightMm = heightMm,
                Priority = priority,
                ElementId = element.Id,
                WidthSource = widthSource,
                HeightSource = heightSource
            };
            return true;
        }

        private static double ResolveDoorFamilyLengthMm(FamilyInstance door, bool width, out string source)
        {
            source = string.Empty;
            FamilySymbol symbol = door != null ? door.Symbol : null;
            IEnumerable<string> actualNames = width
                ? new[] { "Width", "Door Width", "宽度", "寬度" }
                : new[] { "Height", "Door Height", "高度" };
            IEnumerable<string> roughNames = width
                ? new[] { "Rough Width" }
                : new[] { "Rough Height" };

            double value = ResolveDoorFamilyLengthMmFromElement(symbol, actualNames, width ? "Type.Width" : "Type.Height", out source);
            if (IsPositiveFinite(value))
            {
                return value;
            }

            value = ResolveDoorFamilyLengthMmFromElement(door, actualNames, width ? "Instance.Width" : "Instance.Height", out source);
            if (IsPositiveFinite(value))
            {
                return value;
            }

            value = ResolveDoorFamilyLengthMmFromElement(symbol, roughNames, width ? "Type.RoughWidth" : "Type.RoughHeight", out source);
            if (IsPositiveFinite(value))
            {
                return value;
            }

            value = ResolveDoorFamilyLengthMmFromElement(door, roughNames, width ? "Instance.RoughWidth" : "Instance.RoughHeight", out source);
            return IsPositiveFinite(value) ? value : 0.0;
        }

        private static double ResolveDoorFamilyLengthMmFromElement(Element element, IEnumerable<string> names, string sourcePrefix, out string source)
        {
            source = string.Empty;
            Parameter parameter = FindPositiveLengthParameter(element, names);
            if (parameter != null && TryConvertInternalLengthToMm(parameter.AsDouble(), out double valueMm))
            {
                source = sourcePrefix + "." + (parameter.Definition != null ? parameter.Definition.Name ?? string.Empty : string.Empty);
                return valueMm;
            }

            return 0.0;
        }

        private static Parameter FindPositiveLengthParameter(Element element, IEnumerable<string> names)
        {
            if (element == null)
            {
                return null;
            }

            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.Double && parameter.AsDouble() > 1.0e-6)
                {
                    return parameter;
                }
            }

            return null;
        }

        private static void ResolveDoorOpeningBoxMetricMm(Element element, Wall hostWall, out double widthMm, out double heightMm)
        {
            widthMm = 0.0;
            heightMm = 0.0;
            BoundingBoxXYZ box = element != null ? element.get_BoundingBox(null) : null;
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            double heightFeet = Math.Abs(box.Max.Z - box.Min.Z);
            TryConvertInternalLengthToMm(heightFeet, out heightMm);

            XYZ wallDirection = ResolveWallDirection(hostWall);
            if (wallDirection != null)
            {
                List<XYZ> corners = BuildBoxPlanCorners(box);
                double min = corners.Min(x => Dot2D(x, wallDirection));
                double max = corners.Max(x => Dot2D(x, wallDirection));
                TryConvertInternalLengthToMm(Math.Abs(max - min), out widthMm);
                return;
            }

            double widthFeet = Math.Max(Math.Abs(box.Max.X - box.Min.X), Math.Abs(box.Max.Y - box.Min.Y));
            TryConvertInternalLengthToMm(widthFeet, out widthMm);
        }

        private static List<XYZ> BuildBoxPlanCorners(BoundingBoxXYZ box)
        {
            return new List<XYZ>
            {
                new XYZ(box.Min.X, box.Min.Y, 0.0),
                new XYZ(box.Min.X, box.Max.Y, 0.0),
                new XYZ(box.Max.X, box.Min.Y, 0.0),
                new XYZ(box.Max.X, box.Max.Y, 0.0)
            };
        }

        private static XYZ ResolveWallDirection(Wall wall)
        {
            LocationCurve location = wall != null ? wall.Location as LocationCurve : null;
            Line line = location != null ? location.Curve as Line : null;
            if (line == null)
            {
                return null;
            }

            XYZ direction = line.Direction;
            double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            return length > 1.0e-9 ? new XYZ(direction.X / length, direction.Y / length, 0.0) : null;
        }

        private static bool IsDoorMetricElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            if (element is Opening || IsMarkedDoorOpening(element))
            {
                return true;
            }

            Category category = element.Category;
            if (category != null && category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
            {
                return true;
            }

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null)
            {
                string text = ((familyInstance.Symbol != null ? familyInstance.Symbol.Name : string.Empty) + " " +
                               (familyInstance.Symbol != null && familyInstance.Symbol.Family != null ? familyInstance.Symbol.Family.Name : string.Empty) + " " +
                               (familyInstance.Name ?? string.Empty)).ToLowerInvariant();
                return text.Contains("door") || text.Contains("opening");
            }

            string name = (element.Name ?? string.Empty).ToLowerInvariant();
            return name.Contains("door") || name.Contains("opening");
        }

        private static bool IsMarkedDoorOpening(Element element)
        {
            if (element == null)
            {
                return false;
            }

            string comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;
            string mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
            string name = element.Name ?? string.Empty;
            return ContainsInvariant(comments, "CadToRevit_DoorOpening") ||
                   ContainsInvariant(mark, "CadToRevit_DoorOpening") ||
                   ContainsInvariant(name, "CadToRevit_DoorOpening") ||
                   ContainsInvariant(comments, "RVT_DoorFamilyConvertedToOpening") ||
                   ContainsInvariant(mark, "RVT_DoorFamilyConvertedToOpening") ||
                   ContainsInvariant(name, "RVT_DoorFamilyConvertedToOpening");
        }

        private static bool ContainsInvariant(string text, string token)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsElementNearRoomLevel(Document doc, Element element, ElementId levelId)
        {
            if (doc == null || element == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            Level level = doc.GetElement(levelId) as Level;
            if (level == null)
            {
                return true;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                ElementId elementLevelId = ResolveElementLevelId(element);
                return elementLevelId == null || elementLevelId == ElementId.InvalidElementId || elementLevelId.IntegerValue == levelId.IntegerValue;
            }

            double z = (box.Min.Z + box.Max.Z) * 0.5;
            double tolerance = UnitUtils.ConvertToInternalUnits(5000.0, UnitTypeId.Millimeters);
            return Math.Abs(z - level.Elevation) <= tolerance ||
                   (box.Min.Z <= level.Elevation + tolerance && box.Max.Z >= level.Elevation - tolerance);
        }

        private static ElementId ResolveElementLevelId(Element element)
        {
            if (element == null)
            {
                return ElementId.InvalidElementId;
            }

            Parameter levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) ??
                                   element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ??
                                   element.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT) ??
                                   element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            ElementId levelId = levelParam != null ? levelParam.AsElementId() : ElementId.InvalidElementId;
            return levelId != null ? levelId : ElementId.InvalidElementId;
        }

        private static bool IsAcceptableRoomDoorMetricCandidate(RoomSemanticRecord room, DoorMetricCandidate candidate)
        {
            if (room == null || candidate == null || candidate.Center == null)
            {
                return false;
            }

            if (!IsPointInsideExpandedRoomBoundingBox(room, candidate.Center, 800.0))
            {
                return false;
            }

            bool insideLoop = IsPointInsideRoomLoop(room.LoopPoints, candidate.Center);
            double boundaryTolerance = UnitUtils.ConvertToInternalUnits(800.0, UnitTypeId.Millimeters);
            bool nearBoundary = candidate.BoundaryDistance <= boundaryTolerance;
            return candidate.Priority <= 10 ? insideLoop || nearBoundary : nearBoundary;
        }

        private static bool IsDoorCenterNearRoomBoundary(RoomSemanticRecord room, XYZ point, double maxDistanceMm)
        {
            double distance = DistanceToRoomBoundaryFeet(room, point);
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance == double.MaxValue)
            {
                return false;
            }

            return distance <= UnitUtils.ConvertToInternalUnits(Math.Max(0.0, maxDistanceMm), UnitTypeId.Millimeters);
        }

        private static bool IsPointInsideExpandedRoomBoundingBox(RoomSemanticRecord room, XYZ point, double marginMm)
        {
            if (room == null || point == null || room.BBox == null || room.BBox.Min == null || room.BBox.Max == null)
            {
                return true;
            }

            double margin = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, marginMm), UnitTypeId.Millimeters);
            return point.X >= Math.Min(room.BBox.Min.X, room.BBox.Max.X) - margin &&
                   point.X <= Math.Max(room.BBox.Min.X, room.BBox.Max.X) + margin &&
                   point.Y >= Math.Min(room.BBox.Min.Y, room.BBox.Max.Y) - margin &&
                   point.Y <= Math.Max(room.BBox.Min.Y, room.BBox.Max.Y) + margin;
        }

        private static bool IsPointInsideRoomLoop(IList<XYZ> loopPoints, XYZ point)
        {
            if (loopPoints == null || loopPoints.Count < 3 || point == null)
            {
                return false;
            }

            List<XYZ> pts = loopPoints.Where(x => x != null).ToList();
            if (pts.Count < 3)
            {
                return false;
            }

            bool inside = false;
            int j = pts.Count - 1;
            for (int i = 0; i < pts.Count; i++)
            {
                double xi = pts[i].X;
                double yi = pts[i].Y;
                double xj = pts[j].X;
                double yj = pts[j].Y;
                bool intersect = ((yi > point.Y) != (yj > point.Y)) &&
                                 (point.X < (xj - xi) * (point.Y - yi) / ((yj - yi) == 0.0 ? 1e-12 : (yj - yi)) + xi);
                if (intersect)
                {
                    inside = !inside;
                }

                j = i;
            }

            return inside;
        }

        private static double DistanceToRoomBoundaryFeet(RoomSemanticRecord room, XYZ point)
        {
            if (room == null || point == null || room.LoopPoints == null || room.LoopPoints.Count < 2)
            {
                return double.MaxValue;
            }

            List<XYZ> pts = room.LoopPoints.Where(x => x != null).ToList();
            if (pts.Count < 2)
            {
                return double.MaxValue;
            }

            double min = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a = pts[i];
                XYZ b = pts[(i + 1) % pts.Count];
                double d = DistancePointToSegmentXY(point, a, b);
                if (d < min)
                {
                    min = d;
                }
            }

            return min;
        }

        private static double DistancePointToSegmentXY(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                return HorizontalDistanceXY(p, a);
            }

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            XYZ projected = new XYZ(a.X + t * dx, a.Y + t * dy, p.Z);
            return HorizontalDistanceXY(p, projected);
        }

        private static double HorizontalDistanceXY(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Dot2D(XYZ point, XYZ direction)
        {
            return point.X * direction.X + point.Y * direction.Y;
        }

        private static XYZ GetElementBoundingBoxCenter(Element element)
        {
            BoundingBoxXYZ box = element != null ? element.get_BoundingBox(null) : null;
            return box != null && box.Min != null && box.Max != null ? (box.Min + box.Max) * 0.5 : null;
        }

        private static Wall ResolveNearestBoundaryWall(Document doc, RoomSemanticRecord room, XYZ point)
        {
            if (doc == null || room == null || point == null || room.BoundaryWalls == null)
            {
                return null;
            }

            Wall bestWall = null;
            double bestDistance = double.MaxValue;
            foreach (RoomBoundaryWallReference wallRef in room.BoundaryWalls)
            {
                if (wallRef == null || wallRef.ElementId <= 0)
                {
                    continue;
                }

                Wall wall = doc.GetElement(new ElementId(wallRef.ElementId)) as Wall;
                LocationCurve location = wall != null ? wall.Location as LocationCurve : null;
                Curve curve = location != null ? location.Curve : null;
                if (curve == null)
                {
                    continue;
                }

                IntersectionResult projection = curve.Project(point);
                XYZ projected = projection != null ? projection.XYZPoint : null;
                double distance = projected != null ? HorizontalDistanceXY(point, projected) : double.MaxValue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestWall = wall;
                }
            }

            return bestWall;
        }

        private static string ResolveRoomBoundaryWallHeightText(Document doc, RoomSemanticRecord room)
        {
            if (doc == null || room == null || room.BoundaryWalls == null || room.BoundaryWalls.Count == 0)
            {
                return "-";
            }

            List<double> heightsMm = new List<double>();
            foreach (RoomBoundaryWallReference wallRef in room.BoundaryWalls)
            {
                if (wallRef == null || wallRef.ElementId <= 0)
                {
                    continue;
                }

                Wall wall = doc.GetElement(new ElementId(wallRef.ElementId)) as Wall;
                if (wall == null)
                {
                    continue;
                }

                if (TryResolveWallHeightMm(doc, wall, out double heightMm))
                {
                    heightsMm.Add(heightMm);
                }
            }

            if (heightsMm.Count == 0)
            {
                return "-";
            }

            double selectedHeightMm = heightsMm.Max();
            return Math.Round(selectedHeightMm).ToString("F0") + " mm";
        }

        private static bool TryResolveWallHeightMm(Document doc, Wall wall, out double heightMm)
        {
            heightMm = 0.0;
            if (doc == null || wall == null)
            {
                return false;
            }

            Parameter unconnectedHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (unconnectedHeight != null &&
                unconnectedHeight.StorageType == StorageType.Double &&
                TryConvertInternalLengthToMm(unconnectedHeight.AsDouble(), out heightMm))
            {
                return true;
            }

            if (TryResolveConstrainedWallHeightMm(doc, wall, out heightMm))
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveConstrainedWallHeightMm(Document doc, Wall wall, out double heightMm)
        {
            heightMm = 0.0;
            Parameter baseConstraint = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            ElementId baseLevelId = baseConstraint != null ? baseConstraint.AsElementId() : ElementId.InvalidElementId;
            ElementId topLevelId = topConstraint != null ? topConstraint.AsElementId() : ElementId.InvalidElementId;

            Level baseLevel = baseLevelId != ElementId.InvalidElementId ? doc.GetElement(baseLevelId) as Level : null;
            Level topLevel = topLevelId != ElementId.InvalidElementId ? doc.GetElement(topLevelId) as Level : null;
            if (baseLevel == null || topLevel == null)
            {
                return false;
            }

            double baseOffset = 0.0;
            Parameter baseOffsetParameter = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (baseOffsetParameter != null && baseOffsetParameter.StorageType == StorageType.Double)
            {
                baseOffset = baseOffsetParameter.AsDouble();
            }

            double topOffset = 0.0;
            Parameter topOffsetParameter = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
            if (topOffsetParameter != null && topOffsetParameter.StorageType == StorageType.Double)
            {
                topOffset = topOffsetParameter.AsDouble();
            }

            double heightFeet = Math.Abs((topLevel.Elevation + topOffset) - (baseLevel.Elevation + baseOffset));
            return TryConvertInternalLengthToMm(heightFeet, out heightMm);
        }

        private static bool TryConvertInternalLengthToMm(double lengthFeet, out double lengthMm)
        {
            lengthMm = 0.0;
            if (!IsPositiveFinite(lengthFeet))
            {
                return false;
            }

            try
            {
                lengthMm = UnitUtils.ConvertFromInternalUnits(lengthFeet, UnitTypeId.Millimeters);
            }
            catch
            {
                lengthMm = lengthFeet * 304.8;
            }

            return IsPositiveFinite(lengthMm);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 1.0e-6 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FormatLengthFeetAsMm(double lengthFeet)
        {
            double lengthMm = lengthFeet * 304.8;
            if (!IsPositiveFinite(lengthMm))
            {
                return "-";
            }

            return Math.Round(lengthMm).ToString("F0") + " mm";
        }

        private static string BuildRoomSizeLine(RoomCardMetricDisplay metrics)
        {
            return "Room Size(mm) : L:" + StripMetricUnit(metrics != null ? metrics.RoomLengthText : null) +
                " x W:" + StripMetricUnit(metrics != null ? metrics.RoomWidthText : null) +
                " x H:" + StripMetricUnit(metrics != null ? metrics.RoomHeightText : null);
        }

        private static string BuildRoomDoorSizeLine(RoomCardMetricDisplay metrics)
        {
            return "Door Size(mm) : W:" + StripMetricUnit(metrics != null ? metrics.DoorWidthText : null) +
                " x H:" + StripMetricUnit(metrics != null ? metrics.DoorHeightText : null);
        }

        private static string BuildRoomAreaLine(string areaText)
        {
            return "Area(m2) : " + StripAreaUnit(areaText);
        }

        private static string StripMetricUnit(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), Loc.T("Common.NA"), StringComparison.OrdinalIgnoreCase))
            {
                return "-";
            }

            string result = value.Trim();
            if (result.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - 2).Trim();
            }

            return string.IsNullOrWhiteSpace(result) ? "-" : result;
        }

        private static string StripAreaUnit(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), Loc.T("Common.NA"), StringComparison.OrdinalIgnoreCase))
            {
                return "-";
            }

            string result = value.Trim()
                .Replace("m²", string.Empty)
                .Replace("m2", string.Empty)
                .Trim();
            return string.IsNullOrWhiteSpace(result) ? "-" : result;
        }
        private static string BuildRoomCardLine(string label, string value)
        {
            return label + ": " + (string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private sealed class RoomCardMetricDisplay
        {
            public string RoomLengthText { get; set; }

            public string RoomWidthText { get; set; }

            public string RoomHeightText { get; set; }

            public string DoorWidthText { get; set; }

            public string DoorHeightText { get; set; }

            public string AvailableUsableAreaText { get; set; }
        }

        private sealed class DoorMetricDisplay
        {
            public string WidthText { get; set; }

            public string HeightText { get; set; }
        }

        private sealed class DoorMetricCandidate
        {
            public XYZ Center { get; set; }

            public double WidthMm { get; set; }

            public double HeightMm { get; set; }

            public int Priority { get; set; }

            public double BoundaryDistance { get; set; } = double.MaxValue;

            public ElementId ElementId { get; set; }

            public string WidthSource { get; set; }

            public string HeightSource { get; set; }
        }

        private static string FormatArea(double areaM2)
        {
            if (areaM2 <= 0.0)
            {
                return Loc.T("Common.NA");
            }

            return areaM2.ToString("F1") + " m²";
        }

        private static List<EditorWallOptionViewModel> BuildEditorWallOptions(RoomSemanticRecord room)
        {
            List<EditorWallOptionViewModel> result = new List<EditorWallOptionViewModel>();
            if (room == null || room.BoundaryWalls == null)
            {
                return result;
            }

            HashSet<int> usedIds = new HashSet<int>();
            int displayIndex = 1;
            foreach (RoomBoundaryWallReference wall in room.BoundaryWalls)
            {
                if (wall == null || wall.ElementId <= 0 || !usedIds.Add(wall.ElementId))
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(wall.DisplayName)
                    ? "WALL-" + displayIndex.ToString("0000")
                    : wall.DisplayName;

                result.Add(new EditorWallOptionViewModel
                {
                    DisplayName = displayName,
                    ElementId = wall.ElementId,
                    UniqueId = wall.UniqueId ?? string.Empty,
                    RevitName = wall.RevitName ?? string.Empty,
                    LengthMm = wall.LengthMm
                });

                displayIndex++;
            }

            return result;
        }

        private static ElementId ResolveWallElementId(Document doc, LayoutWallSelectionDto wall)
        {
            if (doc == null || wall == null)
            {
                return ElementId.InvalidElementId;
            }

            if (!string.IsNullOrWhiteSpace(wall.UniqueId))
            {
                Element element = doc.GetElement(wall.UniqueId);
                if (element != null)
                {
                    return element.Id;
                }
            }

            if (wall.ElementId > 0)
            {
                ElementId id = new ElementId(wall.ElementId);
                if (doc.GetElement(id) != null)
                {
                    return id;
                }
            }

            return ElementId.InvalidElementId;
        }

        private static bool IsValidWall(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId;
        }

        private static void TryDrawSavedDeliveryRoute(UIDocument uiDoc, Document doc, RoomLayoutPlanDto plan)
        {
            if (uiDoc == null || doc == null || plan == null || plan.DeliveryRoute == null)
            {
                return;
            }

            if (!plan.DeliveryRoute.HasRoute || string.IsNullOrWhiteSpace(plan.DeliveryRoute.ResponseBody))
            {
                try
                {
                    using (Transaction tx = new Transaction(doc, "Clear Delivery Route Path"))
                    {
                        tx.Start();
                        Path3DVisualizationService.Clear(doc);
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[DeliveryRoute] Clear existing path before detail failed: " + ex);
                }

                return;
            }

            try
            {
                CalculatePathExecutionResult result = CalculatePathApiService.DrawPathInActiveViewFromResponse(
                    doc,
                    uiDoc,
                    plan.DeliveryRoute.ResponseBody);
                if (result == null || !result.Success || !result.Drawn)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DeliveryRoute] Failed to redraw saved route. LayoutId=" +
                        (plan.LayoutId ?? string.Empty) +
                        ", Message=" +
                        (result != null ? result.Message ?? string.Empty : string.Empty));
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DeliveryRoute] Redraw saved route failed. LayoutId=" +
                    (plan.LayoutId ?? string.Empty) +
                    ", Error=" +
                    ex);
            }
        }

        private static LayoutGeneratedElementsDto CaptureGeneratedElementsForRoom(Document doc, string roomKey)
        {
            LayoutGeneratedElementsDto dto = new LayoutGeneratedElementsDto();

            ElementId equipmentId = ElementId.InvalidElementId;
            List<ElementId> ductIds = new List<ElementId>();
            List<ElementId> pipeIds = new List<ElementId>();

            lock (SyncRoot)
            {
                if (_state.RoomCustomFamilyInstanceIdByRoomKey.TryGetValue(roomKey, out ElementId storedEquipmentId))
                {
                    equipmentId = storedEquipmentId;
                }

                if (_state.RoomGeneratedDuctElementIdsByRoomKey.TryGetValue(roomKey, out List<ElementId> storedDucts))
                {
                    ductIds = storedDucts != null ? storedDucts.ToList() : new List<ElementId>();
                }

                if (_state.RoomGeneratedPipeElementIdsByRoomKey.TryGetValue(roomKey, out List<ElementId> storedPipes))
                {
                    pipeIds = storedPipes != null ? storedPipes.ToList() : new List<ElementId>();
                }
            }

            dto.EquipmentInstance = ToElementRef(doc, equipmentId);
            dto.DuctElements = ductIds.Select(x => ToElementRef(doc, x)).Where(x => x != null).ToList();
            dto.PipeElements = pipeIds.Select(x => ToElementRef(doc, x)).Where(x => x != null).ToList();

            return dto;
        }

        private static LayoutElementRefDto ToElementRef(Document doc, ElementId id)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId)
            {
                return new LayoutElementRefDto();
            }

            Element element = doc.GetElement(id);
            if (element == null)
            {
                return new LayoutElementRefDto { ElementId = id.IntegerValue };
            }

            return new LayoutElementRefDto
            {
                ElementId = id.IntegerValue,
                UniqueId = element.UniqueId ?? string.Empty,
                CategoryName = element.Category != null ? element.Category.Name ?? string.Empty : string.Empty,
                Name = element.Name ?? string.Empty
            };
        }

        private static void ExecuteOnUiThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            Application app = Application.Current;
            if (app == null || app.Dispatcher == null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            app.Dispatcher.Invoke(action);
        }

        private sealed class RoomListCommand : ICommand
        {
            private readonly Action<object> _execute;

            public RoomListCommand(Action<object> execute)
            {
                _execute = execute;
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object parameter)
            {
                return _execute != null;
            }

            public void Execute(object parameter)
            {
                _execute?.Invoke(parameter);
            }
        }
    }
}
