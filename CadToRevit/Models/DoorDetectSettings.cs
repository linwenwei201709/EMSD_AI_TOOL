namespace CadToRevit.Models
{
    /// <summary>
    /// 门识别参数（单位：mm/deg）。
    /// 控制门候选提取、配对合并与宿主墙匹配阈值。
    /// </summary>
    public class DoorDetectSettings
    {
        /// <summary>门宽下限。</summary>
        public double DoorWidthMinMm { get; set; } = 650.0;

        /// <summary>门宽上限。</summary>
        public double DoorWidthMaxMm { get; set; } = 1200.0;

        /// <summary>平行判定容差角度。</summary>
        public double ParallelAngleTolDeg { get; set; } = 5.0;

        /// <summary>最小重叠长度。</summary>
        public double OverlapMinMm { get; set; } = 100.0;

        /// <summary>候选到宿主墙最大匹配距离。</summary>
        public double WallMatchDistTolMm { get; set; } = 300.0;

        /// <summary>门候选中心合并距离容差。</summary>
        public double MergeCenterTolMm { get; set; } = 200.0;

        /// <summary>门宽合并容差。</summary>
        public double MergeWidthTolMm { get; set; } = 100.0;

        /// <summary>有效门线最小长度。</summary>
        public double SegmentLengthMinMm { get; set; } = 150.0;

        /// <summary>有效门线最大长度。</summary>
        public double SegmentLengthMaxMm { get; set; } = 1500.0;

        /// <summary>门弧最小扫角。</summary>
        public double ArcMinSweepDeg { get; set; } = 60.0;

        /// <summary>门弧最大扫角。</summary>
        public double ArcMaxSweepDeg { get; set; } = 140.0;

        /// <summary>门弧最小半径。</summary>
        public double ArcMinRadiusMm { get; set; } = 300.0;

        /// <summary>门弧最大半径。</summary>
        public double ArcMaxRadiusMm { get; set; } = 1200.0;

        /// <summary>门弧端点吸附容差。</summary>
        public double ArcEndpointSnapTolMm { get; set; } = 120.0;

        /// <summary>门弧配对线平行容差。</summary>
        public double ArcPairLineParallelTolDeg { get; set; } = 8.0;

        /// <summary>门弧推导门宽容差。</summary>
        public double ArcDoorWidthTolMm { get; set; } = 150.0;

        /// <summary>铰链点推断容差。</summary>
        public double ArcHingeTolMm { get; set; } = 180.0;

        /// <summary>门扇线最小长度。</summary>
        public double ArcLeafLineMinLengthMm { get; set; } = 500.0;

        /// <summary>门扇线最大长度。</summary>
        public double ArcLeafLineMaxLengthMm { get; set; } = 2000.0;

        /// <summary>门簇合并距离因子（相对于门宽）。</summary>
        public double DoorClusterTolFactor { get; set; } = 0.8;

        /// <summary>门簇合并距离下限。</summary>
        public double DoorClusterTolMinMm { get; set; } = 300.0;

        /// <summary>门簇合并距离上限。</summary>
        public double DoorClusterTolMaxMm { get; set; } = 600.0;

        /// <summary>
        /// 是否优先采用规则 R3（弧线规则）结果。
        /// </summary>
        public bool PreferR3OverOthers { get; set; } = true;

        // Enable pairing two single-leaf arc candidates into one double-door candidate.
        public bool EnableDoubleDoorRecognition { get; set; } = true;

        // Max spacing on the same wall for two leaves to be considered one double door.
        public double DoorPairSpacingMaxMm { get; set; } = 2200.0;

        // Enable compatibility arc-door rule for non-standard CAD door symbols.
        public bool EnableAltArcDoorRecognition { get; set; } = true;

        // Max distance from nearby line to arc endpoints/midpoint for compatibility rule.
        public double AltArcLineSnapTolMm { get; set; } = 180.0;

        // Max endpoint-to-support-line projection distance for compatibility rule.
        public double AltArcProjectionTolMm { get; set; } = 250.0;
    }
}
