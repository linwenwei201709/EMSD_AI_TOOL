using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 门创建阶段结果统计。
    /// </summary>
    public class DoorCreateResult
    {
        /// <summary>参与创建流程的门候选数量。</summary>
        public int DoorCandidates { get; set; }

        /// <summary>成功创建门实例数量。</summary>
        public int CreatedDoors { get; set; }

        /// <summary>跳过创建数量。</summary>
        public int SkippedDoors { get; set; }

        /// <summary>门高度参数写入成功数量。</summary>
        public int HeightSetSuccessCount { get; set; }

        /// <summary>门高度参数写入失败数量。</summary>
        public int HeightSetFailedCount { get; set; }

        /// <summary>门宽参数写入成功数量。</summary>
        public int WidthSetSuccessCount { get; set; }

        /// <summary>门宽参数写入失败数量。</summary>
        public int WidthSetFailedCount { get; set; }

        /// <summary>用于创建的门族类型名称。</summary>
        public string DoorSymbolName { get; set; }

        /// <summary>跳过原因列表（用于定位失败原因）。</summary>
        public List<string> SkipReasons { get; set; } = new List<string>();

        /// <summary>Created door instance ids.</summary>
        public List<int> CreatedElementIds { get; set; } = new List<int>();

        /// <summary>Created auxiliary patch-wall ids for dedicated no-wall door pipelines.</summary>
        public List<int> CreatedAuxWallElementIds { get; set; } = new List<int>();
    }
}
