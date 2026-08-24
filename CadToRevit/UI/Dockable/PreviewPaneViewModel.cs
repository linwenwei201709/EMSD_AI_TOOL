using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Data;

namespace CadToRevit.UI.Dockable
{
    public sealed class PreviewPaneViewModel : INotifyPropertyChanged
    {
        private readonly Dispatcher _uiDispatcher;
        private string _documentTitle = "-";
        private string _levelName = "-";
        private string _dwgName = "-";
        private string _unitText = "-";
        private string _analyzeStatus = "-";
        private string _analyzeTimeText = "-";
        private int _layerCount;
        private int _selectedLayerCount;
        private int _ignoreLayerCount;
        private bool? _allVisibleLayerSelectionState;
        private bool _canToggleAllLayerSelection;
        private int _segmentCount;
        private int _arcCount;
        private int _polylineCount;
        private int _previewWallCount;
        private int _previewDoorCount;
        private bool _isBusy;
        private bool _isCadVisible = true;
        private bool _isBuildingElementsVisible = true;
        private bool _isSettingsMode;
        private bool _isGlobalAdvancedExpanded;
        private bool _safeModeEnabled = true;
        private bool _autoJoinWallsAfterCreate = true;
        private string _roomTextLayerNames = RoomRecognitionSettings.DefaultRoomTextLayerNames;
        private double _roomDoorGapMaxMm = RoomRecognitionSettings.DefaultDoorGapMaxMm;
        private double _roomSmallGapPatchMaxMm = RoomRecognitionSettings.DefaultSmallGapPatchMaxMm;
        private string _roomTargetKeywordsText = RoomRecognitionSettings.DefaultTargetKeywordsText;
        private double _roomRecognitionWindowSizeM = RoomRecognitionSettings.DefaultModelRecognitionWindowSizeM;
        private double _headRoomMm = GlobalGenerationSettings.DefaultHeadRoomMm;
        private bool _useGlobalWallHeightOverride;
        private double _globalWallHeightMm = GlobalGenerationSettings.DefaultWallHeightMm;
        private bool _useGlobalDoorHeightOverride;
        private double _globalDoorHeightMm = GlobalGenerationSettings.DefaultDoorHeightMm;
        private bool _useGlobalDoorSillHeightOverride;
        private double _globalDoorSillHeightMm = GlobalGenerationSettings.DefaultDoorSillHeightMm;
        private bool _selectedIsWall;
        private bool _selectedIsDoor;
        private bool _selectedIsWindow;
        private bool _selectedIsColumn;
        private bool _selectedIsBeam;
        private string _lastHighlightRequestKey;
        private bool _suppressNextLayerHighlight;
        private string _settingsTitle = "-";
        private string _revitParamsSummary = "-";
        private string _debugSummary = "-";
        private int _revitSelectionCount;
        private int _restorableDetachedElementCount;
        private int _undoDetachElementCount;
        private int _detachedSessionCount;
        private bool _isBatchUpdatingSelection;
        private PreviewPaneLayerItem _selectedLayerMapping;
        private PreviewPaneAnalyzeReport _lastReport;
        private bool _showValidCategoryFilter = true;
        private bool _showInvalidCategoryFilter = true;
        private bool _showNotForBuildCategoryFilter = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public string DocumentTitle
        {
            get { return _documentTitle; }
            set { Set(ref _documentTitle, value); }
        }

