using System.Collections.Generic;

namespace CadToRevit.Models.Mapping
{

    public sealed class AdvancedSettingsRow
    {
        // Placement mode constants for single-wall creation from double-line pairs.
        public const string WallPlaceModeCenterline = "Centerline";
        public const string WallPlaceModeInsideFaceOnCadLine = "InsideFaceOnCadLine";

        // Double-line centerline span policy constants: Overlap / LongerSide / Adaptive / Union.
        public const string WallDoubleLineLengthPolicyOverlap = "Overlap";
        public const string WallDoubleLineLengthPolicyLongerSide = "LongerSide";
        public const string WallDoubleLineLengthPolicyAdaptive = "Adaptive";
        public const string WallDoubleLineLengthPolicyUnion = "Union";

        public bool EnableLayerOverride { get; set; }

        public bool ApplyAsCategoryDefault { get; set; }

        public double? DoorExpectedWidthMm { get; set; }

        /// <summary>门宽下限（mm），小于该值不作为有效门洞。</summary>
        public double? MinDoorWidthMm { get; set; }

        /// <summary>门宽上限（mm），大于该值不作为有效门洞。</summary>
        public double? MaxDoorWidthMm { get; set; }

        /// <summary>门候选到宿主墙的匹配距离容差（mm）。</summary>
        public double? DoorWallMatchTolMm { get; set; }

        /// <summary>是否启用固定门宽兜底。</summary>
        public bool? UseFixedDoorWidth { get; set; }

        /// <summary>是否优先使用几何识别出的门洞宽度。</summary>
        public bool? PreferGeometryOpeningWidth { get; set; }

        public double? WallMinWallLengthMm { get; set; }

        public double? WallThicknessTolMm { get; set; }


        public double? WallMaxWallThicknessMm { get; set; }

        public double? WallDefaultSingleWallThicknessMm { get; set; }


        public double? WallParallelAngleTolDeg { get; set; }

        public double? WallEndpointMergeTolMm { get; set; }

        public double? WallArcThicknessTolMm { get; set; }

        public double? WallHeightMm { get; set; }

        public double? WallBaseOffsetMm { get; set; }

        public bool? WallEnableExtendCollinear { get; set; }

        public bool? WallEnableMergeCollinear { get; set; }

        public double? WallExtendCollinearTolMm { get; set; }

        public double? WallEndpointClusterTolMm { get; set; }

        public double? WallExtendSearchTolMm { get; set; }

        public double? WallDuplicateTolMm { get; set; }

        public double? WallAngleSnapDeg { get; set; }

        public bool? WallEnableOrthogonalSnap { get; set; }

        public bool? WallEnableExtendToIntersection { get; set; }

        public bool? WallEnableEndpointClustering { get; set; }

        public bool? WallEnableDuplicateRemoval { get; set; }

        public double? WallCollinearOffsetTolMm { get; set; }

        public double? WallExtendProjectionTolMm { get; set; }

        public bool? WallUseDirectionalClustering { get; set; }

        // Enable multi-thickness auto detection for double-line walls.
        public bool? WallEnableAutoDoubleLineThickness { get; set; }

        // Top K thickness peaks used in auto detection.
        public int? WallAutoThicknessTopK { get; set; }

        // Histogram bin size (mm) for thickness clustering.
        public double? WallAutoThicknessBinMm { get; set; }

        // Minimum thickness (mm) for double-line candidate scan.
        public double? WallMinDoubleLineThicknessMm { get; set; }

        // Minimum overlap length (mm) for double-line candidate scan.
        public double? WallMinDoubleLineOverlapLenMm { get; set; }

        // Force single-line wall recognition and skip all double-line detection/pairing.
        public bool? WallForceSingleLineMode { get; set; }

        // Placement strategy for double-line pairs when force single-line mode is enabled.
        public string WallDoubleLineSingleWallPlaceMode { get; set; }

        // Double-line centerline length policy: Overlap / LongerSide / Adaptive.
        public string WallDoubleLineLengthPolicy { get; set; }

        // Adaptive policy: containment tolerance in mm.
        public double? WallDoubleLineAdaptiveContainTolMm { get; set; }

        // Adaptive policy: max extension at each end in mm.
        public double? WallDoubleLineAdaptiveExtendMaxMm { get; set; }

        public double? DoorHeightMm { get; set; }

        // Door sill height in mm (instance sill offset from level).
        public double? DoorSillHeightMm { get; set; }

        public bool? DoorPreferHeadHeight { get; set; }

        public double? BeamMinLengthMm { get; set; }

        public double? BeamElevationOffsetMm { get; set; }

        public bool? BeamEnableMergeCollinear { get; set; }

