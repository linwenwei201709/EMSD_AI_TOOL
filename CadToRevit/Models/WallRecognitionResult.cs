using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 墙识别总结果。
    /// 汇总双线/单线识别结果与拓扑修复统计，用于创建与日志输出。
    /// </summary>
    public class WallRecognitionResult
    {
        /// <summary>最终可用于创建墙的中心线集合。</summary>
        public List<WallCenterlineCandidate> Centerlines { get; set; } = new List<WallCenterlineCandidate>();

        /// <summary>输入参与识别的墙线段总数。</summary>
        public int TotalWallSegments { get; set; }

        /// <summary>A 类：双线墙识别数量。</summary>
        public int TypeADoubleLineWalls { get; set; }

        /// <summary>B 类：单线墙补识别数量。</summary>
        public int TypeBSingleLineWalls { get; set; }

        /// <summary>C 类：多段折线墙识别数量（预留统计位）。</summary>
        public int TypeCPolylineWalls { get; set; }

        /// <summary>D 类：弧墙识别数量（预留统计位）。</summary>
        public int TypeDArcWalls { get; set; }

        /// <summary>中心线合并后的数量。</summary>
        public int MergedWalls { get; set; }

        /// <summary>拓扑修复后最终数量。</summary>
        public int RefinedWalls { get; set; }

        /// <summary>去重删除数量。</summary>
        public int DuplicateRemovedCount { get; set; }

        /// <summary>端点延伸次数。</summary>
        public int ExtendedEndpointCount { get; set; }

        /// <summary>端点聚类移动次数。</summary>
        public int ClusteredEndpointCount { get; set; }

        /// <summary>正交吸附调整次数。</summary>
        public int OffAxisSnappedCount { get; set; }

        /// <summary>共线合并次数。</summary>
        public int CollinearMergedCount { get; set; }
    }
}
