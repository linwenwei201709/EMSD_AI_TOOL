using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 门候选模型。
    /// 记录门识别阶段的几何、规则来源、宿主墙匹配与最终放置信息。
    /// </summary>
    public class DoorCandidate
    {
        /// <summary>候选唯一编号（本轮识别内）。</summary>
        public int CandidateId { get; set; }

        /// <summary>候选中心点。</summary>
        public XYZ CenterPoint { get; set; }

        /// <summary>门宽（mm）。</summary>
        public double WidthMm { get; set; }

        /// <summary>候选来源规则标识（如 R1/R2/R3）。</summary>
        public string RuleSource { get; set; }

        /// <summary>Hard door-family routing tag used by detection/merge/prune/create.</summary>
        public DoorSymbolFamilyKind SymbolFamilyKind { get; set; } = DoorSymbolFamilyKind.Unknown;

        /// <summary>构成该候选的 CAD 线段 Id 列表。</summary>
        public IList<int> SegmentIds { get; set; } = new List<int>();

        /// <summary>匹配到的宿主墙中心线候选。</summary>
        public WallCenterlineCandidate MatchedWall { get; set; }

        /// <summary>匹配到的宿主墙元素 Id。</summary>
        public ElementId MatchedWallId { get; set; }

        /// <summary>候选点到宿主墙的距离（mm）。</summary>
        public double DistToWallMm { get; set; }

        /// <summary>候选中心点投影到墙线后的点。</summary>
        public XYZ ProjectedPointOnWall { get; set; }

        /// <summary>未匹配/未创建原因描述。</summary>
        public string UnmatchedReason { get; set; }

        /// <summary>门弧半径（mm，弧线规则下使用）。</summary>
        public double ArcRadiusMm { get; set; }

        /// <summary>门弧扫角（deg）。</summary>
        public double ArcSweepDeg { get; set; }

        /// <summary>推断的门铰链点。</summary>
        public XYZ HingePoint { get; set; }

        /// <summary>门扇铰链端点。</summary>
        public XYZ LeafHinge { get; set; }

        /// <summary>门扇闭合端点。</summary>
        public XYZ LeafLatch { get; set; }

        /// <summary>开口中心点（用于最终放置修正）。</summary>
        public XYZ OpeningCenterPoint { get; set; }

        /// <summary>门扇线段 Id（若可定位到单条门扇线）。</summary>
        public int LeafLineSegmentId { get; set; }

        /// <summary>门弧中点（辅助判断开门方向）。</summary>
        public XYZ ArcMidPoint { get; set; }

        /// <summary>墙方向提示向量（辅助门朝向）。</summary>
        public XYZ WallDirHint { get; set; }

        /// <summary>门宽来源说明（规则推断/配置指定等）。</summary>
        public string WidthSource { get; set; }
        public double OpeningWidthMm { get; set; }
        public string PlacementSource { get; set; }
        public bool IsDoubleDoor { get; set; }
        public XYZ LeftEdgePoint { get; set; }
        public XYZ RightEdgePoint { get; set; }
        public double CombinedWidthMm { get; set; }
        public XYZ CombinedCenter { get; set; }

        /// <summary>最终用于创建族实例的放置点。</summary>
        public XYZ FinalPlacementPoint { get; set; }

        /// <summary>最终放置点沿墙方向相对投影点的偏移（mm）。</summary>
        public double DeltaAlongWallMm { get; set; }
        public double FinalWidthMmApplied { get; set; }
        public double FinalHeightMmApplied { get; set; }

        /// <summary>Preferred host wall id for opening-base-first matching (alt arc candidates only).</summary>
        public ElementId PreferredHostWallId { get; set; }

        /// <summary>Preferred projected host point for opening-base-first matching.</summary>
        public XYZ PreferredHostPoint { get; set; }

        /// <summary>Opening base start point inferred from CAD symbol.</summary>
        public XYZ OpeningBaseStartPoint { get; set; }

        /// <summary>Opening base end point inferred from CAD symbol.</summary>
        public XYZ OpeningBaseEndPoint { get; set; }

        /// <summary>Whether this candidate should prefer opening-base host selection.</summary>
        public bool PreferOpeningBaseHost { get; set; }

        /// <summary>Door leaf base start inferred from simplified green-door symbol.</summary>
        public XYZ DoorLeafBaseStart { get; set; }

        /// <summary>Door leaf base end inferred from simplified green-door symbol.</summary>
        public XYZ DoorLeafBaseEnd { get; set; }

        /// <summary>Door leaf base center inferred from simplified green-door symbol.</summary>
        public XYZ DoorLeafBaseCenter { get; set; }

        /// <summary>Virtual opening baseline start point for simplified swing trajectory symbols.</summary>
        public XYZ VirtualOpeningBaseStart { get; set; }

        /// <summary>Virtual opening baseline end point for simplified swing trajectory symbols.</summary>
        public XYZ VirtualOpeningBaseEnd { get; set; }

        /// <summary>Virtual opening baseline center point.</summary>
        public XYZ VirtualOpeningBaseCenter { get; set; }

        /// <summary>Virtual opening width inferred from simplified symbol geometry.</summary>
        public double VirtualOpeningWidthMm { get; set; }

        /// <summary>Whether this candidate should prefer virtual-opening host selection.</summary>
        public bool PreferVirtualOpeningHost { get; set; }
    }
}