        public double? BeamEndpointMergeTolMm { get; set; }

        public double? BeamParallelAngleTolDeg { get; set; }

        public bool? BeamAllowArc { get; set; }

        public double? WindowHeightMm { get; set; }

        public double? WindowSillHeightMm { get; set; }

        public bool? WindowUseSillPlusHeight { get; set; }
        /// <summary>柱生成高度（mm）。为空时使用全局墙高，仍为空则使用默认 4000mm。</summary>
        public double? ColumnHeightMm { get; set; }

        /// <summary>柱聚类算法（MidpointBFS / EndpointGraph）。</summary>
        public string ColumnClusterAlgorithm { get; set; }

        /// <summary>柱聚类距离（mm）。</summary>
        public double? ColumnClusterTolMm { get; set; }

        /// <summary>柱端点容差（mm）。</summary>
        public double? ColumnEndpointTolMm { get; set; }

        /// <summary>柱分段断裂容差（mm）。</summary>
        public double? ColumnGapTolMm { get; set; }

        /// <summary>柱候选最小线段数。</summary>
        public int? ColumnMinGroupSegments { get; set; }

        /// <summary>柱候选最小尺寸（mm）。</summary>
        public double? ColumnMinSizeMm { get; set; }

        /// <summary>柱候选最大尺寸（mm）。</summary>
        public double? ColumnMaxSizeMm { get; set; }

        /// <summary>柱候选最小面积（m2）。</summary>
        public double? ColumnMinAreaM2 { get; set; }

        /// <summary>柱候选最大长宽比。</summary>
        public double? ColumnMaxAspectRatio { get; set; }

        /// <summary>柱候选最小填充率。</summary>
        public double? ColumnMinFillRatio { get; set; }

        /// <summary>是否启用柱长线过滤。</summary>
        public bool? ColumnEnableLongLineFilter { get; set; }

        /// <summary>柱图元最大线长（mm）。</summary>
        public double? ColumnMaxSegmentLengthMm { get; set; }

        /// <summary>是否启用柱候选合并去重。</summary>
        public bool? ColumnEnableMerge { get; set; }

        /// <summary>柱候选合并距离（mm）。</summary>
        public double? ColumnMergeTolMm { get; set; }

        /// <summary>柱候选合并策略（KeepBest / UnionBbox / MaxArea）。</summary>
        public string ColumnMergeStrategy { get; set; }

        /// <summary>与已放置柱去重距离（mm）。</summary>
        public double? ColumnDedupePlacedTolMm { get; set; }

        /// <summary>柱评分面积权重。</summary>
        public double? ColumnAreaWeight { get; set; }

        /// <summary>柱评分线段数权重。</summary>
        public double? ColumnSegmentCountWeight { get; set; }

        /// <summary>柱评分矩形度权重。</summary>
        public double? ColumnRectnessWeight { get; set; }

        /// <summary>柱评分长线惩罚。</summary>
        public double? ColumnLongLinePenalty { get; set; }

        /// <summary>是否启用贴墙吸附。</summary>
        public bool? ColumnAttachToWallEnable { get; set; }

        /// <summary>柱贴墙吸附距离（mm）。</summary>
        public double? ColumnAttachToWallSnapTolMm { get; set; }

        /// <summary>柱贴墙目标（WallCenterline / WallFace）。</summary>
        public string ColumnAttachToWallTarget { get; set; }

        /// <summary>柱吸附后是否允许与墙重叠。</summary>
        public bool? ColumnAttachToWallAllowOverlap { get; set; }

        /// <summary>柱调试：显示候选。</summary>
        public bool? ColumnDebugDrawCandidates { get; set; }

        /// <summary>柱调试：显示编号。</summary>
        public bool? ColumnDebugDrawClusterId { get; set; }

        /// <summary>柱调试：显示剔除原因。</summary>
        public bool? ColumnDebugDrawRejectReason { get; set; }

        /// <summary>柱调试：导出报告。</summary>
        public bool? ColumnDebugExportReport { get; set; }

        /// <summary>是否启用异形柱识别。</summary>
        public bool? ColumnIrregularEnable { get; set; }

        /// <summary>异形柱最大尺寸（mm）。</summary>
        public double? ColumnIrregularMaxSizeMm { get; set; }

        /// <summary>异形柱缺口容差（mm）。</summary>
        public double? ColumnIrregularGapTolMm { get; set; }

        /// <summary>异形柱最小面积（m2）。</summary>
        public double? ColumnIrregularMinAreaM2 { get; set; }

        public JunctureSettings Juncture { get; set; } = new JunctureSettings();

        public List<ParameterMapping> ParameterMappings { get; set; } = new List<ParameterMapping>();
    }
}
