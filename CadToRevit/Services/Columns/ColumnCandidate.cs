using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Services.Columns
{
    public sealed class ColumnCandidate
    {
        // 中文注释：候选形状类型，Rect 表示矩形族柱，Irregular 表示异形轮廓柱。
        public string ShapeType { get; set; } = "Rect";

        public int ClusterId { get; set; }

        public XYZ Center { get; set; }

        public XYZ OriginalCenter { get; set; }

        public double WidthFt { get; set; }

        public double DepthFt { get; set; }

        public double AreaFt2 { get; set; }

        public double AspectRatio { get; set; }

        public int SegmentCount { get; set; }

        public bool HasLongLine { get; set; }

        public double Rectness { get; set; }

        public double FillRatio { get; set; }

        public double Score { get; set; }

        public bool IsRejected { get; set; }

        public string RejectReason { get; set; }

        public string MergeAction { get; set; }

        public string AttachInfo { get; set; }

        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }

        // 中文注释：异形柱闭合轮廓点（首尾闭合），用于 DirectShape 拉伸。
        public List<XYZ> Footprint { get; set; } = new List<XYZ>();

        public double FootprintAreaFt2 { get; set; }

        public double ObbWidthFt { get; set; }

        public double ObbDepthFt { get; set; }

        public double ObbAngleRad { get; set; }

        // 中文注释：异形柱闭环是否由柱图层自身完成。
        public bool IrregularClosedBySelf { get; set; }

        // 中文注释：异形柱闭环时借用的辅助边数量。
        public int HelperEdgeUsedCount { get; set; }

        // 中文注释：闭环前图的悬挂端点数量，用于排查缺口问题。
        public int DanglingEndpoints { get; set; }

        // 中文注释：标记是否通过缺口补边完成闭环。
        public bool GapHealed { get; set; }

        // 中文注释：标记是否由碎片合并得到该候选。
        public bool FragmentMerged { get; set; }

        // 中文注释：碎片合并来源 clusterId 列表，便于日志排障。
        public string FragmentSourceClusterIds { get; set; }

        // 中文注释：碎片合并触发原因（距离/角度）。
        public string FragmentMergeReason { get; set; }

        // 中文注释：保留候选柱来源线段，用于放置后方向降级推断。
        public List<CadSegment> SourceSegments { get; set; } = new List<CadSegment>();
    }
}