        public string LevelName
        {
            get { return _levelName; }
            set
            {
                if (Set(ref _levelName, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public string DwgName
        {
            get { return _dwgName; }
            set { Set(ref _dwgName, value); }
        }

        public string UnitText
        {
            get { return _unitText; }
            set
            {
                if (Set(ref _unitText, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public string AnalyzeStatus
        {
            get { return _analyzeStatus; }
            set { Set(ref _analyzeStatus, value); }
        }

        public string AnalyzeTimeText
        {
            get { return _analyzeTimeText; }
            set
            {
                if (Set(ref _analyzeTimeText, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public string HeaderSummaryText
        {
            get
            {
                return string.Format(
                    "Unit: {0}   Level: {1}   Segments: {2}   Arc: {3}   Polyline: {4}   Last Action: {5}",
                    string.IsNullOrWhiteSpace(UnitText) ? "-" : UnitText,
                    string.IsNullOrWhiteSpace(LevelName) ? "-" : LevelName,
                    SegmentCount,
                    ArcCount,
                    PolylineCount,
                    NormalizeLastActionText(AnalyzeTimeText));
            }
        }

        public int LayerCount
        {
            get { return _layerCount; }
            set { Set(ref _layerCount, value); }
        }

        public int SelectedLayerCount
        {
            get { return _selectedLayerCount; }
            set { Set(ref _selectedLayerCount, value); }
        }

        public int IgnoreLayerCount
        {
            get { return _ignoreLayerCount; }
            set { Set(ref _ignoreLayerCount, value); }
        }

        public bool? AllVisibleLayerSelectionState
        {
            get { return _allVisibleLayerSelectionState; }
            private set { Set(ref _allVisibleLayerSelectionState, value); }
        }

        public bool CanToggleAllLayerSelection
        {
            get { return _canToggleAllLayerSelection; }
            private set { Set(ref _canToggleAllLayerSelection, value); }
        }

        public int SegmentCount
        {
            get { return _segmentCount; }
            set
            {
                if (Set(ref _segmentCount, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public int ArcCount
        {
            get { return _arcCount; }
            set
            {
                if (Set(ref _arcCount, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public int PolylineCount
        {
            get { return _polylineCount; }
            set
            {
                if (Set(ref _polylineCount, value))
                {
                    OnPropertyChanged(nameof(HeaderSummaryText));
                }
            }
        }

        public int PreviewWallCount
        {
            get { return _previewWallCount; }
            set { Set(ref _previewWallCount, value); }
        }

        public int PreviewDoorCount
        {
            get { return _previewDoorCount; }
            set { Set(ref _previewDoorCount, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { Set(ref _isBusy, value); }
        }

        public bool IsCadVisible
        {
            get { return _isCadVisible; }
            set
            {
                if (Set(ref _isCadVisible, value))
                {
                    OnPropertyChanged(nameof(CadVisibilityButtonText));
                }
            }
        }

        public bool IsBuildingElementsVisible
        {
            get { return _isBuildingElementsVisible; }
            set
            {
                if (Set(ref _isBuildingElementsVisible, value))
                {
                    OnPropertyChanged(nameof(BuildingElementsVisibilityButtonText));
                }
            }
        }

        public string CadVisibilityButtonText
        {
            get { return IsCadVisible ? Loc.T("DockablePane.Button.CadHide") : Loc.T("DockablePane.Button.CadShow"); }
        }

        public string BuildingElementsVisibilityButtonText
        {
            get { return IsBuildingElementsVisible ? Loc.T("DockablePane.Button.BuildingHide") : Loc.T("DockablePane.Button.BuildingShow"); }
        }

        public string DetachElementsButtonText
        {
            get
            {
                return _revitSelectionCount > 0
                    ? "Detach Elements (" + _revitSelectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"
                    : "Detach Elements";
            }
        }

        public bool CanDetachSelectedElements
        {
            get { return _revitSelectionCount > 0; }
        }

        public int RestorableDetachedElementCount
        {
            get { return _restorableDetachedElementCount; }
        }

        public bool CanRestoreBinding
        {
            get { return _restorableDetachedElementCount > 0; }
        }

        public string RestoreBindingButtonText
        {
            get
            {
                return _restorableDetachedElementCount > 0
                    ? "Restore Binding (" + _restorableDetachedElementCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"
                    : "Restore Binding";
            }
        }

        public bool CanUndoDetach
        {
            get { return _undoDetachElementCount > 0; }
        }

        public string UndoDetachButtonText
        {
            get
            {
                return _undoDetachElementCount > 0
                    ? "Undo Detach (" + _undoDetachElementCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"
                    : "Undo Detach";
            }
        }

        public string DetachedElementsSessionStatusText
        {
            get
            {
                return "Selected: " + _detachedSessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public bool IsSettingsMode
        {
            get { return _isSettingsMode; }
            set
            {
                if (Set(ref _isSettingsMode, value))
                {
                    OnPropertyChanged(nameof(MainVisibility));
                    OnPropertyChanged(nameof(SettingsVisibility));
                }
            }
        }

        public Visibility MainVisibility
        {
            get { return IsSettingsMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility SettingsVisibility
        {
            get { return IsSettingsMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string SettingsTitle
        {
            get { return _settingsTitle; }
            set { Set(ref _settingsTitle, value); }
        }

        public bool IsGlobalAdvancedExpanded
        {
            get { return _isGlobalAdvancedExpanded; }
            set
            {
                if (Set(ref _isGlobalAdvancedExpanded, value))
                {
                    OnPropertyChanged(nameof(GlobalAdvancedVisibility));
                }
            }
        }

        public Visibility GlobalAdvancedVisibility
        {
            get { return IsGlobalAdvancedExpanded ? Visibility.Visible : Visibility.Collapsed; }
        }

        public bool SafeModeEnabled
        {
            get { return _safeModeEnabled; }
            set { Set(ref _safeModeEnabled, value); }
        }

        public bool AutoJoinWallsAfterCreate
        {
            get { return _autoJoinWallsAfterCreate; }
            set { Set(ref _autoJoinWallsAfterCreate, value); }
        }

        public string RoomTextLayerNames
        {
            get { return _roomTextLayerNames; }
            set { Set(ref _roomTextLayerNames, value); }
        }

        public double RoomDoorGapMaxMm
        {
            get { return _roomDoorGapMaxMm; }
            set { Set(ref _roomDoorGapMaxMm, value); }
        }

        public double RoomSmallGapPatchMaxMm
        {
            get { return _roomSmallGapPatchMaxMm; }
            set { Set(ref _roomSmallGapPatchMaxMm, value); }
        }

        public string RoomTargetKeywordsText
        {
            get { return _roomTargetKeywordsText; }
            set { Set(ref _roomTargetKeywordsText, value); }
        }

        public double RoomRecognitionWindowSizeM
        {
            get { return _roomRecognitionWindowSizeM; }
            set { Set(ref _roomRecognitionWindowSizeM, RoomRecognitionSettings.NormalizeModelRecognitionWindowSizeM(value)); }
        }

        public double HeadRoomMm
        {
            get { return _headRoomMm; }
            set { Set(ref _headRoomMm, value); }
        }

        public bool UseGlobalWallHeightOverride
        {
            get { return _useGlobalWallHeightOverride; }
            set { Set(ref _useGlobalWallHeightOverride, value); }
        }

        public double GlobalWallHeightMm
        {
            get { return _globalWallHeightMm; }
            set { Set(ref _globalWallHeightMm, value); }
        }

        public bool UseGlobalDoorHeightOverride
        {
            get { return _useGlobalDoorHeightOverride; }
            set { Set(ref _useGlobalDoorHeightOverride, value); }
        }

        public double GlobalDoorHeightMm
        {
            get { return _globalDoorHeightMm; }
            set { Set(ref _globalDoorHeightMm, value); }
        }

        public bool UseGlobalDoorSillHeightOverride
        {
            get { return _useGlobalDoorSillHeightOverride; }
            set { Set(ref _useGlobalDoorSillHeightOverride, value); }
        }

        public double GlobalDoorSillHeightMm
        {
            get { return _globalDoorSillHeightMm; }
            set { Set(ref _globalDoorSillHeightMm, value); }
        }

        public bool SelectedIsWall
        {
            get { return _selectedIsWall; }
            set { Set(ref _selectedIsWall, value); }
        }

        public bool SelectedIsDoor
        {
            get { return _selectedIsDoor; }
            set { Set(ref _selectedIsDoor, value); }
        }

        public bool SelectedIsWindow
        {
            get { return _selectedIsWindow; }
            set { Set(ref _selectedIsWindow, value); }
        }

        public bool SelectedIsColumn
        {
            get { return _selectedIsColumn; }
            set { Set(ref _selectedIsColumn, value); }
        }

        public bool SelectedIsBeam
        {
            get { return _selectedIsBeam; }
            set { Set(ref _selectedIsBeam, value); }
        }

        public string RevitParamsSummary
        {
            get { return _revitParamsSummary; }
            set { Set(ref _revitParamsSummary, value); }
        }

        public string DebugSummary
        {
            get { return _debugSummary; }
            set { Set(ref _debugSummary, value); }
        }

        public PreviewPaneLayerItem SelectedLayerMapping
        {
            get { return _selectedLayerMapping; }
            set
            {
                PreviewPaneLayerItem previous = _selectedLayerMapping;
                if (Set(ref _selectedLayerMapping, value))
                {
                    if (previous != null)
                    {
                        previous.IsUiRowSelected = false;
                    }

                    if (_selectedLayerMapping != null)
                    {
                        _selectedLayerMapping.IsUiRowSelected = true;
                    }

                    OnPropertyChanged(nameof(HasSelectedLayer));
                    OpenSettings(_selectedLayerMapping);
                    UpdateSelectedCategoryFlags();
                    PreviewPaneProvider.RefreshLayerMappingsGrid();
                    if (_suppressNextLayerHighlight)
                    {
                        _suppressNextLayerHighlight = false;
                        return;
                    }

                    _ = HighlightSelectedLayerGeneratedElementsAsync(_selectedLayerMapping);
                }
            }
        }

        public bool HasSelectedLayer
        {
            get { return SelectedLayerMapping != null; }
        }

        public ObservableCollection<PreviewPaneLayerItem> LayerMappings { get; } = new ObservableCollection<PreviewPaneLayerItem>();
        public ObservableCollection<string> ErrorList { get; } = new ObservableCollection<string>();
        public ObservableCollection<MapCategory> CategoryOptions { get; } = new ObservableCollection<MapCategory>(
            Enum.GetValues(typeof(MapCategory)).Cast<MapCategory>()
                .Where(x => x != MapCategory.Ignore && x != MapCategory.Ceilings && x != MapCategory.Floors));
        public ObservableCollection<string> ColumnClusterAlgorithmOptions { get; } = new ObservableCollection<string> { "MidpointBFS", "EndpointGraph" };
        public ObservableCollection<string> ColumnMergeStrategyOptions { get; } = new ObservableCollection<string> { "KeepBest", "UnionBbox", "MaxArea" };
        public ObservableCollection<string> ColumnAttachTargetOptions { get; } = new ObservableCollection<string> { "WallCenterline", "WallFace" };
        public ObservableCollection<string> WallDoubleLineSingleWallPlaceModeOptions { get; } = new ObservableCollection<string>
        {
            AdvancedSettingsRow.WallPlaceModeCenterline,
            AdvancedSettingsRow.WallPlaceModeInsideFaceOnCadLine
        };
        public ObservableCollection<string> WallDoubleLineLengthPolicyOptions { get; } = new ObservableCollection<string>
        {
            AdvancedSettingsRow.WallDoubleLineLengthPolicyOverlap,
            AdvancedSettingsRow.WallDoubleLineLengthPolicyLongerSide,
            AdvancedSettingsRow.WallDoubleLineLengthPolicyAdaptive,
            AdvancedSettingsRow.WallDoubleLineLengthPolicyUnion
        };
        public ObservableCollection<double> RoomRecognitionWindowSizeOptions { get; } = new ObservableCollection<double> { 12.0, 15.0, 18.0 };

        public bool ShowValidCategoryFilter
        {
            get { return _showValidCategoryFilter; }
            set
            {
                if (Set(ref _showValidCategoryFilter, value))
                {
                    RefreshLayerMappingsView();
                }
            }
        }

        public bool ShowInvalidCategoryFilter
        {
            get { return _showInvalidCategoryFilter; }
            set
            {
                if (Set(ref _showInvalidCategoryFilter, value))
                {
                    RefreshLayerMappingsView();
                }
            }
        }

        public bool ShowNotForBuildCategoryFilter
        {
            get { return _showNotForBuildCategoryFilter; }
            set
            {
                if (Set(ref _showNotForBuildCategoryFilter, value))
                {
                    RefreshLayerMappingsView();
                }
            }
        }

        public ICommand TestExternalEventCommand { get; }
        public ICommand RefreshPaneStateCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand BuildPreviewCommand { get; }
        public ICommand PreviewInRevitCommand { get; }
        public ICommand ClearPreviewCommand { get; }
        public ICommand CreateWallsCommand { get; }
        public ICommand CreateDoorsCommand { get; }
        public ICommand CreateFloorsCommand { get; }
        public ICommand CreateGroundFloorCommand { get; }
        public ICommand CreateElementsCommand { get; }
        public ICommand ExportPresetCommand { get; }
        public ICommand RegenerateCommand { get; }
        public ICommand DeleteSelectedLayersCommand { get; }
        public ICommand GenerateLayerCommand { get; }
        public ICommand DeleteLayerCommand { get; }
        public ICommand ToggleLayerVisibilityCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveMappingsCommand { get; }
        public ICommand SelectAllLayersCommand { get; }
        public ICommand DeselectAllLayersCommand { get; }
        public ICommand ToggleAllLayerSelectionCommand { get; }
        public ICommand DetachSelectedElementsCommand { get; }
        public ICommand RestoreBindingCommand { get; }
        public ICommand UndoDetachCommand { get; }
        public ICommand ToggleCadVisibilityCommand { get; }
        public ICommand ToggleBuildingElementsVisibilityCommand { get; }

        public PreviewPaneViewModel()
        {
            ApplyLocalizationDefaults();
            _uiDispatcher = Dispatcher.CurrentDispatcher;
            TestExternalEventCommand = new RelayCommand(async _ => await PreviewPaneRuntime.RaiseTestExternalEventAsync());
            RefreshPaneStateCommand = new RelayCommand(async _ => await RefreshPaneStateAsync());
            AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAsync());
            BuildPreviewCommand = new RelayCommand(_ => BuildPreviewUi());
            PreviewInRevitCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.Preview, Loc.T("DockablePane.Status.Previewing")));
            ClearPreviewCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.ClearPreview, Loc.T("DockablePane.Status.Clearing")));
            CreateWallsCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.CreateWalls, Loc.T("DockablePane.Status.CreatingWalls")));
            CreateDoorsCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.CreateDoors, Loc.T("DockablePane.Status.CreatingDoors")));
            CreateFloorsCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.CreateFloors, Loc.T("DockablePane.Status.CreatingFloors")));
            CreateGroundFloorCommand = new RelayCommand(async _ => await RunRevitActionAsync(PreviewPaneRequestType.CreateGroundFloor, "Creating ground floor..."));
            CreateElementsCommand = new RelayCommand(async _ => await CreateElementsAsync());
            ExportPresetCommand = new RelayCommand(_ => ExportPreset());
            RegenerateCommand = new RelayCommand(async _ => await RegenerateAsync());
            DeleteSelectedLayersCommand = new RelayCommand(async _ => await DeleteSelectedLayersAsync());
            GenerateLayerCommand = new RelayCommand(async p => await GenerateLayerAsync(p as PreviewPaneLayerItem));
            DeleteLayerCommand = new RelayCommand(async p => await DeleteLayerAsync(p as PreviewPaneLayerItem));
            ToggleLayerVisibilityCommand = new RelayCommand(async p => await ToggleLayerVisibilityAsync(p as PreviewPaneLayerItem));
            OpenSettingsCommand = new RelayCommand(p => OpenSettings(p as PreviewPaneLayerItem));
            SaveMappingsCommand = new RelayCommand(async _ => await SaveMappingsAsync(requireConfirm: true));
            SelectAllLayersCommand = new RelayCommand(_ => SetAllLayerSelection(true));
            DeselectAllLayersCommand = new RelayCommand(_ => SetAllLayerSelection(false));
            ToggleAllLayerSelectionCommand = new RelayCommand(_ => ToggleAllVisibleLayerSelection());
            DetachSelectedElementsCommand = new RelayCommand(async _ => await DetachSelectedElementsAsync());
            RestoreBindingCommand = new RelayCommand(async _ => await RestoreSelectedBindingsAsync());
            UndoDetachCommand = new RelayCommand(async _ => await UndoLastDetachBatchAsync());
            ToggleCadVisibilityCommand = new RelayCommand(async _ => await ToggleCadVisibilityAsync());
            ToggleBuildingElementsVisibilityCommand = new RelayCommand(async _ => await ToggleBuildingElementsVisibilityAsync());
            ConfigureLayerMappingsView();
        }

        public void UpdateRevitSelectionCount(int count)
        {
            UpdateRevitSelectionCounts(count, 0);
        }

        public void UpdateRevitSelectionCounts(int detachableCount, int restorableDetachedCount)
        {
            UpdateRevitSelectionCounts(detachableCount, restorableDetachedCount, _undoDetachElementCount);
        }

        public void UpdateRevitSelectionCounts(int detachableCount, int restorableDetachedCount, int undoDetachCount)
        {
            int normalizedDetach = Math.Max(0, detachableCount);
            int normalizedRestore = Math.Max(0, restorableDetachedCount);
            int normalizedUndo = Math.Max(0, undoDetachCount);
            if (_revitSelectionCount == normalizedDetach &&
                _restorableDetachedElementCount == normalizedRestore &&
                _undoDetachElementCount == normalizedUndo)
            {
                return;
            }

            _revitSelectionCount = normalizedDetach;
            _restorableDetachedElementCount = normalizedRestore;
            _undoDetachElementCount = normalizedUndo;
            OnPropertyChanged(nameof(DetachElementsButtonText));
            OnPropertyChanged(nameof(CanDetachSelectedElements));
            OnPropertyChanged(nameof(RestorableDetachedElementCount));
            OnPropertyChanged(nameof(CanRestoreBinding));
            OnPropertyChanged(nameof(RestoreBindingButtonText));
            OnPropertyChanged(nameof(CanUndoDetach));
            OnPropertyChanged(nameof(UndoDetachButtonText));
        }

        public async Task RefreshPaneStateAsync()
        {
            PreviewPaneResponse stateResp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.RefreshState);
            PreviewPaneRuntime.ApplyState(stateResp != null ? stateResp.State : null);
            PreviewPaneRuntime.UpdateRevitSelectionCount(PreviewPaneRuntime.UiApplication);
            PreviewPaneResponse mapResp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.LoadLayerMappings);
            foreach (PreviewPaneLayerItem item in LayerMappings)
            {
                item.PropertyChanged -= LayerItemOnPropertyChanged;
            }

            ListReplace(LayerMappings, FilterGenerationLayerItems(mapResp != null ? mapResp.LayerMappings : null));
            foreach (PreviewPaneLayerItem item in LayerMappings)
            {
                item.PropertyChanged += LayerItemOnPropertyChanged;
                ResetFamilyOptionsForCategory(item);
                item.IsDirty = false;
            }

            SelectedLayerMapping = null;
            RefreshLayerMappingsView();
            UpdateLayerCounts();
            await RefreshDwgSummaryAsync();
        }

        private async Task RefreshDwgSummaryAsync()
        {
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.CaptureAnalyzeSnapshot);
                PreviewPaneDataService service = new PreviewPaneDataService();
                PreviewPaneAnalyzeReport report = await Task.Run(() => service.ComputeAnalyzeReport(resp != null ? resp.Snapshot : null));
                _lastReport = report;

                string dwgName = report != null ? report.DwgName : null;
                DwgName = string.IsNullOrWhiteSpace(dwgName) || string.Equals(dwgName, "(No DWG)", StringComparison.OrdinalIgnoreCase) ? "-" : dwgName;

                string unitText = report != null ? report.UnitText : null;
                UnitText = string.IsNullOrWhiteSpace(unitText) || string.Equals(unitText, "Auto", StringComparison.OrdinalIgnoreCase) ? "-" : unitText;

                SegmentCount = report != null ? report.SegmentCount : 0;
                ArcCount = report != null ? report.ArcCount : 0;
                PolylineCount = report != null ? report.PolylineCount : 0;

                if (SegmentCount > 0 || ArcCount > 0 || PolylineCount > 0)
                {
                    AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                    if (string.IsNullOrWhiteSpace(AnalyzeTimeText) || AnalyzeTimeText.Contains(Loc.T("Common.NA")))
                    {
                        AnalyzeTimeText = Loc.T("DockablePane.Label.LastAnalyzeFormat", DateTime.Now.ToString("HH:mm:ss"));
                    }
                }
                else
                {
                    AnalyzeStatus = Loc.T("DockablePane.Status.NotAnalyzed");
                    AnalyzeTimeText = Loc.T("DockablePane.Label.LastAnalyzeFormat", Loc.T("Common.NA"));
                }
            }
            catch
            {
                // Keep the existing status text on refresh failures.
            }
        }

        public async Task AnalyzeAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = Loc.T("DockablePane.Status.Analyzing");
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.CaptureAnalyzeSnapshot);
                PreviewPaneAnalyzeSnapshot snapshot = resp != null ? resp.Snapshot : null;
                PreviewPaneDataService service = new PreviewPaneDataService();
                PreviewPaneAnalyzeReport report = await Task.Run(() => service.ComputeAnalyzeReport(snapshot));
                _lastReport = report;
                DwgName = report != null ? report.DwgName : "-";
                UnitText = report != null ? report.UnitText : "-";
                LayerCount = report != null ? report.LayerCount : 0;
                SegmentCount = report != null ? report.SegmentCount : 0;
                ArcCount = report != null ? report.ArcCount : 0;
                PolylineCount = report != null ? report.PolylineCount : 0;
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastAnalyzeFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, report != null ? report.Errors : null);
                BuildPreviewUi();
                UpdateLayerCounts();

                // Run EMSD layer-standard analysis and show result window.
                LayerStandardAnalyzeResult layerResult = LayerStandardAnalyzer.AnalyzeLayers(
                    snapshot != null ? snapshot.RawLayerNames : null);
                LayerAnalysisResultWindow window = new LayerAnalysisResultWindow(layerResult, snapshot != null ? snapshot.DwgName : DwgName);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastAnalyzeFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RefreshAndAnalyzeAsync()
        {
            // Always load mapping rows first, then compute analyze snapshot.
            await RefreshPaneStateAsync();
            await AnalyzeAsync();
        }

        private void BuildPreviewUi()
        {
            PreviewWallCount = _lastReport != null ? _lastReport.PreviewWallCount : 0;
            PreviewDoorCount = _lastReport != null ? _lastReport.PreviewDoorCount : 0;
        }

        private async Task HighlightSelectedLayerGeneratedElementsAsync(PreviewPaneLayerItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
            {
                _lastHighlightRequestKey = null;
                try
                {
                    await PreviewPaneRuntime.RequestClearGeneratedElementHighlightAsync();
                }
                catch
                {
                    // Ignore clear failures during selection reset.
                }
                return;
            }

            string requestKey = item.RawLayerName.Trim();
            if (string.Equals(_lastHighlightRequestKey, requestKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastHighlightRequestKey = requestKey;

            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestHighlightSelectedLayerAsync(item.RawLayerName, item.Category);
                if (resp != null && !string.IsNullOrWhiteSpace(resp.Message))
                {
                    AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                    AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                    ListReplace(ErrorList, new[] { resp.Message });
                }
            }
            catch (Exception ex)
            {
                ListReplace(ErrorList, new[] { ex.Message });
            }
        }

        public void ResetSelectedLayerHighlightState()
        {
            _lastHighlightRequestKey = null;
            SelectedLayerMapping = null;
        }

        public void SuppressNextSelectedLayerHighlight()
        {
            _suppressNextLayerHighlight = true;
        }

        public async Task ClearGeneratedElementHighlightAsync()
        {
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestClearGeneratedElementHighlightAsync();
                if (resp != null && !string.IsNullOrWhiteSpace(resp.Message))
                {
                    AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                    AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                    ListReplace(ErrorList, new[] { resp.Message });
                }
            }
            catch (Exception ex)
            {
                ListReplace(ErrorList, new[] { ex.Message });
            }
        }

        private async Task<bool> SaveMappingsAsync(bool requireConfirm = false)
        {
            if (IsBusy)
            {
                return false;
            }

            if (requireConfirm)
            {
                bool confirm = LocalizedDialogService.Confirm(
                    PreviewPaneRuntime.UiApplication,
                    Loc.T("DockablePane.Message.ConfirmSave"),
                    Loc.T("Ribbon.Tab.CadToRevit"));
                if (!confirm)
                {
                    return false;
                }
            }

            IsBusy = true;
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(
                    PreviewPaneRequestType.SaveLayerMappings,
                    LayerMappings.ToList(),
                    new RoomRecognitionSettings
                    {
                        RoomTextLayerNames = RoomTextLayerNames,
                        DoorGapMaxMm = RoomDoorGapMaxMm,
                        SmallGapPatchMaxMm = RoomSmallGapPatchMaxMm,
                        TargetKeywordsText = RoomTargetKeywordsText,
                        LiftGeometryLayerNames = ResolveSavedLiftGeometryLayerNames(),
                        ModelRecognitionWindowSizeM = RoomRecognitionWindowSizeM
                    },
                    new GlobalGenerationSettings
                    {
                        SafeModeEnabled = SafeModeEnabled,
                        AutoJoinWallsAfterCreate = AutoJoinWallsAfterCreate,
                        HeadRoomMm = HeadRoomMm,
                        UseGlobalWallHeightOverride = UseGlobalWallHeightOverride,
                        GlobalWallHeightMm = GlobalWallHeightMm,
                        UseGlobalDoorHeightOverride = UseGlobalDoorHeightOverride,
                        GlobalDoorHeightMm = GlobalDoorHeightMm,
                        UseGlobalDoorSillHeightOverride = UseGlobalDoorSillHeightOverride,
                        GlobalDoorSillHeightMm = GlobalDoorSillHeightMm
                    });
                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                    return false;
                }
                else
                {
                    foreach (PreviewPaneLayerItem item in LayerMappings)
                    {
                        item.IsDirty = false;
                    }

                    // Update status timestamp after batch save.
                    AnalyzeStatus = Loc.T("DockablePane.Status.Saved");
                    AnalyzeTimeText = Loc.T("DockablePane.Label.SavedAtFormat", DateTime.Now.ToString("HH:mm:ss"));
                    ListReplace(ErrorList, new[] { resp != null ? resp.Message : Loc.T("DockablePane.Message.Saved") });
                    return true;
                }
            }
            catch (Exception ex)
            {
                ListReplace(ErrorList, new[] { ex.Message });
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
            }
        }

        private static string ResolveSavedLiftGeometryLayerNames()
        {
            try
            {
                var uiDoc = PreviewPaneRuntime.UiApplication != null
                    ? PreviewPaneRuntime.UiApplication.ActiveUIDocument
                    : null;
                LayerOverrideStoreData store = LayerOverrideStoreService.Load(uiDoc != null ? uiDoc.Document : null);
                RoomRecognitionSettings settings = RoomRecognitionSettings.Clone(store != null ? store.RoomRecognitionSettings : null);
                return settings.LiftGeometryLayerNames;
            }
            catch
            {
                return RoomRecognitionSettings.DefaultLiftGeometryLayerNames;
            }
        }

        private async Task CreateElementsAsync()
        {
            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                "Do you want to generate models for the selected layers?",
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            await SaveMappingsAsync(requireConfirm: false);
            await RunRevitActionAsync(PreviewPaneRequestType.CreateElements, Loc.T("DockablePane.Status.Syncing"));
        }


        private async Task GenerateLayerAsync(PreviewPaneLayerItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
            {
                ListReplace(ErrorList, new[] { "Please select a valid layer before generating." });
                return;
            }

            if (!item.Category.HasValue || !IsGeneratableCategory(item.Category.Value))
            {
                ShowLayerMappingRequiredMessage(item);
                return;
            }

            if (string.IsNullOrWhiteSpace(item.FamilyTypeName) ||
                string.Equals(item.FamilyTypeName.Trim(), UnknownFamilyTypePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                ShowLayerMappingRequiredMessage(item);
                return;
            }

            string actionText = item.IsGenerated ? "Rebuild" : "Generate";
            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                actionText + " layer '" + item.RawLayerName + "'?",
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            item.IsSelected = true;
            bool saved = await SaveMappingsAsync(requireConfirm: false);
            if (!saved)
            {
                ListReplace(ErrorList, new[] { "Failed to save layer mappings. Generation was cancelled." });
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = Loc.T("DockablePane.Status.Syncing");
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestGenerateSingleLayerAsync(item);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                bool generationSucceeded = resp != null &&
                    (resp.Errors == null || resp.Errors.Count == 0);
                if (generationSucceeded)
                {
                    MarkLayerGeneratedVisible(item);
                }

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Layer generation finished." });
                }

                ShowSingleLayerGenerationResultDialog(actionText, resp);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
            }
        }

        private static void MarkLayerGeneratedVisible(PreviewPaneLayerItem item)
        {
            if (item == null)
            {
                return;
            }

            item.IsGenerated = true;
            item.IsGeneratedElementsHidden = false;
        }

        private void ShowLayerMappingRequiredMessage(PreviewPaneLayerItem item)
        {
            string layerName = item != null && !string.IsNullOrWhiteSpace(item.RawLayerName)
                ? item.RawLayerName
                : "selected layer";
            string message = "Please set a valid Category and Family Type before generating this layer." +
                Environment.NewLine + Environment.NewLine +
                "Layer: " + layerName;

            LocalizedDialogService.Info(
                PreviewPaneRuntime.UiApplication,
                message,
                Loc.T("Ribbon.Tab.CadToRevit"));

            ListReplace(ErrorList, new[] { message });
        }

        private void MarkSelectedGeneratableLayersGeneratedVisible()
        {
            foreach (PreviewPaneLayerItem item in LayerMappings)
            {
                if (item == null || !item.IsSelected || !item.IsGeneratableLayer)
                {
                    continue;
                }

                MarkLayerGeneratedVisible(item);
            }
        }

        private async Task ToggleLayerVisibilityAsync(PreviewPaneLayerItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName) || !item.IsGenerated)
            {
                ListReplace(ErrorList, new[] { "No generated elements found for this layer." });
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = Loc.T("DockablePane.Status.Syncing");
            try
            {
                bool hiddenBeforeAction = item.IsGeneratedElementsHidden;
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestToggleLayerGeneratedElementsVisibilityAsync(item.RawLayerName, item.Category);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                bool toggleSucceeded = resp != null && (resp.Errors == null || resp.Errors.Count == 0);
                if (resp != null && resp.LayerGeneratedElementsHidden.HasValue)
                {
                    item.IsGeneratedElementsHidden = resp.LayerGeneratedElementsHidden.Value;
                }
                else if (toggleSucceeded)
                {
                    // Runtime normally returns the actual hidden state. Keep the UI responsive
                    // even if an older handler returns only a success message.
                    item.IsGeneratedElementsHidden = !hiddenBeforeAction;
                }

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Layer visibility updated." });
                }
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }


        private async Task DeleteLayerAsync(PreviewPaneLayerItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName) || !item.IsGenerated)
            {
                ListReplace(ErrorList, new[] { "No generated elements found for this layer." });
                return;
            }

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                "Delete generated elements for layer '" + item.RawLayerName + "'?",
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = Loc.T("DockablePane.Status.Syncing");
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestDeleteSingleLayerAsync(item.RawLayerName, item.Category);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                item.IsGenerated = false;

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Layer delete finished." });
                }

                ShowDeleteResultDialog(false, resp);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }

        private async Task RegenerateAsync()
        {
            int selectedGeneratableCount = LayerMappings.Count(x => x != null && x.IsGeneratableLayer);
            if (selectedGeneratableCount == 0)
            {
                ListReplace(ErrorList, new[] { "Please select at least one valid layer before rebuilding." });
                return;
            }

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                "Do you want to rebuild models for the selected layers? Existing generated elements for these layers will be replaced.",
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            await SaveMappingsAsync(requireConfirm: false);
            await RunRevitActionAsync(PreviewPaneRequestType.RegenerateAll, Loc.T("DockablePane.Status.Syncing"));
        }

        private async Task DeleteSelectedLayersAsync()
        {
            int selectedCount = LayerMappings.Count(x => x != null && x.IsSelected);
            if (selectedCount == 0)
            {
                ListReplace(ErrorList, new[] { "Please select at least one generated layer before deleting." });
                return;
            }

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                "Delete generated elements for all selected layers? Selected layers=" + selectedCount,
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            await SaveMappingsAsync(requireConfirm: false);

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = Loc.T("DockablePane.Status.Syncing");
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestDeleteSelectedLayersAsync();
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Selected layer delete finished." });
                }

                await RefreshLayerMappingsFromStoreAsync();
                ShowDeleteResultDialog(true, resp);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }

        private async Task DetachSelectedElementsAsync()
        {
            if (IsBusy)
            {
                return;
            }

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                "Detach selected generated elements from CAD layer tracking? Detached elements will remain in the model and will not be deleted by layer Rebuild or Delete.",
                Loc.T("Ribbon.Tab.CadToRevit"));
            if (!confirm)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = "Detaching selected elements...";
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.DetachSelectedElements);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Detach selected elements finished." });
                }

                if (resp != null && resp.DetachedElementCount > 0)
                {
                    _detachedSessionCount += resp.DetachedElementCount;
                    OnPropertyChanged(nameof(DetachedElementsSessionStatusText));
                }

                await RefreshLayerMappingsFromStoreAsync();
                PreviewPaneRuntime.UpdateRevitSelectionCount(PreviewPaneRuntime.UiApplication);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }

        private async Task RestoreSelectedBindingsAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = "Restoring selected bindings...";
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.RestoreSelectedBindings);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Restore binding finished." });
                }

