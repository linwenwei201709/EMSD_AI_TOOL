using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 墙检测阶段统计结果。
    /// 主要描述双线检测过程中的各过滤/通过数量。
    /// </summary>
    public class WallDetectResult
    {
        public WallDetectResult()
        {
            Centerlines = new List<WallCenterlineCandidate>();
        }

        /// <summary>检测得到的墙中心线候选集合。</summary>
        public List<WallCenterlineCandidate> Centerlines { get; private set; }

        /// <summary>输入墙线段总数。</summary>
        public int InputWallSegmentCount { get; set; }

        /// <summary>方向分组数量。</summary>
        public int DirectionGroupCount { get; set; }

        /// <summary>双线配对候选总数。</summary>
        public int PairCandidateCount { get; set; }

        /// <summary>通过平行过滤的候选数。</summary>
        public int PassedParallelCount { get; set; }

        /// <summary>通过厚度过滤的候选数。</summary>
        public int PassedThicknessCount { get; set; }

        /// <summary>通过重叠长度过滤的候选数。</summary>
        public int PassedOverlapCount { get; set; }

        /// <summary>未匹配为双线墙的线段数。</summary>
        public int UnmatchedWallSegmentCount { get; set; }
    }
}
