using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 门识别统计结果。
    /// 包含规则命中数量、匹配数量、宽度分布与日志文件路径。
    /// </summary>
    public class DoorDetectResult
    {
        /// <summary>识别得到的门候选集合。</summary>
        public List<DoorCandidate> Candidates { get; set; } = new List<DoorCandidate>();

        /// <summary>门图层线段总数。</summary>
        public int DoorSegmentsTotal { get; set; }

        /// <summary>规则 R1 命中数量。</summary>
        public int Rule1Count { get; set; }

        /// <summary>规则 R2 命中数量。</summary>
        public int Rule2Count { get; set; }

        /// <summary>规则 R3 命中数量。</summary>
        public int Rule3Count { get; set; }

        /// <summary>候选合并后数量。</summary>
        public int MergedCandidateCount { get; set; }

        /// <summary>成功匹配宿主墙数量。</summary>
        public int MatchedCount { get; set; }

        /// <summary>未匹配宿主墙数量。</summary>
        public int UnmatchedCount { get; set; }

        /// <summary>门宽位于 650~800mm 的数量。</summary>
        public int WidthRange650To800 { get; set; }

        /// <summary>门宽位于 800~1000mm 的数量。</summary>
        public int WidthRange800To1000 { get; set; }

        /// <summary>门宽位于 1000~1200mm 的数量。</summary>
        public int WidthRange1000To1200 { get; set; }

        /// <summary>检测到的弧线段总数。</summary>
        public int ArcSegmentsTotal { get; set; }

        /// <summary>位于门图层上的弧线数量。</summary>
        public int ArcCountOnDoorLayer { get; set; }

        /// <summary>当前启用的规则列表。</summary>
        public List<string> EnabledRules { get; set; } = new List<string>();

        /// <summary>JSON 诊断日志路径。</summary>
        public string JsonLogPath { get; set; }

        /// <summary>CSV 诊断日志路径。</summary>
        public string CsvLogPath { get; set; }
    }
}
