namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// 可选参数元信息（供 UI 下拉选择与类型判断）。
    /// </summary>
    public sealed class ParameterOption
    {
        /// <summary>
        /// 参数显示名/查找名。
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// 参数存储类型字符串（对应 Revit StorageType）。
        /// </summary>
        public string StorageType { get; set; }

        /// <summary>
        /// 是否为“楼层类 ElementId 参数”。
        /// true 时 UI 会按楼层列表提供可选值。
        /// </summary>
        public bool IsLevelElementId { get; set; }
    }
}
