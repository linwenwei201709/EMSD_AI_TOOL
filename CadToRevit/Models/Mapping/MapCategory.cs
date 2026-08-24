namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// 映射类别：用于决定某个 CAD 图层按哪类 Revit 元素处理。
    /// </summary>
    public enum MapCategory
    {
        /// <summary>按墙识别与创建流程处理。</summary>
        Walls = 0,
        /// <summary>按结构柱识别与放置流程处理。</summary>
        Columns = 1,
        /// <summary>按门识别与放置流程处理。</summary>
        Doors = 2,
        /// <summary>按窗识别与放置流程处理。</summary>
        Windows = 3,
        /// <summary>按楼板流程处理（保留扩展）。</summary>
        Floors = 4,
        /// <summary>按天花流程处理（保留扩展）。</summary>
        Ceilings = 5,
        /// <summary>按结构梁流程处理。</summary>
        Beams = 6,
        // Keep layer row but exclude it from model creation pipeline.
        Ignore = 7,
        /// <summary>图层未匹配 EMSD CAD 标准，保留在列表中但不参与生成。</summary>
        Unknown = 8,
        // Visible in Layer List, but not selected for BIM creation by default.
        NotForBuild = 9
    }
}
