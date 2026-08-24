namespace CadToRevit.Models
{
    /// <summary>
    /// 窗创建参数（单位：mm/deg）。
    /// </summary>
    public class WindowCreateSettings
    {
        /// <summary>窗宽下限。</summary>
        public double MinWindowWidthMm { get; set; } = 600.0;

        /// <summary>窗宽上限。</summary>
        public double MaxWindowWidthMm { get; set; } = 2400.0;

        /// <summary>微小线段容差，小于该值可视为噪声。</summary>
        public double TinySegmentTolMm { get; set; } = 10.0;

        /// <summary>窗候选合并容差。</summary>
        public double MergeTolMm { get; set; } = 50.0;

        /// <summary>角度判定容差。</summary>
        public double AngleTolDeg { get; set; } = 5.0;

        /// <summary>窗候选到宿主墙最大匹配距离。</summary>
        public double WindowMatchMaxDistMm { get; set; } = 2000.0;

        /// <summary>默认窗台高度。</summary>
        public double DefaultSillHeightMm { get; set; } = 900.0;
    }
}
