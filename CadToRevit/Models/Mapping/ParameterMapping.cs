namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// 单个 Revit 参数映射项。
    /// 用于把界面配置的值写入到创建后的 Revit 元素参数。
    /// </summary>
    public sealed class ParameterMapping
    {
        /// <summary>
        /// 目标参数名（LookupParameter 使用的名称）。
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// 参数存储类型（String/Double/Integer/ElementId）。
        /// </summary>
        public string StorageType { get; set; }

        /// <summary>
        /// 要写入的值。
        /// 运行时会按 StorageType 做类型转换后写入。
        /// </summary>
        public object Value { get; set; }
    }
}
