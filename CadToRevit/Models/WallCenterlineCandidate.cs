using Autodesk.Revit.DB;
using CadToRevit.Services;

namespace CadToRevit.Models
{
    /// <summary>
    /// 墙中心线候选。
    /// 识别阶段的输出，创建墙时以该中心线为几何依据。
    /// </summary>
    public class WallCenterlineCandidate
    {
        /// <summary>
        /// 墙中心线（Revit Line）。
        /// </summary>
        public Line CenterLine { get; set; }

        /// <summary>
        /// 估算墙厚（mm）。
        /// 来自双线间距或单线默认厚度。
        /// </summary>
        public double ThicknessMm { get; set; }

        /// <summary>
        /// 双线识别时的第一条边线来源段；单线墙时通常为当前线段。
        /// </summary>
        public CadSegment SideA { get; set; }

        /// <summary>
        /// 双线识别时的第二条边线来源段；单线墙时通常为空。
        /// </summary>
        public CadSegment SideB { get; set; }

        /// <summary>
        /// 参与配对的重叠长度（mm）。
        /// 可用于评估该候选的可靠度。
        /// </summary>
        public double OverlapLengthMm { get; set; }

        // True when this single-line candidate is tagged from a double-line pair.
        public bool IsDoubleLinePairedSingleWall { get; set; }

        // Mate segment id for pair-aware single-line wall placement.
        public int? MateSegmentId { get; set; }

        // Inside normal points from current line to its paired line.
        public XYZ InsideNormal { get; set; }
    }
}
