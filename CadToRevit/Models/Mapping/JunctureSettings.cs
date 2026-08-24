namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// 连接修复阈值配置（单位：mm）。
    /// 用于控制哪些“端点间距/连接宽度”允许被自动修复。
    /// </summary>
    public sealed class JunctureSettings
    {
        /// <summary>
        /// 忽略小于该值的连接宽度。
        /// 常用于排除过小噪声连接。
        /// </summary>
        public double IgnoreSmallerThanMm { get; set; } = 0.0;

        /// <summary>
        /// 最小连接宽度阈值。
        /// 小于该值的连接不参与修复。
        /// </summary>
        public double MinJunctureWidthMm { get; set; } = 0.0;

        /// <summary>
        /// 忽略大于该值的连接宽度。
        /// 常用于避免把大跨度误当成连接缝。
        /// </summary>
        public double IgnoreLargerThanMm { get; set; } = 0.0;

        /// <summary>
        /// 最大连接宽度阈值。
        /// 大于该值的连接不参与修复。
        /// </summary>
        public double MaxJunctureWidthMm { get; set; } = 0.0;
    }
}
