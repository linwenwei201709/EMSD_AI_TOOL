using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Columns
{
    public sealed class ColumnDetectionResult
    {
        public List<ColumnCandidate> Candidates { get; set; } = new List<ColumnCandidate>();

        public List<ColumnCandidate> RejectedCandidates { get; set; } = new List<ColumnCandidate>();

        public int InputSegmentCount { get; set; }

        public int PrefilterSegmentCount { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class SegmentCluster
    {
        public int ClusterId { get; set; }

        public List<CadSegment> Segments { get; } = new List<CadSegment>();

        // 中文注释：记录碎片合并来源 cluster id，便于输出诊断。
        public List<int> SourceClusterIds { get; } = new List<int>();

        public string MergeReason { get; set; }
    }

    internal sealed class WallCenterlineInfo
    {
        public ElementId WallId { get; set; }

        public Line Centerline { get; set; }
    }

    internal sealed class LoopBuildInfo
    {
        public bool ClosedBySelf { get; set; }

        public int HelperEdgeUsedCount { get; set; }

        public int DanglingEndpoints { get; set; }
    }

    public static class ColumnCandidateDetector
    {
        private const double MmPerFt = 304.8;

        public static List<ColumnCandidate> DetectByRawLayer(IReadOnlyList<CadSegment> segments, string rawLayerName)
        {
            ColumnDetectionResult result = DetectByRawLayer(segments, rawLayerName, null, null);
            return result.Candidates;
        }

        public static ColumnDetectionResult DetectByRawLayer(
            IReadOnlyList<CadSegment> segments,
            string rawLayerName,
            AdvancedSettingsRow rowSettings,
            Document doc)
        {
            ColumnRecognitionDefaults settings = ColumnRecognitionConfigProvider.ResolveForLayer(rawLayerName, rowSettings);
            ColumnDetectionResult result = new ColumnDetectionResult();

            List<CadSegment> source = (segments ?? new List<CadSegment>())
                .Where(x => x != null &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                            string.Equals(x.RawLayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            result.InputSegmentCount = source.Count;
            if (source.Count == 0)
            {
                return result;
            }

            List<CadSegment> filtered = PreFilterSegments(source, settings);
            result.PrefilterSegmentCount = filtered.Count;
            if (filtered.Count == 0)
            {
                return result;
            }

            List<CadSegment> helperPool = BuildHelperPool(segments, rawLayerName, settings);

            List<SegmentCluster> clusters = ClusterSegments(filtered, settings.Cluster);
            clusters = MergeFragmentClusters(clusters, settings);
            List<ColumnCandidate> candidates = BuildCandidates(clusters, helperPool, settings);
            ScoreCandidates(candidates, settings.Score);

            List<ColumnCandidate> accepted = new List<ColumnCandidate>();
            foreach (ColumnCandidate candidate in candidates)
            {
                if (candidate.IsRejected)
                {
                    result.RejectedCandidates.Add(candidate);
                    continue;
                }

                accepted.Add(candidate);
            }

            accepted = MergeAndDedupeCandidates(accepted, settings.Merge);
            AttachToWall(accepted, settings.AttachToWall, doc);
            accepted = DedupeAgainstPlacedColumns(accepted, settings.Merge, doc, result.RejectedCandidates);
            result.Candidates = accepted;

            if (settings.Debug.ExportReport)
            {
                result.ReportPath = ExportReport(rawLayerName, result, settings);
            }

            DiagnosticRecorder.AppendDebug(
                "[ColumnDetect] Layer=" + (rawLayerName ?? string.Empty) +
                ", Input=" + result.InputSegmentCount +
                ", Prefilter=" + result.PrefilterSegmentCount +
                ", Cluster=" + clusters.Count +
                ", Accepted=" + result.Candidates.Count +
                ", Rejected=" + result.RejectedCandidates.Count +
                (string.IsNullOrWhiteSpace(result.ReportPath) ? string.Empty : (", Report=" + result.ReportPath)));

            return result;
        }

        private static List<CadSegment> PreFilterSegments(List<CadSegment> source, ColumnRecognitionDefaults settings)
        {
            if (source == null || source.Count == 0)
            {
                return new List<CadSegment>();
            }

            if (!settings.LineFilter.Enable)
            {
                return new List<CadSegment>(source);
            }

            double maxLenFt = settings.LineFilter.MaxSegmentLengthMm / MmPerFt;
            return source.Where(x => x.P0 != null && x.P1 != null && x.P0.DistanceTo(x.P1) <= maxLenFt).ToList();
        }

        private static List<CadSegment> BuildHelperPool(
            IReadOnlyList<CadSegment> allSegments,
            string rawLayerName,
            ColumnRecognitionDefaults settings)
        {
            ColumnIrregularSettings irregular = settings?.Irregular ?? new ColumnIrregularSettings();
            if (!irregular.Enable || !irregular.EnableHelperEdges)
            {
                return new List<CadSegment>();
            }

            HashSet<string> keywords = new HashSet<string>(
                (irregular.HelperLayerKeywords ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant()));
            if (keywords.Count == 0)
            {
                return new List<CadSegment>();
            }

            double maxLenFt = irregular.MaxHelperEdgeLenMm / MmPerFt;
            return (allSegments ?? new List<CadSegment>())
                .Where(x => x != null &&
                            x.P0 != null &&
                            x.P1 != null &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                            !string.Equals(x.RawLayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                .Where(x =>
                {
                    string layer = (x.RawLayerName ?? string.Empty).ToUpperInvariant();
                    return keywords.Any(k => layer.Contains(k));
                })
                .Where(x => x.P0.DistanceTo(x.P1) <= maxLenFt)
                .ToList();
        }

        private static List<SegmentCluster> ClusterSegments(List<CadSegment> segments, ColumnClusterSettings settings)
        {
            string algorithm = (settings.Algorithm ?? "MidpointBFS").Trim();
            if (string.Equals(algorithm, "EndpointGraph", StringComparison.OrdinalIgnoreCase))
            {
                return ClusterByEndpointGraph(segments, settings.EndpointTolMm / MmPerFt, settings.GapTolMm / MmPerFt);
            }

            return ClusterByMidpointBfs(segments, settings.ClusterTolMm / MmPerFt);
        }

        private static List<SegmentCluster> ClusterByMidpointBfs(List<CadSegment> segments, double tolFt)
        {
            List<SegmentCluster> clusters = new List<SegmentCluster>();
            bool[] used = new bool[segments.Count];
            int clusterId = 1;
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                SegmentCluster cluster = new SegmentCluster { ClusterId = clusterId++ };
                cluster.SourceClusterIds.Add(cluster.ClusterId);
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                used[i] = true;
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    CadSegment current = segments[idx];
                    cluster.Segments.Add(current);
                    XYZ mid = Midpoint(current);
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        if (mid.DistanceTo(Midpoint(segments[j])) <= tolFt)
                        {
                            used[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static List<SegmentCluster> ClusterByEndpointGraph(List<CadSegment> segments, double endpointTolFt, double gapTolFt)
        {
            List<SegmentCluster> clusters = new List<SegmentCluster>();
            bool[] used = new bool[segments.Count];
            int clusterId = 1;
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                SegmentCluster cluster = new SegmentCluster { ClusterId = clusterId++ };
                cluster.SourceClusterIds.Add(cluster.ClusterId);
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                used[i] = true;
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    CadSegment current = segments[idx];
                    cluster.Segments.Add(current);
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        CadSegment other = segments[j];
                        if (AreEndpointConnected(current, other, endpointTolFt) || SegmentDistance(current, other) <= gapTolFt)
                        {
                            used[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static bool AreEndpointConnected(CadSegment a, CadSegment b, double tolFt)
        {
            return a.P0.DistanceTo(b.P0) <= tolFt ||
                   a.P0.DistanceTo(b.P1) <= tolFt ||
                   a.P1.DistanceTo(b.P0) <= tolFt ||
                   a.P1.DistanceTo(b.P1) <= tolFt;
        }

        private static double SegmentDistance(CadSegment a, CadSegment b)
        {
            double d1 = PointToSegmentDistance(a.P0, b.P0, b.P1);
            double d2 = PointToSegmentDistance(a.P1, b.P0, b.P1);
            double d3 = PointToSegmentDistance(b.P0, a.P0, a.P1);
            double d4 = PointToSegmentDistance(b.P1, a.P0, a.P1);
            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        private static List<SegmentCluster> MergeFragmentClusters(List<SegmentCluster> clusters, ColumnRecognitionDefaults settings)
        {
            List<SegmentCluster> input = clusters ?? new List<SegmentCluster>();
            ColumnIrregularSettings irregular = settings?.Irregular ?? new ColumnIrregularSettings();
            if (!irregular.Enable || input.Count <= 1)
            {
                return input;
            }

            double mergeTolFt = irregular.FragmentMergeTolMm / MmPerFt;
            double maxSizeFt = irregular.MaxSizeMm / MmPerFt;
            int rectMinSeg = Math.Max(1, settings.Cluster.MinGroupSegments);
            int maxFragmentsPerGroup = Math.Max(2, irregular.MaxFragmentsPerGroup);
            List<SegmentCluster> working = new List<SegmentCluster>(input);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < working.Count; i++)
                {
                    SegmentCluster seed = working[i];
                    if (seed == null || seed.Segments.Count >= rectMinSeg)
                    {
                        continue;
                    }

                    List<int> group = new List<int> { i };
                    while (group.Count < maxFragmentsPerGroup)
                    {
                        int next = FindBestFragmentNeighbor(working, group, mergeTolFt, maxSizeFt, rectMinSeg);
                        if (next < 0)
                        {
                            break;
                        }

                        group.Add(next);
                    }

                    if (group.Count <= 1)
                    {
                        continue;
                    }

                    SegmentCluster merged = MergeClusterGroup(working, group);
                    if (merged == null)
                    {
                        continue;
                    }

                    int keepIndex = group.Min();
                    HashSet<int> removeSet = new HashSet<int>(group.Where(x => x != keepIndex));
                    List<SegmentCluster> nextWorking = new List<SegmentCluster>();
                    for (int k = 0; k < working.Count; k++)
                    {
                        if (k == keepIndex)
                        {
                            nextWorking.Add(merged);
                            continue;
                        }

                        if (!removeSet.Contains(k))
                        {
                            nextWorking.Add(working[k]);
                        }
                    }

                    working = nextWorking;
                    changed = true;
                    break;
                }
            }

            return working;
        }

        private static int FindBestFragmentNeighbor(
            List<SegmentCluster> clusters,
            List<int> currentGroup,
            double mergeTolFt,
            double maxSizeFt,
            int rectMinSeg)
        {
            SegmentCluster mergedCurrent = MergeClusterGroup(clusters, currentGroup);
            if (mergedCurrent == null)
            {
                return -1;
            }

            XYZ currentCenter = GetClusterCenter(mergedCurrent);
            double currentDir = GetClusterDominantAngle(mergedCurrent);
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < clusters.Count; i++)
            {
                if (currentGroup.Contains(i))
                {
                    continue;
                }

                SegmentCluster candidate = clusters[i];
                if (candidate == null || candidate.Segments.Count >= rectMinSeg)
                {
                    continue;
                }

                XYZ candidateCenter = GetClusterCenter(candidate);
                double dist = currentCenter.DistanceTo(candidateCenter);
                if (dist > mergeTolFt)
                {
                    continue;
                }

                double candDir = GetClusterDominantAngle(candidate);
                double diffDeg = AngleDiffDeg(currentDir, candDir);
                bool dirOk = diffDeg <= 15.0 || Math.Abs(diffDeg - 90.0) <= 15.0;
                if (!dirOk)
                {
                    continue;
                }

                List<SegmentCluster> testGroup = currentGroup.Select(x => clusters[x]).ToList();
                testGroup.Add(candidate);
                SegmentCluster testMerged = MergeClusterGroup(testGroup);
                if (testMerged == null)
                {
                    continue;
                }

                XYZ testMin;
                XYZ testMax;
                GetClusterBounds(testMerged, out testMin, out testMax);
                if ((testMax.X - testMin.X) > maxSizeFt || (testMax.Y - testMin.Y) > maxSizeFt)
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        private static SegmentCluster MergeClusterGroup(List<SegmentCluster> clusters, List<int> indices)
        {
            return MergeClusterGroup((indices ?? new List<int>())
                .Where(x => x >= 0 && x < (clusters == null ? 0 : clusters.Count))
                .Select(x => clusters[x])
                .ToList());
        }

        private static SegmentCluster MergeClusterGroup(List<SegmentCluster> group)
        {
            List<SegmentCluster> list = (group ?? new List<SegmentCluster>()).Where(x => x != null).ToList();
            if (list.Count == 0)
            {
                return null;
            }

            SegmentCluster merged = new SegmentCluster
            {
                ClusterId = list.Min(x => x.ClusterId),
                MergeReason = "distance/angle"
            };
            foreach (SegmentCluster c in list)
            {
                merged.Segments.AddRange(c.Segments.Where(x => x != null));
                foreach (int id in c.SourceClusterIds)
                {
                    if (!merged.SourceClusterIds.Contains(id))
                    {
                        merged.SourceClusterIds.Add(id);
                    }
                }
            }

            if (merged.SourceClusterIds.Count == 0)
            {
                merged.SourceClusterIds.Add(merged.ClusterId);
            }

            return merged;
        }

        private static XYZ GetClusterCenter(SegmentCluster cluster)
        {
            XYZ min;
            XYZ max;
            GetClusterBounds(cluster, out min, out max);
            return new XYZ((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, 0.0);
        }

        private static void GetClusterBounds(SegmentCluster cluster, out XYZ min, out XYZ max)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (CadSegment s in (cluster == null ? new List<CadSegment>() : cluster.Segments).Where(x => x != null && x.P0 != null && x.P1 != null))
            {
                UpdateBounds(s.P0, ref minX, ref minY, ref maxX, ref maxY);
                UpdateBounds(s.P1, ref minX, ref minY, ref maxX, ref maxY);
            }

            if (minX == double.MaxValue)
            {
                minX = minY = maxX = maxY = 0.0;
            }

            min = new XYZ(minX, minY, 0.0);
            max = new XYZ(maxX, maxY, 0.0);
        }

        private static double GetClusterDominantAngle(SegmentCluster cluster)
        {
            double maxLen = -1.0;
            double angle = 0.0;
            foreach (CadSegment s in (cluster == null ? new List<CadSegment>() : cluster.Segments).Where(x => x != null && x.P0 != null && x.P1 != null))
            {
                XYZ v = s.P1 - s.P0;
                double len = v.GetLength();
                if (len > maxLen)
                {
                    maxLen = len;
                    angle = Math.Atan2(v.Y, v.X);
                }
            }

            return NormalizeHalfTurn(angle);
        }

        private static double NormalizeHalfTurn(double angleRad)
        {
            double a = angleRad % Math.PI;
            if (a < 0)
            {
                a += Math.PI;
            }

            return a;
        }

        private static double AngleDiffDeg(double a, double b)
        {
            double d = Math.Abs(NormalizeHalfTurn(a) - NormalizeHalfTurn(b));
            d = Math.Min(d, Math.PI - d);
            return d * 180.0 / Math.PI;
        }

        private static List<ColumnCandidate> BuildCandidates(
            List<SegmentCluster> clusters,
            List<CadSegment> helperPool,
            ColumnRecognitionDefaults settings)
        {
            List<ColumnCandidate> candidates = new List<ColumnCandidate>();
            int minSeg = Math.Max(1, settings.Cluster.MinGroupSegments);
            double minSizeFt = settings.SizeFilter.MinSizeMm / MmPerFt;
            double rectMaxSizeFt = settings.SizeFilter.MaxSizeMm / MmPerFt;
            double maxLenFt = settings.LineFilter.MaxSegmentLengthMm / MmPerFt;
            double minAreaFt2 = settings.SizeFilter.MinAreaM2 * 10.7639104167;
            double endpointTolFt = settings.Cluster.EndpointTolMm / MmPerFt;
            ColumnIrregularSettings irregular = settings.Irregular ?? new ColumnIrregularSettings();
            double irregularMinAreaFt2 = irregular.MinAreaM2 * 10.7639104167;
            double irregularMaxAreaFt2 = irregular.MaxAreaM2 * 10.7639104167;

            foreach (SegmentCluster cluster in clusters ?? new List<SegmentCluster>())
            {
                if (cluster == null || cluster.Segments.Count == 0)
                {
                    continue;
                }

                ColumnCandidate rectCandidate = BuildCandidate(cluster, maxLenFt);
                ApplyRectFilters(
                    rectCandidate,
                    cluster.Segments.Count,
                    minSeg,
                    minSizeFt,
                    rectMaxSizeFt,
                    minAreaFt2,
                    settings.SizeFilter);
                if (!rectCandidate.IsRejected)
                {
                    candidates.Add(rectCandidate);
                    continue;
                }

                if (irregular.Enable)
                {
                    ColumnCandidate irregularCandidate = TryBuildIrregularCandidate(
                        cluster,
                        maxLenFt,
                        endpointTolFt,
                        irregular,
                        helperPool);
                    if (irregularCandidate != null)
                    {
                        ApplyIrregularFilters(
                            irregularCandidate,
                            cluster.Segments.Count,
                            Math.Max(2, irregular.MinGroupSegments),
                            minSizeFt,
                            irregularMinAreaFt2,
                            irregularMaxAreaFt2,
                            irregular);
                        candidates.Add(irregularCandidate);
                        continue;
                    }
                }

                candidates.Add(rectCandidate);
            }

            return candidates;
        }

        private static void ApplyRectFilters(
            ColumnCandidate candidate,
            int segmentCount,
            int minSeg,
            double minSizeFt,
            double maxSizeFt,
            double minAreaFt2,
            ColumnSizeFilterSettings sizeFilter)
        {
            if (candidate == null)
            {
                return;
            }

            if (segmentCount < minSeg)
            {
                Reject(candidate, "TooFewSegments");
            }

            if (candidate.WidthFt < minSizeFt || candidate.DepthFt < minSizeFt)
            {
                Reject(candidate, "TooSmall");
            }

            if (candidate.WidthFt > maxSizeFt || candidate.DepthFt > maxSizeFt)
            {
                Reject(candidate, "TooLarge");
            }

            if (candidate.AreaFt2 < minAreaFt2)
            {
                Reject(candidate, "TooSmallArea");
            }

            if (candidate.AspectRatio > sizeFilter.MaxAspectRatio)
            {
                Reject(candidate, "AspectRatioTooHigh");
            }

            if (candidate.FillRatio < sizeFilter.MinFillRatio)
            {
                Reject(candidate, "LowFillRatio");
            }
        }

        private static void ApplyIrregularFilters(
            ColumnCandidate candidate,
            int segmentCount,
            int minSeg,
            double minSizeFt,
            double minAreaFt2,
            double maxAreaFt2,
            ColumnIrregularSettings irregular)
        {
            if (candidate == null)
            {
                return;
            }

            if (segmentCount < minSeg)
            {
                Reject(candidate, "TooFewSegments");
            }

            double irregularMaxSizeFt = (irregular == null ? 1800.0 : irregular.MaxSizeMm) / MmPerFt;
            if (candidate.WidthFt < minSizeFt || candidate.DepthFt < minSizeFt)
            {
                Reject(candidate, "TooSmall");
            }

            if (candidate.WidthFt > irregularMaxSizeFt || candidate.DepthFt > irregularMaxSizeFt)
            {
                Reject(candidate, "TooLarge");
            }

            if (candidate.FootprintAreaFt2 < minAreaFt2)
            {
                Reject(candidate, "TooSmallArea");
            }

            if (candidate.FootprintAreaFt2 > maxAreaFt2)
            {
                Reject(candidate, "TooLargeArea");
            }

            if (candidate.AspectRatio > irregular.MaxAspectRatio)
            {
                Reject(candidate, "AspectRatioTooHigh");
            }

            if (candidate.FillRatio < irregular.MinFillRatio)
            {
                Reject(candidate, "LowFillRatio");
            }
        }

        private static ColumnCandidate BuildCandidate(SegmentCluster cluster, double maxSegmentLengthFt)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double totalLen = 0.0;
            bool hasLongLine = false;

            foreach (CadSegment s in cluster.Segments)
            {
                UpdateBounds(s.P0, ref minX, ref minY, ref maxX, ref maxY);
                UpdateBounds(s.P1, ref minX, ref minY, ref maxX, ref maxY);
                double len = s.P0.DistanceTo(s.P1);
                totalLen += len;
                if (len > maxSegmentLengthFt)
                {
                    hasLongLine = true;
                }
            }

            double width = Math.Max(0.0, maxX - minX);
            double depth = Math.Max(0.0, maxY - minY);
            double area = width * depth;
            double minSide = Math.Max(1e-9, Math.Min(width, depth));
            double maxSide = Math.Max(width, depth);
            double aspect = maxSide / minSide;
            double perimeter = 2.0 * (width + depth);
            double fill = perimeter <= 1e-9 ? 0.0 : Math.Min(2.0, totalLen / perimeter);
            double rectness = Math.Max(0.0, Math.Min(1.0, fill));

            return new ColumnCandidate
            {
                ShapeType = "Rect",
                ClusterId = cluster.ClusterId,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Center = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0),
                OriginalCenter = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0),
                WidthFt = width,
                DepthFt = depth,
                AreaFt2 = area,
                AspectRatio = aspect,
                SegmentCount = cluster.Segments.Count,
                HasLongLine = hasLongLine,
                Rectness = rectness,
                FillRatio = fill,
                FragmentMerged = cluster.SourceClusterIds.Count > 1,
                FragmentSourceClusterIds = string.Join(",", cluster.SourceClusterIds.OrderBy(x => x)),
                FragmentMergeReason = cluster.MergeReason,
                // 中文注释：复制来源线段，供后续柱方向自动旋转逻辑使用。
                SourceSegments = cluster.Segments.Where(x => x != null).ToList()
            };
        }

        private static ColumnCandidate TryBuildIrregularCandidate(
            SegmentCluster cluster,
            double maxSegmentLengthFt,
            double endpointTolFt,
            ColumnIrregularSettings irregular,
            List<CadSegment> helperPool)
        {
            if (cluster == null || cluster.Segments == null || cluster.Segments.Count == 0)
            {
                return null;
            }

            List<XYZ> footprint;
            LoopBuildInfo loopInfo;
            if (!TryBuildClosedLoopGraph(cluster.Segments, endpointTolFt, out footprint, out loopInfo))
            {
                if (irregular == null || !irregular.EnableHelperEdges)
                {
                    return null;
                }

                double gapTolFt = irregular.GapTolMm / MmPerFt;
                double maxVirtualEdgeLenFt = irregular.MaxVirtualEdgeLenMm / MmPerFt;
                double maxHelperLenFt = irregular.MaxHelperEdgeLenMm / MmPerFt;
                int maxHelperEdges = Math.Max(1, irregular.MaxHelperEdges);
                if (!TryBuildClosedLoopWithHelpers(
                    cluster.Segments,
                    helperPool,
                    endpointTolFt,
                    gapTolFt,
                    maxVirtualEdgeLenFt,
                    maxHelperLenFt,
                    maxHelperEdges,
                    out footprint,
                    out loopInfo))
                {
                    return null;
                }
            }

            if (footprint == null || footprint.Count < 4)
            {
                return null;
            }

            double totalLen = 0.0;
            bool hasLongLine = false;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (CadSegment s in cluster.Segments.Where(x => x != null && x.P0 != null && x.P1 != null))
            {
                UpdateBounds(s.P0, ref minX, ref minY, ref maxX, ref maxY);
                UpdateBounds(s.P1, ref minX, ref minY, ref maxX, ref maxY);
                double len = s.P0.DistanceTo(s.P1);
                totalLen += len;
                if (len > maxSegmentLengthFt)
                {
                    hasLongLine = true;
                }
            }

            double areaFt2 = Math.Abs(ComputePolygonSignedArea(footprint));
            double perimeter = ComputePerimeter(footprint);
            double fill = perimeter <= 1e-9 ? 0.0 : Math.Min(2.0, totalLen / perimeter);
            double rectness = Math.Max(0.0, Math.Min(1.0, fill));

            double obbWidth;
            double obbDepth;
            double obbAngleRad;
            ComputeObbByPca(footprint, out obbWidth, out obbDepth, out obbAngleRad);
            double minSide = Math.Max(1e-9, Math.Min(obbWidth, obbDepth));
            double maxSide = Math.Max(obbWidth, obbDepth);
            double aspect = maxSide / minSide;
            XYZ center = ComputeFootprintCenter(footprint);

            return new ColumnCandidate
            {
                ShapeType = "Irregular",
                ClusterId = cluster.ClusterId,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Center = center,
                OriginalCenter = center,
                WidthFt = obbWidth,
                DepthFt = obbDepth,
                AreaFt2 = areaFt2,
                FootprintAreaFt2 = areaFt2,
                AspectRatio = aspect,
                SegmentCount = cluster.Segments.Count,
                HasLongLine = hasLongLine,
                Rectness = rectness,
                FillRatio = fill,
                Footprint = footprint,
                ObbWidthFt = obbWidth,
                ObbDepthFt = obbDepth,
                ObbAngleRad = obbAngleRad,
                IrregularClosedBySelf = loopInfo != null && loopInfo.ClosedBySelf,
                HelperEdgeUsedCount = loopInfo == null ? 0 : loopInfo.HelperEdgeUsedCount,
                DanglingEndpoints = loopInfo == null ? 0 : loopInfo.DanglingEndpoints,
                GapHealed = loopInfo != null && !loopInfo.ClosedBySelf && loopInfo.HelperEdgeUsedCount > 0,
                FragmentMerged = cluster.SourceClusterIds.Count > 1,
                FragmentSourceClusterIds = string.Join(",", cluster.SourceClusterIds.OrderBy(x => x)),
                FragmentMergeReason = cluster.MergeReason,
                // 中文注释：保留来源线段，异形柱也可复用后续分析链路。
                SourceSegments = cluster.Segments.Where(x => x != null).ToList()
            };
        }

        private static bool TryBuildClosedLoopGraph(
            List<CadSegment> segments,
            double tolFt,
            out List<XYZ> loop,
            out LoopBuildInfo info)
        {
            loop = null;
            info = new LoopBuildInfo();
            List<CadSegment> valid = (segments ?? new List<CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
            if (valid.Count < 3)
            {
                return false;
            }

            List<XYZ> nodes;
            List<Tuple<int, int>> edges;
            List<List<int>> adjacency;
            BuildSnappedGraph(valid, tolFt, out nodes, out edges, out adjacency);
            if (nodes.Count < 3 || edges.Count < 3)
            {
                info.DanglingEndpoints = 0;
                return false;
            }

            info.DanglingEndpoints = adjacency.Count(x => x.Count == 1);
            List<int> nodePath;
            if (!TryTraceOuterLoop(nodes, adjacency, out nodePath))
            {
                return false;
            }

            List<XYZ> chain = nodePath.Select(idx => nodes[idx]).ToList();
            List<XYZ> simplified = SimplifyPolyline(chain, Math.Max(1e-6, tolFt * 0.2));
            if (simplified.Count < 4)
            {
                return false;
            }

            info.ClosedBySelf = true;
            info.HelperEdgeUsedCount = 0;
            loop = simplified;
            return true;
        }

        private static bool TryBuildClosedLoopWithHelpers(
            List<CadSegment> sourceSegments,
            List<CadSegment> helperPool,
            double endpointTolFt,
            double gapTolFt,
            double maxVirtualEdgeLenFt,
            double maxHelperLenFt,
            int maxHelperEdges,
            out List<XYZ> loop,
            out LoopBuildInfo info)
        {
            loop = null;
            info = new LoopBuildInfo();

            List<XYZ> nodes;
            List<Tuple<int, int>> edges;
            List<List<int>> adjacency;
            BuildSnappedGraph(sourceSegments, endpointTolFt, out nodes, out edges, out adjacency);
            List<int> dangling = Enumerable.Range(0, adjacency.Count).Where(i => adjacency[i].Count == 1).ToList();
            info.DanglingEndpoints = dangling.Count;
            if (dangling.Count != 2)
            {
                return false;
            }

            XYZ a = nodes[dangling[0]];
            XYZ b = nodes[dangling[1]];
            double gapLen = a.DistanceTo(b);
            if (gapLen > gapTolFt || gapLen > maxVirtualEdgeLenFt)
            {
                return false;
            }

            List<CadSegment> helpers = FindHelperBridges(a, b, helperPool, gapTolFt, maxHelperLenFt, maxHelperEdges);
            if (helpers.Count == 0)
            {
                return false;
            }

            List<CadSegment> merged = new List<CadSegment>(sourceSegments ?? new List<CadSegment>());
            merged.AddRange(helpers);

            List<XYZ> mergedNodes;
            List<Tuple<int, int>> mergedEdges;
            List<List<int>> mergedAdjacency;
            BuildSnappedGraph(merged, endpointTolFt, out mergedNodes, out mergedEdges, out mergedAdjacency);
            List<int> nodePath;
            if (!TryTraceOuterLoop(mergedNodes, mergedAdjacency, out nodePath))
            {
                return false;
            }

            List<XYZ> chain = nodePath.Select(idx => mergedNodes[idx]).ToList();
            List<XYZ> simplified = SimplifyPolyline(chain, Math.Max(1e-6, endpointTolFt * 0.2));
            if (simplified.Count < 4)
            {
                return false;
            }

            info.ClosedBySelf = false;
            info.HelperEdgeUsedCount = helpers.Count;
            loop = simplified;
            return true;
        }

        private static List<CadSegment> FindHelperBridges(
            XYZ a,
            XYZ b,
            List<CadSegment> helperPool,
            double gapTolFt,
            double maxHelperLenFt,
            int maxHelperEdges)
        {
            List<CadSegment> result = new List<CadSegment>();
            if (a == null || b == null || helperPool == null || helperPool.Count == 0 || maxHelperEdges <= 0)
            {
                return result;
            }

            XYZ ab = b - a;
            double abLen = ab.GetLength();
            if (abLen <= 1e-9)
            {
                return result;
            }

            CadSegment best = null;
            double bestScore = double.MaxValue;
            foreach (CadSegment helper in helperPool)
            {
                if (helper == null || helper.P0 == null || helper.P1 == null)
                {
                    continue;
                }

                XYZ h0 = ToPlanar(helper.P0);
                XYZ h1 = ToPlanar(helper.P1);
                double len = h0.DistanceTo(h1);
                if (len <= 1e-9 || len > maxHelperLenFt)
                {
                    continue;
                }

                double dA = PointToSegmentDistance(a, h0, h1);
                double dB = PointToSegmentDistance(b, h0, h1);
                if (dA > gapTolFt || dB > gapTolFt)
                {
                    continue;
                }

                XYZ hv = h1 - h0;
                double hvLen = hv.GetLength();
                if (hvLen <= 1e-9)
                {
                    continue;
                }

                double align = Math.Abs((ab.X * hv.X + ab.Y * hv.Y) / (abLen * hvLen));
                if (align < 0.75)
                {
                    continue;
                }

                double score = dA + dB + (1.0 - align);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = helper;
                }
            }

            // 中文注释：缺口修复统一使用端点直连，借助 helper 仅用于提升判定可信度。
            result.Add(new CadSegment
            {
                P0 = a,
                P1 = b,
                RawLayerName = "__HELPER__",
                LayerName = "__HELPER__",
                NormalizedLayer = "__HELPER__",
                SemanticLayer = "__HELPER__"
            });

            return result.Take(maxHelperEdges).ToList();
        }

        private static void BuildSnappedGraph(
            List<CadSegment> segments,
            double tolFt,
            out List<XYZ> nodes,
            out List<Tuple<int, int>> edges,
            out List<List<int>> adjacency)
        {
            nodes = new List<XYZ>();
            edges = new List<Tuple<int, int>>();
            adjacency = new List<List<int>>();
            foreach (CadSegment s in (segments ?? new List<CadSegment>()).Where(x => x != null && x.P0 != null && x.P1 != null))
            {
                XYZ p0 = ToPlanar(s.P0);
                XYZ p1 = ToPlanar(s.P1);
                if (p0.DistanceTo(p1) <= 1e-9)
                {
                    continue;
                }

                int n0 = FindOrCreateNode(nodes, p0, tolFt);
                int n1 = FindOrCreateNode(nodes, p1, tolFt);
                if (n0 == n1)
                {
                    continue;
                }

                int a = Math.Min(n0, n1);
                int b = Math.Max(n0, n1);
                if (edges.Any(x => x.Item1 == a && x.Item2 == b))
                {
                    continue;
                }

                edges.Add(Tuple.Create(a, b));
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                adjacency.Add(new List<int>());
            }

            foreach (Tuple<int, int> edge in edges)
            {
                adjacency[edge.Item1].Add(edge.Item2);
                adjacency[edge.Item2].Add(edge.Item1);
            }
        }

        private static int FindOrCreateNode(List<XYZ> nodes, XYZ p, double tolFt)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].DistanceTo(p) <= tolFt)
                {
                    XYZ merged = new XYZ((nodes[i].X + p.X) * 0.5, (nodes[i].Y + p.Y) * 0.5, 0.0);
                    nodes[i] = merged;
                    return i;
                }
            }

            nodes.Add(p);
            return nodes.Count - 1;
        }

        private static bool TryTraceOuterLoop(List<XYZ> nodes, List<List<int>> adjacency, out List<int> nodePath)
        {
            nodePath = null;
            if (nodes == null || adjacency == null || nodes.Count < 3 || adjacency.Count != nodes.Count)
            {
                return false;
            }

            int start = Enumerable.Range(0, nodes.Count)
                .OrderBy(i => nodes[i].X)
                .ThenBy(i => nodes[i].Y)
                .First();
            List<int> firstCandidates = adjacency[start];
            if (firstCandidates == null || firstCandidates.Count == 0)
            {
                return false;
            }

            int first = firstCandidates
                .OrderBy(i =>
                {
                    XYZ d = nodes[i] - nodes[start];
                    return Math.Atan2(d.Y, d.X);
                })
                .First();

            List<int> path = new List<int> { start };
            HashSet<string> visitedDirected = new HashSet<string>(StringComparer.Ordinal);
            int prev = start;
            int current = first;
            XYZ incoming = new XYZ(-1, 0, 0);
            for (int guard = 0; guard < nodes.Count * 8; guard++)
            {
                path.Add(current);
                string key = prev + "->" + current;
                if (visitedDirected.Contains(key))
                {
                    return false;
                }

                visitedDirected.Add(key);
                if (current == start && path.Count > 3)
                {
                    if (Math.Abs(ComputePolygonSignedArea(path.Select(i => nodes[i]).ToList())) <= 1e-9)
                    {
                        return false;
                    }

                    nodePath = path;
                    return true;
                }

                List<int> neighbors = adjacency[current];
                if (neighbors == null || neighbors.Count == 0)
                {
                    return false;
                }

                XYZ baseDir = nodes[current] - nodes[prev];
                if (baseDir.GetLength() <= 1e-9)
                {
                    baseDir = incoming;
                }

                int next = -1;
                double bestTurn = -1.0;
                foreach (int n in neighbors)
                {
                    if (n == prev && neighbors.Count > 1)
                    {
                        continue;
                    }

                    XYZ dir = nodes[n] - nodes[current];
                    if (dir.GetLength() <= 1e-9)
                    {
                        continue;
                    }

                    double turn = ComputeCcwTurn(baseDir, dir);
                    if (turn > bestTurn)
                    {
                        bestTurn = turn;
                        next = n;
                    }
                }

                if (next < 0)
                {
                    return false;
                }

                incoming = nodes[next] - nodes[current];
                prev = current;
                current = next;
            }

            return false;
        }

        private static double ComputeCcwTurn(XYZ from, XYZ to)
        {
            double cross = from.X * to.Y - from.Y * to.X;
            double dot = from.X * to.X + from.Y * to.Y;
            double angle = Math.Atan2(cross, dot);
            if (angle < 0)
            {
                angle += Math.PI * 2.0;
            }

            return angle;
        }

        private static List<XYZ> SimplifyPolyline(List<XYZ> points, double tolFt)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p == null)
                {
                    continue;
                }

                if (result.Count == 0 || result[result.Count - 1].DistanceTo(p) > tolFt)
                {
                    result.Add(p);
                }
            }

            if (result.Count >= 2 && result[0].DistanceTo(result[result.Count - 1]) > tolFt)
            {
                result.Add(result[0]);
            }

            return result;
        }

        private static double ComputePolygonSignedArea(List<XYZ> loop)
        {
            if (loop == null || loop.Count < 4)
            {
                return 0.0;
            }

            double area2 = 0.0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                XYZ a = loop[i];
                XYZ b = loop[i + 1];
                area2 += a.X * b.Y - b.X * a.Y;
            }

            return area2 * 0.5;
        }

        private static double ComputePerimeter(List<XYZ> loop)
        {
            if (loop == null || loop.Count < 2)
            {
                return 0.0;
            }

            double perimeter = 0.0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                perimeter += loop[i].DistanceTo(loop[i + 1]);
            }

            return perimeter;
        }

        private static XYZ ComputeFootprintCenter(List<XYZ> loop)
        {
            if (loop == null || loop.Count < 4)
            {
                return XYZ.Zero;
            }

            double area2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                XYZ a = loop[i];
                XYZ b = loop[i + 1];
                double cross = a.X * b.Y - b.X * a.Y;
                area2 += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            if (Math.Abs(area2) <= 1e-9)
            {
                double avgX = loop.Take(loop.Count - 1).Average(x => x.X);
                double avgY = loop.Take(loop.Count - 1).Average(x => x.Y);
                return new XYZ(avgX, avgY, 0);
            }

            double factor = 1.0 / (3.0 * area2);
            return new XYZ(cx * factor, cy * factor, 0);
        }

        private static void ComputeObbByPca(List<XYZ> loop, out double width, out double depth, out double angleRad)
        {
            width = 0.0;
            depth = 0.0;
            angleRad = 0.0;
            if (loop == null || loop.Count < 4)
            {
                return;
            }

            List<XYZ> points = loop.Take(loop.Count - 1).ToList();
            double meanX = points.Average(x => x.X);
            double meanY = points.Average(x => x.Y);
            double sxx = 0.0;
            double sxy = 0.0;
            double syy = 0.0;
            foreach (XYZ p in points)
            {
                double dx = p.X - meanX;
                double dy = p.Y - meanY;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            angleRad = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            ComputeRotatedBounds(points, angleRad, out width, out depth);
            if (depth > width)
            {
                double t = width;
                width = depth;
                depth = t;
                angleRad += Math.PI * 0.5;
            }
        }

        private static void ComputeRotatedBounds(List<XYZ> points, double angleRad, out double width, out double depth)
        {
            double c = Math.Cos(angleRad);
            double s = Math.Sin(angleRad);
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (XYZ p in points)
            {
                double x = p.X * c + p.Y * s;
                double y = -p.X * s + p.Y * c;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            width = Math.Max(0.0, maxX - minX);
            depth = Math.Max(0.0, maxY - minY);
        }

        private static XYZ ToPlanar(XYZ p)
        {
            return new XYZ(p.X, p.Y, 0.0);
        }

        private static void ScoreCandidates(List<ColumnCandidate> candidates, ColumnScoreSettings score)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            double maxArea = Math.Max(1e-9, candidates.Max(x => x.AreaFt2));
            double maxSeg = Math.Max(1e-9, candidates.Max(x => (double)x.SegmentCount));
            foreach (ColumnCandidate c in candidates)
            {
                double areaNorm = c.AreaFt2 / maxArea;
                double segNorm = c.SegmentCount / maxSeg;
                c.Score = score.AreaWeight * areaNorm +
                          score.SegmentCountWeight * segNorm +
                          score.RectnessWeight * c.Rectness -
                          score.LongLinePenalty * (c.HasLongLine ? 1.0 : 0.0);
            }
        }

        private static List<ColumnCandidate> MergeAndDedupeCandidates(List<ColumnCandidate> input, ColumnMergeSettings merge)
        {
            if (input == null || input.Count == 0 || !merge.Enable)
            {
                return input ?? new List<ColumnCandidate>();
            }

            double mergeTolFt = merge.MergeTolMm / MmPerFt;
            bool[] used = new bool[input.Count];
            List<ColumnCandidate> result = new List<ColumnCandidate>();
            for (int i = 0; i < input.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                List<ColumnCandidate> group = new List<ColumnCandidate>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                used[i] = true;
                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    ColumnCandidate current = input[idx];
                    group.Add(current);
                    for (int j = 0; j < input.Count; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        if (current.Center.DistanceTo(input[j].Center) <= mergeTolFt)
                        {
                            used[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                result.Add(SelectMergedCandidate(group, merge.Strategy));
            }

            return result;
        }

        private static ColumnCandidate SelectMergedCandidate(List<ColumnCandidate> group, string strategy)
        {
            if (group == null || group.Count == 0)
            {
                return null;
            }

            if (string.Equals(strategy, "MaxArea", StringComparison.OrdinalIgnoreCase))
            {
                ColumnCandidate best = group.OrderByDescending(x => x.AreaFt2).First();
                best.MergeAction = "MaxArea";
                return best;
            }

            if (string.Equals(strategy, "UnionBbox", StringComparison.OrdinalIgnoreCase))
            {
                double minX = group.Min(x => x.MinX);
                double minY = group.Min(x => x.MinY);
                double maxX = group.Max(x => x.MaxX);
                double maxY = group.Max(x => x.MaxY);
                ColumnCandidate seed = group.OrderByDescending(x => x.Score).First();
                seed.MinX = minX;
                seed.MinY = minY;
                seed.MaxX = maxX;
                seed.MaxY = maxY;
                seed.WidthFt = maxX - minX;
                seed.DepthFt = maxY - minY;
                seed.AreaFt2 = seed.WidthFt * seed.DepthFt;
                seed.Center = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
                seed.MergeAction = "UnionBbox";
                return seed;
            }

            ColumnCandidate keepBest = group.OrderByDescending(x => x.Score).First();
            keepBest.MergeAction = "KeepBest";
            return keepBest;
        }

        private static void AttachToWall(List<ColumnCandidate> candidates, ColumnAttachToWallSettings attach, Document doc)
        {
            if (candidates == null || candidates.Count == 0 || !attach.Enable || doc == null)
            {
                return;
            }

            List<WallCenterlineInfo> wallLines = GetWallCenterlines(doc);
            if (wallLines.Count == 0)
            {
                return;
            }

            double tolFt = attach.SnapTolMm / MmPerFt;
            foreach (ColumnCandidate candidate in candidates)
            {
                WallCenterlineInfo nearest = null;
                XYZ nearestPoint = null;
                double nearestDist = double.MaxValue;
                foreach (WallCenterlineInfo wall in wallLines)
                {
                    XYZ projected = ProjectPointToLine(candidate.Center, wall.Centerline);
                    double dist = projected.DistanceTo(candidate.Center);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = wall;
                        nearestPoint = projected;
                    }
                }

                if (nearest != null && nearestPoint != null && nearestDist <= tolFt)
                {
                    candidate.Center = new XYZ(nearestPoint.X, nearestPoint.Y, candidate.Center.Z);
                    candidate.AttachInfo = "Attached:WallId=" + nearest.WallId.IntegerValue;
                }
            }
        }

        private static List<WallCenterlineInfo> GetWallCenterlines(Document doc)
        {
            List<WallCenterlineInfo> result = new List<WallCenterlineInfo>();
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall == null)
                {
                    continue;
                }

                LocationCurve location = wall.Location as LocationCurve;
                Line line = location?.Curve as Line;
                if (line == null)
                {
                    continue;
                }

                result.Add(new WallCenterlineInfo
                {
                    WallId = wall.Id,
                    Centerline = line
                });
            }

            return result;
        }

        private static List<ColumnCandidate> DedupeAgainstPlacedColumns(
            List<ColumnCandidate> candidates,
            ColumnMergeSettings merge,
            Document doc,
            List<ColumnCandidate> rejected)
        {
            if (candidates == null || candidates.Count == 0 || doc == null)
            {
                return candidates ?? new List<ColumnCandidate>();
            }

            double tolFt = merge.DedupePlacedTolMm / MmPerFt;
            List<XYZ> placedCenters = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Select(x => x.Location as LocationPoint)
                .Where(x => x != null)
                .Select(x => x.Point)
                .Where(x => x != null)
                .ToList();

            if (placedCenters.Count == 0)
            {
                return candidates;
            }

            List<ColumnCandidate> result = new List<ColumnCandidate>();
            foreach (ColumnCandidate candidate in candidates)
            {
                bool duplicated = placedCenters.Any(x => x.DistanceTo(candidate.Center) <= tolFt);
                if (duplicated)
                {
                    Reject(candidate, "DuplicatedWithPlaced");
                    rejected?.Add(candidate);
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        private static string ExportReport(string rawLayerName, ColumnDetectionResult result, ColumnRecognitionDefaults settings)
        {
            try
            {
                string root = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(root, "m10_5_column_report_" + stamp + ".json");

                StringBuilder sb = new StringBuilder();
                // 中文注释：为售后定位问题保留完整候选、剔除与最终结果。
                sb.AppendLine("{");
                sb.AppendLine("  \"layer\": \"" + Escape(rawLayerName) + "\",");
                sb.AppendLine("  \"inputSegments\": " + result.InputSegmentCount + ",");
                sb.AppendLine("  \"prefilterSegments\": " + result.PrefilterSegmentCount + ",");
                sb.AppendLine("  \"acceptedCount\": " + result.Candidates.Count + ",");
                sb.AppendLine("  \"rejectedCount\": " + result.RejectedCandidates.Count + ",");
                sb.AppendLine("  \"acceptedRectCount\": " + result.Candidates.Count(x => string.Equals(x.ShapeType, "Rect", StringComparison.OrdinalIgnoreCase)) + ",");
                sb.AppendLine("  \"acceptedIrregularCount\": " + result.Candidates.Count(x => string.Equals(x.ShapeType, "Irregular", StringComparison.OrdinalIgnoreCase)) + ",");
                sb.AppendLine("  \"settings\": {");
                sb.AppendLine("    \"algorithm\": \"" + Escape(settings.Cluster.Algorithm) + "\",");
                sb.AppendLine("    \"clusterTolMm\": " + settings.Cluster.ClusterTolMm.ToString("F2") + ",");
                sb.AppendLine("    \"minGroupSegments\": " + settings.Cluster.MinGroupSegments + ",");
                sb.AppendLine("    \"mergeStrategy\": \"" + Escape(settings.Merge.Strategy) + "\",");
                sb.AppendLine("    \"columnIrregularEnable\": " + (settings.Irregular.Enable ? "true" : "false") + ",");
                sb.AppendLine("    \"irregularMaxSizeMm\": " + settings.Irregular.MaxSizeMm.ToString("F2") + ",");
                sb.AppendLine("    \"irregularMinAreaM2\": " + settings.Irregular.MinAreaM2.ToString("F4") + ",");
                sb.AppendLine("    \"irregularGapTolMm\": " + settings.Irregular.GapTolMm.ToString("F2") + ",");
                sb.AppendLine("    \"irregularMinGroupSegments\": " + settings.Irregular.MinGroupSegments + ",");
                sb.AppendLine("    \"irregularFragmentMergeTolMm\": " + settings.Irregular.FragmentMergeTolMm.ToString("F2") + ",");
                sb.AppendLine("    \"irregularMaxVirtualEdgeLenMm\": " + settings.Irregular.MaxVirtualEdgeLenMm.ToString("F2"));
                sb.AppendLine("  },");
                sb.AppendLine("  \"accepted\": [");
                for (int i = 0; i < result.Candidates.Count; i++)
                {
                    ColumnCandidate c = result.Candidates[i];
                    sb.Append("    " + CandidateToJson(c));
                    sb.AppendLine(i == result.Candidates.Count - 1 ? string.Empty : ",");
                }

                sb.AppendLine("  ],");
                sb.AppendLine("  \"rejected\": [");
                for (int i = 0; i < result.RejectedCandidates.Count; i++)
                {
                    ColumnCandidate c = result.RejectedCandidates[i];
                    sb.Append("    " + CandidateToJson(c));
                    sb.AppendLine(i == result.RejectedCandidates.Count - 1 ? string.Empty : ",");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ColumnReport] export failed: " + ex.Message);
                return string.Empty;
            }
        }

        private static string CandidateToJson(ColumnCandidate c)
        {
            if (c == null)
            {
                return "{}";
            }

            return "{" +
                   "\"clusterId\":" + c.ClusterId + "," +
                   "\"shapeType\":\"" + Escape(c.ShapeType) + "\"," +
                   "\"center\":\"(" + c.Center.X.ToString("F4") + "," + c.Center.Y.ToString("F4") + ")\"," +
                   "\"widthFt\":" + c.WidthFt.ToString("F4") + "," +
                   "\"depthFt\":" + c.DepthFt.ToString("F4") + "," +
                   "\"segmentCount\":" + c.SegmentCount + "," +
                   "\"irregularClosedBySelf\":" + (c.IrregularClosedBySelf ? "true" : "false") + "," +
                   "\"helperEdgeUsedCount\":" + c.HelperEdgeUsedCount + "," +
                   "\"danglingEndpoints\":" + c.DanglingEndpoints + "," +
                   "\"gapHealed\":" + (c.GapHealed ? "true" : "false") + "," +
                   "\"fragmentMerged\":" + (c.FragmentMerged ? "true" : "false") + "," +
                   "\"fragmentSourceClusterIds\":\"" + Escape(c.FragmentSourceClusterIds) + "\"," +
                   "\"fragmentMergeReason\":\"" + Escape(c.FragmentMergeReason) + "\"," +
                   "\"score\":" + c.Score.ToString("F6") + "," +
                   "\"rejectReason\":\"" + Escape(c.RejectReason) + "\"," +
                   "\"mergeAction\":\"" + Escape(c.MergeAction) + "\"," +
                   "\"attachInfo\":\"" + Escape(c.AttachInfo) + "\"" +
                   "}";
        }

        private static void Reject(ColumnCandidate candidate, string reason)
        {
            if (candidate == null)
            {
                return;
            }

            candidate.IsRejected = true;
            if (string.IsNullOrWhiteSpace(candidate.RejectReason))
            {
                candidate.RejectReason = reason;
            }
        }

        private static XYZ Midpoint(CadSegment segment)
        {
            return new XYZ(
                (segment.P0.X + segment.P1.X) * 0.5,
                (segment.P0.Y + segment.P1.Y) * 0.5,
                (segment.P0.Z + segment.P1.Z) * 0.5);
        }

        private static void UpdateBounds(XYZ p, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (p.X < minX)
            {
                minX = p.X;
            }

            if (p.Y < minY)
            {
                minY = p.Y;
            }

            if (p.X > maxX)
            {
                maxX = p.X;
            }

            if (p.Y > maxY)
            {
                maxY = p.Y;
            }
        }

        private static XYZ ProjectPointToLine(XYZ point, Line line)
        {
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ v = p1 - p0;
            double len2 = v.DotProduct(v);
            if (len2 <= 1e-9)
            {
                return p0;
            }

            double t = (point - p0).DotProduct(v) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return p0 + v.Multiply(t);
        }

        private static double PointToSegmentDistance(XYZ point, XYZ a, XYZ b)
        {
            XYZ projected = ProjectPointToLine(point, Line.CreateBound(a, b));
            return projected.DistanceTo(point);
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