                await RefreshLayerMappingsFromStoreAsync();
                PreviewPaneRuntime.UpdateRevitSelectionCount(PreviewPaneRuntime.UiApplication);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }

        private async Task UndoLastDetachBatchAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = "Undoing last detach batch...";
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.UndoLastDetachBatch);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else
                {
                    ListReplace(ErrorList, new[] { resp != null && !string.IsNullOrWhiteSpace(resp.Message) ? resp.Message : "Undo Detach finished." });
                }

                await RefreshLayerMappingsFromStoreAsync();
                PreviewPaneRuntime.UpdateRevitSelectionCount(PreviewPaneRuntime.UiApplication);
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
            }
            finally
            {
                IsBusy = false;
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
        }

        private async Task ToggleCadVisibilityAsync()
        {
            await RunRevitActionAsync(PreviewPaneRequestType.ToggleCadVisibility, Loc.T("DockablePane.Status.TogglingCad"), refreshStateAfterAction: true);
        }

        private async Task ToggleBuildingElementsVisibilityAsync()
        {
            await RunRevitActionAsync(PreviewPaneRequestType.ToggleBuildingElementsVisibility, Loc.T("DockablePane.Status.TogglingBuilding"), refreshStateAfterAction: true);
        }

        private async Task RefreshLayerMappingsFromStoreAsync()
        {
            try
            {
                string selectedRawLayerName = SelectedLayerMapping != null ? SelectedLayerMapping.RawLayerName : null;
                MapCategory? selectedCategory = SelectedLayerMapping != null ? SelectedLayerMapping.Category : null;

                PreviewPaneResponse mapResp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.LoadLayerMappings);
                IList<PreviewPaneLayerItem> refreshedItems = FilterGenerationLayerItems(mapResp != null ? mapResp.LayerMappings : null);
                if (refreshedItems == null)
                {
                    return;
                }

                MergeGeneratedStateFromStore(refreshedItems);

                PreviewPaneLayerItem selected = null;
                if (!string.IsNullOrWhiteSpace(selectedRawLayerName))
                {
                    selected = LayerMappings.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.RawLayerName, selectedRawLayerName, StringComparison.OrdinalIgnoreCase) &&
                        x.Category == selectedCategory);
                }

                SelectedLayerMapping = selected;
                RefreshLayerMappingsView();
                UpdateLayerCounts();
                PreviewPaneProvider.RefreshLayerMappingsGrid();
            }
            catch
            {
                // The generation itself has already completed. Keep the existing layer grid if the refresh fails.
            }
        }

        private void MergeGeneratedStateFromStore(IList<PreviewPaneLayerItem> refreshedItems)
        {
            Dictionary<string, PreviewPaneLayerItem> byLayerAndCategory = refreshedItems
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName) && x.Category.HasValue)
                .GroupBy(x => BuildLayerCategoryKey(x.RawLayerName, x.Category))
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, PreviewPaneLayerItem> byLayer = refreshedItems
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .GroupBy(x => x.RawLayerName.Trim())
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (PreviewPaneLayerItem item in LayerMappings)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
                {
                    continue;
                }

                PreviewPaneLayerItem refreshed = null;
                string layerCategoryKey = BuildLayerCategoryKey(item.RawLayerName, item.Category);
                if (!string.IsNullOrWhiteSpace(layerCategoryKey))
                {
                    byLayerAndCategory.TryGetValue(layerCategoryKey, out refreshed);
                }

                if (refreshed == null)
                {
                    byLayer.TryGetValue(item.RawLayerName.Trim(), out refreshed);
                }

                if (refreshed == null)
                {
                    continue;
                }

                item.IsGenerated = refreshed.IsGenerated;
                item.IsGeneratedElementsHidden = refreshed.IsGeneratedElementsHidden;
                item.IsDirty = false;
            }
        }

        private static string BuildLayerCategoryKey(string rawLayerName, MapCategory? category)
        {
            if (string.IsNullOrWhiteSpace(rawLayerName) || !category.HasValue)
            {
                return null;
            }

            return rawLayerName.Trim() + "|" + category.Value;
        }

        private async Task RunRevitActionAsync(PreviewPaneRequestType requestType, string busyStatus, bool refreshStateAfterAction = false)
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            AnalyzeStatus = busyStatus;
            try
            {
                PreviewPaneResponse resp = await PreviewPaneRuntime.RequestAsync(requestType);
                AnalyzeStatus = Loc.T("DockablePane.Status.Success");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));

                if (resp != null && resp.Errors != null && resp.Errors.Count > 0)
                {
                    ListReplace(ErrorList, resp.Errors);
                }
                else if (resp != null && !string.IsNullOrWhiteSpace(resp.Message))
                {
                    ListReplace(ErrorList, new List<string> { resp.Message });
                }
                else
                {
                    ListReplace(ErrorList, new List<string> { Loc.T("DockablePane.Message.Done") });
                }

                if (requestType == PreviewPaneRequestType.CreateElements ||
                    requestType == PreviewPaneRequestType.RegenerateAll)
                {
                    ShowBatchActionResultDialog(requestType, resp);
                }

                if (requestType == PreviewPaneRequestType.CreateElements ||
                    requestType == PreviewPaneRequestType.RegenerateAll)
                {
                    await RefreshLayerMappingsFromStoreAsync();
                    if (resp == null || resp.Errors == null || resp.Errors.Count == 0)
                    {
                        // Generated / regenerated elements are visible immediately after creation.
                        // Keep the eye column in the Hide state for all selected generated layers.
                        MarkSelectedGeneratableLayersGeneratedVisible();
                        PreviewPaneProvider.RefreshLayerMappingsGrid();
                    }
                }

                if (refreshStateAfterAction)
                {
                    PreviewPaneResponse stateResp = await PreviewPaneRuntime.RequestAsync(PreviewPaneRequestType.RefreshState);
                    PreviewPaneRuntime.ApplyState(stateResp != null ? stateResp.State : null);
                }
            }
            catch (Exception ex)
            {
                AnalyzeStatus = Loc.T("DockablePane.Status.Failed");
                AnalyzeTimeText = Loc.T("DockablePane.Label.LastActionFormat", DateTime.Now.ToString("HH:mm:ss"));
                ListReplace(ErrorList, new[] { ex.Message });
                if (requestType == PreviewPaneRequestType.CreateElements ||
                    requestType == PreviewPaneRequestType.RegenerateAll)
                {
                    LocalizedDialogService.Error(
                        PreviewPaneRuntime.UiApplication,
                        requestType == PreviewPaneRequestType.RegenerateAll
                            ? "Batch rebuild failed. Please check the log for details."
                            : "Batch generate failed. Please check the log for details.",
                        "EMSD AI Tool");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenSettings(PreviewPaneLayerItem item)
        {
            if (item == null)
            {
                SettingsTitle = Loc.T("DockablePane.Settings.Title");
                RevitParamsSummary = "-";
                DebugSummary = "-";
                return;
            }

            SettingsTitle = Loc.T("DockablePane.Settings.TitleWithLayer", item.RawLayerName);
            RevitParamsSummary = string.Format(Loc.T("DockablePane.Settings.RevitParamsSummaryFormat"),
                item.Category,
                string.IsNullOrWhiteSpace(item.FamilyTypeName) ? "(none)" : item.FamilyTypeName);
            DebugSummary = string.Format(Loc.T("DockablePane.Settings.DebugSummaryFormat"),
                item.EnableLayerOverride,
                item.ApplyAsCategoryDefault,
                item.MinWallLengthMm.HasValue ? item.MinWallLengthMm.Value.ToString("F2") : "-",
                item.DefaultSingleWallThicknessMm.HasValue ? item.DefaultSingleWallThicknessMm.Value.ToString("F2") : "-");
            IsSettingsMode = false;
        }

        private void LayerItemOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            PreviewPaneLayerItem item = sender as PreviewPaneLayerItem;
            if (item == null)
            {
                return;
            }

            if (e.PropertyName == nameof(PreviewPaneLayerItem.IsSelected))
            {
                // Selected state should not mutate category/family selections.
            }
            else if (e.PropertyName == nameof(PreviewPaneLayerItem.Category))
            {
                ResetFamilyOptionsForCategory(item, forceFirstSelection: true);
                RefreshLayerMappingsView();
                if (ReferenceEquals(item, SelectedLayerMapping))
                {
                    OpenSettings(SelectedLayerMapping);
                    UpdateSelectedCategoryFlags();
                }
            }
            if (e.PropertyName != nameof(PreviewPaneLayerItem.IsDirty) &&
                e.PropertyName != nameof(PreviewPaneLayerItem.IsGenerated) &&
                e.PropertyName != nameof(PreviewPaneLayerItem.IsGeneratedElementsHidden) &&
                e.PropertyName != nameof(PreviewPaneLayerItem.IsUiRowSelected) &&
                e.PropertyName != nameof(PreviewPaneLayerItem.GenerationActionText))
            {
                item.IsDirty = true;
            }

            if (_isBatchUpdatingSelection && e.PropertyName == nameof(PreviewPaneLayerItem.IsSelected))
            {
                return;
            }

            if (e.PropertyName == nameof(PreviewPaneLayerItem.IsLayerStandardInvalid))
            {
                RefreshLayerMappingsView();
            }

            UpdateLayerCounts();
        }

        private void SetAllLayerSelection(bool isSelected)
        {
            if (LayerMappings.Count == 0)
            {
                UpdateLayerCounts();
                return;
            }

            _isBatchUpdatingSelection = true;
            try
            {
                foreach (PreviewPaneLayerItem item in GetVisibleLayerMappings())
                {
                    if (item == null)
                    {
                        continue;
                    }

                    // Select All should not auto-select non-generatable layers because they cannot be generated
                    // until the user maps them to a supported category.
                    item.IsSelected = isSelected && item.Category.HasValue && IsGeneratableCategory(item.Category.Value);
                }
            }
            finally
            {
                _isBatchUpdatingSelection = false;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(LayerMappings);
            if (view != null)
            {
                view.Refresh();
            }

            OnPropertyChanged(nameof(LayerMappings));
            UpdateLayerCounts();
            PreviewPaneProvider.RefreshLayerMappingsGrid();
        }

        private void ToggleAllVisibleLayerSelection()
        {
            SetAllLayerSelection(AllVisibleLayerSelectionState != true);
        }

        private void ConfigureLayerMappingsView()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(LayerMappings);
            if (view != null)
            {
                view.Filter = FilterLayerMappingByCategory;
            }
        }

        private void RefreshLayerMappingsView()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(LayerMappings);
            if (view != null)
            {
                if (view.Filter == null)
                {
                    view.Filter = FilterLayerMappingByCategory;
                }

                view.Refresh();
            }

            if (SelectedLayerMapping != null && !FilterLayerMappingByCategory(SelectedLayerMapping))
            {
                SelectedLayerMapping = null;
            }

            OnPropertyChanged(nameof(LayerMappings));
            PreviewPaneProvider.RefreshLayerMappingsGrid();
        }

        private IEnumerable<PreviewPaneLayerItem> GetVisibleLayerMappings()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(LayerMappings);
            if (view == null)
            {
                return LayerMappings;
            }

            return view.Cast<object>().OfType<PreviewPaneLayerItem>().ToList();
        }

        private bool FilterLayerMappingByCategory(object value)
        {
            PreviewPaneLayerItem item = value as PreviewPaneLayerItem;
            if (item == null)
            {
                return false;
            }

            if (item.Category.HasValue && IsValidFilterCategory(item.Category.Value))
            {
                return ShowValidCategoryFilter;
            }

            if (item.ShowUnknownLayerIcon || (item.Category.HasValue && item.Category.Value == MapCategory.Unknown))
            {
                return ShowInvalidCategoryFilter;
            }

            if (item.Category.HasValue && item.Category.Value == MapCategory.NotForBuild)
            {
                return ShowNotForBuildCategoryFilter;
            }

            return ShowValidCategoryFilter;
        }

        private static bool IsValidFilterCategory(MapCategory category)
        {
            return category == MapCategory.Walls ||
                category == MapCategory.Columns ||
                category == MapCategory.Doors ||
                category == MapCategory.Windows ||
                category == MapCategory.Beams;
        }

        private static IList<PreviewPaneLayerItem> FilterGenerationLayerItems(IList<PreviewPaneLayerItem> items)
        {
            if (items == null)
            {
                return null;
            }

            return items
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .OrderBy(x => GetLayerListSortGroup(x))
                .ThenBy(x => x.RawLayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ShouldShowLayerInGenerationList(string rawLayerName)
        {
            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return false;
            }

            LayerStandardMatchItem match = TryAnalyzeLayerStandard(rawLayerName);
            if (match == null)
            {
                return true;
            }

            if (!match.IsValid)
            {
                return true;
            }

            return !IsExcludedFromGenerationListStandardLabel(match.MatchedStandard);
        }

        private static int GetLayerListSortGroup(PreviewPaneLayerItem item)
        {
            if (item == null || !item.Category.HasValue)
            {
                return 1;
            }

            if (item.Category.Value == MapCategory.NotForBuild)
            {
                return 2;
            }

            if (item.Category.Value == MapCategory.Unknown || item.Category.Value == MapCategory.Ignore)
            {
                return 1;
            }

            return 0;
        }

        private static bool IsExcludedFromGenerationListStandardLabel(string matchedStandard)
        {
            if (string.IsNullOrWhiteSpace(matchedStandard))
            {
                return false;
            }

            return ContainsIgnoreCase(matchedStandard, "Text") ||
                   ContainsIgnoreCase(matchedStandard, "Grids") ||
                   ContainsIgnoreCase(matchedStandard, "Dimensions") ||
                   ContainsIgnoreCase(matchedStandard, "Stairs") ||
                   ContainsIgnoreCase(matchedStandard, "Ramps");
        }

        private static LayerStandardMatchItem TryAnalyzeLayerStandard(string rawLayerName)
        {
            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return null;
            }

            try
            {
                LayerStandardAnalyzeResult analysis = LayerStandardAnalyzer.AnalyzeLayers(new[] { rawLayerName });
                return analysis != null
                    ? analysis.Matches.FirstOrDefault(x => x != null && string.Equals(x.LayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private const string UnknownFamilyTypePlaceholder = "Please select";

        private static bool IsGeneratableCategory(MapCategory category)
        {
            return category != MapCategory.Ignore &&
                category != MapCategory.Unknown &&
                category != MapCategory.NotForBuild;
        }

        private static void ResetFamilyOptionsForCategory(PreviewPaneLayerItem item, bool forceFirstSelection = false)
        {
            if (item == null)
            {
                return;
            }

            // Category changed: clear stale selected family first so DataGrid editor cannot keep old category value.
            if (forceFirstSelection)
            {
                item.FamilyTypeName = string.Empty;
            }

            item.FamilyTypeOptions.Clear();
            if (item.Category.HasValue &&
                (item.Category.Value == MapCategory.Unknown || item.Category.Value == MapCategory.NotForBuild))
            {
                item.FamilyTypeOptions.Add(UnknownFamilyTypePlaceholder);
                item.FamilyTypeName = UnknownFamilyTypePlaceholder;
                return;
            }

            if (item.Category.HasValue && item.FamilyTypeOptionsByCategory.TryGetValue(item.Category.Value, out List<string> options))
            {
                foreach (string name in options)
                {
                    item.FamilyTypeOptions.Add(name);
                }
            }

            if (item.FamilyTypeOptions.Count > 0)
            {
                if (forceFirstSelection)
                {
                    item.FamilyTypeName = item.FamilyTypeOptions[0];
                }
                else if (string.IsNullOrWhiteSpace(item.FamilyTypeName) || !item.FamilyTypeOptions.Contains(item.FamilyTypeName))
                {
                    item.FamilyTypeName = ResolvePreferredFamilyTypeName(item.Category, item.FamilyTypeOptions) ?? item.FamilyTypeOptions[0];
                }
            }
            else
            {
                item.FamilyTypeName = string.Empty;
            }
        }

        private static string ResolvePreferredFamilyTypeName(MapCategory? category, IList<string> options)
        {
            if (!category.HasValue || options == null || options.Count == 0)
            {
                return null;
            }

            if (category.Value == MapCategory.Doors)
            {
                foreach (string name in options)
                {
                    if (!string.IsNullOrWhiteSpace(name) &&
                        name.IndexOf("Passage-Single", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return name;
                    }
                }

                return null;
            }

            if (category.Value != MapCategory.Walls)
            {
                return null;
            }

            string[] preferredPatterns =
            {
                @"^\s*Generic\s*-\s*100\s*mm\b",
                @"^\s*Generic\s*-\s*150\s*mm\b",
                @"^\s*Generic\s*-\s*200\s*mm\b"
            };

            foreach (string pattern in preferredPatterns)
            {
                foreach (string name in options)
                {
                    if (!string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    {
                        return name;
                    }
                }
            }

            return null;
        }

        private void UpdateLayerCounts()
        {
            LayerCount = LayerMappings.Count;
            SelectedLayerCount = LayerMappings.Count(x => x != null && x.IsSelected);
            IgnoreLayerCount = Math.Max(0, LayerCount - SelectedLayerCount);

            List<PreviewPaneLayerItem> visibleSelectable = GetVisibleLayerMappings()
                .Where(x => x != null && x.Category.HasValue && IsGeneratableCategory(x.Category.Value))
                .ToList();
            CanToggleAllLayerSelection = visibleSelectable.Count > 0;
            if (visibleSelectable.Count == 0)
            {
                AllVisibleLayerSelectionState = false;
            }
            else if (visibleSelectable.All(x => x.IsSelected))
            {
                AllVisibleLayerSelectionState = true;
            }
            else if (visibleSelectable.All(x => !x.IsSelected))
            {
                AllVisibleLayerSelectionState = false;
            }
            else
            {
                AllVisibleLayerSelectionState = null;
            }
        }

        private void UpdateSelectedCategoryFlags()
        {
            if (SelectedLayerMapping == null || !SelectedLayerMapping.Category.HasValue)
            {
                SelectedIsWall = false;
                SelectedIsDoor = false;
                SelectedIsWindow = false;
                SelectedIsColumn = false;
                SelectedIsBeam = false;
                return;
            }

            MapCategory c = SelectedLayerMapping.Category.Value;
            SelectedIsWall = c == MapCategory.Walls;
            SelectedIsDoor = c == MapCategory.Doors;
            SelectedIsWindow = c == MapCategory.Windows;
            SelectedIsColumn = c == MapCategory.Columns;
            SelectedIsBeam = c == MapCategory.Beams;
        }

        private void ExportPreset()
        {
            AnalyzeStatus = Loc.T("DockablePane.Status.Preset");
            AnalyzeTimeText = Loc.T("DockablePane.Label.PresetExportedAtFormat", DateTime.Now.ToString("HH:mm:ss"));
            ListReplace(ErrorList, new[] { Loc.T("DockablePane.Message.PresetExported") });
        }

        private void ApplyLocalizationDefaults()
        {
            AnalyzeStatus = Loc.T("DockablePane.Status.NotAnalyzed");
            AnalyzeTimeText = Loc.T("DockablePane.Label.LastAnalyzeFormat", Loc.T("Common.NA"));
            SettingsTitle = Loc.T("DockablePane.Settings.Title");
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string NormalizeLastActionText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            string text = value.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0 && colon + 1 < text.Length)
            {
                string suffix = text.Substring(colon + 1).Trim();
                Match timeMatch = Regex.Match(text, @"\d{2}:\d{2}:\d{2}");
                return timeMatch.Success ? timeMatch.Value : suffix;
            }

            return text;
        }

        private void ListReplace<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            Action replaceAction = () =>
            {
                target.Clear();
                if (source == null)
                {
                    return;
                }

                foreach (T item in source)
                {
                    target.Add(item);
                }
            };

            Dispatcher dispatcher = _uiDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                replaceAction();
                return;
            }

            dispatcher.Invoke(replaceAction);
        }

        private static void ShowBatchActionResultDialog(PreviewPaneRequestType requestType, PreviewPaneResponse response)
        {
            bool isRebuild = requestType == PreviewPaneRequestType.RegenerateAll;
            string rawMessage = response != null ? response.Message : null;
            if (!string.IsNullOrWhiteSpace(rawMessage))
            {
                DiagnosticRecorder.AppendDebug("[BatchResult] " + rawMessage);
            }

            bool hasErrors = response != null && response.Errors != null && response.Errors.Count > 0;
            if (!hasErrors)
            {
                LocalizedDialogService.Success(
                    PreviewPaneRuntime.UiApplication,
                    isRebuild
                        ? "Batch rebuild completed successfully."
                        : "Batch generate completed successfully.",
                    "EMSD AI Tool");
                return;
            }

            LocalizedDialogService.Success(
                PreviewPaneRuntime.UiApplication,
                isRebuild
                    ? "Batch rebuild completed with issues. Please check the log for details."
                    : "Batch generate completed with issues. Please check the log for details.",
                "EMSD AI Tool");
        }

        private static void ShowSingleLayerGenerationResultDialog(string actionText, PreviewPaneResponse response)
        {
            bool isRebuild = string.Equals(actionText, "Rebuild", StringComparison.OrdinalIgnoreCase);
            string rawMessage = response != null ? response.Message : null;
            if (!string.IsNullOrWhiteSpace(rawMessage))
            {
                DiagnosticRecorder.AppendDebug("[LayerGenerationResult] " + rawMessage);
            }

            bool hasErrors = response != null && response.Errors != null && response.Errors.Count > 0;
            if (!hasErrors)
            {
                LocalizedDialogService.Success(
                    PreviewPaneRuntime.UiApplication,
                    isRebuild
                        ? "Layer rebuild completed successfully."
                        : "Layer generate completed successfully.",
                    "EMSD AI Tool");
                return;
            }

            LocalizedDialogService.Success(
                PreviewPaneRuntime.UiApplication,
                isRebuild
                    ? "Layer rebuild completed with issues. Please check the log for details."
                    : "Layer generate completed with issues. Please check the log for details.",
                "EMSD AI Tool");
        }

        private static void ShowDeleteResultDialog(bool isBatch, PreviewPaneResponse response)
        {
            string rawMessage = response != null ? response.Message : null;
            if (!string.IsNullOrWhiteSpace(rawMessage))
            {
                DiagnosticRecorder.AppendDebug("[LayerDeleteResult] " + rawMessage);
            }

            bool hasErrors = response != null && response.Errors != null && response.Errors.Count > 0;
            if (!hasErrors)
            {
                LocalizedDialogService.Success(
                    PreviewPaneRuntime.UiApplication,
                    isBatch
                        ? "Selected layers deleted successfully."
                        : "Layer deleted successfully.",
                    "EMSD AI Tool");
                return;
            }

            LocalizedDialogService.Success(
                PreviewPaneRuntime.UiApplication,
                isBatch
                    ? "Selected layers deleted with issues. Please check the log for details."
                    : "Layer deleted with issues. Please check the log for details.",
                "EMSD AI Tool");
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Func<object, Task> _executeAsync;
            private readonly Action<object> _execute;

            public RelayCommand(Action<object> execute)
            {
                _execute = execute;
            }

            public RelayCommand(Func<object, Task> executeAsync)
            {
                _executeAsync = executeAsync;
            }

            public bool CanExecute(object parameter)
            {
                return _execute != null || _executeAsync != null;
            }

            public void Execute(object parameter)
            {
                if (_execute != null)
                {
                    _execute(parameter);
                    return;
                }

                if (_executeAsync != null)
                {
                    _ = _executeAsync(parameter);
                }
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }
        }
    }
}
