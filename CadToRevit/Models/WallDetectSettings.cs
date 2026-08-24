namespace CadToRevit.Models
{
    public enum WallDoubleLineLengthPolicy
    {
        Overlap = 0,
        LongerSide = 1,
        Adaptive = 2,
        Union = 3
    }

    /// <summary>
    /// 双线墙检测参数（内部单位：英尺，角度：度）。
    /// </summary>
    public class WallDetectSettings
    {
        /// <summary>目标墙厚（ft），用于筛选双线配对。</summary>
        public double TargetThicknessFt { get; set; } = 0.656168;

        /// <summary>墙厚容差（ft）。</summary>
        public double ThicknessTolFt { get; set; } = 0.032808;

        /// <summary>平行判定角度容差（deg）。</summary>
        public double ParallelAngleTolDeg { get; set; } = 2.0;

        /// <summary>最小重叠长度（ft），防止短接触误匹配。</summary>
        public double MinOverlapFt { get; set; } = 0.984252;

        /// <summary>双线墙长度策略。</summary>
        public WallDoubleLineLengthPolicy DoubleLineLengthPolicy { get; set; } = WallDoubleLineLengthPolicy.Union;

        /// <summary>Adaptive：包含关系容差（ft）。</summary>
        public double AdaptiveContainTolFt { get; set; } = 0.328084;

        /// <summary>Adaptive：端点最大延伸（ft）。</summary>
        public double AdaptiveExtendMaxFt { get; set; } = 1.968504;
    }
}
