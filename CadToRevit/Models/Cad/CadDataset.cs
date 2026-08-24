using System;
using System.Collections.Generic;

namespace CadToRevit.Models.Cad
{
    /// <summary>
    /// CAD 数据集模型。
    /// 保存从 ImportInstance 解析后的线段集合与按图层索引。
    /// </summary>
    public sealed class CadDataset
    {
        /// <summary>
        /// 全量线段列表（已归一化为插件内部线段类型）。
        /// </summary>
        public List<Services.CadSegment> Segments { get; set; } = new List<Services.CadSegment>();

        /// <summary>
        /// 按原始图层分组的线段索引。
        /// Key 为 RawLayerName，Value 为该图层线段列表。
        /// </summary>
        public Dictionary<string, List<Services.CadSegment>> SegmentsByRawLayer { get; set; }
            = new Dictionary<string, List<Services.CadSegment>>(StringComparer.OrdinalIgnoreCase);

        public List<CadText> Texts { get; set; } = new List<CadText>();

        public Dictionary<string, List<CadText>> TextsByRawLayer { get; set; }
            = new Dictionary<string, List<CadText>>(StringComparer.OrdinalIgnoreCase);
    }
}
