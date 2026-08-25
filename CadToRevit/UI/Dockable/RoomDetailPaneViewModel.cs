using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.EquipmentValidation;
using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.EquipmentValidation;
using CadToRevit.Services.Rooms.LayoutPlans;
using System.IO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomDetailPaneViewModel : INotifyPropertyChanged
    {
        private RoomDetailPageMode _currentPageMode = RoomDetailPageMode.Overview;
        private string _headerTitle = Loc.T("DockablePane.RoomDetail.Title");
        private string _roomName = "-";
        private string _targetRoomType = "-";
        private string _areaText = "-";
        private string _levelText = "-";
        private string _statusText = "-";
        private string _boundaryLayersText = "-";
        private string _roomKeyText = "-";
        private string _closeGapText = "-";
        private string _chilledWaterSupplyText = string.Empty;
        private string _selectedFlowRate = "Select flow rate";
        private string _chilledWaterReturnText = string.Empty;
        private string _hotWaterSupplyText = string.Empty;
        private string _hotWaterReturnText = string.Empty;
        private string _sadText1 = string.Empty;
        private string _sadText2 = string.Empty;
        private string _radText1 = string.Empty;
        private string _radText2 = string.Empty;
        private string _fadText1 = string.Empty;
        private string _fadText2 = string.Empty;
        private bool _hasSelection;
        private string _selectedRoomKey;
        private string _highlightedFamilyKey;
        private bool _isEquipmentSelectionExpanded = true;
        private bool _isSizeEvaluationCompleted;
        private EquipmentSelectionCardViewModel _selectedEquipmentOption;
        private bool _isEquipmentInsertStatusVisible;
        private string _equipmentInsertStatusText = string.Empty;
        private int _equipmentInsertStatusVersion;
        private bool _isAhuSubModuleConfigurationVisible;
        private string _confirmedEquipmentName = string.Empty;
        private string _confirmedEquipmentDimensionsValueText = string.Empty;
        private string _confirmedEquipmentAirflowValueText = string.Empty;
        private string _confirmedEquipmentWeightValueText = string.Empty;
        private string _confirmedEquipmentMaintenanceSpaceValueText = string.Empty;
        private EquipmentPlacementValidationDto _currentEquipmentValidation;
        private string _ahuSubModuleCountText = "System Determined 0 Sub-modules";
        private bool _isConnectivityExpanded = true;
        private bool _isConnectivityUnlocked;
        private bool _isDuctWorkGenerated;
        private bool _isPipeWorkGenerated;
        private bool _isDeliveryRouteExpanded;
        private EditorLiftOptionViewModel _selectedDeliveryStartLift;
        private EditorRoomOptionViewModel _selectedDeliveryTargetRoom;
        private string _deliveryStartPointName = "Not selected";
        private string _deliveryStartPointStatus = "Waiting";
        private string _deliveryTargetName = "Not selected";
        private string _deliveryTargetStatus = "Waiting";
        private string _deliveryRouteHintText = "Start point logged. Define the destination next.";
        private bool _isDeliveryRouteResultVisible;
        private string _deliveryRouteResultMessage = string.Empty;
        private string _deliveryRouteLengthText = string.Empty;
        private string _savedDeliveryRouteResponseBody = string.Empty;
        private double? _savedDeliveryRouteLengthMeters;
        private string _savedDeliveryRouteStartLiftKey = string.Empty;
        private string _savedDeliveryRouteStartLiftName = string.Empty;
        private string _savedDeliveryRouteTargetRoomKey = string.Empty;
        private string _savedDeliveryRouteTargetRoomName = string.Empty;
        private SolutionEditorViewModel _currentEditor;
        private string _currentEditorLayoutId;
        private bool _isLayoutCompareMode;
        private int _compareRoutesDisplayedCount;
        private readonly List<string> _selectedCompareLayoutIds = new List<string>();
        private readonly Dictionary<string, string> _editorOriginalFamilyKeyByRoomKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _editorTouchedRoomKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Rooms that currently contain a temporary AHU preview created by this
        // editor session. This is intentionally separate from _editorTouchedRoomKeys:
        // a Detail session may track an original submitted AHU for rollback even
        // before the user has created a new preview.
        private readonly HashSet<string> _editorPreviewRoomKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _lastEditorRoomKey = string.Empty;
        private readonly AhuPlacementValidationService _ahuPlacementValidationService =
            new AhuPlacementValidationService();

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<RoomCustomFamilyItemViewModel> FamilyOptions { get; } =
            new ObservableCollection<RoomCustomFamilyItemViewModel>();

        public ObservableCollection<LayoutPlanCardViewModel> LayoutPlans { get; } =
            new ObservableCollection<LayoutPlanCardViewModel>();

        public ObservableCollection<EquipmentSelectionCardViewModel> EquipmentOptions { get; } =
            new ObservableCollection<EquipmentSelectionCardViewModel>();

        public ObservableCollection<EquipmentSelectionCardViewModel> RecommendedEquipmentOptions { get; } =
            new ObservableCollection<EquipmentSelectionCardViewModel>();

        public ObservableCollection<EquipmentSelectionCardViewModel> OptionalEquipmentOptions { get; } =
            new ObservableCollection<EquipmentSelectionCardViewModel>();

        public ObservableCollection<AhuSubModuleRowViewModel> AhuSubModules { get; } =
            new ObservableCollection<AhuSubModuleRowViewModel>();

        public ObservableCollection<string> ConfirmedEquipmentValidationReasons { get; } =
            new ObservableCollection<string>();

        public ObservableCollection<string> DuctWorkSizeOptions { get; } =
            new ObservableCollection<string>();

        public ObservableCollection<string> PipeWorkSizeOptions { get; } =
            new ObservableCollection<string>();

        public ObservableCollection<string> FlowRateOptions { get; } =
            new ObservableCollection<string>();

        public ObservableCollection<EditorRoomOptionViewModel> EditorRoomOptions { get; } =
            new ObservableCollection<EditorRoomOptionViewModel>();

        public ObservableCollection<EditorLiftOptionViewModel> EditorLiftOptions { get; } =
            new ObservableCollection<EditorLiftOptionViewModel>();

        public ICommand NewSolutionCommand { get; }

        public ICommand EnterLayoutCompareModeCommand { get; }

        public ICommand FinishLayoutCompareModeCommand { get; }

        public ICommand CancelLayoutCompareModeCommand { get; }

        public ICommand ClearCompareRoutesCommand { get; }

        public ICommand CancelEditorCommand { get; }

        public ICommand SaveSolutionCommand { get; }

        public ICommand SaveAndSubmitSolutionCommand { get; }

        public ICommand PlaceholderActionCommand { get; }

        public ICommand SizeEvaluationCommand { get; }

        public ICommand ToggleEquipmentOptionsCommand { get; }

        public ICommand ConfirmEquipmentCommand { get; }

        public ICommand ChangeConfirmedEquipmentCommand { get; }

        public ICommand ToggleConnectivityAdvancedCommand { get; }

        public ICommand EditSadSizeCommand { get; }

        public ICommand EditRadSizeCommand { get; }

        public ICommand EditChwsPipeSizeCommand { get; }

        public ICommand EditChwrPipeSizeCommand { get; }

        public ICommand CreateDuctWorkCommand { get; }

        public ICommand RemoveDuctWorkCommand { get; }

        public ICommand CreatePipeWorkCommand { get; }

        public ICommand RemovePipeWorkCommand { get; }

        public ICommand PickPipeWallCommand { get; }

        public ICommand CreatePipeSystemCommand { get; }

        public ICommand PickDuctWallCommand { get; }

        public ICommand CreateDuctSystemCommand { get; }

        public ICommand DetailLayoutPlanCommand { get; }

        public ICommand ExportLayoutPlanCommand { get; }

        public ICommand DeleteLayoutPlanCommand { get; }

        public ICommand ToggleDeliveryRouteCommand { get; }

        public ICommand DefineDeliveryStartPointCommand { get; }

        public ICommand DefineDeliveryTargetPointCommand { get; }

        public ICommand GenerateDeliveryRouteCommand { get; }

        public RoomDetailPageMode CurrentPageMode
        {
            get { return _currentPageMode; }
            set
            {
                if (Set(ref _currentPageMode, value))
                {
                    HeaderTitle = value == RoomDetailPageMode.SolutionEditor ? "Equipment Planner" : Loc.T("DockablePane.RoomDetail.Title");
                }
            }
        }


        public bool IsLayoutCompareMode
        {
            get { return _isLayoutCompareMode; }
            set
            {
                if (Set(ref _isLayoutCompareMode, value))
                {
                    OnPropertyChanged(nameof(NewLayoutButtonVisibility));
                    OnPropertyChanged(nameof(CompareModeButtonVisibility));
                    OnPropertyChanged(nameof(CancelCompareButtonVisibility));
                    OnPropertyChanged(nameof(DoneCompareButtonVisibility));
                    OnPropertyChanged(nameof(CompareRoutesDisplayedVisibility));
                    RefreshLayoutPlanCompareState();
                }
            }
        }

        public Visibility NewLayoutButtonVisibility
        {
            get { return IsLayoutCompareMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility CompareModeButtonVisibility
        {
            get { return IsLayoutCompareMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility CancelCompareButtonVisibility
        {
            get { return IsLayoutCompareMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility DoneCompareButtonVisibility
        {
            get { return IsLayoutCompareMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility CompareRoutesDisplayedVisibility
        {
            get { return !IsLayoutCompareMode && _compareRoutesDisplayedCount > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string CompareRoutesDisplayedText
        {
            get { return "Compare routes displayed: " + _compareRoutesDisplayedCount.ToString(); }
        }

        public string DoneCompareButtonText
        {
            get
            {
                int count = _selectedCompareLayoutIds.Count;
                return count > 0 ? "Done (" + count.ToString() + ")" : "Done";
            }
        }

        public string HeaderTitle
        {
            get { return _headerTitle; }
            set { Set(ref _headerTitle, value); }
        }

        public string RoomName
        {
            get { return _roomName; }
            set { Set(ref _roomName, value); }
        }

        public string TargetRoomType
        {
            get { return _targetRoomType; }
            set { Set(ref _targetRoomType, value); }
        }

        public string AreaText
        {
            get { return _areaText; }
            set { Set(ref _areaText, value); }
        }

        public string LevelText
        {
            get { return _levelText; }
            set { Set(ref _levelText, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            set { Set(ref _statusText, value); }
        }

        public string BoundaryLayersText
        {
            get { return _boundaryLayersText; }
            set { Set(ref _boundaryLayersText, value); }
        }

        public string RoomKeyText
        {
            get { return _roomKeyText; }
            set { Set(ref _roomKeyText, value); }
        }

        public string CloseGapText
        {
            get { return _closeGapText; }
            set { Set(ref _closeGapText, value); }
        }

        public string ChilledWaterSupplyText
        {
            get { return _chilledWaterSupplyText; }
            set { Set(ref _chilledWaterSupplyText, value); }
        }

        public string SelectedFlowRate
        {
            get { return _selectedFlowRate; }
            set { Set(ref _selectedFlowRate, value); }
        }

        public string ChilledWaterReturnText
        {
            get { return _chilledWaterReturnText; }
            set { Set(ref _chilledWaterReturnText, value); }
        }

        public string HotWaterSupplyText
        {
            get { return _hotWaterSupplyText; }
            set { Set(ref _hotWaterSupplyText, value); }
        }

        public string HotWaterReturnText
        {
            get { return _hotWaterReturnText; }
            set { Set(ref _hotWaterReturnText, value); }
        }

        public string SadText1
        {
            get { return _sadText1; }
            set { Set(ref _sadText1, value); }
        }

        public string SadText2
        {
            get { return _sadText2; }
            set { Set(ref _sadText2, value); }
        }

        public string RadText1
        {
            get { return _radText1; }
            set { Set(ref _radText1, value); }
        }

        public string RadText2
        {
            get { return _radText2; }
            set { Set(ref _radText2, value); }
        }

        public string FadText1
        {
            get { return _fadText1; }
            set { Set(ref _fadText1, value); }
        }

        public string FadText2
        {
            get { return _fadText2; }
            set { Set(ref _fadText2, value); }
        }

        public bool HasSelection
        {
            get { return _hasSelection; }
            set { Set(ref _hasSelection, value); }
        }

        public SolutionEditorViewModel CurrentEditor
        {
            get { return _currentEditor; }
            set
            {
                if (ReferenceEquals(_currentEditor, value))
                {
                    return;
                }

                if (_currentEditor != null)
                {
                    _currentEditor.PropertyChanged -= OnCurrentEditorPropertyChanged;
                }

                _currentEditor = value;

                if (_currentEditor != null)
                {
                    _currentEditor.PropertyChanged += OnCurrentEditorPropertyChanged;
                }

                // Establish the room baseline for this editor instance. A later
                // RoomKey change means the Target Room dropdown really changed.
                _lastEditorRoomKey = _currentEditor != null
                    ? (_currentEditor.RoomKey ?? string.Empty)
                    : string.Empty;

                OnPropertyChanged();
                NotifyLayoutStepVisibilityChanged();
            }
        }

        public bool IsEquipmentSelectionExpanded
        {
            get { return _isEquipmentSelectionExpanded; }
            set
            {
                if (Set(ref _isEquipmentSelectionExpanded, value))
                {
                    OnPropertyChanged(nameof(EquipmentOptionsToggleText));
                }
            }
        }

        public string EquipmentOptionsToggleText
        {
            get { return IsEquipmentSelectionExpanded ? "OPTIONS  -" : "OPTIONS  +"; }
        }

        public bool IsSizeEvaluationCompleted
        {
            get { return _isSizeEvaluationCompleted; }
            set
            {
                if (Set(ref _isSizeEvaluationCompleted, value))
                {
                    OnPropertyChanged(nameof(ShowEquipmentDefaultMessage));
                    OnPropertyChanged(nameof(ShowEquipmentOptions));
                }
            }
        }

        public bool ShowEquipmentDefaultMessage
        {
            get { return !IsSizeEvaluationCompleted; }
        }

        public bool ShowEquipmentOptions
        {
            get { return IsSizeEvaluationCompleted; }
        }

        public EquipmentSelectionCardViewModel SelectedEquipmentOption
        {
            get { return _selectedEquipmentOption; }
            set
            {
                if (Set(ref _selectedEquipmentOption, value))
                {
                    OnPropertyChanged(nameof(CanConfirmEquipment));
                }
            }
        }

        public bool CanConfirmEquipment
        {
            get { return SelectedEquipmentOption != null; }
        }

        public bool IsEquipmentInsertStatusVisible
        {
            get { return _isEquipmentInsertStatusVisible; }
            set { Set(ref _isEquipmentInsertStatusVisible, value); }
        }

        public string EquipmentInsertStatusText
        {
            get { return _equipmentInsertStatusText; }
            set { Set(ref _equipmentInsertStatusText, value); }
        }

        public bool IsAhuSubModuleConfigurationVisible
        {
            get { return _isAhuSubModuleConfigurationVisible; }
            set
            {
                if (Set(ref _isAhuSubModuleConfigurationVisible, value))
                {
                    OnPropertyChanged(nameof(ConfirmEquipmentButtonText));
                    OnPropertyChanged(nameof(IsFlowRateCardVisible));
                    OnPropertyChanged(nameof(IsEquipmentSelectionVisible));
                    OnPropertyChanged(nameof(IsConnectivityLayoutVisible));
                }
            }
        }

        public bool IsFlowRateCardVisible
        {
            get { return !IsAhuSubModuleConfigurationVisible; }
        }

        public bool HasValidRoomSelection
        {
            get { return CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey); }
        }

        public bool HasValidFlowRateSelection
        {
            get
            {
                return CurrentEditor != null &&
                    !string.IsNullOrWhiteSpace(CurrentEditor.SelectedFlowRate) &&
                    !string.Equals(CurrentEditor.SelectedFlowRate, "Select flow rate", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsEquipmentSelectionVisible
        {
            get
            {
                return HasValidRoomSelection &&
                    HasValidFlowRateSelection &&
                    !IsAhuSubModuleConfigurationVisible;
            }
        }

        public bool IsConnectivityLayoutVisible
        {
            get { return IsAhuSubModuleConfigurationVisible && IsConnectivityUnlocked; }
        }

        public bool IsDeliveryRouteCardVisible
        {
            get { return false; }
        }

        public string ConfirmEquipmentButtonText
        {
            get { return IsAhuSubModuleConfigurationVisible ? "Equipment Confirmed" : "Confirm Equipment"; }
        }

        public string ConfirmedEquipmentName
        {
            get { return _confirmedEquipmentName; }
            set { Set(ref _confirmedEquipmentName, NormalizeCubicMeterUnit(value)); }
        }

        public string ConfirmedEquipmentDimensionsValueText
        {
            get { return _confirmedEquipmentDimensionsValueText; }
            set { Set(ref _confirmedEquipmentDimensionsValueText, value); }
        }

        public string ConfirmedEquipmentAirflowValueText
        {
            get { return _confirmedEquipmentAirflowValueText; }
            set { Set(ref _confirmedEquipmentAirflowValueText, NormalizeCubicMeterUnit(value)); }
        }

        public string ConfirmedEquipmentWeightValueText
        {
            get { return _confirmedEquipmentWeightValueText; }
            set { Set(ref _confirmedEquipmentWeightValueText, value); }
        }

        public string ConfirmedEquipmentMaintenanceSpaceValueText
        {
            get { return _confirmedEquipmentMaintenanceSpaceValueText; }
            set { Set(ref _confirmedEquipmentMaintenanceSpaceValueText, value); }
        }

        public bool HasConfirmedEquipmentValidationResult
        {
            get { return _currentEquipmentValidation != null && _currentEquipmentValidation.HasResult; }
        }

        public bool HasConfirmedEquipmentValidationReasons
        {
            get { return ConfirmedEquipmentValidationReasons.Count > 0; }
        }

        public string ConfirmedEquipmentValidationStatusText
        {
            get
            {
                return HasConfirmedEquipmentValidationResult
                    ? _currentEquipmentValidation.Status ?? string.Empty
                    : string.Empty;
            }
        }

        public string ConfirmedEquipmentClearanceCheckText
        {
            get
            {
                if (!HasConfirmedEquipmentValidationResult)
                {
                    return "-";
                }

                return _currentEquipmentValidation.IsValid ? "Passed" : "Failed";
            }
        }

        public Brush ConfirmedEquipmentValidationBadgeBackground
        {
            get
            {
                if (!HasConfirmedEquipmentValidationResult)
                {
                    return Brushes.Transparent;
                }

                return _currentEquipmentValidation.IsValid
                    ? new SolidColorBrush(Color.FromRgb(27, 124, 73))
                    : new SolidColorBrush(Color.FromRgb(180, 35, 24));
            }
        }

        public string AhuSubModuleCountText
        {
            get { return _ahuSubModuleCountText; }
            set { Set(ref _ahuSubModuleCountText, value); }
        }

        public bool IsConnectivityExpanded
        {
            get { return _isConnectivityExpanded; }
            set
            {
                if (Set(ref _isConnectivityExpanded, value))
                {
                    OnPropertyChanged(nameof(ConnectivityAdvancedToggleText));
                }
            }
        }

        public string ConnectivityAdvancedToggleText
        {
            get { return IsConnectivityExpanded ? "ADVANCED  -" : "ADVANCED  +"; }
        }

        public bool IsConnectivityUnlocked
        {
            get { return _isConnectivityUnlocked; }
            set
            {
                if (Set(ref _isConnectivityUnlocked, value))
                {
                    OnPropertyChanged(nameof(IsConnectivityLayoutVisible));
                    RefreshConnectivityState();
                }
            }
        }

        public bool IsDuctWorkGenerated
        {
            get { return _isDuctWorkGenerated; }
            set
            {
                if (Set(ref _isDuctWorkGenerated, value))
                {
                    RefreshConnectivityState();
                }
            }
        }

        public bool IsPipeWorkGenerated
        {
            get { return _isPipeWorkGenerated; }
            set
            {
                if (Set(ref _isPipeWorkGenerated, value))
                {
                    RefreshConnectivityState();
                }
            }
        }

        public string DuctWorkActionButtonText
        {
            get { return IsDuctWorkGenerated ? "Regenerate Ductwork" : "Create Ductwork"; }
        }

        public string PipeWorkActionButtonText
        {
            get { return IsPipeWorkGenerated ? "Regenerate Pipework" : "Create Pipework"; }
        }

        public bool CanRemoveDuctWork
        {
            get { return IsDuctWorkGenerated && CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey); }
        }

        public bool CanRemovePipeWork
        {
            get { return IsPipeWorkGenerated && CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey); }
        }

        public bool IsDeliveryRouteExpanded
        {
            get { return _isDeliveryRouteExpanded; }
            set
            {
                if (Set(ref _isDeliveryRouteExpanded, value))
                {
                    OnPropertyChanged(nameof(DeliveryRouteToggleText));
                }
            }
        }

        public string DeliveryRouteToggleText
        {
            get { return IsDeliveryRouteExpanded ? "ROUTE  -" : "ROUTE  +"; }
        }

        public EditorLiftOptionViewModel SelectedDeliveryStartLift
        {
            get { return _selectedDeliveryStartLift; }
            set
            {
                if (Set(ref _selectedDeliveryStartLift, value))
                {
                    ClearDeliveryRouteResult();
                    if (value != null &&
                        !string.IsNullOrWhiteSpace(value.Key))
                    {
                        _ = RoomRecognitionPaneRuntime.RequestFocusLiftAsync(value.Key);
                    }
                }
            }
        }

        public EditorRoomOptionViewModel SelectedDeliveryTargetRoom
        {
            get { return _selectedDeliveryTargetRoom; }
            set
            {
                if (Set(ref _selectedDeliveryTargetRoom, value))
                {
                    ClearDeliveryRouteResult();
                    if (value != null &&
                        !string.IsNullOrWhiteSpace(value.Key))
                    {
                        _ = RoomRecognitionPaneRuntime.RequestFocusRoomAsync(value.Key);
                    }
                }
            }
        }

        public string DeliveryStartPointName
        {
            get { return _deliveryStartPointName; }
            set { Set(ref _deliveryStartPointName, value); }
        }

        public string DeliveryStartPointStatus
        {
            get { return _deliveryStartPointStatus; }
            set { Set(ref _deliveryStartPointStatus, value); }
        }

        public string DeliveryTargetName
        {
            get { return _deliveryTargetName; }
            set { Set(ref _deliveryTargetName, value); }
        }

        public string DeliveryTargetStatus
        {
            get { return _deliveryTargetStatus; }
            set { Set(ref _deliveryTargetStatus, value); }
        }

        public string DeliveryRouteHintText
        {
            get { return _deliveryRouteHintText; }
            set { Set(ref _deliveryRouteHintText, value); }
        }

        public bool IsDeliveryRouteResultVisible
        {
            get { return _isDeliveryRouteResultVisible; }
            set { Set(ref _isDeliveryRouteResultVisible, value); }
        }

        public string DeliveryRouteResultMessage
        {
            get { return _deliveryRouteResultMessage; }
            set { Set(ref _deliveryRouteResultMessage, value); }
        }

        public string DeliveryRouteLengthText
        {
            get { return _deliveryRouteLengthText; }
            set { Set(ref _deliveryRouteLengthText, value); }
        }

        public string DeliveryRouteDisassemblyText
        {
            get
            {
                int count = AhuSubModules != null ? AhuSubModules.Count : 0;
                return count <= 0 ? "-" : count + " sub-modules";
            }
        }

        public string DeliveryRouteMaxDimsText
        {
            get { return ResolveDeliveryRouteMaxDimsText(); }
        }

        public bool CanCreateDuctWork
        {
            get
            {
                return IsConnectivityUnlocked && CurrentEditor != null &&
                       IsConcreteSelection(CurrentEditor.SelectedSadSize) &&
                       IsConcreteSelection(CurrentEditor.SelectedRadSize) &&
                       IsConcreteWallSelection(CurrentEditor.SelectedSadWallOption) &&
                       IsConcreteWallSelection(CurrentEditor.SelectedRadWallOption);
            }
        }

        public bool CanCreatePipeWork
        {
            get
            {
                return IsConnectivityUnlocked && CurrentEditor != null &&
                       IsConcreteSelection(CurrentEditor.SelectedChwsPipeSize) &&
                       IsConcreteSelection(CurrentEditor.SelectedChwrPipeSize) &&
                       IsConcreteWallSelection(CurrentEditor.SelectedChwsWallOption) &&
                       IsConcreteWallSelection(CurrentEditor.SelectedChwrWallOption);
            }
        }

        public bool HasOptionalEquipment
        {
            get { return OptionalEquipmentOptions.Count > 0; }
        }

        internal string SelectedRoomKey
        {
            get { return _selectedRoomKey; }
            set { Set(ref _selectedRoomKey, value); }
        }

        internal string HighlightedFamilyKey
        {
            get { return _highlightedFamilyKey; }
            set
            {
                if (Set(ref _highlightedFamilyKey, value))
                {
                    RefreshFamilyHighlight();
                    RefreshEquipmentHighlight();
                }
            }
        }

        public RoomDetailPaneViewModel()
        {
            NewSolutionCommand = new DelegateCommand(_ => OpenCreateLayoutDialog());
            EnterLayoutCompareModeCommand = new DelegateCommand(_ => EnterLayoutCompareMode());
            FinishLayoutCompareModeCommand = new DelegateCommand(_ => FinishLayoutCompareMode());
            CancelLayoutCompareModeCommand = new DelegateCommand(_ => CancelLayoutCompareMode());
            ClearCompareRoutesCommand = new DelegateCommand(_ => ClearCompareRoutes());
            CancelEditorCommand = new DelegateCommand(_ => CancelEditor());
            SaveSolutionCommand = new DelegateCommand(_ => SaveCurrentSolution(false));
            SaveAndSubmitSolutionCommand = new DelegateCommand(_ => SaveCurrentSolution(true));
            PlaceholderActionCommand = new DelegateCommand(_ => ShowNotImplementedMessage());
            SizeEvaluationCommand = new DelegateCommand(_ => RunSizeEvaluation());
            ToggleEquipmentOptionsCommand = new DelegateCommand(_ => ToggleEquipmentOptions());
            ConfirmEquipmentCommand = new DelegateCommand(_ => ConfirmEquipment());
            ChangeConfirmedEquipmentCommand = new DelegateCommand(_ => ChangeConfirmedEquipment());
            ToggleConnectivityAdvancedCommand = new DelegateCommand(_ => ToggleConnectivityAdvanced());
            EditSadSizeCommand = new DelegateCommand(_ => EditDuctSize("SAD"));
            EditRadSizeCommand = new DelegateCommand(_ => EditDuctSize("RAD"));
            EditChwsPipeSizeCommand = new DelegateCommand(_ => EditPipeSize("CHWS"));
            EditChwrPipeSizeCommand = new DelegateCommand(_ => EditPipeSize("CHWR"));
            CreateDuctWorkCommand = new DelegateCommand(_ => CreateDuctWork());
            RemoveDuctWorkCommand = new DelegateCommand(_ => RemoveDuctWork());
            CreatePipeWorkCommand = new DelegateCommand(_ => CreatePipeWork());
            RemovePipeWorkCommand = new DelegateCommand(_ => RemovePipeWork());
            PickPipeWallCommand = new DelegateCommand(_ => PickPipeWall());
            CreatePipeSystemCommand = new DelegateCommand(_ => CreatePipeSystem());
            PickDuctWallCommand = new DelegateCommand(_ => PickDuctWall());
            CreateDuctSystemCommand = new DelegateCommand(_ => CreateDuctSystem());
            DetailLayoutPlanCommand = new DelegateCommand(parameter => ShowLayoutPlanDetail(parameter as LayoutPlanCardViewModel));
            ExportLayoutPlanCommand = new DelegateCommand(parameter => ExportLayoutPlan(parameter as LayoutPlanCardViewModel));
            DeleteLayoutPlanCommand = new DelegateCommand(parameter => DeleteLayoutPlan(parameter as LayoutPlanCardViewModel));
            ToggleDeliveryRouteCommand = new DelegateCommand(_ => ToggleDeliveryRoute());
            DefineDeliveryStartPointCommand = new DelegateCommand(_ => DefineDeliveryStartPoint());
            DefineDeliveryTargetPointCommand = new DelegateCommand(_ => DefineDeliveryTargetPoint());
            GenerateDeliveryRouteCommand = new DelegateCommand(_ => GenerateDeliveryRoute());

            string[] flowRates = new[]
            {
                "Select flow rate",
                "1 m³/s",
                "2 m³/s",
                "3 m³/s",
                "4 m³/s",
                "5 m³/s",
                "6 m³/s",
                "7 m³/s",
                "8 m³/s",
                "9 m³/s",
                "10 m³/s"
            };
            foreach (string flowRate in flowRates.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                FlowRateOptions.Add(flowRate);
            }

            SetConnectivitySizeOptions(null);

            CurrentEditor = CreateDefaultEditor();
            LoadFamilyOptions();
            ResetEquipmentSelectionToDefault();
            ResetDeliveryRouteState();
        }

        public void LoadFamilyOptions()
        {
            FamilyOptions.Clear();

            List<CadToRevit.Services.Rooms.RoomCustomFamilyOption> options =
                CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService.GetOptions().ToList();

            foreach (var option in options)
            {
                string familyKey = option.Key;
                Action insertAction = () =>
                {
                    string displayName = option.DisplayName ?? option.FileName ?? string.Empty;
                    InsertFamilyWithInlineStatus(familyKey, displayName, null);
                };

                FamilyOptions.Add(new RoomCustomFamilyItemViewModel
                {
                    FamilyKey = familyKey,
                    DisplayName = option.DisplayName,
                    FileName = option.FileName,
                    FullPath = option.FullPath,
                    Description = option.Description,
                    AirflowM3s = option.AirflowM3s,
                    TotalLengthMm = option.TotalLengthMm,
                    HeightMm = option.HeightMm,
                    WidthMm = option.WidthMm,
                    MbLengthMm = option.MbLengthMm,
                    FilterLengthMm = option.FilterLengthMm,
                    CoilLengthMm = option.CoilLengthMm,
                    FanLengthMm = option.FanLengthMm,
                    ValveChamberLengthMm = option.ValveChamberLengthMm,
                    ValveChamberWidthMm = option.ValveChamberWidthMm,
                    ElChamberLengthMm = option.ElChamberLengthMm,
                    ElChamberWidthMm = option.ElChamberWidthMm,
                    WeightKg = option.WeightKg,
                    RequiredMaintenanceSpaceMm = option.RequiredMaintenanceSpaceMm,
                    RequiredMaintenanceSpaceSide = option.RequiredMaintenanceSpaceSide,
                    MaintenanceDoorSideMm = option.MaintenanceDoorSideMm,
                    MaintenanceOtherSideMm = option.MaintenanceOtherSideMm,
                    MaintenanceFrontBackMm = option.MaintenanceFrontBackMm,
                    IsMissing = !File.Exists(option.FullPath),
                    SetCommand = new DelegateCommand(_ => insertAction())
                });
            }

            RefreshFamilyHighlight();
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
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

        private void OnCurrentEditorPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            string propertyName = e != null ? e.PropertyName : string.Empty;
            bool isRoomProperty =
                string.IsNullOrWhiteSpace(propertyName) ||
                string.Equals(propertyName, nameof(SolutionEditorViewModel.SelectedRoomOption), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, nameof(SolutionEditorViewModel.RoomKey), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, nameof(SolutionEditorViewModel.RoomName), StringComparison.OrdinalIgnoreCase);
            bool isFlowRateProperty =
                string.IsNullOrWhiteSpace(propertyName) ||
                string.Equals(propertyName, nameof(SolutionEditorViewModel.SelectedFlowRate), StringComparison.OrdinalIgnoreCase);

            if (isRoomProperty || isFlowRateProperty)
            {
                NotifyLayoutStepVisibilityChanged();
            }

            if (!isRoomProperty)
            {
                return;
            }

            // SelectedRoomOption raises PropertyChanged before it copies the new
            // RoomKey. Handle the actual room transition only when RoomKey itself
            // changes, otherwise the previous room's validation badge can leak into
            // the newly selected room.
            if (string.Equals(propertyName, nameof(SolutionEditorViewModel.RoomKey), StringComparison.OrdinalIgnoreCase))
            {
                string previousRoomKey = _lastEditorRoomKey ?? string.Empty;
                string newRoomKey = CurrentEditor != null
                    ? (CurrentEditor.RoomKey ?? string.Empty)
                    : string.Empty;

                if (!string.Equals(previousRoomKey, newRoomKey, StringComparison.OrdinalIgnoreCase))
                {
                    _lastEditorRoomKey = newRoomKey;

                    ResetEquipmentSelectionForTargetRoomChange();

                    DiagnosticRecorder.AppendDebug(
                        "[LayoutTargetRoom] Changed. PreviousRoomKey=" + previousRoomKey +
                        ", NewRoomKey=" + newRoomKey +
                        ", EquipmentValidationCleared=True, EquipmentOptionsPreserved=" +
                        IsSizeEvaluationCompleted);

                    _ = CleanupPreviousRoomPreviewAfterTargetRoomChangeAsync(previousRoomKey, newRoomKey);
                }
            }

            SetDeliveryTargetFromCurrentRoom();
            ClearDeliveryRouteResult();
        }

        private void ResetEquipmentSelectionForTargetRoomChange()
        {
            // The candidate list is based on Flow Rate and may be reused, but every
            // fit result is room-specific. Keep Size Evaluation/options visible and
            // clear only the previous room's selection/validation state.
            ClearEquipmentInsertStatus();

            foreach (EquipmentSelectionCardViewModel option in EquipmentOptions)
            {
                if (option == null)
                {
                    continue;
                }

                option.IsSelected = false;
                option.IsChecking = false;
                option.ClearValidationResult();
            }

            SelectedEquipmentOption = null;
            HighlightedFamilyKey = string.Empty;

            HideAhuSubModuleConfiguration();
            AhuSubModules.Clear();
            RefreshDeliveryRouteModuleSummary();

            IsDuctWorkGenerated = false;
            IsPipeWorkGenerated = false;
            RefreshConnectivityState();

            if (CurrentEditor != null)
            {
                CurrentEditor.SelectedEquipmentDisplayName = string.Empty;
                CurrentEditor.SelectedEquipmentFamilyKey = string.Empty;
            }

            OnPropertyChanged(nameof(HasOptionalEquipment));
            OnPropertyChanged(nameof(CanConfirmEquipment));
        }

        private async Task CleanupPreviousRoomPreviewAfterTargetRoomChangeAsync(
            string previousRoomKey,
            string newRoomKey)
        {
            try
            {
                // placement_point is global preview state; once the Target Room
                // changes the previous marker is no longer meaningful.
                await RoomRecognitionPaneRuntime.RequestClearAhuPlacementPointMarkerAsync();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutTargetRoom] Placement marker clear failed. Error=" + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(previousRoomKey) ||
                string.Equals(previousRoomKey, newRoomKey, StringComparison.OrdinalIgnoreCase) ||
                !_editorPreviewRoomKeys.Contains(previousRoomKey))
            {
                return;
            }

            try
            {
                // Remove only a preview that this editor session actually inserted.
                // Do not touch an untouched submitted AHU merely because the user
                // changes the dropdown.
                await RoomRecognitionPaneRuntime.RequestClearRoomEquipmentLayoutAsync(previousRoomKey);

                if (_editorOriginalFamilyKeyByRoomKey.TryGetValue(previousRoomKey, out string originalFamilyKey) &&
                    !string.IsNullOrWhiteSpace(originalFamilyKey))
                {
                    await RoomRecognitionPaneRuntime.RequestSetRoomCustomFamilyAsync(
                        previousRoomKey,
                        originalFamilyKey);
                }

                _editorPreviewRoomKeys.Remove(previousRoomKey);

                DiagnosticRecorder.AppendDebug(
                    "[LayoutTargetRoom] Previous preview cleared. PreviousRoomKey=" +
                    previousRoomKey +
                    ", OriginalEquipmentRestored=" +
                    (!string.IsNullOrWhiteSpace(originalFamilyKey)));
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutTargetRoom] Previous preview cleanup failed. PreviousRoomKey=" +
                    previousRoomKey +
                    ", Error=" + ex.Message);
            }
        }

        private void NotifyLayoutStepVisibilityChanged()
        {
            OnPropertyChanged(nameof(HasValidRoomSelection));
            OnPropertyChanged(nameof(HasValidFlowRateSelection));
            OnPropertyChanged(nameof(IsFlowRateCardVisible));
            OnPropertyChanged(nameof(IsEquipmentSelectionVisible));
            OnPropertyChanged(nameof(IsConnectivityLayoutVisible));
            OnPropertyChanged(nameof(IsDeliveryRouteCardVisible));
        }

        private void SetDeliveryTargetFromCurrentRoom()
        {
            EditorRoomOptionViewModel room = null;

            if (CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                room = EditorRoomOptions.FirstOrDefault(x =>
                    string.Equals(x.Key, CurrentEditor.RoomKey, StringComparison.OrdinalIgnoreCase));
            }

            if (room == null && CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomName))
            {
                room = EditorRoomOptions.FirstOrDefault(x =>
                    string.Equals(x.RoomName, CurrentEditor.RoomName, StringComparison.OrdinalIgnoreCase));
            }

            if (room != null && !string.IsNullOrWhiteSpace(room.Key))
            {
                _selectedDeliveryTargetRoom = room;
                OnPropertyChanged(nameof(SelectedDeliveryTargetRoom));

                DeliveryTargetName = string.IsNullOrWhiteSpace(room.RoomName)
                    ? (CurrentEditor != null ? CurrentEditor.RoomName : string.Empty)
                    : room.RoomName;
                DeliveryTargetStatus = "Ready";
                return;
            }

            string fallbackName = CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomName)
                ? CurrentEditor.RoomName
                : RoomName;

            DeliveryTargetName = string.IsNullOrWhiteSpace(fallbackName) || string.Equals(fallbackName, "-", StringComparison.OrdinalIgnoreCase)
                ? "Not selected"
                : fallbackName;
            DeliveryTargetStatus = string.Equals(DeliveryTargetName, "Not selected", StringComparison.OrdinalIgnoreCase)
                ? "Waiting"
                : "Ready";
        }

        private void RefreshDeliveryRouteModuleSummary()
        {
            OnPropertyChanged(nameof(DeliveryRouteDisassemblyText));
            OnPropertyChanged(nameof(DeliveryRouteMaxDimsText));
        }

        private void RefreshFamilyHighlight()
        {
            foreach (RoomCustomFamilyItemViewModel item in FamilyOptions)
            {
                item.IsHighlighted = string.Equals(item.FamilyKey, HighlightedFamilyKey, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void RefreshEquipmentHighlight()
        {
        }

        internal void ResetForNoSelection()
        {
            CurrentEditorLayoutId = string.Empty;
            CurrentPageMode = RoomDetailPageMode.Overview;
            CurrentEditor = CreateDefaultEditor();
            HighlightedFamilyKey = string.Empty;
            ResetEquipmentSelectionToDefault();
            ResetDeliveryRouteState();
        }

        internal void PrepareLayoutPlansOverview()
        {
            ExitLayoutCompareMode();
            CurrentPageMode = RoomDetailPageMode.Overview;
        }

        internal void HandleSelectedRoomChanged(string roomKey, string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                ResetForNoSelection();
                return;
            }

            if (CurrentEditor == null ||
                !string.Equals(CurrentEditor.RoomKey, roomKey, StringComparison.OrdinalIgnoreCase))
            {
                CurrentEditorLayoutId = string.Empty;
                CurrentPageMode = RoomDetailPageMode.Overview;
                CurrentEditor = CreateDefaultEditor(roomKey, roomName);
                ResetDeliveryRouteState();
            }

            SyncCurrentEditorRoomSelection();
            ResetEquipmentSelectionToDefault();
        }


        private void EnterLayoutCompareMode()
        {
            _selectedCompareLayoutIds.Clear();
            OnPropertyChanged(nameof(DoneCompareButtonText));
            IsLayoutCompareMode = true;
        }

        private void CancelLayoutCompareMode()
        {
            _selectedCompareLayoutIds.Clear();
            IsLayoutCompareMode = false;
            OnPropertyChanged(nameof(DoneCompareButtonText));
            OnPropertyChanged(nameof(CompareRoutesDisplayedVisibility));
            RefreshLayoutPlanCompareState();
        }

        private async void ClearCompareRoutes()
        {
            try
            {
                bool ok = await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
                if (!ok)
                {
                    MessageBox.Show(
                        "Failed to clear route comparison paths.",
                        "CadToRevit - Route Compare",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SetCompareRoutesDisplayedCount(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CadToRevit - Route Compare", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetCompareRoutesDisplayedCount(int count)
        {
            int normalized = Math.Max(0, count);
            if (_compareRoutesDisplayedCount == normalized)
            {
                return;
            }

            _compareRoutesDisplayedCount = normalized;
            OnPropertyChanged(nameof(CompareRoutesDisplayedText));
            OnPropertyChanged(nameof(CompareRoutesDisplayedVisibility));
        }

        private async void FinishLayoutCompareMode()
        {
            List<string> selectedIds = _selectedCompareLayoutIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            IsLayoutCompareMode = false;
            RefreshLayoutPlanCompareState();

            if (selectedIds.Count == 0)
            {
                return;
            }

            try
            {
                CalculatePathExecutionResult result = await RoomRecognitionPaneRuntime.RequestDrawLayoutPlanRouteComparisonAsync(selectedIds);
                if (result == null || !result.Success || !result.Drawn)
                {
                    MessageBox.Show(
                        result != null && !string.IsNullOrWhiteSpace(result.Message)
                            ? result.Message
                            : "Failed to draw route comparison.",
                        "CadToRevit - Route Compare",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    SetCompareRoutesDisplayedCount(0);
                }
                else
                {
                    SetCompareRoutesDisplayedCount(selectedIds.Count);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CadToRevit - Route Compare", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _selectedCompareLayoutIds.Clear();
                OnPropertyChanged(nameof(DoneCompareButtonText));
                RefreshLayoutPlanCompareState();
            }
        }

        private void ToggleLayoutPlanCompare(LayoutPlanCardViewModel plan)
        {
            if (!IsLayoutCompareMode || plan == null || string.IsNullOrWhiteSpace(plan.LayoutId))
            {
                return;
            }

            if (!plan.HasDeliveryRoute)
            {
                MessageBox.Show(
                    "This layout plan has no saved delivery route data.",
                    "CadToRevit - Route Compare",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string existing = _selectedCompareLayoutIds.FirstOrDefault(x =>
                string.Equals(x, plan.LayoutId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _selectedCompareLayoutIds.Remove(existing);
            }
            else
            {
                if (_selectedCompareLayoutIds.Count >= 3)
                {
                    MessageBox.Show(
                        "You can select up to 3 routes to compare.",
                        "CadToRevit - Route Compare",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _selectedCompareLayoutIds.Add(plan.LayoutId);
            }

            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshLayoutPlanCompareState();
        }

        private void RefreshLayoutPlanCompareState()
        {
            foreach (LayoutPlanCardViewModel card in LayoutPlans)
            {
                if (card == null)
                {
                    continue;
                }

                card.IsCompareMode = IsLayoutCompareMode;
                card.IsCompareSelected = _selectedCompareLayoutIds.Any(x =>
                    string.Equals(x, card.LayoutId, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void ExitLayoutCompareMode()
        {
            if (!IsLayoutCompareMode && _selectedCompareLayoutIds.Count == 0)
            {
                return;
            }

            _selectedCompareLayoutIds.Clear();
            IsLayoutCompareMode = false;
            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshLayoutPlanCompareState();
        }

        private void OpenCreateLayoutDialog()
        {
            ExitLayoutCompareMode();
            CreateLayoutContextWindow dialog = new CreateLayoutContextWindow();

            try
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch
            {
            }

            bool? result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            BeginNewSolution(dialog.SelectedPlanningContext, dialog.SelectedEquipmentType);
        }

        private void BeginNewSolution(
            string planningContext = "New Building Design",
            string equipmentType = "AHU")
        {
            ResetEditorEquipmentRollbackState();

            string roomKey = SelectedRoomKey ?? string.Empty;
            string roomName = string.IsNullOrWhiteSpace(RoomName) || RoomName == "-" ? string.Empty : RoomName;
            CurrentEditorLayoutId = string.Empty;
            CurrentEditor = CreateDefaultEditor(roomKey, roomName);
            if (CurrentEditor != null)
            {
                CurrentEditor.PlanningContext = planningContext ?? "New Building Design";
                CurrentEditor.EquipmentType = equipmentType ?? "AHU";
                if (string.Equals(CurrentEditor.EquipmentType, "PAU", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentEditor.SelectedEquipmentDisplayName = "Primary Air Handling Unit (PAU)";
                }
            }

            SyncCurrentEditorRoomSelection();
            CurrentPageMode = RoomDetailPageMode.SolutionEditor;
            ResetEquipmentSelectionToDefault();
            ResetDeliveryRouteState();
        }

        private async void CancelEditor()
        {
            try
            {
                await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            }
            catch
            {
            }

            try
            {
                await RoomRecognitionPaneRuntime.RequestClearAhuPlacementPointMarkerAsync();
            }
            catch
            {
            }

            // Cancel means discarding only the current, unsubmitted editor preview.
            // Any AHU that was already visible before this editor session (normally the
            // AHU from the latest Save & Submit for that room) must be restored.
            List<string> touchedRoomKeys = _editorTouchedRoomKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string roomKey in touchedRoomKeys)
            {
                try
                {
                    await RoomRecognitionPaneRuntime.RequestClearRoomEquipmentLayoutAsync(roomKey);

                    if (_editorOriginalFamilyKeyByRoomKey.TryGetValue(roomKey, out string originalFamilyKey) &&
                        !string.IsNullOrWhiteSpace(originalFamilyKey))
                    {
                        await RoomRecognitionPaneRuntime.RequestSetRoomCustomFamilyAsync(
                            roomKey,
                            originalFamilyKey);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "CadToRevit - Room Detail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            ResetEditorEquipmentRollbackState();
            CurrentEditorLayoutId = string.Empty;
            CurrentEditor = CreateDefaultEditor(SelectedRoomKey, RoomName);
            CurrentPageMode = RoomDetailPageMode.Overview;
            ResetEquipmentSelectionToDefault();
            ResetDeliveryRouteState();
        }

        private void CaptureEditorEquipmentRollbackState(string roomKey)
        {
            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            if (!_editorOriginalFamilyKeyByRoomKey.ContainsKey(roomKey))
            {
                _editorOriginalFamilyKeyByRoomKey[roomKey] =
                    RoomRecognitionPaneRuntime.GetPlacedRoomCustomFamilyKey(roomKey);
            }

            _editorTouchedRoomKeys.Add(roomKey);
        }

        private void ResetEditorEquipmentRollbackState()
        {
            _editorOriginalFamilyKeyByRoomKey.Clear();
            _editorTouchedRoomKeys.Clear();
            _editorPreviewRoomKeys.Clear();
        }

        private void PrepareEditorRollbackState(
            string roomKey,
            string originalSubmittedFamilyKey)
        {
            ResetEditorEquipmentRollbackState();

            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return;
            }

            // Store an empty value as well. An empty value means that the room had no
            // submitted AHU before this editor session, so Cancel must leave it empty
            // after clearing the temporary Detail preview.
            _editorOriginalFamilyKeyByRoomKey[roomKey] =
                originalSubmittedFamilyKey ?? string.Empty;
            _editorTouchedRoomKeys.Add(roomKey);
        }

        private async void SaveCurrentSolution(bool submitAndApply)
        {
            if (CurrentEditor == null)
            {
                return;
            }

            if (TryBlockRmaaOversizedAction(
                submitAndApply ? "saved and submitted" : "saved"))
            {
                return;
            }

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                submitAndApply
                    ? "Do you want to save and submit this layout plan?"
                    : "Do you want to save this layout plan?",
                "EMSD AI Tool");
            if (!confirm)
            {
                return;
            }

            RoomLayoutPlanDto dto = BuildCurrentLayoutPlanDto();
            if (dto == null)
            {
                return;
            }

            // First persist the complete layout configuration while all preview elements still exist.
            bool ok = await RoomRecognitionPaneRuntime.RequestSaveLayoutPlanAsync(dto, false, false);
            if (!ok)
            {
                return;
            }

            bool cleanupOk = await FinalizeSavedLayoutVisualsAsync(dto.RoomKey, submitAndApply);
            if (!cleanupOk)
            {
                return;
            }

            // Save again after cleanup so ActiveGeneratedElements matches the model's final state:
            // Save            -> no active AHU / ductwork / pipework references.
            // Save & Submit   -> keep only the submitted AHU reference for this room.
            bool finalStateSaved = await RoomRecognitionPaneRuntime.RequestSaveLayoutPlanAsync(dto, submitAndApply);
            if (!finalStateSaved)
            {
                return;
            }

            ResetEditorEquipmentRollbackState();
            CurrentEditorLayoutId = string.Empty;
            CurrentEditor = CreateDefaultEditor(SelectedRoomKey, RoomName);
            CurrentPageMode = RoomDetailPageMode.Overview;
            ResetEquipmentSelectionToDefault();
            ResetDeliveryRouteState();
        }

        private async Task<bool> FinalizeSavedLayoutVisualsAsync(
            string roomKey,
            bool submitAndApply)
        {
            bool routeCleared = await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            if (!routeCleared)
            {
                MessageBox.Show(
                    "The layout plan was saved, but the delivery route preview could not be cleared.",
                    "EMSD AI Tool",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            bool placementMarkerCleared =
                await RoomRecognitionPaneRuntime.RequestClearAhuPlacementPointMarkerAsync();
            if (!placementMarkerCleared)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuPlacementPointMarker] Save cleanup could not confirm marker clear.");
            }

            if (string.IsNullOrWhiteSpace(roomKey))
            {
                return true;
            }

            if (!submitAndApply)
            {
                // Save only: keep the saved plan data, but remove every temporary model element
                // (AHU, ductwork and pipework) from the current Revit view/model.
                bool cleared = await RoomRecognitionPaneRuntime.RequestClearRoomEquipmentLayoutAsync(roomKey);
                if (!cleared)
                {
                    MessageBox.Show(
                        "The layout plan was saved, but the AHU / ductwork / pipework preview could not be cleared.",
                        "EMSD AI Tool",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return cleared;
            }

            // Save & Submit: the currently selected AHU remains in the room, while all generated
            // ductwork and pipework are removed before returning to Saved Layout Plans.
            bool ductCleared = await RoomRecognitionPaneRuntime.RequestRemoveDuctWorkAsync(roomKey);
            bool pipeCleared = await RoomRecognitionPaneRuntime.RequestRemovePipeWorkAsync(roomKey);
            if (!ductCleared || !pipeCleared)
            {
                MessageBox.Show(
                    "The layout plan was submitted, but the generated ductwork or pipework could not be cleared.",
                    "EMSD AI Tool",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // RoomCustomFamilyPlacementService.PlaceOrReplace() and the equipment selection flow
            // already remove all previously managed AHU instances for the same RoomKey before
            // placing the new one. Therefore only the AHU from the latest submitted layout remains
            // visible in each room.
            return true;
        }

        private async void DeleteLayoutPlan(LayoutPlanCardViewModel plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.LayoutId))
            {
                return;
            }

            string message = plan.IsActiveLayout
                ? "This layout plan is currently submitted to the selected room.\n\nDeleting it will also remove the active AHU from the Revit model.\n\nContinue?"
                : "Delete this saved layout plan?\n\nThis action cannot be undone.";

            bool confirm = LocalizedDialogService.Confirm(
                PreviewPaneRuntime.UiApplication,
                message,
                "Saved Layout Plans");
            if (!confirm)
            {
                return;
            }

            await RoomRecognitionPaneRuntime.RequestDeleteLayoutPlanAsync(plan.LayoutId);
        }

        private async void ExportLayoutPlan(LayoutPlanCardViewModel plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.LayoutId))
            {
                return;
            }

            await RoomRecognitionPaneRuntime.RequestExportLayoutPlanAsync(plan.LayoutId);
        }

        private async void ShowLayoutPlanDetail(LayoutPlanCardViewModel plan)
        {
            ExitLayoutCompareMode();
            if (plan == null || string.IsNullOrWhiteSpace(plan.LayoutId))
            {
                return;
            }

            await RoomRecognitionPaneRuntime.RequestActivateLayoutPlanAsync(plan.LayoutId);
        }

        private void PickPipeWall()
        {
            if (!HasSelection || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            _ = RoomRecognitionPaneRuntime.RequestPickPipeWallPointAsync(CurrentEditor.RoomKey);
        }

        private void CreatePipeSystem()
        {
            if (!HasSelection || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            _ = RoomRecognitionPaneRuntime.RequestCreatePipeSystemAsync(
                CurrentEditor.RoomKey,
                CurrentEditor.SelectedChwSupply);
        }

        private void PickDuctWall()
        {
            if (!HasSelection || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            _ = RoomRecognitionPaneRuntime.RequestPickDuctWallPointAsync(CurrentEditor.RoomKey);
        }

        private void CreateDuctSystem()
        {
            if (!HasSelection || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            _ = RoomRecognitionPaneRuntime.RequestCreateDuctSystemAsync(
                CurrentEditor.RoomKey,
                CurrentEditor.SelectedSupplyAirDuct);
        }

        private static void ShowNotImplementedMessage()
        {
            MessageBox.Show("Not implemented yet.", "Room Detail", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleEquipmentOptions()
        {
            IsEquipmentSelectionExpanded = !IsEquipmentSelectionExpanded;
        }

        private void ToggleConnectivityAdvanced()
        {
            IsConnectivityExpanded = !IsConnectivityExpanded;
        }

        private void ToggleDeliveryRoute()
        {
            IsDeliveryRouteExpanded = !IsDeliveryRouteExpanded;
        }

        private void DefineDeliveryStartPoint()
        {
            ClearDeliveryRouteResult();

            if (SelectedDeliveryStartLift == null ||
                string.IsNullOrWhiteSpace(SelectedDeliveryStartLift.Key) ||
                string.IsNullOrWhiteSpace(SelectedDeliveryStartLift.DisplayName))
            {
                const string message = "Please select a start point first.";
                LocalizedDialogService.Warning(null, message, "EMSD AI Tool");
                DeliveryRouteHintText = message;
                return;
            }

            DeliveryStartPointName = SelectedDeliveryStartLift.DisplayName;
            DeliveryStartPointStatus = "Ready";
            SetDeliveryTargetFromCurrentRoom();
            DeliveryRouteHintText = string.Equals(DeliveryTargetStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                ? "Route points are ready. Generate the delivery route."
                : "Start point logged. Define the destination next.";
        }

        private void DefineDeliveryTargetPoint()
        {
            ClearDeliveryRouteResult();

            if (SelectedDeliveryTargetRoom == null ||
                string.IsNullOrWhiteSpace(SelectedDeliveryTargetRoom.Key) ||
                string.IsNullOrWhiteSpace(SelectedDeliveryTargetRoom.RoomName))
            {
                DeliveryRouteHintText = "Please select a target room first.";
                return;
            }

            DeliveryTargetName = SelectedDeliveryTargetRoom.RoomName;
            DeliveryTargetStatus = "Ready";

            if (string.Equals(DeliveryStartPointStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                DeliveryRouteHintText = "Route points are ready. Generate the delivery route.";
                return;
            }

            DeliveryRouteHintText = "Start point logged. Define the destination next.";
        }

        private bool IsEquipmentConfirmedForDeliveryRoute()
        {
            if (!IsConnectivityUnlocked)
            {
                return false;
            }

            if (AhuSubModules == null || AhuSubModules.Count == 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ConfirmedEquipmentName))
            {
                return true;
            }

            return CurrentEditor != null &&
                   !string.IsNullOrWhiteSpace(CurrentEditor.SelectedEquipmentDisplayName) &&
                   !string.IsNullOrWhiteSpace(CurrentEditor.SelectedEquipmentFamilyKey);
        }

        private async void GenerateDeliveryRoute()
        {
            ClearDeliveryRouteResult();

            if (SelectedDeliveryStartLift == null ||
                string.IsNullOrWhiteSpace(SelectedDeliveryStartLift.Key) ||
                !string.Equals(DeliveryStartPointStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                DeliveryRouteHintText = "Please define the start point first.";
                return;
            }

            if (!IsEquipmentConfirmedForDeliveryRoute())
            {
                const string message = "Please confirm the selected equipment before generating the delivery route.";
                LocalizedDialogService.Warning(null, message, "EMSD AI Tool");
                DeliveryRouteHintText = message;
                return;
            }

            SetDeliveryTargetFromCurrentRoom();

            if (SelectedDeliveryTargetRoom == null ||
                string.IsNullOrWhiteSpace(SelectedDeliveryTargetRoom.Key))
            {
                DeliveryRouteHintText = "Current target room is not available.";
                return;
            }

            DeliveryRouteConfirmWindow confirm = new DeliveryRouteConfirmWindow(
                SelectedDeliveryStartLift.DisplayName,
                SelectedDeliveryTargetRoom.RoomName);
            bool? ok = confirm.ShowDialog();
            if (ok != true)
            {
                return;
            }

            DeliveryRouteLoadingWindow loadingWindow = null;

            DeliveryRouteHintText = "Preparing route planner...";
            try
            {
                loadingWindow = new DeliveryRouteLoadingWindow();
                loadingWindow.Show();

                DeliveryRoutePreparationResult preparation =
                    await RoomRecognitionPaneRuntime.RequestPrepareDeliveryRouteAsync(
                        SelectedDeliveryStartLift.Key,
                        SelectedDeliveryTargetRoom.Key);
                if (preparation == null || !preparation.Success)
                {
                    string message = preparation != null && !string.IsNullOrWhiteSpace(preparation.Message)
                        ? preparation.Message
                        : "Failed to generate delivery route.";
                    if (message.IndexOf("Route API is not available", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LocalizedDialogService.Warning(null, message, "EMSD AI Tool");
                    }
                    else
                    {
                        LocalizedDialogService.Error(null, message);
                    }
                    DeliveryRouteHintText = "Failed to generate delivery route.";
                    return;
                }

                // Keep the original calculate_path request for fallback/testing.
                // string requestJson = CalculatePathApiService.BuildRequestJson(
                //     preparation.SessionId,
                //     preparation.StartXmm,
                //     preparation.StartYmm,
                //     preparation.GoalXmm,
                //     preparation.GoalYmm);
                // string responseBody = await Task.Run(() => CalculatePathApiService.PostCalculatePath(requestJson));

                int routeModelId = ParseAhuFamilyKeyNumber(
                    CurrentEditor != null ? CurrentEditor.SelectedEquipmentFamilyKey : string.Empty);
                if (routeModelId <= 0 && SelectedEquipmentOption != null)
                {
                    routeModelId = ParseAhuFamilyKeyNumber(SelectedEquipmentOption.FamilyKey);
                }
                if (routeModelId <= 0)
                {
                    const string modelIdMessage =
                        "The confirmed AHU model could not be resolved for route planning.";
                    LocalizedDialogService.Error(null, modelIdMessage);
                    DeliveryRouteHintText = modelIdMessage;
                    return;
                }

                string requestJson = CalculatePathApiService.BuildCutAndReplanRequestJson(
                    preparation.SessionId,
                    routeModelId,
                    preparation.StartXmm,
                    preparation.StartYmm,
                    preparation.GoalXmm,
                    preparation.GoalYmm,
                    preparation.RestrictedAreas);

                DeliveryRouteHintText = "Generating delivery route...";
                string responseBody = await Task.Run(() => CalculatePathApiService.PostCutAndReplan(requestJson));
                CalculatePathExecutionResult result =
                    await RoomRecognitionPaneRuntime.RequestDrawDeliveryRoutePathAsync(responseBody);

                if (result != null && result.Success && result.Drawn)
                {
                    string liftName = SelectedDeliveryStartLift != null
                        ? SelectedDeliveryStartLift.DisplayName
                        : DeliveryStartPointName;
                    string roomName = SelectedDeliveryTargetRoom != null
                        ? SelectedDeliveryTargetRoom.RoomName
                        : DeliveryTargetName;

                    DeliveryRouteResultMessage =
                        "AI logistics planning completed successfully from " +
                        liftName +
                        " to " +
                        roomName +
                        ".";
                    DeliveryRouteLengthText = FormatRouteLength(result.PathLengthMeters);
                    _savedDeliveryRouteResponseBody = !string.IsNullOrWhiteSpace(result.ResponseBody) ? result.ResponseBody : responseBody;
                    _savedDeliveryRouteLengthMeters = result.PathLengthMeters;
                    _savedDeliveryRouteStartLiftKey = SelectedDeliveryStartLift != null ? SelectedDeliveryStartLift.Key ?? string.Empty : string.Empty;
                    _savedDeliveryRouteStartLiftName = liftName ?? string.Empty;
                    _savedDeliveryRouteTargetRoomKey = SelectedDeliveryTargetRoom != null ? SelectedDeliveryTargetRoom.Key ?? string.Empty : string.Empty;
                    _savedDeliveryRouteTargetRoomName = roomName ?? string.Empty;
                    IsDeliveryRouteResultVisible = true;
                    DeliveryRouteHintText = result.PathLengthMeters.HasValue
                        ? "Delivery route generated. Length: " + result.PathLengthMeters.Value.ToString("F2") + " m"
                        : "Delivery route generated.";
                    return;
                }

                IsDeliveryRouteResultVisible = false;
                DeliveryRouteHintText = !string.IsNullOrWhiteSpace(result != null ? result.Message : null)
                    ? "Path planning failed: " + result.Message
                    : "Failed to generate delivery route.";
            }
            catch (Exception ex)
            {
                IsDeliveryRouteResultVisible = false;
                string responseMessage = CalculatePathApiService.ExtractResponseMessage(ex.Message);
                string message = !string.IsNullOrWhiteSpace(responseMessage)
                    ? responseMessage
                    : "Failed to generate delivery route." + Environment.NewLine + ex.Message;
                LocalizedDialogService.Error(null, message);
                DeliveryRouteHintText = "Failed to generate delivery route.";
            }
            finally
            {
                if (loadingWindow != null)
                {
                    loadingWindow.Close();
                }
            }
        }

        private void RunSizeEvaluation()
        {
            if (CurrentEditor == null ||
                string.IsNullOrWhiteSpace(CurrentEditor.SelectedFlowRate) ||
                string.Equals(CurrentEditor.SelectedFlowRate, "Select flow rate", StringComparison.OrdinalIgnoreCase))
            {
                ResetEquipmentSelectionToDefault();
                LocalizedDialogService.Warning(
                    null,
                    "Please select a flow rate before running Size Evaluation.",
                    "EMSD AI Tool");
                return;
            }

            IsEquipmentSelectionExpanded = true;
            IsSizeEvaluationCompleted = true;
            SelectedEquipmentOption = null;
            ClearCurrentEquipmentValidation();
            HideAhuSubModuleConfiguration();
            LoadDemoEquipmentOptions();
        }

        private void ConfirmEquipment()
        {
            if (CurrentEditor == null)
            {
                return;
            }

            if (TryBlockRmaaOversizedAction("confirmed"))
            {
                return;
            }

            if (!CanConfirmEquipment)
            {
                return;
            }

            int flowRateNumber = 0;
            string confirmedName = string.Empty;

            if (SelectedEquipmentOption != null)
            {
                flowRateNumber = ParseAhuFamilyKeyNumber(SelectedEquipmentOption.FamilyKey);
                if (flowRateNumber <= 0)
                {
                    flowRateNumber = ParseFlowRateNumber(SelectedEquipmentOption.DisplayName);
                }

                confirmedName = SelectedEquipmentOption.DisplayName ?? string.Empty;
            }

            if (flowRateNumber <= 0)
            {
                string selectedFlowRate = CurrentEditor.SelectedFlowRate;
                flowRateNumber = ParseFlowRateNumber(selectedFlowRate);
                confirmedName = "AHU Flow Rate " + FormatFlowRateText(selectedFlowRate);
            }

            if (flowRateNumber <= 0)
            {
                return;
            }

            ConfirmedEquipmentName = string.IsNullOrWhiteSpace(confirmedName)
                ? "AHU Flow Rate " + flowRateNumber + " m³/s"
                : confirmedName;
            if (SelectedEquipmentOption != null)
            {
                SetCurrentEquipmentValidation(SelectedEquipmentOption.ToValidationDto());
            }
            ApplyConfirmedEquipmentCardDetails(SelectedEquipmentOption, flowRateNumber);
            PopulateAhuSubModules(flowRateNumber);
            IsAhuSubModuleConfigurationVisible = true;
            IsEquipmentSelectionExpanded = false;
            IsConnectivityUnlocked = true;
        }

        private bool TryBlockRmaaOversizedAction(string actionDescription)
        {
            if (CurrentEditor == null ||
                !IsRmaaReplacementPlanningContext(CurrentEditor.PlanningContext))
            {
                return false;
            }

            EquipmentPlacementValidationDto blockingValidation = null;

            if (SelectedEquipmentOption != null &&
                SelectedEquipmentOption.IsValidationOversized)
            {
                blockingValidation = SelectedEquipmentOption.ToValidationDto();
            }
            else if (_currentEquipmentValidation != null &&
                     _currentEquipmentValidation.HasResult &&
                     !_currentEquipmentValidation.IsValid)
            {
                blockingValidation = _currentEquipmentValidation;
            }

            if (blockingValidation == null)
            {
                return false;
            }

            string action = string.IsNullOrWhiteSpace(actionDescription)
                ? "processed"
                : actionDescription.Trim();

            string message =
                "The selected equipment exceeds the existing room boundaries and cannot be " +
                action +
                " for an RMAA / Replacement layout." +
                Environment.NewLine +
                Environment.NewLine +
                "Please select equipment that fits within the room before continuing.";

            string reason = blockingValidation.Reasons == null
                ? string.Empty
                : string.Join(
                    " ",
                    blockingValidation.Reasons
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim()));

            if (!string.IsNullOrWhiteSpace(reason))
            {
                message +=
                    Environment.NewLine +
                    Environment.NewLine +
                    "Reason: " +
                    reason;
            }

            DiagnosticRecorder.AppendDebug(
                "[RmaaEquipmentValidation] BlockedAction=" +
                action +
                ", RoomKey=" +
                (CurrentEditor.RoomKey ?? string.Empty) +
                ", Status=" +
                (blockingValidation.Status ?? "Oversized") +
                ", Reason=" +
                reason);

            LocalizedDialogService.Warning(
                null,
                message,
                "EMSD AI Tool");

            return true;
        }

        private static bool IsRmaaReplacementPlanningContext(string planningContext)
        {
            string value = (planningContext ?? string.Empty).Trim();
            return value.IndexOf("RMAA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Replacement", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ChangeConfirmedEquipment()
        {
            ResetEquipmentSelectionAndClearModel();
        }

        private void HideAhuSubModuleConfiguration()
        {
            IsAhuSubModuleConfigurationVisible = false;
            ClearConfirmedEquipmentCardDetails();
            ClearCurrentEquipmentValidation();
            LockConnectivityLayout();
        }

        private void ApplyConfirmedEquipmentCardDetails(EquipmentSelectionCardViewModel selectedOption, int flowRateNumber)
        {
            if (selectedOption == null)
            {
                ApplyConfirmedEquipmentCardDetailsFromFamilyKey(string.Empty, flowRateNumber);
                return;
            }

            SetConfirmedEquipmentCardDetails(
                selectedOption.TotalLengthMm,
                selectedOption.WidthMm,
                selectedOption.HeightMm,
                selectedOption.AirflowM3s,
                selectedOption.WeightKg,
                selectedOption.RequiredMaintenanceSpaceMm,
                selectedOption.RequiredMaintenanceSpaceSide,
                flowRateNumber);
        }

        private void ApplyConfirmedEquipmentCardDetailsFromFamilyKey(string familyKey, int flowRateNumber)
        {
            var option = CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService.GetOption(familyKey);
            if (option == null)
            {
                SetConfirmedEquipmentCardDetails(0, 0, 0, 0, 0, 0, string.Empty, flowRateNumber);
                return;
            }

            SetConfirmedEquipmentCardDetails(
                option.TotalLengthMm,
                option.WidthMm,
                option.HeightMm,
                option.AirflowM3s,
                option.WeightKg,
                option.RequiredMaintenanceSpaceMm,
                option.RequiredMaintenanceSpaceSide,
                flowRateNumber);
        }

        private void SetConfirmedEquipmentCardDetails(
            int totalLengthMm,
            int widthMm,
            int heightMm,
            double airflowM3s,
            int weightKg,
            int requiredMaintenanceSpaceMm,
            string requiredMaintenanceSpaceSide,
            int flowRateNumber)
        {
            ConfirmedEquipmentDimensionsValueText = totalLengthMm > 0 && widthMm > 0 && heightMm > 0
                ? "L:" + FormatMm(totalLengthMm) + " x W:" + FormatMm(widthMm) + " x H:" + FormatMm(heightMm)
                : "-";

            double airflow = airflowM3s > 0 ? airflowM3s : flowRateNumber;
            ConfirmedEquipmentAirflowValueText = airflow > 0 ? FormatAirflowValue(airflow) + " m³/s" : "-";
            ConfirmedEquipmentWeightValueText = FormatMm(weightKg > 0 ? weightKg : 1500);

            int maintenanceSpaceMm = requiredMaintenanceSpaceMm > 0 ? requiredMaintenanceSpaceMm : 1200;
            string maintenanceSide = string.IsNullOrWhiteSpace(requiredMaintenanceSpaceSide)
                ? "Access Side"
                : requiredMaintenanceSpaceSide.Trim();
            ConfirmedEquipmentMaintenanceSpaceValueText = FormatMm(maintenanceSpaceMm) + " (" + maintenanceSide + ")";
        }

        private void ClearConfirmedEquipmentCardDetails()
        {
            ConfirmedEquipmentDimensionsValueText = string.Empty;
            ConfirmedEquipmentAirflowValueText = string.Empty;
            ConfirmedEquipmentWeightValueText = string.Empty;
            ConfirmedEquipmentMaintenanceSpaceValueText = string.Empty;
        }

        private void SetCurrentEquipmentValidation(EquipmentPlacementValidationDto validation)
        {
            _currentEquipmentValidation = CloneValidationDto(validation);
            ConfirmedEquipmentValidationReasons.Clear();

            if (_currentEquipmentValidation != null && _currentEquipmentValidation.Reasons != null)
            {
                foreach (string reason in _currentEquipmentValidation.Reasons)
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        ConfirmedEquipmentValidationReasons.Add(reason);
                    }
                }
            }

            OnConfirmedEquipmentValidationChanged();
        }

        private void ClearCurrentEquipmentValidation()
        {
            _currentEquipmentValidation = null;
            ConfirmedEquipmentValidationReasons.Clear();
            OnConfirmedEquipmentValidationChanged();
        }

        private void OnConfirmedEquipmentValidationChanged()
        {
            OnPropertyChanged(nameof(HasConfirmedEquipmentValidationResult));
            OnPropertyChanged(nameof(HasConfirmedEquipmentValidationReasons));
            OnPropertyChanged(nameof(ConfirmedEquipmentValidationStatusText));
            OnPropertyChanged(nameof(ConfirmedEquipmentClearanceCheckText));
            OnPropertyChanged(nameof(ConfirmedEquipmentValidationBadgeBackground));
        }

        private static EquipmentPlacementValidationDto ToValidationDto(AhuPlacementValidationResult result)
        {
            if (result == null || !result.HasResult)
            {
                return null;
            }

            return new EquipmentPlacementValidationDto
            {
                HasResult = result.HasResult,
                IsValid = result.IsValid,
                Status = result.Status ?? string.Empty,
                Reasons = result.Reasons != null
                    ? new List<string>(result.Reasons.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : new List<string>(),
                Source = result.Source ?? string.Empty
            };
        }

        private static EquipmentPlacementValidationDto CloneValidationDto(EquipmentPlacementValidationDto source)
        {
            if (source == null || !source.HasResult)
            {
                return null;
            }

            return new EquipmentPlacementValidationDto
            {
                HasResult = source.HasResult,
                IsValid = source.IsValid,
                Status = source.Status ?? string.Empty,
                Reasons = source.Reasons != null
                    ? new List<string>(source.Reasons.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : new List<string>(),
                Source = source.Source ?? string.Empty
            };
        }

        private static string FormatMm(double value)
        {
            return value > 0 ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : "-";
        }

        private static string FormatAirflowValue(double value)
        {
            if (value <= 0)
            {
                return "-";
            }

            return Math.Abs(value - Math.Round(value)) < 0.0001
                ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string NormalizeCubicMeterUnit(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return text
                .Replace("m3/s", "m³/s")
                .Replace("M3/S", "m³/s")
                .Replace("m^3/s", "m³/s")
                .Replace("M^3/S", "m³/s");
        }

        private void LockConnectivityLayout()
        {
            IsConnectivityUnlocked = false;
            ResetConnectivitySelections();
        }

        internal void SetConnectivitySizeOptions(ConnectivitySizeOptionsPayload payload)
        {
            string sadSize = CurrentEditor != null ? CurrentEditor.SelectedSadSize : "Select";
            string radSize = CurrentEditor != null ? CurrentEditor.SelectedRadSize : "Select";
            string chwsSize = CurrentEditor != null ? CurrentEditor.SelectedChwsPipeSize : "Select";
            string chwrSize = CurrentEditor != null ? CurrentEditor.SelectedChwrPipeSize : "Select";

            List<string> ductSizes = BuildDuctSizeOptions(payload, sadSize, radSize);
            List<string> pipeSizes = BuildPipeSizeOptions(payload, chwsSize, chwrSize);

            DuctWorkSizeOptions.Clear();
            foreach (string size in ductSizes)
            {
                DuctWorkSizeOptions.Add(size);
            }

            PipeWorkSizeOptions.Clear();
            foreach (string size in pipeSizes)
            {
                PipeWorkSizeOptions.Add(size);
            }

            if (CurrentEditor != null)
            {
                CurrentEditor.SelectedSadSize = NormalizeDuctSizeDisplay(sadSize);
                CurrentEditor.SelectedRadSize = NormalizeDuctSizeDisplay(radSize);
                CurrentEditor.SelectedChwsPipeSize = NormalizePipeSizeDisplay(chwsSize);
                CurrentEditor.SelectedChwrPipeSize = NormalizePipeSizeDisplay(chwrSize);
            }
        }

        private async void EditDuctSize(string targetName)
        {
            EditConnectivitySizeWindow window = new EditConnectivitySizeWindow(targetName, true);
            window.SetRevitOwner();

            bool? result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            bool ok = await RoomRecognitionPaneRuntime.RequestAddCustomDuctSizeOptionAsync(window.LengthMm, window.WidthMm);
            if (!ok)
            {
                LocalizedDialogService.Error(null, "Failed to save custom duct size.", "EMSD AI Tool");
                return;
            }

            string displayText = FormatDuctSizeDisplay(window.LengthMm, window.WidthMm);
            EnsureOptionPresent(DuctWorkSizeOptions, displayText);
            if (CurrentEditor != null)
            {
                if (string.Equals(targetName, "RAD", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentEditor.SelectedRadSize = displayText;
                }
                else
                {
                    CurrentEditor.SelectedSadSize = displayText;
                }
            }
        }

        private async void EditPipeSize(string targetName)
        {
            EditConnectivitySizeWindow window = new EditConnectivitySizeWindow(targetName, false);
            window.SetRevitOwner();

            bool? result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            bool ok = await RoomRecognitionPaneRuntime.RequestAddCustomPipeSizeOptionAsync(window.DiameterMm);
            if (!ok)
            {
                LocalizedDialogService.Error(null, "Failed to save custom pipe size.", "EMSD AI Tool");
                return;
            }

            string displayText = FormatPipeSizeDisplay(window.DiameterMm);
            EnsureOptionPresent(PipeWorkSizeOptions, displayText);
            if (CurrentEditor != null)
            {
                if (string.Equals(targetName, "CHWR", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentEditor.SelectedChwrPipeSize = displayText;
                }
                else
                {
                    CurrentEditor.SelectedChwsPipeSize = displayText;
                }
            }
        }

        private static List<string> BuildDuctSizeOptions(ConnectivitySizeOptionsPayload payload, params string[] selectedValues)
        {
            List<Tuple<double, double>> sizes = new List<Tuple<double, double>>
            {
                Tuple.Create(800.0, 800.0),
                Tuple.Create(1000.0, 700.0)
            };

            ConnectivitySizeOptionsPayload normalized = ConnectivitySizeOptionsStorageService.Normalize(payload);
            foreach (RectangularDuctSizeDto size in normalized.DuctSizes)
            {
                AddDuctTuple(sizes, size.LengthMm, size.WidthMm);
            }

            foreach (string value in selectedValues ?? new string[0])
            {
                if (TryParseDuctSize(value, out double lengthMm, out double widthMm))
                {
                    AddDuctTuple(sizes, lengthMm, widthMm);
                }
            }

            List<string> result = new List<string> { "Select" };
            result.AddRange(sizes
                .OrderBy(x => x.Item1)
                .ThenBy(x => x.Item2)
                .Select(x => FormatDuctSizeDisplay(x.Item1, x.Item2)));
            return result;
        }

        private static List<string> BuildPipeSizeOptions(ConnectivitySizeOptionsPayload payload, params string[] selectedValues)
        {
            List<double> sizes = new List<double> { 65.0 };

            ConnectivitySizeOptionsPayload normalized = ConnectivitySizeOptionsStorageService.Normalize(payload);
            foreach (double size in normalized.PipeSizesMm)
            {
                AddPipeSize(sizes, size);
            }

            foreach (string value in selectedValues ?? new string[0])
            {
                if (TryParsePipeSize(value, out double diameterMm))
                {
                    AddPipeSize(sizes, diameterMm);
                }
            }

            List<string> result = new List<string> { "Select" };
            result.AddRange(sizes
                .OrderBy(x => x)
                .Select(FormatPipeSizeDisplay));
            return result;
        }

        private static void AddDuctTuple(List<Tuple<double, double>> sizes, double lengthMm, double widthMm)
        {
            if (lengthMm <= 0.0 || widthMm <= 0.0)
            {
                return;
            }

            if (!sizes.Any(x => AreNearlyEqual(x.Item1, lengthMm) && AreNearlyEqual(x.Item2, widthMm)))
            {
                sizes.Add(Tuple.Create(Math.Round(lengthMm, 3), Math.Round(widthMm, 3)));
            }
        }

        private static void AddPipeSize(List<double> sizes, double diameterMm)
        {
            if (diameterMm > 0.0 && !sizes.Any(x => AreNearlyEqual(x, diameterMm)))
            {
                sizes.Add(Math.Round(diameterMm, 3));
            }
        }

        private static void EnsureOptionPresent(ObservableCollection<string> options, string value)
        {
            if (options == null || string.IsNullOrWhiteSpace(value) || options.Contains(value))
            {
                return;
            }

            options.Add(value);
        }

        private static string NormalizeDuctSizeDisplay(string value)
        {
            return TryParseDuctSize(value, out double lengthMm, out double widthMm)
                ? FormatDuctSizeDisplay(lengthMm, widthMm)
                : string.IsNullOrWhiteSpace(value) ? "Select" : value;
        }

        private static string NormalizePipeSizeDisplay(string value)
        {
            return TryParsePipeSize(value, out double diameterMm)
                ? FormatPipeSizeDisplay(diameterMm)
                : string.IsNullOrWhiteSpace(value) ? "Select" : value;
        }

        private static bool TryParseDuctSize(string value, out double lengthMm, out double widthMm)
        {
            lengthMm = 0.0;
            widthMm = 0.0;
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "Select", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = value
                .ToLowerInvariant()
                .Replace("mm", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("×", "x")
                .Replace("Ã—", "x")
                .Replace("*", "x");

            string[] parts = normalized.Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            return TryParsePositive(parts[0], out lengthMm) &&
                TryParsePositive(parts[1], out widthMm);
        }

        private static bool TryParsePipeSize(string value, out double diameterMm)
        {
            diameterMm = 0.0;
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "Select", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant().Replace("mm", string.Empty).Trim();
            return TryParsePositive(normalized, out diameterMm);
        }

        private static bool TryParsePositive(string value, out double result)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
            {
                return false;
            }

            return !double.IsNaN(result) && !double.IsInfinity(result) && result > 0.0;
        }

        private static string FormatDuctSizeDisplay(double lengthMm, double widthMm)
        {
            return FormatMillimeterNumber(lengthMm) + " × " + FormatMillimeterNumber(widthMm) + " mm";
        }

        private static string FormatPipeSizeDisplay(double diameterMm)
        {
            return FormatMillimeterNumber(diameterMm) + " mm";
        }

        private static string FormatMillimeterNumber(double value)
        {
            double rounded = Math.Round(value, 3);
            if (AreNearlyEqual(rounded, Math.Round(rounded)))
            {
                return Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture);
            }

            return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool AreNearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) < 0.001;
        }

        private void ResetConnectivitySelections()
        {
            if (CurrentEditor != null)
            {
                CurrentEditor.ResetConnectivitySelectionsToDefault();
            }

            RefreshConnectivityState();
        }

        private void RefreshConnectivityState()
        {
            OnPropertyChanged(nameof(CanCreateDuctWork));
            OnPropertyChanged(nameof(CanCreatePipeWork));
            OnPropertyChanged(nameof(CanRemoveDuctWork));
            OnPropertyChanged(nameof(CanRemovePipeWork));
            OnPropertyChanged(nameof(DuctWorkActionButtonText));
            OnPropertyChanged(nameof(PipeWorkActionButtonText));
            OnPropertyChanged(nameof(IsDuctWorkGenerated));
            OnPropertyChanged(nameof(IsPipeWorkGenerated));
        }

        private static bool IsConcreteSelection(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.Equals(value, "Select", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConcreteWallSelection(EditorWallOptionViewModel value)
        {
            return value != null &&
                   !value.IsSelectOption &&
                   value.ElementId > 0;
        }

        private void OnEditorBoundaryWallSelected(EditorWallOptionViewModel wall)
        {
            if (!IsConcreteWallSelection(wall))
            {
                return;
            }

            _ = RoomRecognitionPaneRuntime.RequestSelectBoundaryWallAsync(wall.ElementId);
        }

        private async void CreateDuctWork()
        {
            if (!CanCreateDuctWork)
            {
                return;
            }

            EditorWallOptionViewModel sadWall = CurrentEditor != null ? CurrentEditor.SelectedSadWallOption : null;
            EditorWallOptionViewModel radWall = CurrentEditor != null ? CurrentEditor.SelectedRadWallOption : null;
            if (CurrentEditor == null ||
                string.IsNullOrWhiteSpace(CurrentEditor.RoomKey) ||
                !IsConcreteWallSelection(sadWall) ||
                !IsConcreteWallSelection(radWall))
            {
                return;
            }

            string roomKey = CurrentEditor.RoomKey;

            if (IsDuctWorkGenerated)
            {
                bool removed = await RoomRecognitionPaneRuntime.RequestRemoveDuctWorkAsync(roomKey);
                if (!removed)
                {
                    return;
                }

                IsDuctWorkGenerated = false;
            }

            bool created = await RoomRecognitionPaneRuntime.RequestCreateDuctWorkAsync(
                roomKey,
                CurrentEditor.SelectedSadSize,
                sadWall.ElementId,
                CurrentEditor.SelectedRadSize,
                radWall.ElementId);

            if (created)
            {
                IsDuctWorkGenerated = true;
            }
        }

        private async void RemoveDuctWork()
        {
            if (!CanRemoveDuctWork || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            bool removed = await RoomRecognitionPaneRuntime.RequestRemoveDuctWorkAsync(CurrentEditor.RoomKey);
            if (removed)
            {
                IsDuctWorkGenerated = false;
            }
        }

        private async void CreatePipeWork()
        {
            if (!CanCreatePipeWork)
            {
                return;
            }

            EditorWallOptionViewModel chwsWall = CurrentEditor != null ? CurrentEditor.SelectedChwsWallOption : null;
            EditorWallOptionViewModel chwrWall = CurrentEditor != null ? CurrentEditor.SelectedChwrWallOption : null;
            if (CurrentEditor == null ||
                string.IsNullOrWhiteSpace(CurrentEditor.RoomKey) ||
                !IsConcreteWallSelection(chwsWall) ||
                !IsConcreteWallSelection(chwrWall))
            {
                return;
            }

            string roomKey = CurrentEditor.RoomKey;

            if (IsPipeWorkGenerated)
            {
                bool removed = await RoomRecognitionPaneRuntime.RequestRemovePipeWorkAsync(roomKey);
                if (!removed)
                {
                    return;
                }

                IsPipeWorkGenerated = false;
            }

            bool created = await RoomRecognitionPaneRuntime.RequestCreatePipeWorkAsync(
                roomKey,
                CurrentEditor.SelectedChwsPipeSize,
                chwsWall.ElementId,
                CurrentEditor.SelectedChwrPipeSize,
                chwrWall.ElementId);

            if (created)
            {
                IsPipeWorkGenerated = true;
            }
        }

        private async void RemovePipeWork()
        {
            if (!CanRemovePipeWork || CurrentEditor == null || string.IsNullOrWhiteSpace(CurrentEditor.RoomKey))
            {
                return;
            }

            bool removed = await RoomRecognitionPaneRuntime.RequestRemovePipeWorkAsync(CurrentEditor.RoomKey);
            if (removed)
            {
                IsPipeWorkGenerated = false;
            }
        }

        private void PopulateAhuSubModules(int flowRateNumber)
        {
            AhuSubModules.Clear();

            foreach (AhuSubModuleRowViewModel row in BuildAhuSubModules(flowRateNumber))
            {
                AhuSubModules.Add(row);
            }

            AhuSubModuleCountText = "System Determined " + AhuSubModules.Count + " Sub-modules";
            RefreshDeliveryRouteModuleSummary();
        }

        private static IEnumerable<AhuSubModuleRowViewModel> BuildAhuSubModules(int flowRateNumber)
        {
            return AhuSubModuleScheduleService.Build(flowRateNumber)
                .Select(row => new AhuSubModuleRowViewModel
                {
                    SubModule = row.SubModule,
                    Type = row.Type,
                    DimensionsMm = row.DimensionsMm,
                    Seq = row.Sequence
                });
        }

        private static int ParseFlowRateNumber(string flowRateText)
        {
            if (string.IsNullOrWhiteSpace(flowRateText))
            {
                return 0;
            }

            string trimmed = flowRateText.Trim();
            int value = 0;
            bool hasDigit = false;
            foreach (char ch in trimmed)
            {
                if (char.IsDigit(ch))
                {
                    value = value * 10 + (ch - '0');
                    hasDigit = true;
                    continue;
                }

                if (hasDigit)
                {
                    break;
                }
            }

            return hasDigit ? value : 0;
        }

        private static string FormatFlowRateText(string flowRateText)
        {
            int flowRateNumber = ParseFlowRateNumber(flowRateText);
            return flowRateNumber > 0 ? flowRateNumber + " m³/s" : "-";
        }

        private void LoadDemoEquipmentOptions()
        {
            EquipmentOptions.Clear();
            RecommendedEquipmentOptions.Clear();
            OptionalEquipmentOptions.Clear();

            int selectedFlowRate = CurrentEditor == null ? 0 : ParseFlowRateNumber(CurrentEditor.SelectedFlowRate);
            if (selectedFlowRate <= 0)
            {
                OnPropertyChanged(nameof(HasOptionalEquipment));
                return;
            }

            List<CadToRevit.Services.Rooms.RoomCustomFamilyOption> recommendedOptions =
                GetRecommendedFamilyOptionsForFlowRate(selectedFlowRate).ToList();
            if (recommendedOptions.Count > 0)
            {
                foreach (var option in recommendedOptions)
                {
                    AddDemoEquipmentOption(CreateEquipmentOption(option, "-", false, false));
                }
            }
            else
            {
                foreach (string familyKey in GetRecommendedFamilyKeysForFlowRate(selectedFlowRate))
                {
                    AddDemoEquipmentOption(CreateEquipmentOption(familyKey, "-", false, false));
                }
            }

            OnPropertyChanged(nameof(HasOptionalEquipment));
        }

        private EquipmentSelectionCardViewModel CreateEquipmentOption(string familyKey, string sizeStatus, bool isOptional, bool isExceeded)
        {
            string resolvedFamilyKey = string.IsNullOrWhiteSpace(familyKey) ? string.Empty : familyKey.Trim();
            var catalogOption = CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService.GetOption(resolvedFamilyKey);
            string displayName = catalogOption != null && !string.IsNullOrWhiteSpace(catalogOption.DisplayName)
                ? catalogOption.DisplayName
                : ResolveFamilyDisplayNameByKey(resolvedFamilyKey);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                int flowRateNumber = ParseAhuFamilyKeyNumber(resolvedFamilyKey);
                displayName = flowRateNumber > 0 ? "AHU Flow Rate " + flowRateNumber + " m³/s" : resolvedFamilyKey;
            }

            EquipmentSelectionCardViewModel card = new EquipmentSelectionCardViewModel
            {
                FamilyKey = resolvedFamilyKey,
                DisplayName = displayName,
                SizeStatus = string.IsNullOrWhiteSpace(sizeStatus) ? "-" : sizeStatus,
                IsOptional = isOptional,
                IsExceeded = isExceeded
            };
            ApplyAhuParameters(card, catalogOption);
            return card;
        }

        private EquipmentSelectionCardViewModel CreateEquipmentOption(
            CadToRevit.Services.Rooms.RoomCustomFamilyOption option,
            string sizeStatus,
            bool isOptional,
            bool isExceeded)
        {
            if (option == null)
            {
                return null;
            }

            EquipmentSelectionCardViewModel card = new EquipmentSelectionCardViewModel
            {
                FamilyKey = option.Key ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(option.DisplayName) ? option.Key : option.DisplayName,
                SizeStatus = string.IsNullOrWhiteSpace(sizeStatus) ? "-" : sizeStatus,
                IsOptional = isOptional,
                IsExceeded = isExceeded
            };
            ApplyAhuParameters(card, option);
            return card;
        }

        private static void ApplyAhuParameters(
            EquipmentSelectionCardViewModel card,
            CadToRevit.Services.Rooms.RoomCustomFamilyOption option)
        {
            if (card == null || option == null)
            {
                return;
            }

            card.AirflowM3s = option.AirflowM3s;
            card.TotalLengthMm = option.TotalLengthMm;
            card.WidthMm = option.WidthMm;
            card.HeightMm = option.HeightMm;
            card.WeightKg = option.WeightKg;
            card.RequiredMaintenanceSpaceMm = option.RequiredMaintenanceSpaceMm;
            card.RequiredMaintenanceSpaceSide = option.RequiredMaintenanceSpaceSide;
            card.MbLengthMm = option.MbLengthMm;
            card.FilterLengthMm = option.FilterLengthMm;
            card.CoilLengthMm = option.CoilLengthMm;
            card.FanLengthMm = option.FanLengthMm;
            card.ValveChamberLengthMm = option.ValveChamberLengthMm;
            card.ValveChamberWidthMm = option.ValveChamberWidthMm;
            card.ElChamberLengthMm = option.ElChamberLengthMm;
            card.ElChamberWidthMm = option.ElChamberWidthMm;
            card.MaintenanceDoorSideMm = option.MaintenanceDoorSideMm;
            card.MaintenanceOtherSideMm = option.MaintenanceOtherSideMm;
            card.MaintenanceFrontBackMm = option.MaintenanceFrontBackMm;
        }

        private static IEnumerable<string> GetRecommendedFamilyKeysForFlowRate(int flowRateNumber)
        {
            if (flowRateNumber < 1 || flowRateNumber > 10)
            {
                return Enumerable.Empty<string>();
            }

            return Enumerable.Range(flowRateNumber, 11 - flowRateNumber)
                .Select(value => "ahu_" + value.ToString("000"));
        }

        private static IEnumerable<CadToRevit.Services.Rooms.RoomCustomFamilyOption> GetRecommendedFamilyOptionsForFlowRate(int flowRateNumber)
        {
            if (flowRateNumber <= 0)
            {
                return Enumerable.Empty<CadToRevit.Services.Rooms.RoomCustomFamilyOption>();
            }

            return CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService.GetOptions()
                .Where(x => x != null && x.AirflowM3s >= flowRateNumber)
                .OrderBy(x => x.AirflowM3s)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private void AddDemoEquipmentOption(EquipmentSelectionCardViewModel option)
        {
            if (option == null)
            {
                return;
            }

            option.SelectCommand = new DelegateCommand(_ => SelectEquipmentOption(option));
            EquipmentOptions.Add(option);
            if (option.IsOptional)
            {
                OptionalEquipmentOptions.Add(option);
            }
            else
            {
                RecommendedEquipmentOptions.Add(option);
            }
        }


        internal void ApplyEquipmentPlacementFitResult(string familyKey, string fitStatus, string warningMessage)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return;
            }

            foreach (EquipmentSelectionCardViewModel option in EquipmentOptions)
            {
                if (option == null ||
                    !string.Equals(option.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                option.ApplyPlacementFitResult(fitStatus, warningMessage);
            }
        }

        private void SelectEquipmentOption(EquipmentSelectionCardViewModel selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            // Only the currently selected AHU should keep placement-validation UI.
            // When the user selects another family, clear the previous card's
            // Valid / Oversized badge and warning reasons instead of leaving
            // several cards looking as if they are simultaneously inserted.
            foreach (EquipmentSelectionCardViewModel option in EquipmentOptions)
            {
                if (option == null)
                {
                    continue;
                }

                option.IsSelected = false;
                option.IsChecking = false;

                if (!ReferenceEquals(option, selectedOption))
                {
                    option.ClearValidationResult();
                }
            }

            // The new selection starts from a clean validation state.
            selectedOption.ClearValidationResult();
            selectedOption.IsSelected = true;
            SelectedEquipmentOption = selectedOption;

            // Clear any confirmed-card validation from the previously selected
            // family while the new family is being checked/inserted.
            HideAhuSubModuleConfiguration();

            if (string.IsNullOrWhiteSpace(selectedOption.FamilyKey))
            {
                selectedOption.FamilyKey = ResolveFamilyKeyFromEquipmentDisplayName(selectedOption.DisplayName);
            }

            if (CurrentEditor != null)
            {
                CurrentEditor.SelectedEquipmentDisplayName = selectedOption.DisplayName ?? string.Empty;
                CurrentEditor.SelectedEquipmentFamilyKey = selectedOption.FamilyKey ?? string.Empty;
            }

            TryInsertSelectedEquipmentFamily(selectedOption);
        }

        private void TryInsertSelectedEquipmentFamily(EquipmentSelectionCardViewModel selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            string familyKey = selectedOption.FamilyKey;
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                familyKey = ResolveFamilyKeyFromEquipmentDisplayName(selectedOption.DisplayName);
                selectedOption.FamilyKey = familyKey;
            }

            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return;
            }

            string targetRoomKey = CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey)
                ? CurrentEditor.RoomKey
                : SelectedRoomKey;
            if (string.IsNullOrWhiteSpace(targetRoomKey))
            {
                return;
            }

            InsertFamilyWithInlineStatus(familyKey, selectedOption.DisplayName ?? string.Empty, selectedOption);
        }

        private async void InsertFamilyWithInlineStatus(
            string familyKey,
            string displayName,
            EquipmentSelectionCardViewModel selectedOption)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return;
            }

            string targetRoomKey = CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey)
                ? CurrentEditor.RoomKey
                : SelectedRoomKey;
            if (string.IsNullOrWhiteSpace(targetRoomKey))
            {
                return;
            }

            HighlightedFamilyKey = familyKey;
            if (CurrentEditor != null)
            {
                CurrentEditor.SelectedEquipmentFamilyKey = familyKey;
                CurrentEditor.SelectedEquipmentDisplayName = displayName ?? string.Empty;
            }

            int statusVersion = ++_equipmentInsertStatusVersion;
            EquipmentInsertStatusText = "Checking equipment placement, please wait...";
            IsEquipmentInsertStatusVisible = true;

            try
            {
                if (selectedOption != null)
                {
                    selectedOption.IsChecking = true;
                    selectedOption.ClearValidationResult();
                }

                AhuPlacementValidationPreparationResult preparation =
                    await RoomRecognitionPaneRuntime.RequestPrepareAhuPlacementValidationAsync(targetRoomKey);

                if (statusVersion != _equipmentInsertStatusVersion)
                {
                    return;
                }

                if (preparation == null ||
                    !preparation.Success ||
                    string.IsNullOrWhiteSpace(preparation.SessionId))
                {
                    throw new InvalidOperationException(
                        preparation != null && !string.IsNullOrWhiteSpace(preparation.Message)
                            ? preparation.Message
                            : "Failed to prepare AHU room fit validation.");
                }

                AhuPlacementValidationResult validationResult =
                    await _ahuPlacementValidationService.ValidateAsync(
                        BuildAhuPlacementValidationRequest(familyKey, preparation));

                if (statusVersion != _equipmentInsertStatusVersion)
                {
                    return;
                }

                EquipmentPlacementValidationDto validationDto = ToValidationDto(validationResult);

                // Validation has completed at this point. End the "Checking..."
                // state BEFORE publishing the result so the warning reasons are
                // never visible underneath a still-visible Checking badge.
                if (selectedOption != null)
                {
                    selectedOption.IsChecking = false;
                    selectedOption.ApplyValidationDto(validationDto);
                }

                SetCurrentEquipmentValidation(validationDto);

                if (statusVersion == _equipmentInsertStatusVersion)
                {
                    EquipmentInsertStatusText = "Inserting equipment, please wait...";
                }

                // Capture the AHU that existed before this editor session modifies the room.
                // It is restored if the user later clicks Cancel.
                CaptureEditorEquipmentRollbackState(targetRoomKey);
                _editorPreviewRoomKeys.Add(targetRoomKey);

                // Clear previously inserted equipment and generated duct / pipe work before placing the new family.
                await RoomRecognitionPaneRuntime.RequestClearRoomEquipmentLayoutAsync(targetRoomKey);

                // Changing Target Room invalidates this in-flight insertion. Stop
                // before placing the old room's family after its preview was cleared.
                if (statusVersion != _equipmentInsertStatusVersion)
                {
                    return;
                }

                // Use exactly the same XY point that was sent to Python as
                // placement_point, so the API fit check and the Revit family
                // insertion are evaluating / using the same location.
                // The room-fit API returns an absolute IFC/Revit XY direction in
                // orientation_deg for the current door-based test scenario. Pass it
                // through to placement. The placement service intentionally bypasses
                // the legacy Service Side / RoomLong / RoomShort orientation logic
                // whenever this API angle is available.
                double insertXmm = validationResult != null &&
                    !double.IsNaN(validationResult.PlacementPointXmm) &&
                    !double.IsInfinity(validationResult.PlacementPointXmm)
                    ? validationResult.PlacementPointXmm
                    : preparation.PlacementXmm;
                double insertYmm = validationResult != null &&
                    !double.IsNaN(validationResult.PlacementPointYmm) &&
                    !double.IsInfinity(validationResult.PlacementPointYmm)
                    ? validationResult.PlacementPointYmm
                    : preparation.PlacementYmm;

                await RoomRecognitionPaneRuntime.RequestSetRoomCustomFamilyAsync(
                    targetRoomKey,
                    familyKey,
                    insertXmm,
                    insertYmm,
                    validationResult != null ? validationResult.OrientationDeg : null);

                if (statusVersion == _equipmentInsertStatusVersion)
                {
                    EquipmentInsertStatusText = "Equipment inserted successfully.";
                    await Task.Delay(1500);
                }

                if (statusVersion == _equipmentInsertStatusVersion)
                {
                    ClearEquipmentInsertStatus();
                }
            }
            catch (Exception ex)
            {
                if (statusVersion == _equipmentInsertStatusVersion)
                {
                    EquipmentInsertStatusText = "Equipment insertion failed. Please check Revit warnings and try again.";
                    IsEquipmentInsertStatusVisible = true;
                }

                MessageBox.Show(ex.Message, "CadToRevit - Room Detail", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (selectedOption != null)
                {
                    selectedOption.IsChecking = false;
                }
            }
        }

        private AhuPlacementValidationRequest BuildAhuPlacementValidationRequest(
            string familyKey,
            AhuPlacementValidationPreparationResult preparation)
        {
            double placementXmm = preparation != null ? preparation.PlacementXmm : 0;
            double placementYmm = preparation != null ? preparation.PlacementYmm : 0;
            List<AhuPlacementMaintenanceSpaceRequest> maintenanceSpaces =
                BuildAhuPlacementMaintenanceSpaces(familyKey);

            return new AhuPlacementValidationRequest
            {
                SessionId = preparation != null ? preparation.SessionId ?? string.Empty : string.Empty,
                FamilyId = ParseAhuFamilyKeyNumber(familyKey),
                FamilyKey = familyKey ?? string.Empty,
                RoomKey = CurrentEditor != null ? CurrentEditor.RoomKey ?? string.Empty : SelectedRoomKey ?? string.Empty,
                RoomLengthMm = CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorRoomLengthText) : 0,
                RoomWidthMm = CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorRoomWidthText) : 0,
                RoomHeightMm = CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorRoomHeightText) : 0,
                DoorWidthMm = preparation != null && preparation.DoorFound && preparation.DoorWidthMm > 0.0
                    ? preparation.DoorWidthMm
                    : CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorDoorWidthText) : 0,
                DoorHeightMm = preparation != null && preparation.DoorFound && preparation.DoorHeightMm > 0.0
                    ? preparation.DoorHeightMm
                    : CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorDoorHeightText) : 0,
                UsableAreaM2 = CurrentEditor != null ? ParseFirstNumber(CurrentEditor.EditorAvailableUsableAreaText) : 0,
                PointInRoomXmm = placementXmm,
                PointInRoomYmm = placementYmm,
                PlacementPointXmm = placementXmm,
                PlacementPointYmm = placementYmm,
                Orientation = null,
                EvaluationMode = "find_feasible_placement",
                UseMaintenanceSpace = false,
                EvaluateMaintenanceSpace = true,
                DoorFacingSide = RoomCustomFamilyCatalogService.GetDoorFacingSide(familyKey),
                DoorFacingSideOptions = new List<string> { "bottom", "top", "left", "right" },
                WallFacingSides = RoomCustomFamilyCatalogService.GetWallFacingSides(familyKey).ToList(),
                DoorDirection = preparation != null ? preparation.DoorDirection : null,
                DoorFound = preparation != null && preparation.DoorFound,
                DoorElementId = preparation != null ? preparation.DoorElementId : -1,
                DoorCenterXmm = preparation != null ? preparation.DoorCenterXmm : 0.0,
                DoorCenterYmm = preparation != null ? preparation.DoorCenterYmm : 0.0,
                DoorSource = preparation != null ? preparation.DoorSource : string.Empty,
                RestrictedAreas = preparation != null && preparation.RestrictedAreas != null
                    ? preparation.RestrictedAreas
                    : new List<RestrictedAreaRequestItem>(),
                MaintenanceSpaces = maintenanceSpaces,
                SubModules = BuildAhuPlacementSubModuleFootprints(familyKey)
            };
        }

        private static List<AhuPlacementMaintenanceSpaceRequest> BuildAhuPlacementMaintenanceSpaces(string familyKey)
        {
            IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> configured =
                RoomCustomFamilyCatalogService.GetMaintenanceSpaces(familyKey);

            List<AhuPlacementMaintenanceSpaceRequest> result =
                new List<AhuPlacementMaintenanceSpaceRequest>();
            if (configured == null || configured.Count == 0)
            {
                return result;
            }

            foreach (RoomCustomFamilyMaintenanceSpaceDto row in configured
                .Where(x => x != null)
                .OrderBy(x => x.Sequence))
            {
                string side = NormalizeAhuMaintenanceSideForApi(row.Side);
                if (string.IsNullOrWhiteSpace(side) || row.DimensionMm <= 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] maintenanceSpaceSkipped familyKey=" +
                        (familyKey ?? string.Empty) +
                        ", maintenance=" + (row.MaintenanceCode ?? string.Empty) +
                        ", side=" + (row.Side ?? string.Empty) +
                        ", dimensionMm=" + row.DimensionMm.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                AhuPlacementMaintenanceSpaceRequest item =
                    new AhuPlacementMaintenanceSpaceRequest
                    {
                        Maintenance = string.IsNullOrWhiteSpace(row.MaintenanceCode)
                            ? "M" + row.Sequence.ToString(CultureInfo.InvariantCulture)
                            : row.MaintenanceCode.Trim(),
                        Side = side,
                        DimensionMm = row.DimensionMm,
                        IsWallSide = row.IsWallSide,
                        IsDoorSide = row.IsDoorSide
                    };

                result.Add(item);
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] maintenanceSpace=" + item.Maintenance +
                    ", side=" + item.Side +
                    ", dimensionMm=" + item.DimensionMm.ToString("0.###", CultureInfo.InvariantCulture) +
                    ", isWallSide=" + item.IsWallSide +
                    ", isDoorSide=" + item.IsDoorSide);
            }

            return result;
        }

        private static string NormalizeAhuMaintenanceSideForApi(string side)
        {
            string value = (side ?? string.Empty).Trim();
            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase)) return "top";
            if (string.Equals(value, "Bottom", StringComparison.OrdinalIgnoreCase)) return "bottom";
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)) return "left";
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return "right";
            return string.Empty;
        }

        private static List<AhuPlacementSubModuleRequest> BuildAhuPlacementSubModuleFootprints(string familyKey)
        {
            // Prefer the backend workbook/catalog layout when available. This
            // keeps the colleague UI and persisted family catalog intact while
            // using the same six-module polygon coordinates as the current API.
            int modelId = ParseAhuFamilyKeyNumber(familyKey);
            IReadOnlyList<AhuEquipmentLayoutCatalogService.LayoutModule> apiLayout =
                AhuEquipmentLayoutCatalogService.TryGetLayout(modelId);
            if (apiLayout != null && apiLayout.Count > 0)
            {
                List<AhuPlacementSubModuleRequest> apiResult = apiLayout
                    .Where(x => x != null && x.Points != null && x.Points.Count >= 4)
                    .Select(x => new AhuPlacementSubModuleRequest
                    {
                        Module = x.Key,
                        Name = x.Name ?? string.Empty,
                        Points = x.Points
                            .Where(point => point != null && point.Length >= 2)
                            .Select(point => new AhuPlacementPoint2D(point[0], point[1]))
                            .ToList()
                    })
                    .Where(x => x.Points.Count >= 4)
                    .ToList();
                if (apiResult.Count > 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] subModules source=backend_catalog, familyKey=" +
                        (familyKey ?? string.Empty) + ", count=" + apiResult.Count);
                    return apiResult;
                }
            }

            IReadOnlyList<RoomCustomFamilySubModuleDto> configured =
                RoomCustomFamilyCatalogService.GetSubModules(familyKey);

            List<AhuPlacementSubModuleRequest> result =
                new List<AhuPlacementSubModuleRequest>();
            if (configured == null || configured.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] subModules familyKey=" + (familyKey ?? string.Empty) + ", count=0");
                return result;
            }

            List<RoomCustomFamilySubModuleDto> rows = configured
                .Where(x => x != null)
                .OrderBy(x => x.Sequence)
                .ToList();
            if (rows.Count == 0)
            {
                return result;
            }

            // Local coordinate system agreed with Python:
            //   S1 top-left = (0, 0)
            //   +X = right / Length, +Y = down / Width
            // The visible spacing between UI grid cells is only presentation;
            // adjacent modules are treated as touching with a 0 mm gap.
            double currentX = 0;
            double currentY = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                RoomCustomFamilySubModuleDto current = rows[i];
                if (current.LengthMm <= 0 || current.WidthMm <= 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] subModuleGeometrySkipped familyKey=" +
                        (familyKey ?? string.Empty) +
                        ", module=" + (current.ModuleCode ?? ("S" + current.Sequence)) +
                        ", reason=Length/Width must be greater than 0, lengthMm=" +
                        current.LengthMm.ToString(CultureInfo.InvariantCulture) +
                        ", widthMm=" + current.WidthMm.ToString(CultureInfo.InvariantCulture));
                    return new List<AhuPlacementSubModuleRequest>();
                }

                if (i > 0)
                {
                    RoomCustomFamilySubModuleDto previous = rows[i - 1];
                    int rowDelta = current.GridRow - previous.GridRow;
                    int columnDelta = current.GridColumn - previous.GridColumn;

                    if (rowDelta == 0 && columnDelta == 1)
                    {
                        // Current is immediately to the right of previous.
                        currentX += previous.LengthMm;
                    }
                    else if (rowDelta == 0 && columnDelta == -1)
                    {
                        // Current is immediately to the left of previous.
                        currentX -= current.LengthMm;
                    }
                    else if (rowDelta == 1 && columnDelta == 0)
                    {
                        // Current is immediately below previous.
                        currentY += previous.WidthMm;
                    }
                    else if (rowDelta == -1 && columnDelta == 0)
                    {
                        // Current is immediately above previous.
                        currentY -= current.WidthMm;
                    }
                    else
                    {
                        // The editor/validator should prevent this. Keep the legacy
                        // AHU placement working instead of sending ambiguous geometry.
                        DiagnosticRecorder.AppendDebug(
                            "[AhuRoomFitApi] subModuleLayoutInvalid familyKey=" +
                            (familyKey ?? string.Empty) +
                            ", module=" + (current.ModuleCode ?? ("S" + current.Sequence)) +
                            ", previous=" + (previous.ModuleCode ?? ("S" + previous.Sequence)) +
                            ", rowDelta=" + rowDelta +
                            ", columnDelta=" + columnDelta);
                        return new List<AhuPlacementSubModuleRequest>();
                    }
                }

                double length = current.LengthMm;
                double width = current.WidthMm;
                string moduleCode = string.IsNullOrWhiteSpace(current.ModuleCode)
                    ? "S" + current.Sequence.ToString(CultureInfo.InvariantCulture)
                    : current.ModuleCode.Trim();

                AhuPlacementSubModuleRequest footprint = new AhuPlacementSubModuleRequest
                {
                    Module = moduleCode,
                    Name = (current.Name ?? string.Empty).Trim(),
                    Points = new List<AhuPlacementPoint2D>
                    {
                        new AhuPlacementPoint2D(currentX, currentY),
                        new AhuPlacementPoint2D(currentX + length, currentY),
                        new AhuPlacementPoint2D(currentX + length, currentY + width),
                        new AhuPlacementPoint2D(currentX, currentY + width)
                    }
                };
                result.Add(footprint);

                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] subModule=" + moduleCode +
                    ", name=" + (footprint.Name ?? string.Empty) +
                    ", originMm=[" +
                    currentX.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    currentY.ToString("0.###", CultureInfo.InvariantCulture) +
                    "], pointsMm=" + FormatSubModulePointsForLog(footprint.Points));
            }

            DiagnosticRecorder.AppendDebug(
                "[AhuRoomFitApi] subModules familyKey=" + (familyKey ?? string.Empty) +
                ", count=" + result.Count.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        private static string FormatSubModulePointsForLog(IReadOnlyList<AhuPlacementPoint2D> points)
        {
            if (points == null || points.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(",", points.Select(p =>
                "[" +
                p.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                p.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                "]")) + "]";
        }

        private static double ParseFirstNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string normalized = text.Replace(",", string.Empty);
            int start = -1;
            int length = 0;

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (char.IsDigit(c) || c == '.' || (c == '-' && start < 0))
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    length++;
                    continue;
                }

                if (start >= 0)
                {
                    break;
                }
            }

            if (start < 0 || length <= 0)
            {
                return 0;
            }

            double value;
            return double.TryParse(
                normalized.Substring(start, length),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
                ? value
                : 0;
        }

        private void ClearEquipmentInsertStatus()
        {
            _equipmentInsertStatusVersion++;
            EquipmentInsertStatusText = string.Empty;
            IsEquipmentInsertStatusVisible = false;
        }

        private string ResolveFamilyDisplayNameByKey(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return string.Empty;
            }

            RoomCustomFamilyItemViewModel matched = FamilyOptions.FirstOrDefault(
                x => string.Equals(x.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
            if (matched != null && !string.IsNullOrWhiteSpace(matched.DisplayName))
            {
                return matched.DisplayName;
            }

            var catalogOption = CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService.GetOption(familyKey);
            if (catalogOption != null && !string.IsNullOrWhiteSpace(catalogOption.DisplayName))
            {
                return catalogOption.DisplayName;
            }

            return string.Empty;
        }

        private static int ParseAhuFamilyKeyNumber(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return 0;
            }

            string digits = new string(familyKey.Where(char.IsDigit).ToArray());
            int value;
            return int.TryParse(digits, out value) ? value : 0;
        }

        private string ResolveFamilyKeyFromEquipmentDisplayName(string displayName)
        {
            int flowRateNumber = ParseFlowRateNumber(displayName);
            return ResolveFamilyKeyByFlowRate(flowRateNumber);
        }

        private string ResolveFamilyKeyByFlowRate(int flowRateNumber)
        {
            if (flowRateNumber <= 0)
            {
                return string.Empty;
            }

            RoomCustomFamilyItemViewModel matched = FamilyOptions.FirstOrDefault(x => MatchesFlowRate(x, flowRateNumber));
            if (matched != null)
            {
                return matched.FamilyKey ?? string.Empty;
            }

            var catalogOption = CadToRevit.Services.Rooms.RoomCustomFamilyCatalogService
                .GetOptions()
                .FirstOrDefault(x => MatchesFlowRate(x, flowRateNumber) || MatchesFlowRate(
                    string.Join(" ", new[]
                    {
                        x.Key,
                        x.DisplayName,
                        x.FileName,
                        x.OriginalFileName,
                        x.StoredFileName,
                        x.Description
                    }),
                    flowRateNumber));

            return catalogOption != null ? catalogOption.Key ?? string.Empty : string.Empty;
        }

        private static bool MatchesFlowRate(RoomCustomFamilyItemViewModel item, int flowRateNumber)
        {
            if (item == null)
            {
                return false;
            }

            if (IsSameAirflow(item.AirflowM3s, flowRateNumber))
            {
                return true;
            }

            string searchableText = string.Join(" ", new[]
            {
                item.FamilyKey,
                item.DisplayName,
                item.FileName,
                item.FullPath,
                item.Description
            });

            return MatchesFlowRate(searchableText, flowRateNumber);
        }

        private static bool MatchesFlowRate(CadToRevit.Services.Rooms.RoomCustomFamilyOption option, int flowRateNumber)
        {
            return option != null && IsSameAirflow(option.AirflowM3s, flowRateNumber);
        }

        private static bool IsSameAirflow(double airflowM3s, int flowRateNumber)
        {
            return airflowM3s > 0 && Math.Abs(airflowM3s - flowRateNumber) < 0.0001;
        }

        private static bool MatchesFlowRate(string text, int flowRateNumber)
        {
            if (string.IsNullOrWhiteSpace(text) || flowRateNumber <= 0)
            {
                return false;
            }

            string normalized = text.ToLowerInvariant()
                .Replace("\u00B3", "3")
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ");

            bool hasAhuOrFlowContext = normalized.Contains("ahu") ||
                                       normalized.Contains("flow") ||
                                       normalized.Contains("m3");
            if (!hasAhuOrFlowContext)
            {
                return false;
            }

            int currentNumber = 0;
            bool hasNumber = false;
            foreach (char ch in normalized)
            {
                if (char.IsDigit(ch))
                {
                    currentNumber = (currentNumber * 10) + (ch - '0');
                    hasNumber = true;
                    continue;
                }

                if (hasNumber)
                {
                    if (currentNumber == flowRateNumber)
                    {
                        return true;
                    }

                    currentNumber = 0;
                    hasNumber = false;
                }
            }

            return hasNumber && currentNumber == flowRateNumber;
        }

        private async void ResetEquipmentSelectionAndClearModel()
        {
            string targetRoomKey = CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.RoomKey)
                ? CurrentEditor.RoomKey
                : SelectedRoomKey;

            ResetEquipmentSelectionToDefault();
            HighlightedFamilyKey = string.Empty;

            if (!string.IsNullOrWhiteSpace(targetRoomKey))
            {
                try
                {
                    await RoomRecognitionPaneRuntime.RequestClearRoomEquipmentLayoutAsync(targetRoomKey);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "CadToRevit - Room Detail", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ResetEquipmentSelectionToDefault()
        {
            IsEquipmentSelectionExpanded = true;
            IsSizeEvaluationCompleted = false;
            SelectedEquipmentOption = null;
            ClearEquipmentInsertStatus();
            EquipmentOptions.Clear();
            RecommendedEquipmentOptions.Clear();
            OptionalEquipmentOptions.Clear();
            AhuSubModules.Clear();
            RefreshDeliveryRouteModuleSummary();
            ConfirmedEquipmentName = string.Empty;
            ClearConfirmedEquipmentCardDetails();
            ClearCurrentEquipmentValidation();
            AhuSubModuleCountText = "System Determined 0 Sub-modules";
            IsAhuSubModuleConfigurationVisible = false;
            IsConnectivityUnlocked = false;
            IsDuctWorkGenerated = false;
            IsPipeWorkGenerated = false;
            ResetConnectivitySelections();
            OnPropertyChanged(nameof(HasOptionalEquipment));

            if (CurrentEditor != null)
            {
                CurrentEditor.SelectedEquipmentDisplayName = string.Empty;
                CurrentEditor.SelectedEquipmentFamilyKey = string.Empty;
            }
        }

        private void ResetDeliveryRouteState()
        {
            SelectedDeliveryStartLift = EditorLiftOptions.FirstOrDefault();
            DeliveryStartPointName = "Not selected";
            DeliveryStartPointStatus = "Waiting";
            SetDeliveryTargetFromCurrentRoom();
            DeliveryRouteHintText = string.Equals(DeliveryTargetStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                ? "Define the start point and generate the delivery route."
                : "Start point logged. Define the destination next.";
            IsDeliveryRouteExpanded = false;
            ClearDeliveryRouteResult();
        }

        private void ClearDeliveryRouteResult()
        {
            IsDeliveryRouteResultVisible = false;
            DeliveryRouteResultMessage = string.Empty;
            DeliveryRouteLengthText = string.Empty;
            ClearSavedDeliveryRoutePayload();
        }

        private void ClearSavedDeliveryRoutePayload()
        {
            _savedDeliveryRouteResponseBody = string.Empty;
            _savedDeliveryRouteLengthMeters = null;
            _savedDeliveryRouteStartLiftKey = string.Empty;
            _savedDeliveryRouteStartLiftName = string.Empty;
            _savedDeliveryRouteTargetRoomKey = string.Empty;
            _savedDeliveryRouteTargetRoomName = string.Empty;
        }

        private string ResolveDeliveryRouteMaxDimsText()
        {
            if (AhuSubModules == null || AhuSubModules.Count == 0)
            {
                return "-";
            }

            AhuSubModuleRowViewModel lastModule = AhuSubModules
                .OrderByDescending(x => ParseSubModuleSequence(x != null ? x.Seq : null))
                .FirstOrDefault();
            if (lastModule == null || string.IsNullOrWhiteSpace(lastModule.DimensionsMm))
            {
                return "-";
            }

            return FormatModuleDimensionsForRoute(lastModule.DimensionsMm);
        }

        private static int ParseSubModuleSequence(string seq)
        {
            if (string.IsNullOrWhiteSpace(seq))
            {
                return 0;
            }

            int value;
            return int.TryParse(seq.Trim(), out value) ? value : 0;
        }

        private static string FormatModuleDimensionsForRoute(string dimensionsMm)
        {
            if (string.IsNullOrWhiteSpace(dimensionsMm))
            {
                return "-";
            }

            string[] parts = dimensionsMm
                .Split(new[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (parts.Length >= 3)
            {
                return "L:" + parts[0] + " x W:" + parts[1] + " x H:" + parts[2];
            }

            return dimensionsMm.Trim();
        }

        private static string FormatRouteLength(double? meters)
        {
            if (!meters.HasValue || meters.Value <= 0)
            {
                return "-";
            }

            double value = meters.Value;
            if (Math.Abs(value - Math.Round(value)) < 0.005)
            {
                return Math.Round(value).ToString("0") + " m";
            }

            return value.ToString("0.##") + " m";
        }

        private SolutionEditorViewModel CreateDefaultEditor(string roomKey = null, string roomName = null)
        {
            SolutionEditorViewModel editor = new SolutionEditorViewModel
            {
                RoomKey = roomKey ?? string.Empty,
                RoomName = roomName ?? string.Empty,
                SolutionName = "Layout Plan " + (LayoutPlans.Count + 1),
                PlanningContext = "New Building Design",
                EquipmentType = "AHU",
                SelectedFlowRate = "Select flow rate",
                SelectedEquipmentDisplayName = string.Empty,
                SelectedEquipmentFamilyKey = string.Empty,
                SelectedSupplyAirDuct = "700x450 mm",
                SelectedReturnAirDuct = "700x450 mm",
                SelectedChwSupply = "32 mm",
                SelectedChwReturn = "32 mm",
                SelectedDuctWall = "No wall selected",
                SelectedPipeWall = "No wall selected",
                SelectedSadSize = "Select",
                SelectedRadSize = "Select",
                SelectedChwsPipeSize = "Select",
                SelectedChwrPipeSize = "Select"
            };

            EditorRoomOptionViewModel matchedOption = EditorRoomOptions.FirstOrDefault(x => string.Equals(x.Key, editor.RoomKey, StringComparison.OrdinalIgnoreCase));
            if (matchedOption != null)
            {
                editor.SelectedRoomOption = matchedOption;
            }

            editor.SelectedFlowRateChanged = ResetEquipmentSelectionAndClearModel;
            editor.ConnectivitySelectionChanged = RefreshConnectivityState;
            editor.BoundaryWallSelectionChanged = OnEditorBoundaryWallSelected;
            editor.RefreshWallOptionsFromSelectedRoom();

            return editor;
        }

        internal void SetEditorRoomOptions(IEnumerable<EditorRoomOptionViewModel> options)
        {
            string selectedKey = CurrentEditor != null && CurrentEditor.SelectedRoomOption != null
                ? CurrentEditor.SelectedRoomOption.Key
                : CurrentEditor != null
                    ? CurrentEditor.RoomKey
                    : string.Empty;

            EditorRoomOptions.Clear();
            EditorRoomOptions.Add(new EditorRoomOptionViewModel
            {
                Key = string.Empty,
                DisplayName = "Select",
                RoomName = "Select",
                TargetType = string.Empty,
                AreaText = string.Empty,
                LevelText = string.Empty,
                StatusText = string.Empty
            });
            foreach (EditorRoomOptionViewModel option in options ?? Enumerable.Empty<EditorRoomOptionViewModel>())
            {
                EditorRoomOptions.Add(option);
            }

            if (CurrentEditor == null)
            {
                return;
            }

            CurrentEditor.SelectedRoomOption = EditorRoomOptions.FirstOrDefault(x =>
                string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            SetDeliveryTargetFromCurrentRoom();
        }

        internal void SetLayoutPlans(
            IEnumerable<RoomLayoutPlanDto> plans,
            IDictionary<string, string> activeLayoutIdByRoomKey = null)
        {
            LayoutPlans.Clear();

            foreach (RoomLayoutPlanDto plan in plans ?? Enumerable.Empty<RoomLayoutPlanDto>())
            {
                LayoutPlans.Add(ToCard(plan, activeLayoutIdByRoomKey));
            }

            HashSet<string> existingIds = new HashSet<string>(
                LayoutPlans.Select(x => x.LayoutId).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            _selectedCompareLayoutIds.RemoveAll(x => !existingIds.Contains(x));
            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshLayoutPlanCompareState();
        }

        internal void SetEditorLiftOptions(IEnumerable<LiftRecognitionRecord> lifts)
        {
            EditorLiftOptions.Clear();
            EditorLiftOptions.Add(new EditorLiftOptionViewModel
            {
                Key = string.Empty,
                DisplayName = "Select",
                LiftKind = string.Empty
            });
            foreach (LiftRecognitionRecord lift in lifts ?? Enumerable.Empty<LiftRecognitionRecord>())
            {
                if (lift == null || string.IsNullOrWhiteSpace(lift.Key))
                {
                    continue;
                }

                EditorLiftOptions.Add(new EditorLiftOptionViewModel
                {
                    Key = lift.Key,
                    DisplayName = string.IsNullOrWhiteSpace(lift.LiftName) ? "-" : lift.LiftName,
                    LiftKind = lift.LiftKind ?? string.Empty
                });
            }
        }

        internal void SetEditorLiftOptionItems(IEnumerable<EditorLiftOptionViewModel> lifts)
        {
            EditorLiftOptions.Clear();
            EditorLiftOptions.Add(new EditorLiftOptionViewModel
            {
                Key = string.Empty,
                DisplayName = "Select",
                LiftKind = string.Empty
            });
            foreach (EditorLiftOptionViewModel lift in lifts ?? Enumerable.Empty<EditorLiftOptionViewModel>())
            {
                if (lift == null || string.IsNullOrWhiteSpace(lift.Key))
                {
                    continue;
                }

                EditorLiftOptions.Add(new EditorLiftOptionViewModel
                {
                    Key = lift.Key,
                    DisplayName = string.IsNullOrWhiteSpace(lift.DisplayName) ? "-" : lift.DisplayName,
                    LiftKind = lift.LiftKind ?? string.Empty
                });
            }
        }

        internal void LoadLayoutPlanIntoEditor(
            RoomLayoutPlanDto plan,
            bool activateModel,
            string originalSubmittedFamilyKey = null)
        {
            if (plan == null)
            {
                return;
            }

            // Detail mode materializes the saved AHU / ductwork / pipework as a temporary
            // preview in the RVT model. Register the room as touched immediately so Cancel
            // clears every preview element even when the user does not select another AHU.
            // The AHU that was visible before Detail opened is restored afterwards.
            if (activateModel)
            {
                PrepareEditorRollbackState(
                    plan.RoomKey,
                    originalSubmittedFamilyKey);
            }
            else
            {
                ResetEditorEquipmentRollbackState();
            }

            CurrentEditorLayoutId = plan.LayoutId ?? string.Empty;

            SolutionEditorViewModel editor = CreateDefaultEditor(plan.RoomKey, plan.RoomName);
            editor.SolutionName = plan.SolutionName ?? string.Empty;
            editor.PlanningContext = plan.PlanningContext ?? string.Empty;
            editor.EquipmentType = string.IsNullOrWhiteSpace(plan.EquipmentType) ? "AHU" : plan.EquipmentType;

            EditorRoomOptionViewModel roomOption = EditorRoomOptions.FirstOrDefault(x =>
                string.Equals(x.Key, plan.RoomKey, StringComparison.OrdinalIgnoreCase));

            if (roomOption != null)
            {
                editor.SelectedRoomOption = roomOption;
            }
            else
            {
                editor.RoomKey = plan.RoomKey ?? string.Empty;
                editor.RoomName = plan.RoomName ?? string.Empty;
                editor.EditorRoomName = plan.RoomName ?? string.Empty;
                editor.EditorAreaText = plan.AreaText ?? string.Empty;
                editor.EditorLevelText = plan.LevelText ?? string.Empty;
                editor.EditorStatusText = plan.RoomStatus ?? string.Empty;
            }

            editor.SelectedFlowRate = string.IsNullOrWhiteSpace(plan.FlowRate)
                ? "Select flow rate"
                : plan.FlowRate;

            editor.SelectedEquipmentFamilyKey = plan.EquipmentFamilyKey ?? string.Empty;
            editor.SelectedEquipmentDisplayName = plan.EquipmentDisplayName ?? string.Empty;

            editor.SelectedSadSize = NormalizeDuctSizeDisplay(plan.SadSize);
            editor.SelectedRadSize = NormalizeDuctSizeDisplay(plan.RadSize);
            editor.SelectedChwsPipeSize = NormalizePipeSizeDisplay(plan.ChwsPipeSize);
            editor.SelectedChwrPipeSize = NormalizePipeSizeDisplay(plan.ChwrPipeSize);

            SelectWallOption(editor, plan.SadWall, value => editor.SelectedSadWallOption = value);
            SelectWallOption(editor, plan.RadWall, value => editor.SelectedRadWallOption = value);
            SelectWallOption(editor, plan.ChwsWall, value => editor.SelectedChwsWallOption = value);
            SelectWallOption(editor, plan.ChwrWall, value => editor.SelectedChwrWallOption = value);

            CurrentEditor = editor;
            CurrentPageMode = RoomDetailPageMode.SolutionEditor;
            ResetDeliveryRouteState();
            RestoreDeliveryRouteState(plan);

            IsSizeEvaluationCompleted = plan.SizeEvaluationCompleted || !string.IsNullOrWhiteSpace(plan.EquipmentFamilyKey);
            IsConnectivityUnlocked = plan.EquipmentConfirmed ||
                !string.IsNullOrWhiteSpace(plan.SadSize) ||
                !string.IsNullOrWhiteSpace(plan.ChwsPipeSize);

            HighlightedFamilyKey = plan.EquipmentFamilyKey ?? string.Empty;
            ConfirmedEquipmentName = plan.EquipmentDisplayName ?? string.Empty;
            ClearConfirmedEquipmentCardDetails();
            SetCurrentEquipmentValidation(plan.EquipmentValidation);
            if (plan.EquipmentConfirmed || !string.IsNullOrWhiteSpace(plan.EquipmentDisplayName))
            {
                int savedFlowRateNumber = ParseFlowRateNumber(editor.SelectedFlowRate);
                ApplyConfirmedEquipmentCardDetailsFromFamilyKey(plan.EquipmentFamilyKey, savedFlowRateNumber);
                PopulateAhuSubModules(savedFlowRateNumber);
                IsAhuSubModuleConfigurationVisible = plan.EquipmentConfirmed || AhuSubModules.Count > 0;
            }

            if (IsSizeEvaluationCompleted)
            {
                LoadDemoEquipmentOptions();
                ApplySavedValidationToEquipmentOption(plan.EquipmentFamilyKey, plan.EquipmentValidation);
            }

            IsDuctWorkGenerated = HasSavedPlanDuctWork(plan);
            IsPipeWorkGenerated = HasSavedPlanPipeWork(plan);
            RefreshConnectivityState();
        }

        private void RestoreDeliveryRouteState(RoomLayoutPlanDto plan)
        {
            LayoutDeliveryRouteDto route = plan != null ? plan.DeliveryRoute : null;
            if (route == null || !route.HasRoute || string.IsNullOrWhiteSpace(route.ResponseBody))
            {
                return;
            }

            EditorLiftOptionViewModel liftOption = EditorLiftOptions.FirstOrDefault(x =>
                string.Equals(x.Key, route.StartLiftKey, StringComparison.OrdinalIgnoreCase));
            if (liftOption != null)
            {
                _selectedDeliveryStartLift = liftOption;
                OnPropertyChanged(nameof(SelectedDeliveryStartLift));
            }

            EditorRoomOptionViewModel roomOption = EditorRoomOptions.FirstOrDefault(x =>
                string.Equals(x.Key, route.TargetRoomKey, StringComparison.OrdinalIgnoreCase));
            if (roomOption != null)
            {
                _selectedDeliveryTargetRoom = roomOption;
                OnPropertyChanged(nameof(SelectedDeliveryTargetRoom));
            }

            _savedDeliveryRouteResponseBody = route.ResponseBody ?? string.Empty;
            _savedDeliveryRouteLengthMeters = route.PathLengthMeters > 0 ? (double?)route.PathLengthMeters : null;
            _savedDeliveryRouteStartLiftKey = route.StartLiftKey ?? string.Empty;
            _savedDeliveryRouteStartLiftName = route.StartLiftName ?? string.Empty;
            _savedDeliveryRouteTargetRoomKey = route.TargetRoomKey ?? string.Empty;
            _savedDeliveryRouteTargetRoomName = route.TargetRoomName ?? string.Empty;

            string startName = !string.IsNullOrWhiteSpace(route.StartLiftName) ? route.StartLiftName : "Service Lift";
            string targetName = !string.IsNullOrWhiteSpace(route.TargetRoomName) ? route.TargetRoomName : plan.RoomName ?? "AHU ROOM";

            DeliveryStartPointName = startName;
            DeliveryStartPointStatus = "Ready";
            DeliveryTargetName = targetName;
            DeliveryTargetStatus = "Ready";
            DeliveryRouteLengthText = !string.IsNullOrWhiteSpace(route.RouteLengthText)
                ? route.RouteLengthText
                : FormatRouteLength(route.PathLengthMeters > 0 ? (double?)route.PathLengthMeters : null);
            DeliveryRouteResultMessage = !string.IsNullOrWhiteSpace(route.ResultMessage)
                ? route.ResultMessage
                : "AI logistics planning completed successfully from " + startName + " to " + targetName + ".";
            DeliveryRouteHintText = "Delivery route generated. Length: " + DeliveryRouteLengthText;
            IsDeliveryRouteResultVisible = true;
            IsDeliveryRouteExpanded = true;
        }

        private RoomLayoutPlanDto BuildCurrentLayoutPlanDto()
        {
            if (CurrentEditor == null)
            {
                return null;
            }

            return new RoomLayoutPlanDto
            {
                LayoutId = CurrentEditorLayoutId,
                SolutionName = string.IsNullOrWhiteSpace(CurrentEditor.SolutionName)
                    ? "Layout Plan"
                    : CurrentEditor.SolutionName.Trim(),
                RoomKey = CurrentEditor.RoomKey ?? string.Empty,
                RoomName = CurrentEditor.EditorRoomName ?? string.Empty,
                RoomType = CurrentEditor.SelectedRoomOption != null
                    ? CurrentEditor.SelectedRoomOption.TargetType ?? string.Empty
                    : TargetRoomType ?? string.Empty,
                AreaText = CurrentEditor.EditorAreaText ?? string.Empty,
                LevelText = CurrentEditor.EditorLevelText ?? string.Empty,
                RoomStatus = CurrentEditor.EditorStatusText ?? string.Empty,
                PlanningContext = CurrentEditor.PlanningContextBadgeText ?? string.Empty,
                EquipmentType = CurrentEditor.EquipmentType ?? string.Empty,
                FlowRate = CurrentEditor.SelectedFlowRate ?? string.Empty,
                EquipmentFamilyKey = CurrentEditor.SelectedEquipmentFamilyKey ?? string.Empty,
                EquipmentDisplayName = ResolveEquipmentDisplayNameForSave(),
                SizeEvaluationCompleted = IsSizeEvaluationCompleted,
                EquipmentConfirmed = IsConnectivityUnlocked,
                SadSize = CurrentEditor.SelectedSadSize ?? string.Empty,
                SadWall = ToWallDto(CurrentEditor.SelectedSadWallOption),
                RadSize = CurrentEditor.SelectedRadSize ?? string.Empty,
                RadWall = ToWallDto(CurrentEditor.SelectedRadWallOption),
                ChwsPipeSize = CurrentEditor.SelectedChwsPipeSize ?? string.Empty,
                ChwsWall = ToWallDto(CurrentEditor.SelectedChwsWallOption),
                ChwrPipeSize = CurrentEditor.SelectedChwrPipeSize ?? string.Empty,
                ChwrWall = ToWallDto(CurrentEditor.SelectedChwrWallOption),
                SizeStatus = "Configured",
                FitnessText = "Configured",
                RouteLengthText = IsDeliveryRouteResultVisible && !string.IsNullOrWhiteSpace(DeliveryRouteLengthText)
                    ? DeliveryRouteLengthText
                    : "Not routed",
                EquipmentValidation = CloneValidationDto(_currentEquipmentValidation),
                RoomLengthMm = ParseMm(CurrentEditor.EditorRoomLengthText),
                RoomWidthMm = ParseMm(CurrentEditor.EditorRoomWidthText),
                RoomHeightMm = ParseMm(CurrentEditor.EditorRoomHeightText),
                DoorWidthMm = ParseMm(CurrentEditor.EditorDoorWidthText),
                DoorHeightMm = ParseMm(CurrentEditor.EditorDoorHeightText),
                EquipmentLengthMm = SelectedEquipmentOption != null ? SelectedEquipmentOption.TotalLengthMm : 0,
                EquipmentWidthMm = SelectedEquipmentOption != null ? SelectedEquipmentOption.WidthMm : 0,
                EquipmentHeightMm = SelectedEquipmentOption != null ? SelectedEquipmentOption.HeightMm : 0,
                EquipmentWeightKg = SelectedEquipmentOption != null ? SelectedEquipmentOption.WeightKg : 0,
                RequiredMaintenanceSpaceMm = SelectedEquipmentOption != null ? SelectedEquipmentOption.RequiredMaintenanceSpaceMm : 0,
                RequiredMaintenanceSpaceSide = SelectedEquipmentOption != null ? SelectedEquipmentOption.RequiredMaintenanceSpaceSide ?? string.Empty : string.Empty,
                DeliveryRoute = BuildCurrentDeliveryRouteDto()
            };
        }

        private static double ParseMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            string digits = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            double value;
            return double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
                ? value
                : 0.0;
        }

        private void ApplySavedValidationToEquipmentOption(
            string familyKey,
            EquipmentPlacementValidationDto validation)
        {
            if (string.IsNullOrWhiteSpace(familyKey) || validation == null || !validation.HasResult)
            {
                return;
            }

            foreach (EquipmentSelectionCardViewModel option in EquipmentOptions)
            {
                if (option == null ||
                    !string.Equals(option.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                option.ApplyValidationDto(validation);
                option.IsSelected = true;
                SelectedEquipmentOption = option;
                return;
            }
        }

        private LayoutDeliveryRouteDto BuildCurrentDeliveryRouteDto()
        {
            if (!IsDeliveryRouteResultVisible || string.IsNullOrWhiteSpace(_savedDeliveryRouteResponseBody))
            {
                return new LayoutDeliveryRouteDto();
            }

            string startName = !string.IsNullOrWhiteSpace(_savedDeliveryRouteStartLiftName)
                ? _savedDeliveryRouteStartLiftName
                : DeliveryStartPointName;
            string targetName = !string.IsNullOrWhiteSpace(_savedDeliveryRouteTargetRoomName)
                ? _savedDeliveryRouteTargetRoomName
                : DeliveryTargetName;

            return new LayoutDeliveryRouteDto
            {
                HasRoute = true,
                StartLiftKey = _savedDeliveryRouteStartLiftKey ?? string.Empty,
                StartLiftName = startName ?? string.Empty,
                TargetRoomKey = _savedDeliveryRouteTargetRoomKey ?? string.Empty,
                TargetRoomName = targetName ?? string.Empty,
                ResponseBody = _savedDeliveryRouteResponseBody ?? string.Empty,
                PathLengthMeters = _savedDeliveryRouteLengthMeters.HasValue ? _savedDeliveryRouteLengthMeters.Value : 0.0,
                RouteLengthText = string.IsNullOrWhiteSpace(DeliveryRouteLengthText) ? "-" : DeliveryRouteLengthText,
                ResultMessage = DeliveryRouteResultMessage ?? string.Empty,
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private LayoutPlanCardViewModel ToCard(
            RoomLayoutPlanDto plan,
            IDictionary<string, string> activeLayoutIdByRoomKey)
        {
            bool isActiveLayout =
                plan != null &&
                !string.IsNullOrWhiteSpace(plan.RoomKey) &&
                activeLayoutIdByRoomKey != null &&
                activeLayoutIdByRoomKey.TryGetValue(plan.RoomKey, out string activeLayoutId) &&
                string.Equals(activeLayoutId, plan.LayoutId, StringComparison.OrdinalIgnoreCase);

            return new LayoutPlanCardViewModel
            {
                LayoutId = plan != null ? plan.LayoutId : string.Empty,
                RoomKey = plan != null ? plan.RoomKey : string.Empty,
                PlanName = plan == null || string.IsNullOrWhiteSpace(plan.SolutionName) ? "Layout Plan" : plan.SolutionName,
                PlantRoom = plan == null || string.IsNullOrWhiteSpace(plan.RoomName) ? "-" : plan.RoomName,
                RoomType = plan == null || string.IsNullOrWhiteSpace(plan.RoomType) ? (plan != null ? plan.EquipmentType : string.Empty) : plan.RoomType,
                AreaText = plan == null || string.IsNullOrWhiteSpace(plan.AreaText) ? "-" : plan.AreaText,
                ModelName = plan == null || string.IsNullOrWhiteSpace(plan.EquipmentDisplayName) ? "-" : plan.EquipmentDisplayName,
                LayoutType = ResolvePlanningContextTagText(plan),
                EquipmentTypeTagText = ResolveEquipmentTypeTagText(plan),
                WallText = FormatWallText(plan),
                SizeStatus = plan == null || string.IsNullOrWhiteSpace(plan.SizeStatus) ? "Configured" : plan.SizeStatus,
                FitnessText = plan != null ? (plan.FitnessText ?? string.Empty) : string.Empty,
                RouteLengthText = ResolveSavedPlanRouteLengthText(plan),
                StartPointText = ResolveSavedPlanStartPointText(plan),
                PipingStatus = ResolveSavedPlanPipingStatus(plan),
                PipingStatusForeground = ResolveSavedPlanPipingStatusForeground(plan),
                HasEquipmentValidationResult = plan != null &&
                    plan.EquipmentValidation != null &&
                    plan.EquipmentValidation.HasResult,
                EquipmentValidationStatusText = plan != null &&
                    plan.EquipmentValidation != null &&
                    plan.EquipmentValidation.HasResult
                        ? plan.EquipmentValidation.Status ?? string.Empty
                        : string.Empty,
                EquipmentValidationBadgeBackground = ResolveValidationBadgeBackground(
                    plan != null ? plan.EquipmentValidation : null),
                ModulesText = ResolveSavedPlanModulesText(plan),
                MaxDimsText = ResolveSavedPlanMaxDimsText(plan),
                CreatedAtText = ResolveCreatedAtText(plan),
                HasDeliveryRoute = plan != null && plan.DeliveryRoute != null && plan.DeliveryRoute.HasRoute && !string.IsNullOrWhiteSpace(plan.DeliveryRoute.ResponseBody),
                IsActiveLayout = isActiveLayout,
                DeleteCommand = DeleteLayoutPlanCommand,
                ExportCommand = ExportLayoutPlanCommand,
                DetailCommand = DetailLayoutPlanCommand,
                CompareCommand = new DelegateCommand(parameter => ToggleLayoutPlanCompare(parameter as LayoutPlanCardViewModel))
            };
        }

        private static string ResolvePlanningContextTagText(RoomLayoutPlanDto plan)
        {
            string value = plan != null ? (plan.PlanningContext ?? string.Empty).Trim() : string.Empty;
            if (value.IndexOf("RMAA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Replacement", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "RMAA/Replacement";
            }

            return "New Building Design";
        }

        private static string ResolveEquipmentTypeTagText(RoomLayoutPlanDto plan)
        {
            string value = plan != null ? (plan.EquipmentType ?? string.Empty).Trim() : string.Empty;
            if (string.Equals(value, "PAU", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOf("Primary Air Handling Unit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PAU";
            }

            return "AHU";
        }

        private static Brush ResolveValidationBadgeBackground(EquipmentPlacementValidationDto validation)
        {
            if (validation == null || !validation.HasResult)
            {
                return Brushes.Transparent;
            }

            return validation.IsValid
                ? new SolidColorBrush(Color.FromRgb(27, 124, 73))
                : new SolidColorBrush(Color.FromRgb(180, 35, 24));
        }

        private static string ResolveCreatedAtText(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return string.Empty;
            }

            string raw = !string.IsNullOrWhiteSpace(plan.CreatedAt)
                ? plan.CreatedAt
                : plan.UpdatedAt;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            DateTime parsed;
            if (DateTime.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out parsed) ||
                DateTime.TryParse(raw, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return raw;
        }

        private static string FormatWallText(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return "-";
            }

            return "SAD " + FormatWallName(plan.SadWall) +
                   " / RAD " + FormatWallName(plan.RadWall) +
                   " / CHWS " + FormatWallName(plan.ChwsWall) +
                   " / CHWR " + FormatWallName(plan.ChwrWall);
        }

        private static string FormatWallName(LayoutWallSelectionDto wall)
        {
            return wall != null && !string.IsNullOrWhiteSpace(wall.DisplayName)
                ? wall.DisplayName
                : "-";
        }

        private static string ResolveSavedPlanPipingStatus(RoomLayoutPlanDto plan)
        {
            return IsSavedPlanPipingGenerated(plan) ? "Generated" : "Not Configured";
        }

        private static Brush ResolveSavedPlanPipingStatusForeground(RoomLayoutPlanDto plan)
        {
            return IsSavedPlanPipingGenerated(plan)
                ? new SolidColorBrush(Color.FromRgb(22, 103, 183))
                : new SolidColorBrush(Color.FromRgb(210, 45, 45));
        }

        private static bool IsSavedPlanPipingGenerated(RoomLayoutPlanDto plan)
        {
            return HasSavedPlanDuctWork(plan) && HasSavedPlanPipeWork(plan);
        }

        private static bool HasSavedPlanDuctWork(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return false;
            }

            if (plan.ActiveGeneratedElements != null &&
                plan.ActiveGeneratedElements.DuctElements != null &&
                plan.ActiveGeneratedElements.DuctElements.Count > 0)
            {
                return true;
            }

            return IsConcreteSelection(plan.SadSize) &&
                   IsConcreteSelection(plan.RadSize) &&
                   IsConcreteWallSelection(plan.SadWall) &&
                   IsConcreteWallSelection(plan.RadWall);
        }

        private static bool HasSavedPlanPipeWork(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return false;
            }

            if (plan.ActiveGeneratedElements != null &&
                plan.ActiveGeneratedElements.PipeElements != null &&
                plan.ActiveGeneratedElements.PipeElements.Count > 0)
            {
                return true;
            }

            return IsConcreteSelection(plan.ChwsPipeSize) &&
                   IsConcreteSelection(plan.ChwrPipeSize) &&
                   IsConcreteWallSelection(plan.ChwsWall) &&
                   IsConcreteWallSelection(plan.ChwrWall);
        }

        private static bool IsConcreteWallSelection(LayoutWallSelectionDto wall)
        {
            return wall != null &&
                   wall.ElementId > 0 &&
                   !string.IsNullOrWhiteSpace(wall.DisplayName) &&
                   !string.Equals(wall.DisplayName, "Select", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(wall.DisplayName, "-", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSavedPlanModulesText(RoomLayoutPlanDto plan)
        {
            List<AhuSubModuleRowViewModel> modules = BuildSavedPlanAhuSubModules(plan);
            return modules.Count <= 0 ? "-" : modules.Count + " pcs";
        }

        private static string ResolveSavedPlanMaxDimsText(RoomLayoutPlanDto plan)
        {
            AhuSubModuleRowViewModel lastModule = BuildSavedPlanAhuSubModules(plan)
                .OrderByDescending(x => ParseSubModuleSequence(x != null ? x.Seq : null))
                .FirstOrDefault();

            return lastModule == null || string.IsNullOrWhiteSpace(lastModule.DimensionsMm)
                ? "-"
                : FormatModuleDimensionsForRoute(lastModule.DimensionsMm);
        }

        private static List<AhuSubModuleRowViewModel> BuildSavedPlanAhuSubModules(RoomLayoutPlanDto plan)
        {
            int flowRateNumber = ResolveSavedPlanFlowRateNumber(plan);
            if (flowRateNumber <= 0)
            {
                return new List<AhuSubModuleRowViewModel>();
            }

            return BuildAhuSubModules(flowRateNumber).ToList();
        }

        private static int ResolveSavedPlanFlowRateNumber(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return 0;
            }

            int number = ParseFlowRateNumber(plan.FlowRate);
            if (number > 0)
            {
                return number;
            }

            number = ParseFlowRateNumber(plan.EquipmentDisplayName);
            if (number > 0)
            {
                return number;
            }

            return ParseFlowRateNumber(plan.EquipmentFamilyKey);
        }

        private static string ResolveSavedPlanRouteLengthText(RoomLayoutPlanDto plan)
        {
            if (plan == null)
            {
                return "-";
            }

            if (plan.DeliveryRoute != null && plan.DeliveryRoute.HasRoute)
            {
                if (!string.IsNullOrWhiteSpace(plan.DeliveryRoute.RouteLengthText))
                {
                    return plan.DeliveryRoute.RouteLengthText;
                }

                return FormatRouteLength(plan.DeliveryRoute.PathLengthMeters > 0
                    ? (double?)plan.DeliveryRoute.PathLengthMeters
                    : null);
            }

            if (!string.IsNullOrWhiteSpace(plan.RouteLengthText) &&
                !string.Equals(plan.RouteLengthText, "Not routed", StringComparison.OrdinalIgnoreCase))
            {
                return plan.RouteLengthText;
            }

            return "-";
        }

        private static string ResolveSavedPlanStartPointText(RoomLayoutPlanDto plan)
        {
            return plan != null &&
                   plan.DeliveryRoute != null &&
                   plan.DeliveryRoute.HasRoute &&
                   !string.IsNullOrWhiteSpace(plan.DeliveryRoute.StartLiftName)
                ? plan.DeliveryRoute.StartLiftName
                : "-";
        }

        private string ResolveEquipmentDisplayNameForSave()
        {
            if (CurrentEditor != null && !string.IsNullOrWhiteSpace(CurrentEditor.SelectedEquipmentDisplayName))
            {
                return CurrentEditor.SelectedEquipmentDisplayName;
            }

            if (SelectedEquipmentOption != null && !string.IsNullOrWhiteSpace(SelectedEquipmentOption.DisplayName))
            {
                return SelectedEquipmentOption.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(ConfirmedEquipmentName))
            {
                return ConfirmedEquipmentName;
            }

            return string.Empty;
        }

        private static LayoutWallSelectionDto ToWallDto(EditorWallOptionViewModel wall)
        {
            if (wall == null || wall.IsSelectOption)
            {
                return new LayoutWallSelectionDto();
            }

            return new LayoutWallSelectionDto
            {
                ElementId = wall.ElementId,
                UniqueId = wall.UniqueId ?? string.Empty,
                DisplayName = wall.DisplayName ?? string.Empty,
                RevitName = wall.RevitName ?? string.Empty,
                LengthMm = wall.LengthMm
            };
        }

        private static void SelectWallOption(
            SolutionEditorViewModel editor,
            LayoutWallSelectionDto wall,
            Action<EditorWallOptionViewModel> setter)
        {
            if (editor == null || setter == null)
            {
                return;
            }

            EditorWallOptionViewModel match = null;

            if (wall != null)
            {
                match = editor.WallOptions.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(wall.UniqueId) &&
                    string.Equals(x.UniqueId, wall.UniqueId, StringComparison.OrdinalIgnoreCase));

                if (match == null && wall.ElementId > 0)
                {
                    match = editor.WallOptions.FirstOrDefault(x => x.ElementId == wall.ElementId);
                }

                if (match == null && !string.IsNullOrWhiteSpace(wall.DisplayName))
                {
                    match = editor.WallOptions.FirstOrDefault(x =>
                        string.Equals(x.DisplayName, wall.DisplayName, StringComparison.OrdinalIgnoreCase));
                }
            }

            setter(match ?? editor.WallOptions.FirstOrDefault());
        }

        private string CurrentEditorLayoutId
        {
            get { return _currentEditorLayoutId; }
            set { _currentEditorLayoutId = value; }
        }

        private void SyncCurrentEditorRoomSelection()
        {
            if (CurrentEditor == null)
            {
                return;
            }

            EditorRoomOptionViewModel option = EditorRoomOptions.FirstOrDefault(x =>
                string.Equals(x.Key, CurrentEditor.RoomKey, StringComparison.OrdinalIgnoreCase));
            CurrentEditor.SelectedRoomOption = option;
        }
    }

    public sealed class EditorRoomOptionViewModel
    {
        public string Key { get; set; }

        public string DisplayName { get; set; }

        public string RoomName { get; set; }

        public string TargetType { get; set; }

        public string AreaText { get; set; }

        public string LevelText { get; set; }

        public string StatusText { get; set; }

        public string RoomLengthText { get; set; }

        public string RoomWidthText { get; set; }

        public string RoomHeightText { get; set; }

        public string DoorWidthText { get; set; }

        public string DoorHeightText { get; set; }

        public string AvailableUsableAreaText { get; set; }

        public List<EditorWallOptionViewModel> WallOptions { get; set; } = new List<EditorWallOptionViewModel>();

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? RoomName : DisplayName;
        }
    }

    public sealed class EditorWallOptionViewModel
    {
        public string DisplayName { get; set; }

        public int ElementId { get; set; }

        public string UniqueId { get; set; }

        public string RevitName { get; set; }

        public double LengthMm { get; set; }

        public bool IsSelectOption
        {
            get { return ElementId <= 0 || string.Equals(DisplayName, "Select", StringComparison.OrdinalIgnoreCase); }
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? "Select" : DisplayName;
        }
    }

    public enum RoomDetailPageMode
    {
        Overview,
        SolutionEditor
    }

    public sealed class LayoutPlanCardViewModel : INotifyPropertyChanged
    {
        private string _layoutId;
        private string _roomKey;
        private string _planName;
        private string _plantRoom;
        private string _roomType;
        private string _areaText;
        private string _modelName;
        private string _layoutType;
        private string _equipmentTypeTagText;
        private string _wallText;
        private string _sizeStatus;
        private string _fitnessText;
        private string _routeLengthText;
        private string _startPointText;
        private string _pipingStatus;
        private Brush _pipingStatusForeground = new SolidColorBrush(Color.FromRgb(22, 103, 183));
        private bool _hasEquipmentValidationResult;
        private string _equipmentValidationStatusText;
        private Brush _equipmentValidationBadgeBackground = Brushes.Transparent;
        private string _modulesText;
        private string _maxDimsText;
        private string _createdAtText;
        private bool _isCompareMode;
        private bool _isCompareSelected;
        private bool _hasDeliveryRoute;
        private bool _isActiveLayout;

        public event PropertyChangedEventHandler PropertyChanged;

        public string PlanName
        {
            get { return _planName; }
            set { Set(ref _planName, value); }
        }

        public string LayoutId
        {
            get { return _layoutId; }
            set { Set(ref _layoutId, value); }
        }

        public string RoomKey
        {
            get { return _roomKey; }
            set { Set(ref _roomKey, value); }
        }

        public string PlantRoom
        {
            get { return _plantRoom; }
            set { Set(ref _plantRoom, value); }
        }

        public string RoomType
        {
            get { return _roomType; }
            set { Set(ref _roomType, value); }
        }

        public string AreaText
        {
            get { return _areaText; }
            set { Set(ref _areaText, value); }
        }

        public string ModelName
        {
            get { return _modelName; }
            set { Set(ref _modelName, value); }
        }

        public string LayoutType
        {
            get { return _layoutType; }
            set { Set(ref _layoutType, value); }
        }

        public string EquipmentTypeTagText
        {
            get { return _equipmentTypeTagText; }
            set { Set(ref _equipmentTypeTagText, value); }
        }

        public string WallText
        {
            get { return _wallText; }
            set { Set(ref _wallText, value); }
        }

        public string SizeStatus
        {
            get { return _sizeStatus; }
            set { Set(ref _sizeStatus, value); }
        }

        public string FitnessText
        {
            get { return _fitnessText; }
            set { Set(ref _fitnessText, value); }
        }

        public string RouteLengthText
        {
            get { return _routeLengthText; }
            set { Set(ref _routeLengthText, value); }
        }

        public string StartPointText
        {
            get { return _startPointText; }
            set { Set(ref _startPointText, value); }
        }

        public string PipingStatus
        {
            get { return _pipingStatus; }
            set { Set(ref _pipingStatus, value); }
        }

        public Brush PipingStatusForeground
        {
            get { return _pipingStatusForeground; }
            set { Set(ref _pipingStatusForeground, value); }
        }

        public bool HasEquipmentValidationResult
        {
            get { return _hasEquipmentValidationResult; }
            set { Set(ref _hasEquipmentValidationResult, value); }
        }

        public string EquipmentValidationStatusText
        {
            get { return _equipmentValidationStatusText; }
            set { Set(ref _equipmentValidationStatusText, value); }
        }

        public Brush EquipmentValidationBadgeBackground
        {
            get { return _equipmentValidationBadgeBackground; }
            set { Set(ref _equipmentValidationBadgeBackground, value); }
        }

        public string ModulesText
        {
            get { return _modulesText; }
            set { Set(ref _modulesText, value); }
        }

        public string MaxDimsText
        {
            get { return _maxDimsText; }
            set { Set(ref _maxDimsText, value); }
        }

        public string CreatedAtText
        {
            get { return _createdAtText; }
            set { Set(ref _createdAtText, value); }
        }


        public bool IsCompareMode
        {
            get { return _isCompareMode; }
            set
            {
                if (Set(ref _isCompareMode, value))
                {
                    OnCompareVisualPropertiesChanged();
                }
            }
        }

        public bool IsCompareSelected
        {
            get { return _isCompareSelected; }
            set
            {
                if (Set(ref _isCompareSelected, value))
                {
                    OnCompareVisualPropertiesChanged();
                }
            }
        }

        public bool HasDeliveryRoute
        {
            get { return _hasDeliveryRoute; }
            set
            {
                if (Set(ref _hasDeliveryRoute, value))
                {
                    OnCompareVisualPropertiesChanged();
                }
            }
        }

        public bool IsActiveLayout
        {
            get { return _isActiveLayout; }
            set { Set(ref _isActiveLayout, value); }
        }

        public Visibility CompareButtonVisibility
        {
            get { return IsCompareMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public bool IsCompareButtonEnabled
        {
            get { return IsCompareMode && HasDeliveryRoute; }
        }

        public string CompareButtonText
        {
            get { return IsCompareSelected ? "Selected" : "Compare"; }
        }

        public Brush CompareBorderBrush
        {
            get { return IsCompareSelected ? new SolidColorBrush(Color.FromRgb(43, 131, 234)) : new SolidColorBrush(Color.FromRgb(221, 229, 239)); }
        }

        public Brush CompareCardBackground
        {
            get { return IsCompareSelected ? new SolidColorBrush(Color.FromRgb(244, 250, 255)) : Brushes.White; }
        }

        public Brush CompareButtonForeground
        {
            get { return IsCompareSelected ? new SolidColorBrush(Color.FromRgb(22, 103, 183)) : new SolidColorBrush(Color.FromRgb(54, 64, 72)); }
        }

        public Brush CompareButtonBackground
        {
            get { return IsCompareSelected ? new SolidColorBrush(Color.FromRgb(248, 251, 254)) : Brushes.White; }
        }

        private void OnCompareVisualPropertiesChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareButtonVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompareButtonEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareCardBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareButtonForeground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompareButtonBackground)));
        }

        public ICommand CompareCommand { get; set; }

        public ICommand DeleteCommand { get; set; }

        public ICommand ExportCommand { get; set; }

        public ICommand DetailCommand { get; set; }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public sealed class AhuSubModuleRowViewModel
    {
        public string SubModule { get; set; }

        public string Type { get; set; }

        public string DimensionsMm { get; set; }

        public string Seq { get; set; }
    }

    public sealed class EquipmentSelectionCardViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _sizeStatus;
        private string _fitWarningText;
        private bool _hasFitWarning;
        private bool _isChecking;
        private bool _hasValidationResult;
        private bool _isValidationValid;
        private string _validationStatusText;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FamilyKey { get; set; }

        public string DisplayName { get; set; }

        public string FileName { get; set; }

        public string Description { get; set; }

        public double AirflowM3s { get; set; }

        public int TotalLengthMm { get; set; }

        public int WidthMm { get; set; }

        public int HeightMm { get; set; }

        public int WeightKg { get; set; }

        public int RequiredMaintenanceSpaceMm { get; set; }

        public string RequiredMaintenanceSpaceSide { get; set; }

        public int MbLengthMm { get; set; }

        public int FilterLengthMm { get; set; }

        public int CoilLengthMm { get; set; }

        public int FanLengthMm { get; set; }

        public int ValveChamberLengthMm { get; set; }

        public int ValveChamberWidthMm { get; set; }

        public int ElChamberLengthMm { get; set; }

        public int ElChamberWidthMm { get; set; }

        public int MaintenanceDoorSideMm { get; set; }

        public int MaintenanceOtherSideMm { get; set; }

        public int MaintenanceFrontBackMm { get; set; }

        public string SizeStatus
        {
            get { return _sizeStatus; }
            set
            {
                if (string.Equals(_sizeStatus, value, StringComparison.Ordinal))
                {
                    return;
                }

                _sizeStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeStatus)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
            }
        }

        public bool IsOptional { get; set; }

        public bool IsExceeded { get; set; }

        public ICommand SelectCommand { get; set; }

        public ObservableCollection<string> ValidationReasons { get; } =
            new ObservableCollection<string>();

        public string FitWarningText
        {
            get
            {
                return string.IsNullOrWhiteSpace(_fitWarningText)
                    ? "Maintenance Space does not fit in the selected room."
                    : _fitWarningText;
            }
            set
            {
                if (string.Equals(_fitWarningText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _fitWarningText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FitWarningText)));
            }
        }

        public bool HasFitWarning
        {
            get { return _hasFitWarning; }
            set
            {
                if (_hasFitWarning == value)
                {
                    return;
                }

                _hasFitWarning = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFitWarning)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowExceededWarning)));
            }
        }

        public string SizeText
        {
            get
            {
                string dimensionsText = DimensionsText;
                string airflowText = AirflowRateText;
                if (!string.IsNullOrWhiteSpace(dimensionsText) && !string.IsNullOrWhiteSpace(airflowText))
                {
                    return dimensionsText + Environment.NewLine + airflowText;
                }

                return "Size: " + (SizeStatus ?? string.Empty);
            }
        }

        public string DimensionsText
        {
            get
            {
                if (TotalLengthMm <= 0 || WidthMm <= 0 || HeightMm <= 0)
                {
                    return string.Empty;
                }

                return "Dimensions (mm): L:" + TotalLengthMm + " x W:" + WidthMm + " x H:" + HeightMm;
            }
        }

        public string AirflowRateText
        {
            get
            {
                if (AirflowM3s <= 0)
                {
                    return string.Empty;
                }

                return "Airflow Rate: " + FormatAirflow(AirflowM3s) + " m³/s";
            }
        }

        public bool ShowExceededWarning
        {
            get { return IsSelected && HasFitWarning; }
        }

        public bool IsChecking
        {
            get { return _isChecking; }
            set
            {
                if (_isChecking == value)
                {
                    return;
                }

                _isChecking = value;
                NotifyValidationVisualsChanged();
            }
        }

        public bool HasValidationResult
        {
            get { return _hasValidationResult; }
            private set
            {
                if (_hasValidationResult == value)
                {
                    return;
                }

                _hasValidationResult = value;
                NotifyValidationVisualsChanged();
            }
        }

        public bool IsValidationValid
        {
            get { return _isValidationValid; }
            private set
            {
                if (_isValidationValid == value)
                {
                    return;
                }

                _isValidationValid = value;
                NotifyValidationVisualsChanged();
            }
        }

        public bool IsValidationOversized
        {
            get { return HasValidationResult && !IsValidationValid; }
        }

        public bool HasValidationReasons
        {
            get { return ValidationReasons.Count > 0; }
        }

        public string ValidationStatusText
        {
            get { return _validationStatusText ?? string.Empty; }
            private set
            {
                if (string.Equals(_validationStatusText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _validationStatusText = value;
                NotifyValidationVisualsChanged();
            }
        }

        public string SelectButtonText
        {
            get
            {
                if (IsChecking)
                {
                    return "Checking...";
                }

                return HasValidationResult ? ValidationStatusText : "Select";
            }
        }

        public Brush ValidationBadgeBackground
        {
            get
            {
                if (IsChecking)
                {
                    return new SolidColorBrush(Color.FromRgb(93, 106, 120));
                }

                if (!HasValidationResult)
                {
                    return new SolidColorBrush(Color.FromRgb(22, 103, 183));
                }

                return IsValidationValid
                    ? new SolidColorBrush(Color.FromRgb(27, 124, 73))
                    : new SolidColorBrush(Color.FromRgb(180, 35, 24));
            }
        }

        public void ApplyValidationDto(EquipmentPlacementValidationDto validation)
        {
            ValidationReasons.Clear();

            if (validation == null || !validation.HasResult)
            {
                ClearValidationResult();
                return;
            }

            if (validation.Reasons != null)
            {
                foreach (string reason in validation.Reasons)
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        ValidationReasons.Add(reason);
                    }
                }
            }

            IsValidationValid = validation.IsValid;
            ValidationStatusText = string.IsNullOrWhiteSpace(validation.Status)
                ? (validation.IsValid ? "Valid" : "Oversized")
                : validation.Status;
            HasValidationResult = true;
            NotifyValidationVisualsChanged();
        }

        public EquipmentPlacementValidationDto ToValidationDto()
        {
            if (!HasValidationResult)
            {
                return null;
            }

            return new EquipmentPlacementValidationDto
            {
                HasResult = true,
                IsValid = IsValidationValid,
                Status = ValidationStatusText,
                Reasons = ValidationReasons.ToList(),
                Source = "API"
            };
        }

        public void ClearValidationResult()
        {
            ValidationReasons.Clear();
            _hasValidationResult = false;
            _isValidationValid = false;
            _validationStatusText = string.Empty;
            NotifyValidationVisualsChanged();
        }

        private void NotifyValidationVisualsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecking)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasValidationResult)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidationValid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidationOversized)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasValidationReasons)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationBadgeBackground)));
        }

        public void ApplyPlacementFitResult(string fitStatus, string warningMessage)
        {
            if (string.IsNullOrWhiteSpace(fitStatus))
            {
                SizeStatus = "-";
                FitWarningText = string.Empty;
                HasFitWarning = false;
                return;
            }

            if (string.Equals(fitStatus, "OK", StringComparison.OrdinalIgnoreCase))
            {
                SizeStatus = "Fit";
            }
            else if (string.Equals(fitStatus, "Exceeded", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(fitStatus, "TouchWall", StringComparison.OrdinalIgnoreCase))
            {
                SizeStatus = "Exceeded";
            }
            else
            {
                SizeStatus = "-";
            }

            FitWarningText = warningMessage ?? string.Empty;
            HasFitWarning = !string.IsNullOrWhiteSpace(warningMessage);
        }

        private static string FormatAirflow(double airflowM3s)
        {
            return Math.Abs(airflowM3s - Math.Round(airflowM3s)) < 0.0001
                ? ((int)Math.Round(airflowM3s)).ToString()
                : airflowM3s.ToString("0.###");
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowExceededWarning)));
            }
        }
    }

    public sealed class SolutionEditorViewModel : INotifyPropertyChanged
    {
        private const string NewBuildingDesignText = "New Building Design";
        private const string RmaaReplacementText = "RMAA/Replacement";
        private const string RmaaReplacementInputText = "RMAA / Replacement";
        private string _roomKey;
        private string _roomName;
        private string _solutionName;
        private string _planningContext;
        private string _equipmentType;
        private string _selectedFlowRate;
        private EditorRoomOptionViewModel _selectedRoomOption;
        private string _editorRoomName = "Not selected";
        private string _editorAreaText = "-";
        private string _editorLevelText = "-";
        private string _editorStatusText = "Choose a target room to continue";
        private string _editorRoomLengthText = "-";
        private string _editorRoomWidthText = "-";
        private string _editorRoomHeightText = "-";
        private string _editorDoorWidthText = "-";
        private string _editorDoorHeightText = "-";
        private string _editorAvailableUsableAreaText = "-";
        private Action _selectedFlowRateChanged;
        private string _selectedEquipmentFamilyKey;
        private string _selectedEquipmentDisplayName;
        private string _selectedSupplyAirDuct;
        private string _selectedReturnAirDuct;
        private string _selectedChwSupply;
        private string _selectedChwReturn;
        private string _selectedDuctWall;
        private string _selectedPipeWall;
        private string _selectedSadSize = "Select";
        private string _selectedRadSize = "Select";
        private string _selectedChwsPipeSize = "Select";
        private string _selectedChwrPipeSize = "Select";
        private EditorWallOptionViewModel _selectedSadWallOption;
        private EditorWallOptionViewModel _selectedRadWallOption;
        private EditorWallOptionViewModel _selectedChwsWallOption;
        private EditorWallOptionViewModel _selectedChwrWallOption;
        private Action _connectivitySelectionChanged;
        private Action<EditorWallOptionViewModel> _boundaryWallSelectionChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<EditorWallOptionViewModel> WallOptions { get; } =
            new ObservableCollection<EditorWallOptionViewModel>();

        public string RoomKey
        {
            get { return _roomKey; }
            set { Set(ref _roomKey, value); }
        }

        public string RoomName
        {
            get { return _roomName; }
            set { Set(ref _roomName, value); }
        }

        public string SolutionName
        {
            get { return _solutionName; }
            set { Set(ref _solutionName, value); }
        }

        public string PlanningContext
        {
            get { return _planningContext; }
            set
            {
                if (Set(ref _planningContext, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlanningContextBadgeText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlanningContextHint)));
                }
            }
        }

        public string EquipmentType
        {
            get { return _equipmentType; }
            set { Set(ref _equipmentType, value); }
        }

        public string SelectedFlowRate
        {
            get { return _selectedFlowRate; }
            set
            {
                if (Set(ref _selectedFlowRate, value))
                {
                    _selectedFlowRateChanged?.Invoke();
                }
            }
        }

        internal Action SelectedFlowRateChanged
        {
            get { return _selectedFlowRateChanged; }
            set { _selectedFlowRateChanged = value; }
        }

        internal Action ConnectivitySelectionChanged
        {
            get { return _connectivitySelectionChanged; }
            set { _connectivitySelectionChanged = value; }
        }

        internal Action<EditorWallOptionViewModel> BoundaryWallSelectionChanged
        {
            get { return _boundaryWallSelectionChanged; }
            set { _boundaryWallSelectionChanged = value; }
        }

        public string PlanningContextBadgeText
        {
            get
            {
                return IsRmaaReplacement() ? RmaaReplacementText : NewBuildingDesignText;
            }
        }

        public string PlanningContextHint
        {
            get
            {
                return IsRmaaReplacement()
                    ? "Room bounds are locked to existing conditions and only fitting AHU options remain available."
                    : "Storyboard guidance is active for this new-building layout.";
            }
        }

        public EditorRoomOptionViewModel SelectedRoomOption
        {
            get { return _selectedRoomOption; }
            set
            {
                if (Equals(_selectedRoomOption, value))
                {
                    return;
                }

                _selectedRoomOption = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRoomOption)));

                if (value == null)
                {
                    RoomKey = string.Empty;
                    RoomName = string.Empty;
                    EditorRoomName = "Not selected";
                    EditorAreaText = "-";
                    EditorLevelText = "-";
                    EditorStatusText = "Choose a target room to continue";
                    EditorRoomLengthText = "-";
                    EditorRoomWidthText = "-";
                    EditorRoomHeightText = "-";
                    EditorDoorWidthText = "-";
                    EditorDoorHeightText = "-";
                    EditorAvailableUsableAreaText = "-";
                    RefreshWallOptionsFromSelectedRoom();
                    return;
                }

                RoomKey = value.Key ?? string.Empty;
                RoomName = value.RoomName ?? string.Empty;
                EditorRoomName = string.IsNullOrWhiteSpace(value.RoomName) ? "-" : value.RoomName;
                EditorAreaText = string.IsNullOrWhiteSpace(value.AreaText) ? "-" : value.AreaText;
                EditorLevelText = string.IsNullOrWhiteSpace(value.LevelText) ? "-" : value.LevelText;
                EditorStatusText = string.IsNullOrWhiteSpace(value.StatusText) ? "-" : value.StatusText;
                EditorRoomLengthText = string.IsNullOrWhiteSpace(value.RoomLengthText) ? "-" : value.RoomLengthText;
                EditorRoomWidthText = string.IsNullOrWhiteSpace(value.RoomWidthText) ? "-" : value.RoomWidthText;
                EditorRoomHeightText = string.IsNullOrWhiteSpace(value.RoomHeightText) ? "-" : value.RoomHeightText;
                EditorDoorWidthText = string.IsNullOrWhiteSpace(value.DoorWidthText) ? "-" : value.DoorWidthText;
                EditorDoorHeightText = string.IsNullOrWhiteSpace(value.DoorHeightText) ? "-" : value.DoorHeightText;
                EditorAvailableUsableAreaText = string.IsNullOrWhiteSpace(value.AvailableUsableAreaText) ? "-" : value.AvailableUsableAreaText;
                RefreshWallOptionsFromSelectedRoom();
                if (!string.IsNullOrWhiteSpace(RoomKey))
                {
                    RoomRecognitionPaneRuntime.SyncListSelectionFromEditorRoom(RoomKey);
                }
            }
        }

        public string EditorRoomName
        {
            get { return _editorRoomName; }
            set { Set(ref _editorRoomName, value); }
        }

        public string EditorAreaText
        {
            get { return _editorAreaText; }
            set { Set(ref _editorAreaText, value); }
        }

        public string EditorLevelText
        {
            get { return _editorLevelText; }
            set { Set(ref _editorLevelText, value); }
        }

        public string EditorStatusText
        {
            get { return _editorStatusText; }
            set { Set(ref _editorStatusText, value); }
        }

        public string EditorRoomLengthText
        {
            get { return _editorRoomLengthText; }
            set { Set(ref _editorRoomLengthText, value); }
        }

        public string EditorRoomWidthText
        {
            get { return _editorRoomWidthText; }
            set { Set(ref _editorRoomWidthText, value); }
        }

        public string EditorRoomHeightText
        {
            get { return _editorRoomHeightText; }
            set { Set(ref _editorRoomHeightText, value); }
        }

        public string EditorDoorWidthText
        {
            get { return _editorDoorWidthText; }
            set { Set(ref _editorDoorWidthText, value); }
        }

        public string EditorDoorHeightText
        {
            get { return _editorDoorHeightText; }
            set { Set(ref _editorDoorHeightText, value); }
        }

        public string EditorAvailableUsableAreaText
        {
            get { return _editorAvailableUsableAreaText; }
            set { Set(ref _editorAvailableUsableAreaText, value); }
        }

        public string SelectedEquipmentFamilyKey
        {
            get { return _selectedEquipmentFamilyKey; }
            set { Set(ref _selectedEquipmentFamilyKey, value); }
        }

        public string SelectedEquipmentDisplayName
        {
            get { return _selectedEquipmentDisplayName; }
            set { Set(ref _selectedEquipmentDisplayName, value); }
        }

        public string SelectedSupplyAirDuct
        {
            get { return _selectedSupplyAirDuct; }
            set { Set(ref _selectedSupplyAirDuct, value); }
        }

        public string SelectedReturnAirDuct
        {
            get { return _selectedReturnAirDuct; }
            set { Set(ref _selectedReturnAirDuct, value); }
        }

        public string SelectedChwSupply
        {
            get { return _selectedChwSupply; }
            set { Set(ref _selectedChwSupply, value); }
        }

        public string SelectedChwReturn
        {
            get { return _selectedChwReturn; }
            set { Set(ref _selectedChwReturn, value); }
        }

        public string SelectedDuctWall
        {
            get { return _selectedDuctWall; }
            set { Set(ref _selectedDuctWall, value); }
        }

        public string SelectedPipeWall
        {
            get { return _selectedPipeWall; }
            set { Set(ref _selectedPipeWall, value); }
        }

        public string SelectedSadSize
        {
            get { return _selectedSadSize; }
            set
            {
                if (Set(ref _selectedSadSize, value))
                {
                    _connectivitySelectionChanged?.Invoke();
                }
            }
        }

        public string SelectedRadSize
        {
            get { return _selectedRadSize; }
            set
            {
                if (Set(ref _selectedRadSize, value))
                {
                    _connectivitySelectionChanged?.Invoke();
                }
            }
        }

        public string SelectedChwsPipeSize
        {
            get { return _selectedChwsPipeSize; }
            set
            {
                if (Set(ref _selectedChwsPipeSize, value))
                {
                    _connectivitySelectionChanged?.Invoke();
                }
            }
        }

        public string SelectedChwrPipeSize
        {
            get { return _selectedChwrPipeSize; }
            set
            {
                if (Set(ref _selectedChwrPipeSize, value))
                {
                    _connectivitySelectionChanged?.Invoke();
                }
            }
        }

        public EditorWallOptionViewModel SelectedSadWallOption
        {
            get { return _selectedSadWallOption; }
            set { SetSelectedWallOption(ref _selectedSadWallOption, value, nameof(SelectedSadWallOption)); }
        }

        public EditorWallOptionViewModel SelectedRadWallOption
        {
            get { return _selectedRadWallOption; }
            set { SetSelectedWallOption(ref _selectedRadWallOption, value, nameof(SelectedRadWallOption)); }
        }

        public EditorWallOptionViewModel SelectedChwsWallOption
        {
            get { return _selectedChwsWallOption; }
            set { SetSelectedWallOption(ref _selectedChwsWallOption, value, nameof(SelectedChwsWallOption)); }
        }

        public EditorWallOptionViewModel SelectedChwrWallOption
        {
            get { return _selectedChwrWallOption; }
            set { SetSelectedWallOption(ref _selectedChwrWallOption, value, nameof(SelectedChwrWallOption)); }
        }

        internal void RefreshWallOptionsFromSelectedRoom()
        {
            List<EditorWallOptionViewModel> source = SelectedRoomOption != null
                ? SelectedRoomOption.WallOptions ?? new List<EditorWallOptionViewModel>()
                : new List<EditorWallOptionViewModel>();

            WallOptions.Clear();
            WallOptions.Add(CreateSelectWallOption());

            foreach (EditorWallOptionViewModel wall in source)
            {
                if (wall == null || wall.IsSelectOption)
                {
                    continue;
                }

                WallOptions.Add(wall);
            }

            ResetWallSelectionsToDefault();
        }

        internal void ResetConnectivitySelectionsToDefault()
        {
            SelectedSadSize = "Select";
            SelectedRadSize = "Select";
            SelectedChwsPipeSize = "Select";
            SelectedChwrPipeSize = "Select";
            ResetWallSelectionsToDefault();
        }

        private void ResetWallSelectionsToDefault()
        {
            EditorWallOptionViewModel selectOption = WallOptions.FirstOrDefault() ?? CreateSelectWallOption();
            SelectedSadWallOption = selectOption;
            SelectedRadWallOption = selectOption;
            SelectedChwsWallOption = selectOption;
            SelectedChwrWallOption = selectOption;
            _connectivitySelectionChanged?.Invoke();
        }

        private void SetSelectedWallOption(ref EditorWallOptionViewModel field, EditorWallOptionViewModel value, string propertyName)
        {
            EditorWallOptionViewModel next = value ?? WallOptions.FirstOrDefault() ?? CreateSelectWallOption();
            if (Equals(field, next))
            {
                return;
            }

            field = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            _connectivitySelectionChanged?.Invoke();
            if (!next.IsSelectOption)
            {
                _boundaryWallSelectionChanged?.Invoke(next);
            }
        }

        private static EditorWallOptionViewModel CreateSelectWallOption()
        {
            return new EditorWallOptionViewModel
            {
                DisplayName = "Select",
                ElementId = -1,
                UniqueId = string.Empty,
                RevitName = string.Empty,
                LengthMm = 0.0
            };
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private bool IsRmaaReplacement()
        {
            return string.Equals(PlanningContext, RmaaReplacementInputText, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(PlanningContext, RmaaReplacementText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
