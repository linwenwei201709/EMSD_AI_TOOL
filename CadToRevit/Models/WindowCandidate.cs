using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 窗候选模型。
    /// 描述识别得到的窗几何、宿主墙匹配与创建状态。
    /// </summary>
    public class WindowCandidate
    {
        /// <summary>候选唯一编号（本轮识别内）。</summary>
        public int CandidateId { get; set; }

        /// <summary>构成该候选的线段 Id 列表。</summary>
        public List<int> SegmentIds { get; set; } = new List<int>();

        /// <summary>候选中心点。</summary>
        public XYZ CenterPoint { get; set; }

        /// <summary>候选方向向量（用于朝向判定）。</summary>
        public XYZ Dir { get; set; }

        /// <summary>窗宽（mm）。</summary>
        public double WidthMm { get; set; }

        /// <summary>规则标识（来源规则）。</summary>
        public string RuleId { get; set; }

        /// <summary>到宿主墙的匹配距离（mm）。</summary>
        public double MatchDistMm { get; set; }

        /// <summary>宿主墙 Id（未匹配时为 -1）。</summary>
        public int HostWallId { get; set; } = -1;

        /// <summary>候选状态（Created/Skipped/Failed 等）。</summary>
        public string Status { get; set; }

        /// <summary>失败或跳过原因。</summary>
        public string FailReason { get; set; }

        /// <summary>创建后的窗实例 Id（未创建时为 -1）。</summary>
        public int CreatedElementId { get; set; } = -1;
    }
}
