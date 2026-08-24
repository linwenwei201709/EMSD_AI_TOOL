namespace CadToRevit.Models.Units
{
    /// <summary>
    /// 单位解析上下文。
    /// 描述当前识别流程采用的源单位与缩放参数。
    /// </summary>
    public sealed class UnitContext
    {
        /// <summary>
        /// 最终采用的源单位类型。
        /// </summary>
        public SourceUnit SourceUnit { get; set; }

        /// <summary>
        /// 源单位到英尺的缩放系数。
        /// 所有几何会按该值转换到 Revit 内部单位体系。
        /// </summary>
        public double ScaleToFeet { get; set; }

        /// <summary>
        /// 自动识别单位的置信度（0~1）。
        /// 手动指定单位时该值仅作参考。
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// 单位判定依据说明（日志/调试用）。
        /// </summary>
        public string Evidence { get; set; }
    }
}
