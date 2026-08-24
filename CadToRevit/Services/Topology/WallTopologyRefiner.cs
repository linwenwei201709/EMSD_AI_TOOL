using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Topology
{
    /// <summary>
    /// 拓扑修复结果：包含修复后的中心线和各步骤统计值。
    /// </summary>
    public sealed class WallTopologyRefineResult
    {
        public List<WallCenterlineCandidate> Centerlines { get; set; } = new List<WallCenterlineCandidate>();

        public int ClusteredEndpointCount { get; set; }

        public int ExtendedEndpointCount { get; set; }

        public int DuplicateRemovedCount { get; set; }

        public int OffAxisSnappedCount { get; set; }

        public int CollinearMergedCount { get; set; }
    }

    /// <summary>
    /// 墙中心线拓扑修复器：执行聚类、延伸、去重、合并等处理。
    /// </summary>
    public static class WallTopologyRefiner
    {
        /// <summary>
        /// 拓扑修复总入口。
        /// </summary>
        public static WallTopologyRefineResult Refine(
            List<WallCenterlineCandidate> input,
            WallRecognitionConfigDerived cfg)
        {
            WallTopologyRefineResult result = new WallTopologyRefineResult();
            if (input == null || input.Count == 0)
            {
                return result;
            }

            List<WallCenterlineCandidate> current = input
                .Where(x => x != null && x.CenterLine != null && x.CenterLine.Length > 1e-6)
                .Select(CloneWithLine)
                .ToList();

            NormalizeDirections(current);

            if (cfg.EnableTopologyOrthogonalSnap)
            {
                result.OffAxisSnappedCount = OrthogonalSnap(current, cfg.TopologyAngleSnapDeg);
            }

            if (cfg.EnableTopologyEndpointClustering)
            {
                result.ClusteredEndpointCount = ClusterEndpoints(current, cfg.TopologyEndpointClusterTolFt, cfg);
            }

            if (cfg.EnableTopologyExtendToIntersection)
            {
                result.ExtendedEndpointCount = ExtendToIntersections(current, cfg.TopologyExtendSearchTolFt, cfg);
            }

            if (cfg.EnableTopologyExtendCollinear)
            {
                result.ExtendedEndpointCount += ExtendCollinearToNearby(current, cfg);
            }

            if (cfg.EnableTopologyDuplicateRemoval)
            {
                result.DuplicateRemovedCount = RemoveDuplicates(current, cfg.TopologyDuplicateTolFt);
            }

            if (cfg.EnableMergeCollinear)
            {
                result.CollinearMergedCount = MergeCollinear(current, cfg.TopologyEndpointClusterTolFt, cfg.ParallelAngleTolDeg);
            }
            else
            {
                result.CollinearMergedCount = 0;
            }

            result.Centerlines = current;
            return result;
        }

        /// <summary>
        /// 克隆候选，避免直接修改输入对象。
        /// </summary>
        private static WallCenterlineCandidate CloneWithLine(WallCenterlineCandidate source)
        {
            return new WallCenterlineCandidate
            {
                CenterLine = Line.CreateBound(source.CenterLine.GetEndPoint(0), source.CenterLine.GetEndPoint(1)),
                ThicknessMm = source.ThicknessMm,
                SideA = source.SideA,
                SideB = source.SideB,
                OverlapLengthMm = source.OverlapLengthMm,
                IsDoubleLinePairedSingleWall = source.IsDoubleLinePairedSingleWall,
                MateSegmentId = source.MateSegmentId,
                InsideNormal = source.InsideNormal
            };
        }

        /// <summary>
        /// 统一线段方向，减少后续比较的不一致性。
        /// </summary>
        private static void NormalizeDirections(List<WallCenterlineCandidate> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i].CenterLine;
                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                XYZ d = Normalize2D(p1 - p0);
                if (d.X < 0 || (Math.Abs(d.X) < 1e-9 && d.Y < 0))
                {
                    lines[i].CenterLine = Line.CreateBound(p1, p0);
                }
            }
        }

        /// <summary>
        /// 将接近正交方向的线段吸附到 0/90/180/270 度。
        /// </summary>
        private static int OrthogonalSnap(List<WallCenterlineCandidate> lines, double angleSnapDeg)
        {
            int changed = 0;
            double angleTol = angleSnapDeg * Math.PI / 180.0;
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i].CenterLine;
                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                XYZ d = Normalize2D(p1 - p0);
                double angle = Math.Atan2(d.Y, d.X);
                double nearest = Math.Round(angle / (Math.PI / 2.0)) * (Math.PI / 2.0);
                if (Math.Abs(NormalizeAngle(angle - nearest)) > angleTol)
                {
                    continue;
                }

                XYZ snapped = new XYZ(Math.Cos(nearest), Math.Sin(nearest), 0);
                double len = p0.DistanceTo(p1);
                XYZ newP1 = p0 + snapped.Multiply(len);
                if (newP1.DistanceTo(p1) <= 1e-9)
                {
                    continue;
                }

                lines[i].CenterLine = Line.CreateBound(p0, newP1);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 对近邻端点做聚类并移动到聚类中心。
        /// </summary>
        private static int ClusterEndpoints(List<WallCenterlineCandidate> lines, double tolFt, WallRecognitionConfigDerived cfg)
        {
            List<EndpointRef> endpoints = new List<EndpointRef>();
            for (int i = 0; i < lines.Count; i++)
            {
                XYZ dir = Normalize2D(lines[i].CenterLine.GetEndPoint(1) - lines[i].CenterLine.GetEndPoint(0));
                double thicknessFt = lines[i].ThicknessMm > 0 ? UnitUtils.ConvertToInternalUnits(lines[i].ThicknessMm, UnitTypeId.Millimeters) : 0.0;
                endpoints.Add(new EndpointRef { LineIndex = i, EndIndex = 0, Point = lines[i].CenterLine.GetEndPoint(0), Direction = dir, ThicknessFt = thicknessFt });
                endpoints.Add(new EndpointRef { LineIndex = i, EndIndex = 1, Point = lines[i].CenterLine.GetEndPoint(1), Direction = dir, ThicknessFt = thicknessFt });
            }

            bool[] visited = new bool[endpoints.Count];
            int movedCount = 0;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<int> cluster = new List<int> { i };
                visited[i] = true;
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    if (visited[j])
                    {
                        continue;
                    }

                    if (endpoints[i].Point.DistanceTo(endpoints[j].Point) <= tolFt &&
                        ShouldClusterTogether(endpoints[i], endpoints[j], cfg))
                    {
                        visited[j] = true;
                        cluster.Add(j);
                    }
                }

                if (cluster.Count <= 1)
                {
                    continue;
                }

                XYZ center = Average(cluster.Select(x => endpoints[x].Point));
                foreach (int index in cluster)
                {
                    EndpointRef endpoint = endpoints[index];
                    Line line = lines[endpoint.LineIndex].CenterLine;
                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);
                    if (endpoint.EndIndex == 0)
                    {
                        if (p0.DistanceTo(center) > 1e-9)
                        {
                            p0 = center;
                            movedCount++;
                        }
                    }
                    else
                    {
                        if (p1.DistanceTo(center) > 1e-9)
                        {
                            p1 = center;
                            movedCount++;
                        }
                    }

                    if (p0.DistanceTo(p1) > 1e-6)
                    {
                        lines[endpoint.LineIndex].CenterLine = Line.CreateBound(p0, p1);
                    }
                }
            }

            return movedCount;
        }

        /// <summary>
        /// 将可延伸的端点延伸到交点位置。
        /// </summary>
        private static int ExtendToIntersections(List<WallCenterlineCandidate> lines, double tolFt, WallRecognitionConfigDerived cfg)
        {
            int moved = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    Line a = lines[i].CenterLine;
                    Line b = lines[j].CenterLine;
                    XYZ intersection;
                    if (!TryIntersect2D(a, b, out intersection))
                    {
                        continue;
                    }

                    moved += MoveClosestEndpointToPoint(lines, i, intersection, tolFt, cfg);
                    moved += MoveClosestEndpointToPoint(lines, j, intersection, tolFt, cfg);
                }
            }

            return moved;
        }

        /// <summary>
        /// 将离目标点最近的一个端点移动到目标位置。
        /// </summary>
        private static int MoveClosestEndpointToPoint(
            List<WallCenterlineCandidate> lines,
            int lineIndex,
            XYZ target,
            double tolFt,
            WallRecognitionConfigDerived cfg)
        {
            Line line = lines[lineIndex].CenterLine;
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            double d0 = p0.DistanceTo(target);
            double d1 = p1.DistanceTo(target);
            double width = Math.Min(d0, d1);
            if (width > tolFt)
            {
                return 0;
            }

            if (!IsJunctureWidthAllowed(width, cfg))
            {
                return 0;
            }

            if (d0 <= d1)
            {
                p0 = target;
            }
            else
            {
                p1 = target;
            }

            if (p0.DistanceTo(p1) <= 1e-6)
            {
                return 0;
            }

            lines[lineIndex].CenterLine = Line.CreateBound(p0, p1);
            return 1;
        }

        /// <summary>
        /// 判断连接宽度是否满足连接修复阈值约束。
        /// </summary>
        private static bool IsJunctureWidthAllowed(double widthFt, WallRecognitionConfigDerived cfg)
        {
            if (cfg == null)
            {
                return true;
            }

            if (cfg.TopologyIgnoreSmallerThanFt > 0 && widthFt < cfg.TopologyIgnoreSmallerThanFt)
            {
                return false;
            }

            if (cfg.TopologyMinJunctureWidthFt > 0 && widthFt < cfg.TopologyMinJunctureWidthFt)
            {
                return false;
            }

            if (cfg.TopologyIgnoreLargerThanFt > 0 && widthFt > cfg.TopologyIgnoreLargerThanFt)
            {
                return false;
            }

            if (cfg.TopologyMaxJunctureWidthFt > 0 && widthFt > cfg.TopologyMaxJunctureWidthFt)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 对近邻共线线段执行端点延伸。
        /// </summary>
        private static int ExtendCollinearToNearby(List<WallCenterlineCandidate> lines, WallRecognitionConfigDerived cfg)
        {
            if (lines == null || lines.Count <= 1 || cfg == null)
            {
                return 0;
            }

            int moved = 0;
            double extendTolFt = cfg.TopologyExtendCollinearTolFt;
            if (extendTolFt <= 1e-9)
            {
                return 0;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                moved += ExtendEndpointToNearby(lines, i, 0, cfg);
                moved += ExtendEndpointToNearby(lines, i, 1, cfg);
            }

            return moved;
        }

        /// <summary>
        /// 尝试把指定端点沿射线方向延伸到附近共线目标。
        /// </summary>
        private static int ExtendEndpointToNearby(
            List<WallCenterlineCandidate> lines,
            int lineIndex,
            int endIndex,
            WallRecognitionConfigDerived cfg)
        {
            Line line = lines[lineIndex].CenterLine;
            if (line == null)
            {
                return 0;
            }

            XYZ endpoint = line.GetEndPoint(endIndex);
            XYZ other = line.GetEndPoint(1 - endIndex);
            XYZ rayDir = Normalize2D(endpoint - other);
            XYZ target = null;
            double bestAlong = double.MaxValue;
            double lineThicknessFt = ToThicknessFt(lines[lineIndex].ThicknessMm);
            double cosTol = Math.Cos(cfg.ParallelAngleTolDeg * Math.PI / 180.0);

            for (int j = 0; j < lines.Count; j++)
            {
                if (j == lineIndex)
                {
                    continue;
                }

                Line otherLine = lines[j].CenterLine;
                if (otherLine == null)
                {
                    continue;
                }

                XYZ otherDir = Normalize2D(otherLine.GetEndPoint(1) - otherLine.GetEndPoint(0));
                double alignment = Math.Abs(Dot2D(rayDir, otherDir));
                if (alignment < cosTol)
                {
                    continue;
                }

                if (!IsThicknessCompatible(lineThicknessFt, ToThicknessFt(lines[j].ThicknessMm), cfg.WallThicknessTolFt))
                {
                    continue;
                }

                double offset = DistancePointToInfiniteLine2D(endpoint, otherLine);
                if (offset > cfg.TopologyCollinearOffsetTolFt)
                {
                    continue;
                }

                XYZ p0 = otherLine.GetEndPoint(0);
                XYZ p1 = otherLine.GetEndPoint(1);
                XYZ[] candidates = { p0, p1 };
                for (int k = 0; k < candidates.Length; k++)
                {
                    XYZ c = candidates[k];
                    XYZ v = c - endpoint;
                    double along = Dot2D(v, rayDir);
                    if (along <= 1e-6 || along > cfg.TopologyExtendCollinearTolFt)
                    {
                        continue;
                    }

                    double lateral = Math.Abs(Cross2D(v, rayDir));
                    if (lateral > cfg.TopologyExtendProjectionTolFt)
                    {
                        continue;
                    }

                    if (along < bestAlong)
                    {
                        bestAlong = along;
                        target = c;
                    }
                }
            }

            if (target == null)
            {
                return 0;
            }

            XYZ pA = line.GetEndPoint(0);
            XYZ pB = line.GetEndPoint(1);
            if (endIndex == 0)
            {
                pA = target;
            }
            else
            {
                pB = target;
            }

            if (pA.DistanceTo(pB) <= 1e-6)
            {
                return 0;
            }

            lines[lineIndex].CenterLine = Line.CreateBound(pA, pB);
            return 1;
        }

        /// <summary>
        /// 判断两个端点是否应归为同一聚类。
        /// </summary>
        private static bool ShouldClusterTogether(EndpointRef a, EndpointRef b, WallRecognitionConfigDerived cfg)
        {
            if (cfg == null || !cfg.TopologyUseDirectionalClustering)
            {
                return true;
            }

            double cosTol = Math.Cos(cfg.ParallelAngleTolDeg * Math.PI / 180.0);
            double align = Math.Abs(Dot2D(a.Direction, b.Direction));
            if (align < cosTol)
            {
                return false;
            }

            return IsThicknessCompatible(a.ThicknessFt, b.ThicknessFt, cfg.WallThicknessTolFt);
        }

        /// <summary>
        /// 判断两条墙中心线厚度是否兼容。
        /// </summary>
        private static bool IsThicknessCompatible(double aFt, double bFt, double tolFt)
        {
            if (aFt <= 1e-9 || bFt <= 1e-9 || tolFt <= 1e-9)
            {
                return true;
            }

            return Math.Abs(aFt - bFt) <= tolFt;
        }

        /// <summary>
        /// 计算点到无限直线（2D）的法向距离。
        /// </summary>
        private static double DistancePointToInfiniteLine2D(XYZ point, Line line)
        {
            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            XYZ d = Normalize2D(b - a);
            XYZ v = point - a;
            return Math.Abs(Cross2D(v, d));
        }

        /// <summary>
        /// 墙厚（毫米）转英尺。
        /// </summary>
        private static double ToThicknessFt(double thicknessMm)
        {
            if (thicknessMm <= 0)
            {
                return 0.0;
            }

            return UnitUtils.ConvertToInternalUnits(thicknessMm, UnitTypeId.Millimeters);
        }

        /// <summary>
        /// 合并可连接的共线中心线段。
        /// </summary>
        private static int MergeCollinear(List<WallCenterlineCandidate> lines, double endpointTolFt, double parallelAngleTolDeg)
        {
            if (lines == null || lines.Count <= 1)
            {
                return 0;
            }

            int mergedCount = 0;
            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        Line a = lines[i].CenterLine;
                        Line b = lines[j].CenterLine;
                        if (a == null || b == null)
                        {
                            continue;
                        }

                        XYZ da = Normalize2D(a.GetEndPoint(1) - a.GetEndPoint(0));
                        XYZ db = Normalize2D(b.GetEndPoint(1) - b.GetEndPoint(0));
                        if (Math.Abs((da.X * db.X) + (da.Y * db.Y)) < cosTol)
                        {
                            continue;
                        }

                        if (!IsCollinear2D(a, b, endpointTolFt))
                        {
                            continue;
                        }

                        if (!HasEndpointTouch(a, b, endpointTolFt))
                        {
                            continue;
                        }

                        Line merged = MergeTwoLines2D(a, b);
                        if (merged == null)
                        {
                            continue;
                        }

                        lines[i].CenterLine = merged;
                        lines.RemoveAt(j);
                        mergedCount++;
                        changed = true;
                        goto NextRound;
                    }
                }

            NextRound:
                ;
            }

            return mergedCount;
        }

        /// <summary>
        /// 判断两线在二维平面内是否近似共线。
        /// </summary>
        private static bool IsCollinear2D(Line a, Line b, double tol)
        {
            XYZ ap0 = a.GetEndPoint(0);
            XYZ ap1 = a.GetEndPoint(1);
            XYZ bp0 = b.GetEndPoint(0);
            XYZ da = Normalize2D(ap1 - ap0);
            XYZ n = new XYZ(-da.Y, da.X, 0);
            double dist = Math.Abs((bp0.X - ap0.X) * n.X + (bp0.Y - ap0.Y) * n.Y);
            return dist <= tol;
        }

        /// <summary>
        /// 判断两线端点是否在容差内可接触。
        /// </summary>
        private static bool HasEndpointTouch(Line a, Line b, double tol)
        {
            XYZ[] pa = { a.GetEndPoint(0), a.GetEndPoint(1) };
            XYZ[] pb = { b.GetEndPoint(0), b.GetEndPoint(1) };
            foreach (XYZ p in pa)
            {
                foreach (XYZ q in pb)
                {
                    if (p.DistanceTo(q) <= tol)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 将两条可合并线段合并为一条线。
        /// </summary>
        private static Line MergeTwoLines2D(Line a, Line b)
        {
            XYZ p0 = a.GetEndPoint(0);
            XYZ p1 = a.GetEndPoint(1);
            XYZ u = Normalize2D(p1 - p0);
            XYZ[] pts = { a.GetEndPoint(0), a.GetEndPoint(1), b.GetEndPoint(0), b.GetEndPoint(1) };
            double minS = double.MaxValue;
            double maxS = double.MinValue;
            foreach (XYZ p in pts)
            {
                double s = (p.X * u.X) + (p.Y * u.Y);
                if (s < minS)
                {
                    minS = s;
                }

                if (s > maxS)
                {
                    maxS = s;
                }
            }

            XYZ c0 = PointAtProjection(p0, u, minS);
            XYZ c1 = PointAtProjection(p0, u, maxS);
            if (c0.DistanceTo(c1) <= 1e-6)
            {
                return null;
            }

            return Line.CreateBound(c0, c1);
        }

        /// <summary>
        /// 根据投影值反算方向向量上的点。
        /// </summary>
        private static XYZ PointAtProjection(XYZ anchor, XYZ direction, double targetProj)
        {
            double anchorProj = (anchor.X * direction.X) + (anchor.Y * direction.Y);
            double delta = targetProj - anchorProj;
            return new XYZ(anchor.X + (direction.X * delta), anchor.Y + (direction.Y * delta), anchor.Z);
        }

        /// <summary>
        /// 删除重复或近重复线段。
        /// </summary>
        private static int RemoveDuplicates(List<WallCenterlineCandidate> lines, double tolFt)
        {
            int removed = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (!AreNearDuplicate(lines[i].CenterLine, lines[j].CenterLine, tolFt))
                    {
                        continue;
                    }

                    if (lines[i].CenterLine.Length >= lines[j].CenterLine.Length)
                    {
                        lines.RemoveAt(j);
                    }
                    else
                    {
                        lines.RemoveAt(i);
                        i--;
                    }

                    removed++;
                    break;
                }
            }

            return removed;
        }

        /// <summary>
        /// 判断两线是否为近重复。
        /// </summary>
        private static bool AreNearDuplicate(Line a, Line b, double tolFt)
        {
            XYZ a0 = a.GetEndPoint(0);
            XYZ a1 = a.GetEndPoint(1);
            XYZ b0 = b.GetEndPoint(0);
            XYZ b1 = b.GetEndPoint(1);
            return (a0.DistanceTo(b0) <= tolFt && a1.DistanceTo(b1) <= tolFt) ||
                   (a0.DistanceTo(b1) <= tolFt && a1.DistanceTo(b0) <= tolFt);
        }

        /// <summary>
        /// 计算二维线段的交点。
        /// </summary>
        private static bool TryIntersect2D(Line a, Line b, out XYZ intersection)
        {
            intersection = null;
            XYZ p = a.GetEndPoint(0);
            XYZ r = a.GetEndPoint(1) - a.GetEndPoint(0);
            XYZ q = b.GetEndPoint(0);
            XYZ s = b.GetEndPoint(1) - b.GetEndPoint(0);
            double rxs = Cross2D(r, s);
            if (Math.Abs(rxs) <= 1e-9)
            {
                return false;
            }

            XYZ qp = q - p;
            double t = Cross2D(qp, s) / rxs;
            intersection = new XYZ(p.X + (t * r.X), p.Y + (t * r.Y), 0);
            return true;
        }

        /// <summary>
        /// 二维叉积。
        /// </summary>
        private static double Cross2D(XYZ a, XYZ b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        /// <summary>
        /// 二维点积。
        /// </summary>
        private static double Dot2D(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        /// <summary>
        /// 计算点集平均点。
        /// </summary>
        private static XYZ Average(IEnumerable<XYZ> points)
        {
            double x = 0;
            double y = 0;
            double z = 0;
            int count = 0;
            foreach (XYZ p in points)
            {
                x += p.X;
                y += p.Y;
                z += p.Z;
                count++;
            }

            if (count == 0)
            {
                return new XYZ(0, 0, 0);
            }

            return new XYZ(x / count, y / count, z / count);
        }

        /// <summary>
        /// 二维向量归一化。
        /// </summary>
        private static XYZ Normalize2D(XYZ vector)
        {
            double len = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            if (len <= 1e-9)
            {
                return new XYZ(1, 0, 0);
            }

            return new XYZ(vector.X / len, vector.Y / len, 0);
        }

        /// <summary>
        /// 将角度归一化到 [-PI, PI]。
        /// </summary>
        private static double NormalizeAngle(double rad)
        {
            while (rad > Math.PI)
            {
                rad -= 2.0 * Math.PI;
            }

            while (rad < -Math.PI)
            {
                rad += 2.0 * Math.PI;
            }

            return rad;
        }

        private sealed class EndpointRef
        {
            public int LineIndex { get; set; }

            public int EndIndex { get; set; }

            public XYZ Point { get; set; }

            public XYZ Direction { get; set; }

            public double ThicknessFt { get; set; }
        }
    }
}
