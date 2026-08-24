using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.DeliveryRoutes;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Rooms.LayoutPlans;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class DeliveryRoutePaneViewModel : INotifyPropertyChanged
    {
        public const string StartLocationModeByLift = "By Lift";
        public const string StartLocationModeByPoint = "By Start Point";

        private string _routeName = "Route 1";
        private string _hintText = string.Empty;
        private string _resultTitle = "Route Passed";
        private string _resultMessage = string.Empty;
        private string _failureReasonText = string.Empty;
        private string _routeLengthText = "-";
        private string _currentRouteId = string.Empty;
        private string _generatedResponseBody = string.Empty;
        private string _generatedApiMessage = string.Empty;
        private double? _generatedPathLengthMeters;
        private bool _generatedRouteSuccess;
        private bool _isEditorVisible;
        private bool _isOverviewVisible = true;
        private bool _isGenerating;
        private bool _isResultVisible;
        private bool _isUpdatingOptions;
        private bool _isStartLiftDefined;
        private bool _isStartPointDefined;
        private bool _isTargetRoomDefined;
        private string _selectedStartLocationMode = StartLocationModeByLift;
        private string _startPointName = string.Empty;
        private double? _startPointXmm;
        private double? _startPointYmm;
        private double? _startPointZmm;
        private EditorLiftOptionViewModel _selectedStartLift;
        private EditorRoomOptionViewModel _selectedTargetRoom;
        private DeliveryRouteEquipmentInfo _selectedEquipmentInfo;
        private bool _isCompareMode;
        private int _compareRoutesDisplayedCount;
        private readonly List<string> _selectedCompareRouteIds = new List<string>();

        public DeliveryRoutePaneViewModel()
        {
            NewRouteCommand = new DelegateCommand(_ => OpenEditor());
            CompareModeCommand = new DelegateCommand(_ => EnterCompareMode());
            CancelCompareModeCommand = new DelegateCommand(_ => CancelCompareMode());
            FinishCompareModeCommand = new DelegateCommand(_ => FinishCompareMode());
            ClearCompareRoutesCommand = new DelegateCommand(_ => ClearCompareRoutes());
            ToggleRouteCompareCommand = new DelegateCommand(parameter => ToggleRouteCompare(parameter as DeliveryRouteCardViewModel));
            DefineStartLiftCommand = new DelegateCommand(_ => DefineStartLift());
            SetStartLocationCommand = new DelegateCommand(_ => SetStartLocation());
            DefineStartLocationCommand = new DelegateCommand(_ => DefineStartLocation());
            RemoveStartLocationCommand = new DelegateCommand(_ => RemoveStartLocation());
            DefineTargetRoomCommand = new DelegateCommand(_ => DefineTargetRoom());
            GenerateDeliveryRouteCommand = new DelegateCommand(_ => GenerateDeliveryRoute());
            CancelCommand = new DelegateCommand(_ => CancelEditor());
            SaveCommand = new DelegateCommand(_ => SaveRoute());
            DetailRouteCommand = new DelegateCommand(parameter => OpenSavedRoute(parameter as DeliveryRouteCardViewModel));
            ExportReportCommand = new DelegateCommand(parameter => ExportSavedRoute(parameter as DeliveryRouteCardViewModel));
            DeleteRouteCommand = new DelegateCommand(parameter => DeleteSavedRoute(parameter as DeliveryRouteCardViewModel));
        }

        public ObservableCollection<string> StartLocationModeOptions { get; } =
            new ObservableCollection<string> { StartLocationModeByLift, StartLocationModeByPoint };

        public ObservableCollection<EditorLiftOptionViewModel> StartLiftOptions { get; } =
            new ObservableCollection<EditorLiftOptionViewModel>();

        public ObservableCollection<EditorRoomOptionViewModel> TargetRoomOptions { get; } =
            new ObservableCollection<EditorRoomOptionViewModel>();

        public ObservableCollection<DeliveryRouteSubModuleRowViewModel> SubModuleRows { get; } =
            new ObservableCollection<DeliveryRouteSubModuleRowViewModel>();

        public ObservableCollection<DeliveryRouteCardViewModel> SavedRoutes { get; } =
            new ObservableCollection<DeliveryRouteCardViewModel>();

        public ICommand NewRouteCommand { get; }

        public ICommand CompareModeCommand { get; }

        public ICommand CancelCompareModeCommand { get; }

        public ICommand FinishCompareModeCommand { get; }

        public ICommand ClearCompareRoutesCommand { get; }

        public ICommand ToggleRouteCompareCommand { get; }

        public ICommand DefineStartLiftCommand { get; }

        public ICommand SetStartLocationCommand { get; }

        public ICommand DefineStartLocationCommand { get; }

        public ICommand RemoveStartLocationCommand { get; }

        public ICommand DefineTargetRoomCommand { get; }

        public ICommand GenerateDeliveryRouteCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand SaveCommand { get; }

        public ICommand DetailRouteCommand { get; }

        public ICommand ExportReportCommand { get; }

        public ICommand DeleteRouteCommand { get; }

        public string RouteName
        {
            get { return _routeName; }
            set
            {
                if (Set(ref _routeName, value))
                {
                    OnPropertyChanged(nameof(CanSaveRoute));
                }
            }
        }

        public string HintText
        {
            get { return _hintText; }
            set { Set(ref _hintText, value); }
        }

        public string ResultTitle
        {
            get { return _resultTitle; }
            set { Set(ref _resultTitle, value); }
        }

        public string ResultMessage
        {
            get { return _resultMessage; }
            set { Set(ref _resultMessage, value); }
        }

        public string FailureReasonText
        {
            get { return _failureReasonText; }
            set { Set(ref _failureReasonText, value); }
        }

        public string RouteLengthText
        {
            get { return _routeLengthText; }
            set { Set(ref _routeLengthText, value); }
        }

        public string DisassemblyText
        {
            get
            {
                if (SubModuleRows.Count == 0)
                {
                    return "-";
                }

                return SubModuleRows.Count.ToString(CultureInfo.InvariantCulture) +
                       (SubModuleRows.Count == 1 ? " sub-module" : " sub-modules");
            }
        }

        public string MaxDimsText
        {
            get
            {
                DeliveryRouteSubModuleRowViewModel last = SubModuleRows
                    .OrderByDescending(x => x != null ? x.Sequence : 0)
                    .FirstOrDefault();
                return last == null ? "-" : FormatModuleDimensionsForRoute(last.DimensionsMm);
            }
        }

        public bool HasSubModuleRows
        {
            get { return SubModuleRows.Count > 0; }
        }

        public bool IsRouteSuccess
        {
            get { return _generatedRouteSuccess; }
        }

        public bool IsPassedResultVisible
        {
            get { return IsResultVisible && IsRouteSuccess; }
        }

        public bool IsFailedResultVisible
        {
            get { return IsResultVisible && !IsRouteSuccess; }
        }

        public bool HasDetectedLifts
        {
            get { return StartLiftOptions.Any(x => x != null && !string.IsNullOrWhiteSpace(x.Key)); }
        }

        public bool IsNoLiftsWarningVisible
        {
            get { return IsByLift && !HasDetectedLifts; }
        }

        public bool HasDetectedRooms
        {
            get { return TargetRoomOptions.Any(x => x != null && !string.IsNullOrWhiteSpace(x.Key)); }
        }

        public bool IsNoRoomsWarningVisible
        {
            get { return !HasDetectedRooms; }
        }

        public bool IsNoEquipmentWarningVisible
        {
            get
            {
                return HasDetectedRooms &&
                       SelectedTargetRoom != null &&
                       !string.IsNullOrWhiteSpace(SelectedTargetRoom.Key) &&
                       !HasCommittedEquipment;
            }
        }

        public bool HasCommittedEquipment
        {
            get { return _selectedEquipmentInfo != null && _selectedEquipmentInfo.Found; }
        }

        public int SelectedOriginalModelId
        {
            get { return _selectedEquipmentInfo != null ? _selectedEquipmentInfo.OriginalModelId : 0; }
        }

        public string SelectedEquipmentFamilyKey
        {
            get { return _selectedEquipmentInfo != null ? _selectedEquipmentInfo.FamilyKey ?? string.Empty : string.Empty; }
        }

        public string SelectedEquipmentDisplayName
        {
            get { return _selectedEquipmentInfo != null ? _selectedEquipmentInfo.DisplayName ?? string.Empty : string.Empty; }
        }

        public string SelectedEquipmentInfoText
        {
            get { return BuildEquipmentInfoText(_selectedEquipmentInfo); }
        }

        public bool CanGenerateDeliveryRoute
        {
            get
            {
                return !IsGenerating &&
                       HasValidStartLocation &&
                       SelectedTargetRoom != null &&
                       !string.IsNullOrWhiteSpace(SelectedTargetRoom.Key) &&
                       HasCommittedEquipment &&
                       SelectedOriginalModelId >= 1 &&
                       SelectedOriginalModelId <= 10;
            }
        }

        private bool HasValidStartLocation
        {
            get
            {
                if (IsByStartPoint)
                {
                    return IsStartPointDefined && _startPointXmm.HasValue && _startPointYmm.HasValue;
                }

                return SelectedStartLift != null && !string.IsNullOrWhiteSpace(SelectedStartLift.Key);
            }
        }

        public bool CanSaveRoute
        {
            get
            {
                return IsResultVisible &&
                       !IsGenerating &&
                       !string.IsNullOrWhiteSpace(RouteName) &&
                       !string.IsNullOrWhiteSpace(_generatedResponseBody);
            }
        }

        public string SelectedStartLocationMode
        {
            get { return _selectedStartLocationMode; }
            set
            {
                string normalized = string.Equals(value, StartLocationModeByPoint, StringComparison.OrdinalIgnoreCase)
                    ? StartLocationModeByPoint
                    : StartLocationModeByLift;
                if (!Set(ref _selectedStartLocationMode, normalized))
                {
                    return;
                }

                if (!_isUpdatingOptions && IsByLift && IsStartPointDefined)
                {
                    _ = RoomRecognitionPaneRuntime.RequestClearDeliveryRouteStartPointMarkerAsync();
                    ClearStartPointState();
                }

                OnPropertyChanged(nameof(IsByLift));
                OnPropertyChanged(nameof(IsByStartPoint));
                OnPropertyChanged(nameof(IsStartLocationDefined));
                OnPropertyChanged(nameof(IsNoLiftsWarningVisible));
                OnPropertyChanged(nameof(LiftStartControlsVisibility));
                OnPropertyChanged(nameof(PointStartControlsVisibility));
                OnPropertyChanged(nameof(PointSetButtonVisibility));
                OnPropertyChanged(nameof(PointSummaryVisibility));
                OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
            }
        }

        public bool IsByLift
        {
            get { return !string.Equals(SelectedStartLocationMode, StartLocationModeByPoint, StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsByStartPoint
        {
            get { return string.Equals(SelectedStartLocationMode, StartLocationModeByPoint, StringComparison.OrdinalIgnoreCase); }
        }

        public Visibility LiftStartControlsVisibility
        {
            get { return IsByLift ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility PointStartControlsVisibility
        {
            get { return IsByStartPoint ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility PointSetButtonVisibility
        {
            get { return IsByStartPoint && !IsStartPointDefined ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility PointSummaryVisibility
        {
            get { return IsByStartPoint && IsStartPointDefined ? Visibility.Visible : Visibility.Collapsed; }
        }

        public bool IsStartPointDefined
        {
            get { return _isStartPointDefined; }
            private set
            {
                if (Set(ref _isStartPointDefined, value))
                {
                    OnPropertyChanged(nameof(IsStartLocationDefined));
                    OnPropertyChanged(nameof(PointSetButtonVisibility));
                    OnPropertyChanged(nameof(PointSummaryVisibility));
                    OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
                }
            }
        }

        public bool IsStartLocationDefined
        {
            get { return IsByStartPoint ? IsStartPointDefined : IsStartLiftDefined; }
        }

        public string StartPointName
        {
            get { return string.IsNullOrWhiteSpace(_startPointName) ? "Start Point" : _startPointName; }
        }

        public bool IsStartLiftDefined
        {
            get { return _isStartLiftDefined; }
            private set
            {
                if (Set(ref _isStartLiftDefined, value))
                {
                    OnPropertyChanged(nameof(IsStartLocationDefined));
                }
            }
        }

        public bool IsTargetRoomDefined
        {
            get { return _isTargetRoomDefined; }
            private set { Set(ref _isTargetRoomDefined, value); }
        }

        public bool IsEditorVisible
        {
            get { return _isEditorVisible; }
            set { Set(ref _isEditorVisible, value); }
        }

        public bool IsOverviewVisible
        {
            get { return _isOverviewVisible; }
            set { Set(ref _isOverviewVisible, value); }
        }

        public bool IsEmptyOverviewVisible
        {
            get { return SavedRoutes.Count == 0; }
        }

        public bool IsCompareMode
        {
            get { return _isCompareMode; }
            set
            {
                if (Set(ref _isCompareMode, value))
                {
                    OnPropertyChanged(nameof(NormalOverviewActionsVisibility));
                    OnPropertyChanged(nameof(CompareOverviewActionsVisibility));
                    OnPropertyChanged(nameof(CompareRoutesDisplayedVisibility));
                    RefreshDeliveryRouteCompareState();
                }
            }
        }

        public Visibility NormalOverviewActionsVisibility
        {
            get { return IsCompareMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility CompareOverviewActionsVisibility
        {
            get { return IsCompareMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string DoneCompareButtonText
        {
            get
            {
                return _selectedCompareRouteIds.Count > 0
                    ? "Done (" + _selectedCompareRouteIds.Count.ToString(CultureInfo.InvariantCulture) + ")"
                    : "Done";
            }
        }

        public string CompareRoutesDisplayedText
        {
            get
            {
                return "Compare routes displayed: " +
                       _compareRoutesDisplayedCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        public Visibility CompareRoutesDisplayedVisibility
        {
            get
            {
                return !IsCompareMode && _compareRoutesDisplayedCount > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public bool IsGenerating
        {
            get { return _isGenerating; }
            set
            {
                if (Set(ref _isGenerating, value))
                {
                    OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
                    OnPropertyChanged(nameof(CanSaveRoute));
                }
            }
        }

        public bool IsResultVisible
        {
            get { return _isResultVisible; }
            set
            {
                if (Set(ref _isResultVisible, value))
                {
                    RefreshResultStateProperties();
                }
            }
        }

        public EditorLiftOptionViewModel SelectedStartLift
        {
            get { return _selectedStartLift; }
            set
            {
                if (Set(ref _selectedStartLift, value))
                {
                    if (!_isUpdatingOptions)
                    {
                        IsStartLiftDefined = false;
                        FocusSelectedStartLift();
                    }

                    OnPropertyChanged(nameof(IsStartLocationDefined));
                    OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
                }
            }
        }

        public EditorRoomOptionViewModel SelectedTargetRoom
        {
            get { return _selectedTargetRoom; }
            set
            {
                if (Set(ref _selectedTargetRoom, value))
                {
                    if (!_isUpdatingOptions)
                    {
                        IsTargetRoomDefined = false;
                    }

                    OnTargetRoomChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SetOptions(
            IEnumerable<EditorLiftOptionViewModel> lifts,
            IEnumerable<EditorRoomOptionViewModel> rooms)
        {
            string liftKey = SelectedStartLift != null ? SelectedStartLift.Key : string.Empty;
            string roomKey = SelectedTargetRoom != null ? SelectedTargetRoom.Key : string.Empty;
            List<EditorLiftOptionViewModel> liftList = (lifts ?? Enumerable.Empty<EditorLiftOptionViewModel>()).ToList();
            List<EditorRoomOptionViewModel> roomList = (rooms ?? Enumerable.Empty<EditorRoomOptionViewModel>()).ToList();

            _isUpdatingOptions = true;
            try
            {
                StartLiftOptions.Clear();
                StartLiftOptions.Add(new EditorLiftOptionViewModel { Key = string.Empty, DisplayName = "Select", LiftKind = string.Empty });
                foreach (EditorLiftOptionViewModel lift in liftList)
                {
                    StartLiftOptions.Add(lift);
                }

                TargetRoomOptions.Clear();
                TargetRoomOptions.Add(new EditorRoomOptionViewModel { Key = string.Empty, DisplayName = "Select Room", RoomName = "Select Room" });
                foreach (EditorRoomOptionViewModel room in roomList)
                {
                    TargetRoomOptions.Add(room);
                }

                SelectedStartLift = StartLiftOptions.FirstOrDefault(x => string.Equals(x.Key, liftKey, StringComparison.OrdinalIgnoreCase)) ??
                                    StartLiftOptions.FirstOrDefault();
                SelectedTargetRoom = TargetRoomOptions.FirstOrDefault(x => string.Equals(x.Key, roomKey, StringComparison.OrdinalIgnoreCase)) ??
                                     TargetRoomOptions.FirstOrDefault();
            }
            finally
            {
                _isUpdatingOptions = false;
            }

            RefreshSelectedEquipmentState(false);
        }


        public void RefreshSavedRoutes()
        {
            IList<DeliveryRouteRecordDto> records = RoomRecognitionPaneRuntime.GetDeliveryRouteRecordsSnapshot();
            SavedRoutes.Clear();

            int index = 1;
            IEnumerable<DeliveryRouteRecordDto> orderedRoutes =
                (records ?? new List<DeliveryRouteRecordDto>())
                    .Where(x => x != null)
                    .OrderBy(x => ParseRouteCreatedAt(x.CreatedAt))
                    .ThenBy(x => x.RouteName ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (DeliveryRouteRecordDto route in orderedRoutes)
            {
                if (route == null)
                {
                    continue;
                }

                SavedRoutes.Add(BuildRouteCard(route, index));
                index++;
            }

            _selectedCompareRouteIds.RemoveAll(routeId =>
                !SavedRoutes.Any(card =>
                    card != null &&
                    card.IsCompareEligible &&
                    string.Equals(card.RouteId, routeId, StringComparison.OrdinalIgnoreCase)));

            OnPropertyChanged(nameof(IsEmptyOverviewVisible));
            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshDeliveryRouteCompareState();
        }

        private DeliveryRouteCardViewModel BuildRouteCard(DeliveryRouteRecordDto route, int displayIndex)
        {
            int moduleCount = route.SubModules != null ? route.SubModules.Count : 0;
            string routeLength = route.PathLengthMeters.HasValue
                ? route.PathLengthMeters.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : NormalizeRouteLengthValue(route.RouteLengthText);

            return new DeliveryRouteCardViewModel
            {
                RouteId = route.RouteId ?? string.Empty,
                DisplayTitle = BuildOverviewRouteTitle(route.RouteName, displayIndex),
                CreatedAtText = FormatCreatedAt(route.CreatedAt),
                StartLiftName = ResolveStartLocationDisplayName(route),
                TargetRoomName = string.IsNullOrWhiteSpace(route.TargetRoomName) ? "-" : route.TargetRoomName,
                EquipmentDisplayName = string.IsNullOrWhiteSpace(route.EquipmentDisplayName) ? "-" : route.EquipmentDisplayName,
                ModulesText = moduleCount > 0
                    ? moduleCount.ToString(CultureInfo.InvariantCulture) + " pc"
                    : "-",
                MaxDimsText = string.IsNullOrWhiteSpace(route.MaxDimsText) ? "-" : route.MaxDimsText,
                RouteLengthValue = string.IsNullOrWhiteSpace(routeLength) ? "-" : routeLength,
                StatusText = route.IsSuccess ? "Valid" : "Failed",
                StatusForeground = route.IsSuccess
                    ? new SolidColorBrush(Color.FromRgb(28, 180, 74))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                ModuleSummaryVisibility = moduleCount > 0 || !string.IsNullOrWhiteSpace(route.MaxDimsText)
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                RouteLengthVisibility = route.PathLengthMeters.HasValue ||
                                        (!string.IsNullOrWhiteSpace(route.RouteLengthText) && route.RouteLengthText != "-")
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                DetailCommand = DetailRouteCommand,
                ExportReportCommand = ExportReportCommand,
                DeleteCommand = DeleteRouteCommand,
                CompareCommand = ToggleRouteCompareCommand,
                IsCompareEligible = route.IsSuccess && !string.IsNullOrWhiteSpace(route.ResponseBody),
                Record = route
            };
        }

        private static string BuildOverviewRouteTitle(string routeName, int displayIndex)
        {
            string value = (routeName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                System.Text.RegularExpressions.Match routeMatch = System.Text.RegularExpressions.Regex.Match(
                    value,
                    "^Route\\s+(\\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (routeMatch.Success)
                {
                    int number;
                    if (int.TryParse(routeMatch.Groups[1].Value, out number))
                    {
                        return "Delivery Route " + number.ToString("00", CultureInfo.InvariantCulture);
                    }
                }

                if (value.StartsWith("Delivery Route", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }

                return value;
            }

            return "Delivery Route " + displayIndex.ToString("00", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseRouteCreatedAt(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, out parsed) ? parsed : DateTime.MaxValue;
        }

        private static string FormatCreatedAt(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return value ?? string.Empty;
        }

        private static string NormalizeRouteLengthValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            if (normalized.EndsWith(" m", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 2).Trim();
            }
            else if (normalized.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            }

            return normalized;
        }

        private void EnterCompareMode()
        {
            HintText = string.Empty;
            _selectedCompareRouteIds.Clear();
            OnPropertyChanged(nameof(DoneCompareButtonText));
            IsCompareMode = true;
        }

        private void CancelCompareMode()
        {
            HintText = string.Empty;
            _selectedCompareRouteIds.Clear();
            IsCompareMode = false;
            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshDeliveryRouteCompareState();
        }

        private async void FinishCompareMode()
        {
            List<string> selectedIds = _selectedCompareRouteIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            IsCompareMode = false;
            RefreshDeliveryRouteCompareState();

            if (selectedIds.Count == 0)
            {
                HintText = string.Empty;
                return;
            }

            try
            {
                HintText = "Drawing route comparison...";
                CalculatePathExecutionResult result =
                    await RoomRecognitionPaneRuntime.RequestDrawDeliveryRouteComparisonAsync(selectedIds);

                if (result == null || !result.Success || !result.Drawn)
                {
                    SetCompareRoutesDisplayedCount(0);
                    HintText = result != null && !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "Failed to draw route comparison.";
                }
                else
                {
                    SetCompareRoutesDisplayedCount(selectedIds.Count);
                    HintText = string.Empty;
                }
            }
            catch (Exception ex)
            {
                SetCompareRoutesDisplayedCount(0);
                HintText = ex.Message;
            }
            finally
            {
                _selectedCompareRouteIds.Clear();
                OnPropertyChanged(nameof(DoneCompareButtonText));
                RefreshDeliveryRouteCompareState();
            }
        }

        private async void ClearCompareRoutes()
        {
            try
            {
                bool cleared = await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
                if (!cleared)
                {
                    HintText = "Failed to clear route comparison paths.";
                    return;
                }

                SetCompareRoutesDisplayedCount(0);
                HintText = string.Empty;
            }
            catch (Exception ex)
            {
                HintText = ex.Message;
            }
        }

        private void ToggleRouteCompare(DeliveryRouteCardViewModel card)
        {
            if (!IsCompareMode || card == null || string.IsNullOrWhiteSpace(card.RouteId))
            {
                return;
            }

            if (!card.IsCompareEligible)
            {
                HintText = "Only valid saved routes can be compared.";
                return;
            }

            string existing = _selectedCompareRouteIds.FirstOrDefault(x =>
                string.Equals(x, card.RouteId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(existing))
            {
                _selectedCompareRouteIds.Remove(existing);
            }
            else
            {
                if (_selectedCompareRouteIds.Count >= 3)
                {
                    HintText = "You can select up to 3 routes to compare.";
                    return;
                }

                _selectedCompareRouteIds.Add(card.RouteId);
                HintText = string.Empty;
            }

            OnPropertyChanged(nameof(DoneCompareButtonText));
            RefreshDeliveryRouteCompareState();
        }

        private void RefreshDeliveryRouteCompareState()
        {
            foreach (DeliveryRouteCardViewModel card in SavedRoutes)
            {
                if (card == null)
                {
                    continue;
                }

                card.IsCompareMode = IsCompareMode;
                card.IsCompareSelected = _selectedCompareRouteIds.Any(x =>
                    string.Equals(x, card.RouteId, StringComparison.OrdinalIgnoreCase));
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

        private async void OpenSavedRoute(DeliveryRouteCardViewModel card)
        {
            DeliveryRouteRecordDto route = card != null ? card.Record : null;
            if (route == null)
            {
                return;
            }

            await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            SetCompareRoutesDisplayedCount(0);

            _currentRouteId = route.RouteId ?? string.Empty;
            RouteName = route.RouteName ?? string.Empty;
            HintText = string.Empty;
            IsStartLiftDefined = false;
            IsStartPointDefined = false;
            IsTargetRoomDefined = false;

            _isUpdatingOptions = true;
            try
            {
                SelectedStartLocationMode = IsPointRoute(route)
                    ? StartLocationModeByPoint
                    : StartLocationModeByLift;

                SelectedStartLift = StartLiftOptions.FirstOrDefault(x =>
                    x != null && string.Equals(x.Key, route.StartLiftKey, StringComparison.OrdinalIgnoreCase)) ??
                    StartLiftOptions.FirstOrDefault();

                SelectedTargetRoom = TargetRoomOptions.FirstOrDefault(x =>
                    x != null && string.Equals(x.Key, route.TargetRoomKey, StringComparison.OrdinalIgnoreCase)) ??
                    TargetRoomOptions.FirstOrDefault();
            }
            finally
            {
                _isUpdatingOptions = false;
            }

            if (IsPointRoute(route) && route.StartPointXmm.HasValue && route.StartPointYmm.HasValue)
            {
                _startPointName = route.StartPointName ?? string.Empty;
                _startPointXmm = route.StartPointXmm;
                _startPointYmm = route.StartPointYmm;
                _startPointZmm = route.StartPointZmm;
                IsStartPointDefined = true;
                OnPropertyChanged(nameof(StartPointName));
            }
            else
            {
                ClearStartPointState();
            }

            _selectedEquipmentInfo = new DeliveryRouteEquipmentInfo
            {
                Found = route.OriginalModelId >= 1,
                RoomKey = route.TargetRoomKey ?? string.Empty,
                FamilyKey = route.EquipmentFamilyKey ?? string.Empty,
                OriginalModelId = route.OriginalModelId,
                DisplayName = route.EquipmentDisplayName ?? string.Empty,
                AirflowM3s = route.AirflowM3s
            };
            RefreshRouteAvailabilityProperties();

            SubModuleRows.Clear();
            foreach (DeliveryRouteSubModuleDto row in route.SubModules ?? new List<DeliveryRouteSubModuleDto>())
            {
                if (row == null)
                {
                    continue;
                }

                SubModuleRows.Add(new DeliveryRouteSubModuleRowViewModel
                {
                    Sequence = row.Sequence,
                    SubModule = row.SubModule ?? string.Empty,
                    Type = row.Type ?? string.Empty,
                    DimensionsMm = row.DimensionsMm ?? string.Empty
                });
            }

            _generatedRouteSuccess = route.IsSuccess;
            _generatedResponseBody = route.ResponseBody ?? string.Empty;
            _generatedPathLengthMeters = route.PathLengthMeters;
            _generatedApiMessage = route.ApiMessage ?? string.Empty;
            ResultTitle = !string.IsNullOrWhiteSpace(route.ResultTitle)
                ? route.ResultTitle
                : (route.IsSuccess ? "Route Passed" : "Route Failed");
            ResultMessage = route.ResultMessage ?? string.Empty;
            FailureReasonText = route.FailureReasonText ?? string.Empty;
            RouteLengthText = !string.IsNullOrWhiteSpace(route.RouteLengthText)
                ? route.RouteLengthText
                : FormatRouteLength(route.PathLengthMeters);
            IsResultVisible = true;
            RefreshResultStateProperties();

            IsOverviewVisible = false;
            IsEditorVisible = true;

            if (route.IsSuccess && !string.IsNullOrWhiteSpace(route.ResponseBody))
            {
                try
                {
                    await RoomRecognitionPaneRuntime.RequestDrawDeliveryRoutePathAsync(route.ResponseBody);
                }
                catch
                {
                    // Detail remains available even when the saved path cannot be redrawn.
                }
            }
        }

        private async void ExportSavedRoute(DeliveryRouteCardViewModel card)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.RouteId))
            {
                return;
            }

            HintText = "Preparing delivery route report...";
            bool exported = await RoomRecognitionPaneRuntime.RequestExportDeliveryRouteAsync(card.RouteId);
            HintText = exported ? string.Empty : "Failed to export delivery route report.";
        }

        private async void DeleteSavedRoute(DeliveryRouteCardViewModel card)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.RouteId))
            {
                return;
            }

            DeliveryRouteDeleteConfirmWindow dialog =
                new DeliveryRouteDeleteConfirmWindow(card.DisplayTitle);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            bool deleted = await RoomRecognitionPaneRuntime.RequestDeleteDeliveryRouteAsync(card.RouteId);
            if (!deleted)
            {
                HintText = "Failed to delete delivery route.";
                return;
            }

            await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            SetCompareRoutesDisplayedCount(0);
            HintText = string.Empty;
            RefreshSavedRoutes();
        }

        private int GetNextRouteNumber()
        {
            int max = 0;
            foreach (DeliveryRouteCardViewModel card in SavedRoutes)
            {
                string routeName = card?.Record?.RouteName ?? string.Empty;
                System.Text.RegularExpressions.Match match =
                    System.Text.RegularExpressions.Regex.Match(
                        routeName,
                        "^Route\\s+(\\d+)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int value;
                if (match.Success && int.TryParse(match.Groups[1].Value, out value))
                {
                    max = Math.Max(max, value);
                }
            }

            return max + 1;
        }

        private void OpenEditor()
        {
            _selectedCompareRouteIds.Clear();
            IsCompareMode = false;
            OnPropertyChanged(nameof(DoneCompareButtonText));
            _currentRouteId = Guid.NewGuid().ToString("N");
            RouteName = "Route " + GetNextRouteNumber().ToString(CultureInfo.InvariantCulture);
            HintText = string.Empty;
            ClearResult();

            // A new route must always start from a clean selection state.
            // Do not carry Start Lift / Target Room values over from the
            // previously edited or generated route.
            _isUpdatingOptions = true;
            try
            {
                SelectedStartLocationMode = StartLocationModeByLift;
                SelectedStartLift = StartLiftOptions.FirstOrDefault(x =>
                    x != null && string.IsNullOrWhiteSpace(x.Key)) ??
                    StartLiftOptions.FirstOrDefault();

                SelectedTargetRoom = TargetRoomOptions.FirstOrDefault(x =>
                    x != null && string.IsNullOrWhiteSpace(x.Key)) ??
                    TargetRoomOptions.FirstOrDefault();
            }
            finally
            {
                _isUpdatingOptions = false;
            }

            IsStartLiftDefined = false;
            ClearStartPointState();
            IsTargetRoomDefined = false;
            _ = RoomRecognitionPaneRuntime.RequestClearDeliveryRouteStartPointMarkerAsync();
            SetSelectedEquipmentInfo(null);
            RefreshRouteAvailabilityProperties();

            IsOverviewVisible = false;
            IsEditorVisible = true;
        }

        private async void DefineStartLift()
        {
            if (SelectedStartLift == null || string.IsNullOrWhiteSpace(SelectedStartLift.Key))
            {
                HintText = "Please select a start lift.";
                IsStartLiftDefined = false;
                return;
            }

            bool focused = await RoomRecognitionPaneRuntime.RequestFocusLiftPreserveViewAsync(SelectedStartLift.Key);
            IsStartLiftDefined = focused;
            HintText = focused ? string.Empty : "Unable to locate the selected start lift.";
        }

        private async void SetStartLocation()
        {
            if (!IsByStartPoint)
            {
                return;
            }

            bool started = await RoomRecognitionPaneRuntime.RequestBeginDeliveryRouteStartPointSelectionAsync(
                _startPointName,
                _startPointXmm,
                _startPointYmm,
                _startPointZmm);
            HintText = started ? string.Empty : "Unable to start location selection.";
        }

        private async void DefineStartLocation()
        {
            // The start point has already been picked and named.  This command
            // must NOT re-enter the Pick Start Point workflow.  It simply
            // re-displays/focuses the saved coordinate, matching the intent of
            // "Define Start Lift" for an already selected lift.
            if (!IsByStartPoint ||
                !IsStartPointDefined ||
                !_startPointXmm.HasValue ||
                !_startPointYmm.HasValue)
            {
                HintText = "Please set a start location first.";
                return;
            }

            bool focused = await RoomRecognitionPaneRuntime.RequestFocusDeliveryRouteStartPointAsync(
                _startPointXmm.Value,
                _startPointYmm.Value,
                _startPointZmm ?? 0.0);

            HintText = focused ? string.Empty : "Unable to locate the saved start point.";
        }

        private async void RemoveStartLocation()
        {
            await RoomRecognitionPaneRuntime.RequestClearDeliveryRouteStartPointMarkerAsync();
            ClearStartPointState();
            HintText = string.Empty;
        }

        public void ApplySelectedStartPoint(string name, double xmm, double ymm, double zmm)
        {
            _startPointName = string.IsNullOrWhiteSpace(name) ? GetDefaultStartPointName() : name.Trim();
            _startPointXmm = xmm;
            _startPointYmm = ymm;
            _startPointZmm = zmm;
            IsStartPointDefined = true;
            OnPropertyChanged(nameof(StartPointName));
            OnPropertyChanged(nameof(IsStartLocationDefined));
            OnPropertyChanged(nameof(PointSetButtonVisibility));
            OnPropertyChanged(nameof(PointSummaryVisibility));
            OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
            HintText = string.Empty;
        }

        public string GetDefaultStartPointName()
        {
            int next = 1;
            foreach (DeliveryRouteRecordDto route in RoomRecognitionPaneRuntime.GetDeliveryRouteRecordsSnapshot() ?? new List<DeliveryRouteRecordDto>())
            {
                string value = route != null ? route.StartPointName ?? string.Empty : string.Empty;
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                    value,
                    "^Start\\s+Point\\s+(\\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int number;
                if (match.Success && int.TryParse(match.Groups[1].Value, out number))
                {
                    next = Math.Max(next, number + 1);
                }
            }

            return "Start Point " + next.ToString("00", CultureInfo.InvariantCulture);
        }

        private void ClearStartPointState()
        {
            _startPointName = string.Empty;
            _startPointXmm = null;
            _startPointYmm = null;
            _startPointZmm = null;
            IsStartPointDefined = false;
            OnPropertyChanged(nameof(StartPointName));
            OnPropertyChanged(nameof(IsStartLocationDefined));
            OnPropertyChanged(nameof(PointSetButtonVisibility));
            OnPropertyChanged(nameof(PointSummaryVisibility));
            OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
        }

        private async void DefineTargetRoom()
        {
            if (SelectedTargetRoom == null || string.IsNullOrWhiteSpace(SelectedTargetRoom.Key))
            {
                HintText = "Please select a target room.";
                IsTargetRoomDefined = false;
                return;
            }

            bool focused = await RoomRecognitionPaneRuntime.RequestFocusRoomAsync(SelectedTargetRoom.Key);
            IsTargetRoomDefined = focused;
            HintText = focused ? string.Empty : "Unable to locate the selected target room.";
        }

        private async void FocusSelectedStartLift()
        {
            if (SelectedStartLift == null || string.IsNullOrWhiteSpace(SelectedStartLift.Key))
            {
                return;
            }

            try
            {
                await RoomRecognitionPaneRuntime.RequestFocusLiftPreserveViewAsync(SelectedStartLift.Key);
            }
            catch
            {
                // Selection is still valid even if the active Revit view cannot be focused.
            }
        }

        private async void OnTargetRoomChanged()
        {
            bool userSelection = !_isUpdatingOptions;

            if (userSelection &&
                SelectedTargetRoom != null &&
                !string.IsNullOrWhiteSpace(SelectedTargetRoom.Key))
            {
                try
                {
                    await RoomRecognitionPaneRuntime.RequestFocusRoomAsync(SelectedTargetRoom.Key);
                }
                catch
                {
                    // Keep the selection and equipment state even if focusing fails.
                }
            }

            RefreshSelectedEquipmentState(userSelection);
        }

        private void RefreshSelectedEquipmentState(bool showDebugDialog)
        {
            if (SelectedTargetRoom == null || string.IsNullOrWhiteSpace(SelectedTargetRoom.Key))
            {
                SetSelectedEquipmentInfo(null);
                RefreshRouteAvailabilityProperties();
                return;
            }

            DeliveryRouteEquipmentInfo info = RoomRecognitionPaneRuntime.GetDeliveryRouteEquipmentInfo(SelectedTargetRoom.Key);
            SetSelectedEquipmentInfo(info != null && info.Found ? info : null);
            RefreshRouteAvailabilityProperties();

            if (showDebugDialog && info != null && info.Found)
            {
                //ShowEquipmentDebugDialog(info);
            }
        }

        private void SetSelectedEquipmentInfo(DeliveryRouteEquipmentInfo info)
        {
            _selectedEquipmentInfo = info;
            RefreshRouteAvailabilityProperties();
        }

        private void RefreshRouteAvailabilityProperties()
        {
            OnPropertyChanged(nameof(HasDetectedLifts));
            OnPropertyChanged(nameof(IsNoLiftsWarningVisible));
            OnPropertyChanged(nameof(HasDetectedRooms));
            OnPropertyChanged(nameof(IsNoRoomsWarningVisible));
            OnPropertyChanged(nameof(IsNoEquipmentWarningVisible));
            OnPropertyChanged(nameof(HasCommittedEquipment));
            OnPropertyChanged(nameof(SelectedOriginalModelId));
            OnPropertyChanged(nameof(SelectedEquipmentFamilyKey));
            OnPropertyChanged(nameof(SelectedEquipmentDisplayName));
            OnPropertyChanged(nameof(SelectedEquipmentInfoText));
            OnPropertyChanged(nameof(CanGenerateDeliveryRoute));
            OnPropertyChanged(nameof(CanSaveRoute));
        }

        private static void ShowEquipmentDebugDialog(DeliveryRouteEquipmentInfo info)
        {
            if (info == null || !info.Found)
            {
                return;
            }

            TaskDialog.Show("Delivery Route Equipment", BuildEquipmentInfoText(info));
        }

        private static string BuildEquipmentInfoText(DeliveryRouteEquipmentInfo info)
        {
            if (info == null || !info.Found)
            {
                return string.Empty;
            }

            return "Target Room:" + Environment.NewLine +
                   (info.RoomKey ?? string.Empty) + Environment.NewLine +
                   Environment.NewLine +
                   "Equipment:" + Environment.NewLine +
                   (info.DisplayName ?? string.Empty) + Environment.NewLine +
                   Environment.NewLine +
                   "Family Key:" + Environment.NewLine +
                   (info.FamilyKey ?? string.Empty) + Environment.NewLine +
                   Environment.NewLine +
                   "Original Model ID:" + Environment.NewLine +
                   info.OriginalModelId.ToString() + Environment.NewLine +
                   Environment.NewLine +
                   "Revit Element ID:" + Environment.NewLine +
                   info.RevitElementId.ToString() + Environment.NewLine +
                   Environment.NewLine +
                   "Dimensions:" + Environment.NewLine +
                   "L" + info.TotalLengthMm.ToString() +
                   " x W" + info.WidthMm.ToString() +
                   " x H" + info.HeightMm.ToString() +
                   " mm";
        }

        private async void GenerateDeliveryRoute()
        {
            if (IsGenerating)
            {
                return;
            }

            if (!HasValidStartLocation)
            {
                HintText = IsByStartPoint
                    ? "Please set a start location."
                    : "Please select a start lift.";
                return;
            }

            if (SelectedTargetRoom == null || string.IsNullOrWhiteSpace(SelectedTargetRoom.Key))
            {
                HintText = "Please select a target room.";
                return;
            }

            DeliveryRouteEquipmentInfo equipmentInfo = RoomRecognitionPaneRuntime.GetDeliveryRouteEquipmentInfo(SelectedTargetRoom.Key);
            SetSelectedEquipmentInfo(equipmentInfo != null && equipmentInfo.Found ? equipmentInfo : null);
            if (!HasCommittedEquipment || SelectedOriginalModelId < 1 || SelectedOriginalModelId > 10)
            {
                HintText = "No committed equipment found in this room.";
                return;
            }

            DeliveryRouteConfirmWindow confirmDialog = new DeliveryRouteConfirmWindow(
                CurrentStartLocationDisplayName,
                SelectedTargetRoom.RoomName);
            if (confirmDialog.ShowDialog() != true)
            {
                return;
            }

            ClearResult();
            IsGenerating = true;
            HintText = "Preparing route planner...";
            DeliveryRouteLoadingWindow loadingWindow = null;

            try
            {
                loadingWindow = new DeliveryRouteLoadingWindow();
                loadingWindow.Show();

                DeliveryRoutePreparationResult preparation =
                    await RoomRecognitionPaneRuntime.RequestPrepareDeliveryRouteAsync(
                        IsByStartPoint ? "Point" : "Lift",
                        SelectedStartLift != null ? SelectedStartLift.Key : string.Empty,
                        _startPointXmm,
                        _startPointYmm,
                        _startPointZmm,
                        SelectedTargetRoom.Key);
                if (preparation == null || !preparation.Success)
                {
                    ShowFailure(preparation != null ? preparation.Message : null);
                    return;
                }

                string requestJson = CalculatePathApiService.BuildCutAndReplanRequestJson(
                    preparation.SessionId,
                    equipmentInfo.OriginalModelId,
                    preparation.StartXmm,
                    preparation.StartYmm,
                    preparation.GoalXmm,
                    preparation.GoalYmm,
                    preparation.RestrictedAreas);

                HintText = "Generating delivery route...";
                string responseBody = await Task.Run(() => CalculatePathApiService.PostCutAndReplan(requestJson));
                CalculatePathExecutionResult result =
                    await RoomRecognitionPaneRuntime.RequestDrawDeliveryRoutePathAsync(responseBody);

                if (result != null && result.Success && result.Drawn)
                {
                    ApplySubModules(equipmentInfo);
                    _generatedRouteSuccess = true;
                    _generatedResponseBody = !string.IsNullOrWhiteSpace(result.ResponseBody) ? result.ResponseBody : responseBody;
                    _generatedPathLengthMeters = result.PathLengthMeters;
                    _generatedApiMessage = result.Message ?? string.Empty;
                    ResultTitle = "Route Passed";
                    ResultMessage = "AI logistics planning completed successfully from " +
                                    CurrentStartLocationDisplayName +
                                    " to " +
                                    SelectedTargetRoom.RoomName +
                                    ".";
                    RouteLengthText = FormatRouteLength(result.PathLengthMeters);
                    FailureReasonText = string.Empty;
                    IsResultVisible = true;
                    RefreshResultStateProperties();
                    HintText = result.PathLengthMeters.HasValue
                        ? "Delivery route generated. Length: " + result.PathLengthMeters.Value.ToString("0.##") + " m"
                        : "Delivery route generated.";
                    return;
                }

                if (result != null && IsBusinessFailureResponse(result.ResponseBody))
                {
                    ApplySubModules(equipmentInfo);
                    _generatedRouteSuccess = false;
                    _generatedResponseBody = !string.IsNullOrWhiteSpace(result.ResponseBody) ? result.ResponseBody : responseBody;
                    _generatedPathLengthMeters = result.PathLengthMeters;
                    _generatedApiMessage = result.Message ?? string.Empty;
                    ShowBusinessFailure(result.Message, result.PathLengthMeters);
                }
                else
                {
                    ShowTechnicalFailure(result != null ? result.Message : null);
                }
            }
            catch (Exception ex)
            {
                string responseMessage = CalculatePathApiService.ExtractResponseMessage(ex.Message);
                ShowTechnicalFailure(!string.IsNullOrWhiteSpace(responseMessage) ? responseMessage : ex.Message);
            }
            finally
            {
                IsGenerating = false;
                if (loadingWindow != null)
                {
                    loadingWindow.Close();
                }
            }
        }

        private void ShowFailure(string message)
        {
            ShowTechnicalFailure(message);
        }

        private void ShowBusinessFailure(string message, double? pathLengthMeters)
        {
            string reason = !string.IsNullOrWhiteSpace(message) ? message : "Dimension Exceeded";
            ResultTitle = "Route Failed: " + reason;
            ResultMessage =
                "A valid pathway exists, but the equipment exceeds space clearance even at maximum disassembly.";
            FailureReasonText = "Failure Reason:" + Environment.NewLine +
                                "Equipment collides with building walls/columns.";
            RouteLengthText = FormatRouteLength(pathLengthMeters);
            IsResultVisible = true;
            HintText = "Delivery route failed.";
            RefreshResultStateProperties();
        }

        private void ShowTechnicalFailure(string message)
        {
            _generatedResponseBody = string.Empty;
            _generatedPathLengthMeters = null;
            _generatedApiMessage = message ?? string.Empty;
            _generatedRouteSuccess = false;
            ResultTitle = "Route Failed";
            ResultMessage = !string.IsNullOrWhiteSpace(message)
                ? message
                : "Failed to generate delivery route.";
            FailureReasonText = string.Empty;
            RouteLengthText = "-";
            IsResultVisible = true;
            HintText = "Failed to generate delivery route.";
            RefreshResultStateProperties();
        }

        private void ClearResult()
        {
            _generatedResponseBody = string.Empty;
            _generatedPathLengthMeters = null;
            _generatedApiMessage = string.Empty;
            _generatedRouteSuccess = false;
            ResultMessage = string.Empty;
            FailureReasonText = string.Empty;
            RouteLengthText = "-";
            SubModuleRows.Clear();
            IsResultVisible = false;
            RefreshResultStateProperties();
        }

        private async void CancelEditor()
        {
            await RoomRecognitionPaneRuntime.RequestCancelDeliveryRouteStartPointSelectionAsync();
            await RoomRecognitionPaneRuntime.RequestClearDeliveryRouteStartPointMarkerAsync();
            await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            ResetEditorToOverview();
        }

        private async void SaveRoute()
        {
            if (!CanSaveRoute)
            {
                HintText = "Please generate a delivery route before saving.";
                return;
            }

            DeliveryRouteSaveConfirmWindow dialog = new DeliveryRouteSaveConfirmWindow(
                RouteName,
                CurrentStartLocationDisplayName,
                SelectedTargetRoom != null ? SelectedTargetRoom.RoomName : string.Empty,
                _generatedRouteSuccess);

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            DeliveryRouteRecordDto route = BuildRouteRecord();
            bool saved = await RoomRecognitionPaneRuntime.RequestSaveDeliveryRouteAsync(route);
            if (!saved)
            {
                HintText = "Failed to save delivery route.";
                return;
            }

            bool cleared = await RoomRecognitionPaneRuntime.RequestClearDeliveryRoutePathAsync();
            if (!cleared)
            {
                HintText = "Delivery route was saved, but the preview path could not be cleared.";
                return;
            }

            ResetEditorToOverview();
        }

        private DeliveryRouteRecordDto BuildRouteRecord()
        {
            DeliveryRouteEquipmentInfo equipment = _selectedEquipmentInfo;
            return new DeliveryRouteRecordDto
            {
                RouteId = string.IsNullOrWhiteSpace(_currentRouteId) ? Guid.NewGuid().ToString("N") : _currentRouteId,
                RouteName = RouteName ?? string.Empty,
                StartLiftKey = IsByLift && SelectedStartLift != null ? SelectedStartLift.Key ?? string.Empty : string.Empty,
                StartLiftName = IsByLift && SelectedStartLift != null ? SelectedStartLift.DisplayName ?? string.Empty : string.Empty,
                StartLocationType = IsByStartPoint ? "Point" : "Lift",
                StartPointName = IsByStartPoint ? _startPointName ?? string.Empty : string.Empty,
                StartPointXmm = IsByStartPoint ? _startPointXmm : null,
                StartPointYmm = IsByStartPoint ? _startPointYmm : null,
                StartPointZmm = IsByStartPoint ? _startPointZmm : null,
                TargetRoomKey = SelectedTargetRoom != null ? SelectedTargetRoom.Key ?? string.Empty : string.Empty,
                TargetRoomName = SelectedTargetRoom != null ? SelectedTargetRoom.RoomName ?? string.Empty : string.Empty,
                EquipmentFamilyKey = equipment != null ? equipment.FamilyKey ?? string.Empty : string.Empty,
                EquipmentDisplayName = equipment != null ? equipment.DisplayName ?? string.Empty : string.Empty,
                OriginalModelId = equipment != null ? equipment.OriginalModelId : 0,
                AirflowM3s = equipment != null ? equipment.AirflowM3s : 0,
                IsSuccess = _generatedRouteSuccess,
                StatusText = _generatedRouteSuccess ? "Passed" : "Failed",
                ApiMessage = _generatedApiMessage ?? string.Empty,
                ResultTitle = ResultTitle ?? string.Empty,
                ResultMessage = ResultMessage ?? string.Empty,
                FailureReasonText = FailureReasonText ?? string.Empty,
                ResponseBody = _generatedResponseBody ?? string.Empty,
                PathLengthMeters = _generatedPathLengthMeters,
                RouteLengthText = RouteLengthText ?? string.Empty,
                DisassemblyText = DisassemblyText ?? string.Empty,
                MaxDimsText = MaxDimsText ?? string.Empty,
                SubModules = SubModuleRows.Select(x => new DeliveryRouteSubModuleDto
                {
                    Sequence = x.Sequence,
                    SubModule = x.SubModule ?? string.Empty,
                    Type = x.Type ?? string.Empty,
                    DimensionsMm = x.DimensionsMm ?? string.Empty
                }).ToList()
            };
        }

        private void ResetEditorToOverview()
        {
            ClearResult();
            SetCompareRoutesDisplayedCount(0);
            HintText = string.Empty;
            _currentRouteId = string.Empty;
            IsStartLiftDefined = false;
            ClearStartPointState();
            IsTargetRoomDefined = false;
            IsEditorVisible = false;
            IsOverviewVisible = true;
            RefreshSavedRoutes();
        }

        private string CurrentStartLocationDisplayName
        {
            get
            {
                if (IsByStartPoint)
                {
                    return string.IsNullOrWhiteSpace(_startPointName) ? "Start Point" : _startPointName;
                }

                return SelectedStartLift != null ? SelectedStartLift.DisplayName ?? string.Empty : string.Empty;
            }
        }

        private static bool IsPointRoute(DeliveryRouteRecordDto route)
        {
            return route != null &&
                   string.Equals(route.StartLocationType, "Point", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveStartLocationDisplayName(DeliveryRouteRecordDto route)
        {
            if (route == null)
            {
                return "-";
            }

            if (IsPointRoute(route))
            {
                return string.IsNullOrWhiteSpace(route.StartPointName) ? "Start Point" : route.StartPointName;
            }

            return string.IsNullOrWhiteSpace(route.StartLiftName) ? "-" : route.StartLiftName;
        }

        private void ApplySubModules(DeliveryRouteEquipmentInfo equipmentInfo)
        {
            SubModuleRows.Clear();
            int modelId = equipmentInfo != null ? equipmentInfo.OriginalModelId : 0;
            foreach (AhuSubModuleScheduleRow row in AhuSubModuleScheduleService.Build(modelId))
            {
                int sequence;
                if (!int.TryParse(row.Sequence, out sequence))
                {
                    sequence = SubModuleRows.Count + 1;
                }

                SubModuleRows.Add(new DeliveryRouteSubModuleRowViewModel
                {
                    Sequence = sequence,
                    SubModule = row.SubModule ?? string.Empty,
                    Type = row.Type ?? string.Empty,
                    DimensionsMm = row.DimensionsMm ?? string.Empty
                });
            }

            RefreshResultStateProperties();
        }

        private void RefreshResultStateProperties()
        {
            OnPropertyChanged(nameof(IsPassedResultVisible));
            OnPropertyChanged(nameof(IsFailedResultVisible));
            OnPropertyChanged(nameof(HasSubModuleRows));
            OnPropertyChanged(nameof(DisassemblyText));
            OnPropertyChanged(nameof(MaxDimsText));
            OnPropertyChanged(nameof(CanSaveRoute));
        }

        private static bool IsBusinessFailureResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return false;
            }

            return responseBody.IndexOf("\"success\":false", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   responseBody.IndexOf("\"success\": false", StringComparison.OrdinalIgnoreCase) >= 0;
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
            return parts.Length >= 3
                ? "L:" + parts[0] + " x W:" + parts[1] + " x H:" + parts[2]
                : dimensionsMm.Trim();
        }

        private static string FormatRouteLength(double? meters)
        {
            if (!meters.HasValue || meters.Value <= 0)
            {
                return "-";
            }

            return Math.Abs(meters.Value - Math.Round(meters.Value)) < 0.005
                ? Math.Round(meters.Value).ToString("0") + " m"
                : meters.Value.ToString("0.##") + " m";
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
    }

    public sealed class DeliveryRouteSubModuleRowViewModel
    {
        public int Sequence { get; set; }

        public string SubModule { get; set; }

        public string Type { get; set; }

        public string DimensionsMm { get; set; }
    }

    public sealed class DeliveryRouteCardViewModel : INotifyPropertyChanged
    {
        private bool _isCompareMode;
        private bool _isCompareSelected;

        public string RouteId { get; set; }

        public string DisplayTitle { get; set; }

        public string CreatedAtText { get; set; }

        public string StartLiftName { get; set; }

        public string TargetRoomName { get; set; }

        public string EquipmentDisplayName { get; set; }

        public string ModulesText { get; set; }

        public string MaxDimsText { get; set; }

        public string RouteLengthValue { get; set; }

        public string StatusText { get; set; }

        public Brush StatusForeground { get; set; }

        public Visibility ModuleSummaryVisibility { get; set; }

        public Visibility RouteLengthVisibility { get; set; }

        public ICommand DetailCommand { get; set; }

        public ICommand ExportReportCommand { get; set; }

        public ICommand DeleteCommand { get; set; }

        public ICommand CompareCommand { get; set; }

        public bool IsCompareEligible { get; set; }

        public bool IsCompareMode
        {
            get { return _isCompareMode; }
            set
            {
                if (_isCompareMode == value)
                {
                    return;
                }

                _isCompareMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompareCheckBoxVisibility));
                OnPropertyChanged(nameof(ActionButtonsVisibility));
                OnPropertyChanged(nameof(CompareBorderBrush));
                OnPropertyChanged(nameof(CompareCardBackground));
            }
        }

        public bool IsCompareSelected
        {
            get { return _isCompareSelected; }
            set
            {
                if (_isCompareSelected == value)
                {
                    OnPropertyChanged();
                    return;
                }

                _isCompareSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompareBorderBrush));
                OnPropertyChanged(nameof(CompareCardBackground));
            }
        }

        public Visibility CompareCheckBoxVisibility
        {
            get { return IsCompareMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ActionButtonsVisibility
        {
            get { return IsCompareMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Brush CompareBorderBrush
        {
            get
            {
                return IsCompareSelected
                    ? new SolidColorBrush(Color.FromRgb(43, 131, 234))
                    : new SolidColorBrush(Color.FromRgb(216, 224, 233));
            }
        }

        public Brush CompareCardBackground
        {
            get
            {
                return IsCompareSelected
                    ? new SolidColorBrush(Color.FromRgb(244, 250, 255))
                    : Brushes.White;
            }
        }

        public DeliveryRouteRecordDto Record { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
