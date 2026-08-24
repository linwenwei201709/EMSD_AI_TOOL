namespace CadToRevit.Models
{
    /// <summary>
    /// 垂直参数设置（单位：mm）。
    /// 控制墙/门/窗创建时的高度、标高偏移及优先策略。
    /// </summary>
    public sealed class VerticalDimensionSettings
    {
        /// <summary>墙高度（未连接高度）。</summary>
        public double WallHeightMm { get; set; } = 4000.0;

        /// <summary>墙底部偏移。</summary>
        public double WallBaseOffsetMm { get; set; } = 0.0;

        /// <summary>门高度。</summary>
        public double DoorHeightMm { get; set; } = 2100.0;

        /// <summary>门槛高度（Sill Height）。</summary>
        public double DoorSillHeightMm { get; set; } = 0.0;

        /// <summary>
        /// Legacy flag kept for backward compatibility only.
        /// Current mode does not use DoorHeadHeightMm.
        /// </summary>
        public bool PreferHeadHeightForDoor { get; set; } = false;

        /// <summary>
        /// Legacy field kept for backward compatibility only.
        /// Current mode does not write Head Height for doors.
        /// </summary>
        public double DoorHeadHeightMm { get; set; } = 0.0;

        /// <summary>窗高。</summary>
        public double WindowHeightMm { get; set; } = 1500.0;

        /// <summary>窗台高度。</summary>
        public double WindowSillHeightMm { get; set; } = 900.0;

        /// <summary>窗顶高度（Head Height）。</summary>
        public double WindowHeadHeightMm { get; set; } = 2400.0;

        /// <summary>
        /// 窗参数优先策略。
        /// true：优先按“窗台 + 窗高”推算；false：优先直接写窗顶高度。
        /// </summary>
        public bool PreferSillPlusHeight { get; set; } = true;
    }
}
