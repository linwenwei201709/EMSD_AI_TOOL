using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Ceiling
{
    /// <summary>
    /// Gap 修复策略选项（预览用）。
    /// </summary>
    public sealed class CeilingGapPreviewOptions
    {
        /// <summary>启用端点聚类统计。</summary>
        public bool EnableEndpointClustering { get; set; } = true;
        /// <summary>启用延伸到交点统计。</summary>
        public bool EnableExtendToIntersection { get; set; } = true;
        /// <summary>启用补线（会生成临时房间分隔线）。</summary>
        public bool EnableGapBridging { get; set; } = true;
    }

    /// <summary>
    /// Gap 检测/预览结果。
    /// </summary>
    public sealed class CeilingGapPreviewResult
    {
        public int WallCount { get; set; }
        public int EndpointCount { get; set; }
        public int GapCandidateCount { get; set; }
        public double MaxGapMm { get; set; }
        public int ClusterCount { get; set; }
        public int ExtendCount { get; set; }
        public int BridgeCount { get; set; }
        public int RemainingOpenEstimate { get; set; }
        public List<ElementId> TemporaryLineIds { get; set; } = new List<ElementId>();
        public List<string> Failures { get; set; } = new List<string>();
        public string LogPath { get; set; }
    }

    public static class CeilingBoundaryRepairService
    {
        private const double FtToMm = 304.8;

        /// <summary>
        /// 仅检测缺口，不创建任何元素。
        /// </summary>
        public static CeilingGapPreviewResult DetectGaps(Document doc, ElementId levelId, double gapTolMm)
        {
            CeilingGapPreviewResult result = new CeilingGapPreviewResult();
            if (doc == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                result.Failures.Add("Invalid document/level.");
                result.LogPath = WriteGapLog("DetectGap", gapTolMm, result);
                return result;
            }

            List<Line> walls = CollectLevelWallLines(doc, levelId);
            List<XYZ> endpoints = CollectEndpoints(walls);
            result.WallCount = walls.Count;
            result.EndpointCount = endpoints.Count;

            AnalyzeGapPairs(endpoints, gapTolMm / FtToMm, out int candidates, out double maxGapFt);
            result.GapCandidateCount = candidates;
            result.MaxGapMm = maxGapFt * FtToMm;
            result.RemainingOpenEstimate = Math.Max(0, result.GapCandidateCount);
            result.LogPath = WriteGapLog("DetectGap", gapTolMm, result);
            return result;
        }

        /// <summary>
        /// 执行修复预览：聚类/延伸做统计，补线会创建临时房间分隔线。
        /// </summary>
        public static CeilingGapPreviewResult PreviewRepair(
            Document doc,
            ElementId levelId,
            double gapTolMm,
            CeilingGapPreviewOptions options)
        {
            CeilingGapPreviewResult result = new CeilingGapPreviewResult();
            options = options ?? new CeilingGapPreviewOptions();
            if (doc == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                result.Failures.Add("Invalid document/level.");
                result.LogPath = WriteGapLog("PreviewRepair", gapTolMm, result);
                return result;
            }

            List<Line> walls = CollectLevelWallLines(doc, levelId);
            List<XYZ> endpoints = CollectEndpoints(walls);
            result.WallCount = walls.Count;
            result.EndpointCount = endpoints.Count;
            double tolFt = gapTolMm / FtToMm;
            double shortTolFt = doc.Application.ShortCurveTolerance;
            double minBridgeLenFt = Math.Max(shortTolFt * 1.05, 1e-6);

            if (options.EnableEndpointClustering)
            {
                result.ClusterCount = CountEndpointClusters(endpoints, tolFt);
            }

            if (options.EnableExtendToIntersection)
            {
                result.ExtendCount = CountExtendableIntersections(walls, tolFt);
            }

            AnalyzeGapPairs(endpoints, tolFt, out int candidates, out double maxGapFt);
            result.GapCandidateCount = candidates;
            result.MaxGapMm = maxGapFt * FtToMm;

            if (options.EnableGapBridging)
            {
                List<Line> bridges = BuildBridgeLines(endpoints, tolFt, minBridgeLenFt, out int bridgeCount);
                result.BridgeCount = bridgeCount;
                if (bridges.Count > 0)
                {
                    CreateTemporaryRoomBoundaryLines(doc, levelId, bridges, minBridgeLenFt, result);
                }
            }

            result.RemainingOpenEstimate = Math.Max(0, result.GapCandidateCount - result.BridgeCount);
            result.LogPath = WriteGapLog("PreviewRepair", gapTolMm, result);
            return result;
        }

        /// <summary>
        /// 删除预览产生的临时房间分隔线。
        /// </summary>
        public static int CleanupTemporaryLines(Document doc, IList<ElementId> ids)
        {
            if (doc == null || ids == null || ids.Count == 0)
            {
                return 0;
            }

            int deleted = 0;
            using (Transaction tx = new Transaction(doc, "Cleanup Ceiling Preview Lines"))
            {
                tx.Start();
                foreach (ElementId id in ids.ToList())
                {
                    if (id == null || id == ElementId.InvalidElementId)
                    {
                        continue;
                    }

                    try
                    {
                        if (doc.GetElement(id) != null)
                        {
                            doc.Delete(id);
                            deleted++;
                        }
                    }
                    catch
                    {
                    }
                }
                tx.Commit();
            }
            return deleted;
        }

        /// <summary>
        /// 收集指定标高上的墙中心线（仅直线）。
        /// </summary>
        private static List<Line> CollectLevelWallLines(Document doc, ElementId levelId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(x => x.LevelId != null && x.LevelId.IntegerValue == levelId.IntegerValue)
                .Select(x => x.Location as LocationCurve)
                .Where(x => x != null && x.Curve is Line)
                .Select(x => x.Curve as Line)
                .Where(x => x != null && x.Length > 1e-9)
                .ToList();
        }

        /// <summary>
        /// 收集线段端点。
        /// </summary>
        private static List<XYZ> CollectEndpoints(List<Line> lines)
        {
            List<XYZ> points = new List<XYZ>();
            foreach (Line line in lines)
            {
                points.Add(line.GetEndPoint(0));
                points.Add(line.GetEndPoint(1));
            }
            return points;
        }

        /// <summary>
        /// 统计端点配对缺口。
        /// </summary>
        private static void AnalyzeGapPairs(List<XYZ> points, double tolFt, out int candidateCount, out double maxGapFt)
        {
            candidateCount = 0;
            maxGapFt = 0.0;
            if (points == null || points.Count < 2 || tolFt <= 0)
            {
                return;
            }

            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < points.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                int best = -1;
                double bestDist = double.MaxValue;
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (used.Contains(j))
                    {
                        continue;
                    }

                    double d = points[i].DistanceTo(points[j]);
                    if (d > 1e-9 && d <= tolFt && d < bestDist)
                    {
                        best = j;
                        bestDist = d;
                    }
                }

                if (best >= 0)
                {
                    candidateCount++;
                    maxGapFt = Math.Max(maxGapFt, bestDist);
                    used.Add(i);
                    used.Add(best);
                }
            }
        }

        /// <summary>
        /// 统计端点聚类次数（简化实现）。
        /// </summary>
        private static int CountEndpointClusters(List<XYZ> points, double tolFt)
        {
            int count = 0;
            if (points == null || points.Count < 2 || tolFt <= 0)
            {
                return count;
            }

            bool[] visited = new bool[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<int> cluster = new List<int> { i };
                visited[i] = true;
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (visited[j])
                    {
                        continue;
                    }

                    if (points[i].DistanceTo(points[j]) <= tolFt)
                    {
                        cluster.Add(j);
                        visited[j] = true;
                    }
                }

                if (cluster.Count > 1)
                {
                    count += (cluster.Count - 1);
                }
            }

            return count;
        }

        /// <summary>
        /// 统计可延伸到交点的情况（简化估算）。
        /// </summary>
        private static int CountExtendableIntersections(List<Line> lines, double tolFt)
        {
            int extendCount = 0;
            if (lines == null || lines.Count < 2 || tolFt <= 0)
            {
                return 0;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    IntersectionResultArray ira;
                    SetComparisonResult r = lines[i].Intersect(lines[j], out ira);
                    if (r == SetComparisonResult.Overlap || r == SetComparisonResult.Equal)
                    {
                        continue;
                    }

                    Line a = Line.CreateUnbound(lines[i].Origin, lines[i].Direction);
                    Line b = Line.CreateUnbound(lines[j].Origin, lines[j].Direction);
                    IntersectionResultArray ira2;
                    SetComparisonResult r2 = a.Intersect(b, out ira2);
                    if (r2 != SetComparisonResult.Overlap && r2 != SetComparisonResult.Equal)
                    {
                        continue;
                    }

                    if (ira2 == null || ira2.Size == 0)
                    {
                        continue;
                    }

                    XYZ ip = ira2.get_Item(0).XYZPoint;
                    if (ip == null)
                    {
                        continue;
                    }

                    double da = Math.Min(ip.DistanceTo(lines[i].GetEndPoint(0)), ip.DistanceTo(lines[i].GetEndPoint(1)));
                    double db = Math.Min(ip.DistanceTo(lines[j].GetEndPoint(0)), ip.DistanceTo(lines[j].GetEndPoint(1)));
                    if (da <= tolFt && db <= tolFt)
                    {
                        extendCount++;
                    }
                }
            }

            return extendCount;
        }

        /// <summary>
        /// 基于最近端点构建补线。
        /// </summary>
        private static List<Line> BuildBridgeLines(List<XYZ> endpoints, double tolFt, double minBridgeLenFt, out int bridgeCount)
        {
            bridgeCount = 0;
            List<Line> lines = new List<Line>();
            if (endpoints == null || endpoints.Count < 2 || tolFt <= 0)
            {
                return lines;
            }

            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                int best = -1;
                double bestDist = double.MaxValue;
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    if (used.Contains(j))
                    {
                        continue;
                    }

                    double d = endpoints[i].DistanceTo(endpoints[j]);
                    if (d >= minBridgeLenFt && d <= tolFt && d < bestDist)
                    {
                        best = j;
                        bestDist = d;
                    }
                }

                if (best >= 0)
                {
                    try
                    {
                        Line line = Line.CreateBound(endpoints[i], endpoints[best]);
                        if (line != null && line.Length >= minBridgeLenFt)
                        {
                            lines.Add(line);
                            bridgeCount++;
                        }
                    }
                    catch
                    {
                    }
                    used.Add(i);
                    used.Add(best);
                }
            }
            return lines;
        }

        /// <summary>
        /// 创建临时房间分隔线（用于预览闭合修复效果）。
        /// </summary>
        private static void CreateTemporaryRoomBoundaryLines(
            Document doc,
            ElementId levelId,
            List<Line> bridgeLines,
            double minBridgeLenFt,
            CeilingGapPreviewResult result)
        {
            if (bridgeLines == null || bridgeLines.Count == 0)
            {
                return;
            }

            ViewPlan planView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(x => !x.IsTemplate && x.GenLevel != null && x.GenLevel.Id.IntegerValue == levelId.IntegerValue);
            if (planView == null)
            {
                result.Failures.Add("No plan view for selected level.");
                return;
            }

            using (Transaction tx = new Transaction(doc, "Ceiling Gap Preview Lines"))
            {
                tx.Start();
                try
                {
                    SketchPlane sketch = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, planView.GenLevel.Elevation * XYZ.BasisZ));
                    foreach (Line line in bridgeLines)
                    {
                        if (line == null || line.Length < minBridgeLenFt)
                        {
                            continue;
                        }

                        CurveArray arr = new CurveArray();
                        arr.Append(line);
                        ModelCurveArray mc = doc.Create.NewRoomBoundaryLines(sketch, arr, planView);
                        if (mc == null)
                        {
                            continue;
                        }

                        foreach (ModelCurve c in mc)
                        {
                            if (c != null)
                            {
                                result.TemporaryLineIds.Add(c.Id);
                            }
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    result.Failures.Add(ex.Message);
                    tx.RollBack();
                }
            }
        }

        private static string WriteGapLog(string mode, double gapTolMm, CeilingGapPreviewResult result)
        {
            try
            {
                string logRoot = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(logRoot, "ceiling_autogen_" + stamp + ".log");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Mode=" + mode);
                sb.AppendLine("GapTolMm=" + gapTolMm.ToString("F2"));
                sb.AppendLine("WallCount=" + result.WallCount);
                sb.AppendLine("EndpointCount=" + result.EndpointCount);
                sb.AppendLine("GapCandidateCount=" + result.GapCandidateCount);
                sb.AppendLine("MaxGapMm=" + result.MaxGapMm.ToString("F2"));
                sb.AppendLine("ClusterCount=" + result.ClusterCount);
                sb.AppendLine("ExtendCount=" + result.ExtendCount);
                sb.AppendLine("BridgeCount=" + result.BridgeCount);
                sb.AppendLine("RemainingOpenEstimate=" + result.RemainingOpenEstimate);
                sb.AppendLine("TemporaryLineCount=" + (result.TemporaryLineIds == null ? 0 : result.TemporaryLineIds.Count));
                if (result.Failures != null && result.Failures.Count > 0)
                {
                    sb.AppendLine("Failures=" + result.Failures.Count);
                    foreach (string item in result.Failures)
                    {
                        sb.AppendLine(" - " + item);
                    }
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
