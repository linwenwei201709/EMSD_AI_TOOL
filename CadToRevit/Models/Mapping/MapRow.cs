using Autodesk.Revit.DB;

namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// 映射表单中的一行配置。
    /// 一行描述“某个 CAD 原始图层”如何映射到“某类 Revit 元素”。
    /// </summary>
    public sealed class MapRow
    {
        /// <summary>
        /// CAD 原始图层名（如 E-B-CORE）。
        /// 该字段是识别时筛选线段的主键。
        /// </summary>
        public string RawLayerName { get; set; }

        /// <summary>
        /// 该图层要创建的元素类别（墙/门/窗/柱等）。
        /// </summary>
        public MapCategory Category { get; set; } = MapCategory.Walls;

        /// <summary>
        /// 目标 Revit 类型 Id（可为空，通常由名称解析后使用）。
        /// </summary>
        public ElementId RevitTypeId { get; set; }

        /// <summary>
        /// 目标 Revit 类型显示名（例如“族名 : 类型名”）。
        /// </summary>
        public string RevitTypeName { get; set; }

        /// <summary>
        /// 期望宽度（毫米）。
        /// 主要用于门/窗/墙识别中的宽度约束与评分。
        /// </summary>
        public double? ExpectedWidthMm { get; set; }

        /// <summary>
        /// 行级高级设置（覆盖识别参数、垂直参数、Revit 参数映射）。
        /// </summary>
        public AdvancedSettingsRow Settings { get; set; } = new AdvancedSettingsRow();
    }
}
