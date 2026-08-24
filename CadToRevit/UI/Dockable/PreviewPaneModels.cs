using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CadToRevit.UI.Dockable
{
    public enum PreviewPaneRequestType
    {
        None,
        RefreshState,
        LoadLayerMappings,
        CaptureAnalyzeSnapshot,
        SaveLayerMappings,
        TestExternalEvent,
        Preview,
        ClearPreview,
        CreateWalls,
        CreateDoors,
        CreateFloors,
        CreateGroundFloor,
        CreateElements,
        RegenerateAll,
        GenerateSingleLayer,
        DeleteSingleLayer,
        DeleteSelectedLayers,
        HighlightGeneratedElementsForSelectedLayer,
        ClearGeneratedElementHighlight,
        ToggleCadVisibility,
        ToggleBuildingElementsVisibility,
        ToggleGeneratedElementsVisibilityForLayer,
        DetachSelectedElements,
        RestoreSelectedBindings,
        UndoLastDetachBatch
    }

    public sealed class PreviewPaneResponse
    {
        public PreviewPaneState State { get; set; }

        public List<PreviewPaneLayerItem> LayerMappings { get; set; }

        public PreviewPaneAnalyzeSnapshot Snapshot { get; set; }

        public string Message { get; set; }

        public bool? LayerGeneratedElementsHidden { get; set; }

        public int DetachedElementCount { get; set; }

        public int RestoredElementCount { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class PreviewPaneState
    {
        public string DocumentTitle { get; set; }

        public string LevelName { get; set; }

        public bool IsCadVisible { get; set; } = true;

        public bool IsBuildingElementsVisible { get; set; } = true;

        public RoomRecognitionSettings RoomRecognitionSettings { get; set; } = RoomRecognitionSettings.CreateDefault();

        public GlobalGenerationSettings GlobalGenerationSettings { get; set; } = GlobalGenerationSettings.CreateDefault();
    }

    public sealed class PreviewPaneLayerItem : INotifyPropertyChanged
    {
        private string _rawLayerName;
        private bool _isSelected = true;
        private bool _isDirty;
        private bool _isGenerated;
        private bool _isGeneratedElementsHidden;
        private bool _isUiRowSelected;
        private bool _isLayerStandardInvalid;
        private MapCategory? _category;
        private string _familyTypeName;
        private bool _enableLayerOverride;
        private bool _applyAsCategoryDefault;
        private double? _doorExpectedWidthMm;
        private double? _minDoorWidthMm;
        private double? _maxDoorWidthMm;
        private double? _doorWallMatchTolMm;
        private double? _doorHeightMm;
        private double? _doorSillHeightMm;
        private bool? _useFixedDoorWidth;
        private bool? _preferGeometryOpeningWidth;
        private bool? _doorPreferHeadHeight;
        private double? _windowHeightMm;
        private double? _windowSillHeightMm;
        private bool? _windowUseSillPlusHeight;
        private double? _beamMinLengthMm;
        private double? _beamElevationOffsetMm;
        private double? _columnHeightMm;
        private string _columnClusterAlgorithm;
        private double? _columnClusterTolMm;
        private int? _columnMinGroupSegments;
        private double? _columnEndpointTolMm;
        private double? _columnGapTolMm;
        private double? _columnMinSizeMm;
        private double? _columnMaxSizeMm;
        private double? _columnMinAreaM2;
        private double? _columnMaxAspectRatio;
        private double? _columnMinFillRatio;
        private bool? _columnEnableLongLineFilter;
        private double? _columnMaxSegmentLengthMm;
        private bool? _columnEnableMerge;
        private double? _columnMergeTolMm;
        private string _columnMergeStrategy;
        private double? _columnDedupePlacedTolMm;
        private double? _columnAreaWeight;
        private double? _columnSegmentCountWeight;
        private double? _columnRectnessWeight;
        private double? _columnLongLinePenalty;
        private bool? _columnIrregularEnable;
        private double? _columnIrregularMaxSizeMm;
        private double? _columnIrregularGapTolMm;
        private double? _columnIrregularMinAreaM2;
        private bool? _columnAttachToWallEnable;
        private double? _columnAttachToWallSnapTolMm;
        private string _columnAttachToWallTarget;
        private bool? _columnAttachToWallAllowOverlap;
        private bool? _columnDebugDrawCandidates;
        private bool? _columnDebugDrawClusterId;
        private bool? _columnDebugDrawRejectReason;
        private bool? _columnDebugExportReport;
        private double? _minWallLengthMm;
        private double? _defaultSingleWallThicknessMm;
        private double? _wallHeightMm;
        private double? _wallBaseOffsetMm;
        private double? _wallThicknessTolMm;
        private double? _wallMaxWallThicknessMm;
        private double? _wallParallelAngleTolDeg;
        private double? _wallEndpointMergeTolMm;
        private double? _wallArcThicknessTolMm;
        private double? _wallEndpointClusterTolMm;
        private double? _wallExtendSearchTolMm;
        private double? _wallDuplicateTolMm;
        private double? _wallAngleSnapDeg;
        private double? _wallExtendCollinearTolMm;
        private double? _wallCollinearOffsetTolMm;
        private double? _wallExtendProjectionTolMm;
        private bool? _wallEnableOrthogonalSnap;
        private bool? _wallEnableExtendToIntersection;
        private bool? _wallEnableEndpointClustering;
        private bool? _wallEnableDuplicateRemoval;
        private bool? _wallEnableExtendCollinear;
        private bool? _wallEnableMergeCollinear;
        private bool? _wallUseDirectionalClustering;
        private bool? _wallEnableAutoDoubleLineThickness;
        private int? _wallAutoThicknessTopK;
        private double? _wallAutoThicknessBinMm;
        private double? _wallMinDoubleLineThicknessMm;
        private double? _wallMinDoubleLineOverlapLenMm;
        private string _wallDoubleLineSingleWallPlaceMode;
        private string _wallDoubleLineLengthPolicy;
        private double? _wallDoubleLineAdaptiveContainTolMm;
        private double? _wallDoubleLineAdaptiveExtendMaxMm;
        private bool? _wallForceSingleLineMode;

        public event PropertyChangedEventHandler PropertyChanged;

        public string RawLayerName
        {
            get { return _rawLayerName; }
            set { Set(ref _rawLayerName, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (Set(ref _isSelected, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGeneratableLayer)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionToolTip)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleDeleteActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLayerWarningIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowUnknownLayerIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNotForBuildLayerIcon)));
                }
            }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            set { Set(ref _isDirty, value); }
        }

        public bool IsGenerated
        {
            get { return _isGenerated; }
            set
            {
                if (Set(ref _isGenerated, value))
                {
                    if (!value)
                    {
                        IsGeneratedElementsHidden = false;
                    }

                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionToolTip)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleDeleteActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLayerVisibilityToggleVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHideGeneratedElementsIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowShowGeneratedElementsIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LayerVisibilityToggleToolTip)));
                }
            }
        }

        public bool IsGeneratedElementsHidden
        {
            get { return _isGeneratedElementsHidden; }
            set
            {
                if (Set(ref _isGeneratedElementsHidden, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHideGeneratedElementsIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowShowGeneratedElementsIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LayerVisibilityToggleToolTip)));
                }
            }
        }

        public bool IsUiRowSelected
        {
            get { return _isUiRowSelected; }
            set { Set(ref _isUiRowSelected, value); }
        }

        public bool IsLayerVisibilityToggleVisible
        {
            get { return IsGenerated; }
        }

        public bool ShowHideGeneratedElementsIcon
        {
            get { return IsGenerated && !IsGeneratedElementsHidden; }
        }

        public bool ShowShowGeneratedElementsIcon
        {
            get { return IsGenerated && IsGeneratedElementsHidden; }
        }

        public string LayerVisibilityToggleToolTip
        {
            get
            {
                return IsGeneratedElementsHidden
                    ? "Show generated elements in current view"
                    : "Hide generated elements in current view";
            }
        }

        public string GenerationActionText
        {
            get { return IsGenerated ? "Rebuild" : "Generate"; }
        }

        public string GenerationActionToolTip
        {
            get
            {
                return IsGenerated
                    ? "Rebuild the model for this layer. Warning: Manual modifications will be overwritten."
                    : "Generate the model for this specific layer.";
            }
        }

        public string DeleteSingleLayerToolTip
        {
            get { return "Delete the generated model for this layer."; }
        }

        public bool IsSingleLayerActionVisible
        {
            get { return true; }
        }

        public bool IsSingleDeleteActionVisible
        {
            get { return IsGenerated; }
        }

        public bool IsLayerActionVisible
        {
            get { return IsSingleLayerActionVisible; }
        }

        public bool IsLayerStandardInvalid
        {
            get { return _isLayerStandardInvalid; }
            set
            {
                if (Set(ref _isLayerStandardInvalid, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLayerWarningIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowUnknownLayerIcon)));
                }
            }
        }

        public bool ShowLayerWarningIcon
        {
            get
            {
                return ShowUnknownLayerIcon || ShowNotForBuildLayerIcon;
            }
        }

        public bool ShowUnknownLayerIcon
        {
            get
            {
                return Category.HasValue &&
                    IsLayerStandardInvalid &&
                    Category.Value == MapCategory.Unknown;
            }
        }

        public bool ShowNotForBuildLayerIcon
        {
            get
            {
                return Category.HasValue &&
                    Category.Value == MapCategory.NotForBuild;
            }
        }

        public bool IsGeneratableLayer
        {
            get
            {
                return IsSelected && Category.HasValue &&
                    Category.Value != MapCategory.Ignore &&
                    Category.Value != MapCategory.Unknown &&
                    Category.Value != MapCategory.NotForBuild;
            }
        }

        public MapCategory? Category
        {
            get { return _category; }
            set
            {
                if (Set(ref _category, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGeneratableLayer)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenerationActionToolTip)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleLayerActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSingleDeleteActionVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLayerWarningIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowUnknownLayerIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNotForBuildLayerIcon)));
                }
            }
        }

        public string FamilyTypeName
        {
            get { return _familyTypeName; }
            set { Set(ref _familyTypeName, value); }
        }

        public ObservableCollection<string> FamilyTypeOptions { get; } = new ObservableCollection<string>();

        public Dictionary<MapCategory, List<string>> FamilyTypeOptionsByCategory { get; } = new Dictionary<MapCategory, List<string>>();

        public bool EnableLayerOverride
        {
            get { return _enableLayerOverride; }
            set { Set(ref _enableLayerOverride, value); }
        }

        public bool ApplyAsCategoryDefault
        {
            get { return _applyAsCategoryDefault; }
            set { Set(ref _applyAsCategoryDefault, value); }
        }

        public double? DoorExpectedWidthMm
        {
            get { return _doorExpectedWidthMm; }
            set { Set(ref _doorExpectedWidthMm, value); }
        }

        public double? MinDoorWidthMm
        {
            get { return _minDoorWidthMm; }
            set { Set(ref _minDoorWidthMm, value); }
        }

        public double? MaxDoorWidthMm
        {
            get { return _maxDoorWidthMm; }
            set { Set(ref _maxDoorWidthMm, value); }
        }

        public double? DoorWallMatchTolMm
        {
            get { return _doorWallMatchTolMm; }
            set { Set(ref _doorWallMatchTolMm, value); }
        }

        public double? DoorHeightMm
        {
            get { return _doorHeightMm; }
            set { Set(ref _doorHeightMm, value); }
        }

        public double? DoorSillHeightMm
        {
            get { return _doorSillHeightMm; }
            set { Set(ref _doorSillHeightMm, value); }
        }

        public bool? UseFixedDoorWidth
        {
            get { return _useFixedDoorWidth; }
            set { Set(ref _useFixedDoorWidth, value); }
        }

        public bool? PreferGeometryOpeningWidth
        {
            get { return _preferGeometryOpeningWidth; }
            set { Set(ref _preferGeometryOpeningWidth, value); }
        }

        public bool? DoorPreferHeadHeight
        {
            get { return _doorPreferHeadHeight; }
            set { Set(ref _doorPreferHeadHeight, value); }
        }

        public double? WindowHeightMm
        {
            get { return _windowHeightMm; }
            set { Set(ref _windowHeightMm, value); }
        }

        public double? WindowSillHeightMm
        {
            get { return _windowSillHeightMm; }
            set { Set(ref _windowSillHeightMm, value); }
        }

        public bool? WindowUseSillPlusHeight
        {
            get { return _windowUseSillPlusHeight; }
            set { Set(ref _windowUseSillPlusHeight, value); }
        }

        public double? BeamMinLengthMm
        {
            get { return _beamMinLengthMm; }
            set { Set(ref _beamMinLengthMm, value); }
        }

        public double? BeamElevationOffsetMm
        {
            get { return _beamElevationOffsetMm; }
            set { Set(ref _beamElevationOffsetMm, value); }
        }

        public double? ColumnHeightMm
        {
            get { return _columnHeightMm; }
            set { Set(ref _columnHeightMm, value); }
        }

        public string ColumnClusterAlgorithm
        {
            get { return _columnClusterAlgorithm; }
            set { Set(ref _columnClusterAlgorithm, value); }
        }

        public double? ColumnClusterTolMm
        {
            get { return _columnClusterTolMm; }
            set { Set(ref _columnClusterTolMm, value); }
        }

        public int? ColumnMinGroupSegments
        {
            get { return _columnMinGroupSegments; }
            set { Set(ref _columnMinGroupSegments, value); }
        }

        public double? ColumnEndpointTolMm
        {
            get { return _columnEndpointTolMm; }
            set { Set(ref _columnEndpointTolMm, value); }
        }

        public double? ColumnGapTolMm
        {
            get { return _columnGapTolMm; }
            set { Set(ref _columnGapTolMm, value); }
        }

        public double? ColumnMinSizeMm
        {
            get { return _columnMinSizeMm; }
            set { Set(ref _columnMinSizeMm, value); }
        }

        public double? ColumnMaxSizeMm
        {
            get { return _columnMaxSizeMm; }
            set { Set(ref _columnMaxSizeMm, value); }
        }

        public double? ColumnMinAreaM2
        {
            get { return _columnMinAreaM2; }
            set { Set(ref _columnMinAreaM2, value); }
        }

        public double? ColumnMaxAspectRatio
        {
            get { return _columnMaxAspectRatio; }
            set { Set(ref _columnMaxAspectRatio, value); }
        }

        public double? ColumnMinFillRatio
        {
            get { return _columnMinFillRatio; }
            set { Set(ref _columnMinFillRatio, value); }
        }

        public bool? ColumnEnableLongLineFilter
        {
            get { return _columnEnableLongLineFilter; }
            set { Set(ref _columnEnableLongLineFilter, value); }
        }

        public double? ColumnMaxSegmentLengthMm
        {
            get { return _columnMaxSegmentLengthMm; }
            set { Set(ref _columnMaxSegmentLengthMm, value); }
        }

        public bool? ColumnEnableMerge
        {
            get { return _columnEnableMerge; }
            set { Set(ref _columnEnableMerge, value); }
        }

        public double? ColumnMergeTolMm
        {
            get { return _columnMergeTolMm; }
            set { Set(ref _columnMergeTolMm, value); }
        }

        public string ColumnMergeStrategy
        {
            get { return _columnMergeStrategy; }
            set { Set(ref _columnMergeStrategy, value); }
        }

        public double? ColumnDedupePlacedTolMm
        {
            get { return _columnDedupePlacedTolMm; }
            set { Set(ref _columnDedupePlacedTolMm, value); }
        }

        public double? ColumnAreaWeight
        {
            get { return _columnAreaWeight; }
            set { Set(ref _columnAreaWeight, value); }
        }

        public double? ColumnSegmentCountWeight
        {
            get { return _columnSegmentCountWeight; }
            set { Set(ref _columnSegmentCountWeight, value); }
        }

        public double? ColumnRectnessWeight
        {
            get { return _columnRectnessWeight; }
            set { Set(ref _columnRectnessWeight, value); }
        }

        public double? ColumnLongLinePenalty
        {
            get { return _columnLongLinePenalty; }
            set { Set(ref _columnLongLinePenalty, value); }
        }

        public bool? ColumnIrregularEnable
        {
            get { return _columnIrregularEnable; }
            set { Set(ref _columnIrregularEnable, value); }
        }

        public double? ColumnIrregularMaxSizeMm
        {
            get { return _columnIrregularMaxSizeMm; }
            set { Set(ref _columnIrregularMaxSizeMm, value); }
        }

        public double? ColumnIrregularGapTolMm
        {
            get { return _columnIrregularGapTolMm; }
            set { Set(ref _columnIrregularGapTolMm, value); }
        }

        public double? ColumnIrregularMinAreaM2
        {
            get { return _columnIrregularMinAreaM2; }
            set { Set(ref _columnIrregularMinAreaM2, value); }
        }

        public bool? ColumnAttachToWallEnable
        {
            get { return _columnAttachToWallEnable; }
            set { Set(ref _columnAttachToWallEnable, value); }
        }

        public double? ColumnAttachToWallSnapTolMm
        {
            get { return _columnAttachToWallSnapTolMm; }
            set { Set(ref _columnAttachToWallSnapTolMm, value); }
        }

        public string ColumnAttachToWallTarget
        {
            get { return _columnAttachToWallTarget; }
            set { Set(ref _columnAttachToWallTarget, value); }
        }

        public bool? ColumnAttachToWallAllowOverlap
        {
            get { return _columnAttachToWallAllowOverlap; }
            set { Set(ref _columnAttachToWallAllowOverlap, value); }
        }

        public bool? ColumnDebugDrawCandidates
        {
            get { return _columnDebugDrawCandidates; }
            set { Set(ref _columnDebugDrawCandidates, value); }
        }

        public bool? ColumnDebugDrawClusterId
        {
            get { return _columnDebugDrawClusterId; }
            set { Set(ref _columnDebugDrawClusterId, value); }
        }

        public bool? ColumnDebugDrawRejectReason
        {
            get { return _columnDebugDrawRejectReason; }
            set { Set(ref _columnDebugDrawRejectReason, value); }
        }

        public bool? ColumnDebugExportReport
        {
            get { return _columnDebugExportReport; }
            set { Set(ref _columnDebugExportReport, value); }
        }

        public double? MinWallLengthMm
        {
            get { return _minWallLengthMm; }
            set { Set(ref _minWallLengthMm, value); }
        }

        public double? DefaultSingleWallThicknessMm
        {
            get { return _defaultSingleWallThicknessMm; }
            set { Set(ref _defaultSingleWallThicknessMm, value); }
        }

        public double? WallHeightMm
        {
            get { return _wallHeightMm; }
            set { Set(ref _wallHeightMm, value); }
        }

        public double? WallBaseOffsetMm
        {
            get { return _wallBaseOffsetMm; }
            set { Set(ref _wallBaseOffsetMm, value); }
        }

        public double? WallThicknessTolMm
        {
            get { return _wallThicknessTolMm; }
            set { Set(ref _wallThicknessTolMm, value); }
        }

        public double? WallMaxWallThicknessMm
        {
            get { return _wallMaxWallThicknessMm; }
            set { Set(ref _wallMaxWallThicknessMm, value); }
        }

        public double? WallParallelAngleTolDeg
        {
            get { return _wallParallelAngleTolDeg; }
            set { Set(ref _wallParallelAngleTolDeg, value); }
        }

        public double? WallEndpointMergeTolMm
        {
            get { return _wallEndpointMergeTolMm; }
            set { Set(ref _wallEndpointMergeTolMm, value); }
        }

        public double? WallArcThicknessTolMm
        {
            get { return _wallArcThicknessTolMm; }
            set { Set(ref _wallArcThicknessTolMm, value); }
        }

        public double? WallEndpointClusterTolMm
        {
            get { return _wallEndpointClusterTolMm; }
            set { Set(ref _wallEndpointClusterTolMm, value); }
        }

        public double? WallExtendSearchTolMm
        {
            get { return _wallExtendSearchTolMm; }
            set { Set(ref _wallExtendSearchTolMm, value); }
        }

        public double? WallDuplicateTolMm
        {
            get { return _wallDuplicateTolMm; }
            set { Set(ref _wallDuplicateTolMm, value); }
        }

        public double? WallAngleSnapDeg
        {
            get { return _wallAngleSnapDeg; }
            set { Set(ref _wallAngleSnapDeg, value); }
        }

        public double? WallExtendCollinearTolMm
        {
            get { return _wallExtendCollinearTolMm; }
            set { Set(ref _wallExtendCollinearTolMm, value); }
        }

        public double? WallCollinearOffsetTolMm
        {
            get { return _wallCollinearOffsetTolMm; }
            set { Set(ref _wallCollinearOffsetTolMm, value); }
        }

        public double? WallExtendProjectionTolMm
        {
            get { return _wallExtendProjectionTolMm; }
            set { Set(ref _wallExtendProjectionTolMm, value); }
        }

        public bool? WallEnableOrthogonalSnap
        {
            get { return _wallEnableOrthogonalSnap; }
            set { Set(ref _wallEnableOrthogonalSnap, value); }
        }

        public bool? WallEnableExtendToIntersection
        {
            get { return _wallEnableExtendToIntersection; }
            set { Set(ref _wallEnableExtendToIntersection, value); }
        }

        public bool? WallEnableEndpointClustering
        {
            get { return _wallEnableEndpointClustering; }
            set { Set(ref _wallEnableEndpointClustering, value); }
        }

        public bool? WallEnableDuplicateRemoval
        {
            get { return _wallEnableDuplicateRemoval; }
            set { Set(ref _wallEnableDuplicateRemoval, value); }
        }

        public bool? WallEnableExtendCollinear
        {
            get { return _wallEnableExtendCollinear; }
            set { Set(ref _wallEnableExtendCollinear, value); }
        }

        public bool? WallEnableMergeCollinear
        {
            get { return _wallEnableMergeCollinear; }
            set { Set(ref _wallEnableMergeCollinear, value); }
        }

        public bool? WallUseDirectionalClustering
        {
            get { return _wallUseDirectionalClustering; }
            set { Set(ref _wallUseDirectionalClustering, value); }
        }

        public bool? WallEnableAutoDoubleLineThickness
        {
            get { return _wallEnableAutoDoubleLineThickness; }
            set { Set(ref _wallEnableAutoDoubleLineThickness, value); }
        }

        public int? WallAutoThicknessTopK
        {
            get { return _wallAutoThicknessTopK; }
            set { Set(ref _wallAutoThicknessTopK, value); }
        }

        public double? WallAutoThicknessBinMm
        {
            get { return _wallAutoThicknessBinMm; }
            set { Set(ref _wallAutoThicknessBinMm, value); }
        }

        public double? WallMinDoubleLineThicknessMm
        {
            get { return _wallMinDoubleLineThicknessMm; }
            set { Set(ref _wallMinDoubleLineThicknessMm, value); }
        }

        public double? WallMinDoubleLineOverlapLenMm
        {
            get { return _wallMinDoubleLineOverlapLenMm; }
            set { Set(ref _wallMinDoubleLineOverlapLenMm, value); }
        }

        public bool? WallForceSingleLineMode
        {
            get { return _wallForceSingleLineMode; }
            set { Set(ref _wallForceSingleLineMode, value); }
        }

        public string WallDoubleLineSingleWallPlaceMode
        {
            get { return _wallDoubleLineSingleWallPlaceMode; }
            set { Set(ref _wallDoubleLineSingleWallPlaceMode, value); }
        }

        public string WallDoubleLineLengthPolicy
        {
            get { return _wallDoubleLineLengthPolicy; }
            set { Set(ref _wallDoubleLineLengthPolicy, value); }
        }

        public double? WallDoubleLineAdaptiveContainTolMm
        {
            get { return _wallDoubleLineAdaptiveContainTolMm; }
            set { Set(ref _wallDoubleLineAdaptiveContainTolMm, value); }
        }

        public double? WallDoubleLineAdaptiveExtendMaxMm
        {
            get { return _wallDoubleLineAdaptiveExtendMaxMm; }
            set { Set(ref _wallDoubleLineAdaptiveExtendMaxMm, value); }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public sealed class PreviewPaneAnalyzeSnapshot
    {
        public string DwgName { get; set; }

        public string UnitText { get; set; }

        public int LayerCount { get; set; }

        public int SegmentCount { get; set; }

        public int ArcCount { get; set; }

        public int PolylineCount { get; set; }

        public List<string> RawLayerNames { get; set; } = new List<string>();

        public List<double> LengthsMm { get; set; } = new List<double>();
    }

    public sealed class PreviewPaneAnalyzeReport
    {
        public string DwgName { get; set; }

        public string UnitText { get; set; }

        public int LayerCount { get; set; }

        public int SegmentCount { get; set; }

        public int ArcCount { get; set; }

        public int PolylineCount { get; set; }

        public double P50LengthMm { get; set; }

        public double P90LengthMm { get; set; }

        public int PreviewWallCount { get; set; }

        public int PreviewDoorCount { get; set; }

        public List<string> Errors { get; set; } = new List<string>();
    }
}
