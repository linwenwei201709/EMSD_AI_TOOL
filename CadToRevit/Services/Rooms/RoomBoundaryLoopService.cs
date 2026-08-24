using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class RoomBoundaryLoopService
    {
        private const double MmPerFt = 304.8;

        public static List<RoomCandidate> Detect(
            CadDataset dataset,
            string boundaryLayerName,
            double closeTolMm,
            double maxPatchMm,
            double minAreaM2)
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(boundaryLayerName))
            {
                layers.Add(boundaryLayerName);
            }

            return DetectMulti(dataset, layers, closeTolMm, maxPatchMm, minAreaM2, maxPatchMm, Math.Min(350.0, maxPatchMm));
        }

        public static List<RoomCandidate> DetectMulti(
            CadDataset dataset,
            IEnumerable<string> boundaryLayers,
            double closeTolMm,
            double maxPatchMm,
            double minAreaM2,
            double doorGapMaxMm,
            double smallGapPatchMaxMm,
            bool keepLargestFace = false,
            bool normalizeBoundarySegments = true)
        {
            HashSet<string> layerSet = new HashSet<string>(
                (boundaryLayers ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            bool isModelBoundaryDriven = IsModelBoundaryDrivenLoopDetection(layerSet);
            bool effectiveKeepLargestFace = keepLargestFace || isModelBoundaryDriven;
            List<CadSegment> source = (dataset?.Segments ?? new List<CadSegment>())
                .Where(x => x != null &&
                            x.P0 != null &&
                            x.P1 != null &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                            layerSet.Contains(x.RawLayerName))
                .ToList();
            if (normalizeBoundarySegments)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomBoundaryLoop] DetectMulti: boundary normalization enabled, SourceSegments=" + source.Count);
                source = NormalizeBoundarySegmentsForRooms(source);
            }
            else
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomBoundaryLoop] DetectMulti: boundary normalization skipped, SourceSegments=" + source.Count);
            }
            if (source.Count == 0)
            {
                return new List<RoomCandidate>();
            }

            double closeTolFt = Math.Max(1.0, closeTolMm) / MmPerFt;
            double maxPatchFt = Math.Max(closeTolMm, maxPatchMm) / MmPerFt;
            double minAreaFt2 = Math.Max(0.1, minAreaM2) * 10.7639104167;

            List<XYZ> nodes;
            List<Tuple<int, int>> edges;
            List<List<int>> adjacency;
            BuildSnappedGraph(
                source,
                closeTolFt,
                Math.Max(0.0, doorGapMaxMm) / MmPerFt,
                Math.Max(0.0, smallGapPatchMaxMm) / MmPerFt,
                out nodes,
                out edges,
                out adjacency);

            List<List<Tuple<int, int>>> components = SplitEdgeComponents(edges);
            DiagnosticRecorder.AppendDebug(
                "[RoomBoundaryLoop] DetectMulti: ModelBoundaryMode=" + isModelBoundaryDriven +
                ", KeepLargestFace=" + effectiveKeepLargestFace +
                ", ShellRemoval=" + (!isModelBoundaryDriven));
            List<RoomCandidate> result = new List<RoomCandidate>();
            int index = 1;
            foreach (List<Tuple<int, int>> component in components)
            {
                List<List<XYZ>> faceLoops = ExtractFaceLoops(component, nodes, effectiveKeepLargestFace);
                bool anyClosed = false;
                foreach (List<XYZ> faceLoop in faceLoops)
                {
                    RoomBoundaryStatus status;
                    double gapFt;
                    List<XYZ> closedLoop = CloseLoop(faceLoop, closeTolFt, maxPatchFt, out status, out gapFt);
                    if (status != RoomBoundaryStatus.NeedsFix && closedLoop.Count >= 4)
                    {
                        double areaFt2 = Math.Abs(ComputeArea(closedLoop));
                        if (areaFt2 < minAreaFt2)
                        {
                            continue;
                        }

                        result.Add(BuildCandidate(index++, string.Join(";", layerSet), closedLoop, areaFt2, status, gapFt));
                        anyClosed = true;
                    }
                }

                if (anyClosed)
                {
                    continue;
                }

                // Fallback for degenerate components where no valid face is extracted.
                List<XYZ> chain = TraceChainForComponent(component, nodes);
                if (chain == null || chain.Count < 3)
                {
                    continue;
                }

                RoomBoundaryStatus openStatus;
                double openGapFt;
                CloseLoop(chain, closeTolFt, maxPatchFt, out openStatus, out openGapFt);
                result.Add(BuildOpenCandidate(index++, string.Join(";", layerSet), chain, openStatus, openGapFt));
            }

            // 中文注释：默认按面积从大到小排序并自动编号。
            if (!isModelBoundaryDriven)
            {
                result = RemoveShellLoops(result);
            }
            else
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoomBoundaryLoop] DetectMulti: shell removal skipped for model boundary dataset, Candidates=" + result.Count);
            }
            List<RoomCandidate> closed = result
                .Where(x => x.Status != RoomBoundaryStatus.NeedsFix)
                .OrderByDescending(x => x.AreaM2)
                .ToList();
            for (int i = 0; i < closed.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(closed[i].Name))
                {
                    closed[i].Name = "ROOM-" + (i + 1).ToString("000");
                }
            }

            return result;
        }

        private static RoomCandidate BuildCandidate(
            int index,
            string layer,
            List<XYZ> loop,
            double areaFt2,
            RoomBoundaryStatus status,
            double gapFt)
        {
            BoundingBoxXYZ box = ComputeBBox(loop);
            return new RoomCandidate
            {
                Key = "loop_" + index.ToString("0000"),
                Name = string.Empty,
                Number = index.ToString("000"),
                AreaM2 = areaFt2 / 10.7639104167,
                Status = status,
                CloseGapMm = gapFt * MmPerFt,
                LoopPoints = loop,
                BBox = box,
                Centroid = ComputeCentroid(loop),
                SourceLayer = layer
            };
        }

        private static RoomCandidate BuildOpenCandidate(
            int index,
            string layer,
            List<XYZ> chain,
            RoomBoundaryStatus status,
            double gapFt)
        {
            List<XYZ> points = chain.Select(ToPlanar).ToList();
            return new RoomCandidate
            {
                Key = "loop_" + index.ToString("0000"),
                Name = "ROOM-" + index.ToString("000"),
                Number = index.ToString("000"),
                AreaM2 = 0.0,
                Status = status,
                CloseGapMm = gapFt * MmPerFt,
                LoopPoints = points,
                BBox = ComputeBBox(points),
                Centroid = points.Count == 0 ? XYZ.Zero : new XYZ(points.Average(x => x.X), points.Average(x => x.Y), 0),
                SourceLayer = layer
            };
        }

        private static void BuildSnappedGraph(
            List<CadSegment> segments,
            double tolFt,
            double doorGapMaxFt,
            double smallGapPatchMaxFt,
            out List<XYZ> nodes,
            out List<Tuple<int, int>> edges,
            out List<List<int>> adjacency)
        {
            nodes = new List<XYZ>();
            edges = new List<Tuple<int, int>>();
            adjacency = new List<List<int>>();

            List<CadSegment> graphSegments = SplitSegmentsForGraph(segments, tolFt);
            DiagnosticRecorder.AppendDebug(
                "[RoomBoundaryLoop] BuildSnappedGraph: SourceSegments=" + (segments == null ? 0 : segments.Count) +
                ", GraphSegments=" + graphSegments.Count +
                ", ToleranceFt=" + tolFt.ToString("F6"));

            foreach (CadSegment s in graphSegments)
            {
                int n0 = FindOrCreateNode(nodes, ToPlanar(s.P0), tolFt);
                int n1 = FindOrCreateNode(nodes, ToPlanar(s.P1), tolFt);
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

            foreach (Tuple<int, int> e in edges)
            {
                adjacency[e.Item1].Add(e.Item2);
                adjacency[e.Item2].Add(e.Item1);
            }

            // Patch small door-like gaps by linking nearby loose endpoints.
            if (doorGapMaxFt > tolFt)
            {
                PatchDoorGaps(nodes, edges, adjacency, doorGapMaxFt);
            }

            if (smallGapPatchMaxFt > tolFt)
            {
                PatchSmallGaps(nodes, edges, adjacency, smallGapPatchMaxFt);
            }
        }

        private static List<CadSegment> SplitSegmentsForGraph(List<CadSegment> segments, double tolFt)
        {
            List<CadSegment> source = (segments ?? new List<CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null && x.P0.DistanceTo(x.P1) > tolFt * 0.5)
                .ToList();
            if (source.Count <= 1)
            {
                return source;
            }

            List<List<XYZ>> splitPoints = source
                .Select(x => new List<XYZ> { ToPlanar(x.P0), ToPlanar(x.P1) })
                .ToList();

            for (int i = 0; i < source.Count; i++)
            {
                XYZ a0 = ToPlanar(source[i].P0);
                XYZ a1 = ToPlanar(source[i].P1);
                for (int j = i + 1; j < source.Count; j++)
                {
                    XYZ b0 = ToPlanar(source[j].P0);
                    XYZ b1 = ToPlanar(source[j].P1);

                    XYZ intersection;
                    if (TryGetSegmentIntersection2D(a0, a1, b0, b1, tolFt, out intersection))
                    {
                        AddUniquePoint(splitPoints[i], intersection, tolFt);
                        AddUniquePoint(splitPoints[j], intersection, tolFt);
                    }

                    if (AreCollinear2D(a0, a1, b0, b1, tolFt))
                    {
                        AddEndpointIfOnSegment(splitPoints[i], b0, a0, a1, tolFt);
                        AddEndpointIfOnSegment(splitPoints[i], b1, a0, a1, tolFt);
                        AddEndpointIfOnSegment(splitPoints[j], a0, b0, b1, tolFt);
                        AddEndpointIfOnSegment(splitPoints[j], a1, b0, b1, tolFt);
                    }
                }
            }

            List<CadSegment> result = new List<CadSegment>();
            int nextId = source.Count > 0 ? source.Max(x => x != null ? x.SegmentId : 0) + 1 : 1;
            for (int i = 0; i < source.Count; i++)
            {
                CadSegment s = source[i];
                XYZ start = ToPlanar(s.P0);
                XYZ end = ToPlanar(s.P1);
                XYZ dir = end - start;
                double len = dir.GetLength();
                if (len <= tolFt * 0.5)
                {
                    continue;
                }

                XYZ unit = dir.Normalize();
                List<XYZ> ordered = splitPoints[i]
                    .Select(ToPlanar)
                    .GroupBy(x => FindEquivalentPointIndex(splitPoints[i], x, tolFt))
                    .Select(g => g.First())
                    .OrderBy(x => (x - start).DotProduct(unit))
                    .ToList();
                if (ordered.Count < 2)
                {
                    continue;
                }

                for (int k = 0; k < ordered.Count - 1; k++)
                {
                    XYZ p0 = ordered[k];
                    XYZ p1 = ordered[k + 1];
                    if (p0.DistanceTo(p1) <= tolFt * 0.5)
                    {
                        continue;
                    }

                    CadSegment piece = new CadSegment
                    {
                        SegmentId = nextId++,
                        NormalizedLayer = s.NormalizedLayer,
                        SemanticLayer = s.SemanticLayer,
                        LayerName = s.LayerName,
                        RawLayerName = s.RawLayerName,
                        SourceType = s.SourceType,
                        IsArc = false,
                        P0 = p0,
                        P1 = p1,
                        MidPoint = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, 0.0)
                    };
                    result.Add(piece);
                }
            }

            return result;
        }

        private static void AddEndpointIfOnSegment(List<XYZ> bucket, XYZ point, XYZ segStart, XYZ segEnd, double tolFt)
        {
            if (bucket == null || point == null)
            {
                return;
            }

            XYZ planar = ToPlanar(point);
            if (IsPointOnSegment2D(planar, segStart, segEnd, tolFt))
            {
                AddUniquePoint(bucket, planar, tolFt);
            }
        }

        private static void AddUniquePoint(List<XYZ> points, XYZ point, double tolFt)
        {
            if (points == null || point == null)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].DistanceTo(point) <= tolFt)
                {
                    points[i] = new XYZ((points[i].X + point.X) * 0.5, (points[i].Y + point.Y) * 0.5, 0.0);
                    return;
                }
            }

            points.Add(ToPlanar(point));
        }

        private static int FindEquivalentPointIndex(List<XYZ> points, XYZ point, double tolFt)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].DistanceTo(point) <= tolFt)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryGetSegmentIntersection2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double tolFt, out XYZ intersection)
        {
            intersection = null;
            XYZ r = a1 - a0;
            XYZ s = b1 - b0;
            double rxs = Cross2D(r, s);
            double qpxr = Cross2D(b0 - a0, r);
            if (Math.Abs(rxs) <= 1e-9)
            {
                if (Math.Abs(qpxr) > tolFt)
                {
                    return false;
                }

                return false;
            }

            double t = Cross2D(b0 - a0, s) / rxs;
            double u = Cross2D(b0 - a0, r) / rxs;
            double min = -Math.Max(1e-9, tolFt);
            double max = 1.0 + Math.Max(1e-9, tolFt);
            if (t < min || t > max || u < min || u > max)
            {
                return false;
            }

            XYZ p = a0 + r.Multiply(t);
            if (!IsPointOnSegment2D(p, a0, a1, tolFt) || !IsPointOnSegment2D(p, b0, b1, tolFt))
            {
                return false;
            }

            intersection = ToPlanar(p);
            return true;
        }

        private static bool AreCollinear2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double tolFt)
        {
            XYZ a = a1 - a0;
            XYZ b = b1 - b0;
            if (a.GetLength() <= 1e-9 || b.GetLength() <= 1e-9)
            {
                return false;
            }

            if (Math.Abs(Cross2D(a, b)) > tolFt * 0.5)
            {
                return false;
            }

            return DistancePointToSegment2D(a0, b0, b1) <= tolFt ||
                   DistancePointToSegment2D(a1, b0, b1) <= tolFt ||
                   DistancePointToSegment2D(b0, a0, a1) <= tolFt ||
                   DistancePointToSegment2D(b1, a0, a1) <= tolFt;
        }

        private static bool IsPointOnSegment2D(XYZ p, XYZ a, XYZ b, double tolFt)
        {
            return DistancePointToSegment2D(p, a, b) <= tolFt;
        }

        private static double DistancePointToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double ab2 = ab.DotProduct(ab);
            if (ab2 <= 1e-12)
            {
                return p.DistanceTo(a);
            }

            double t = (p - a).DotProduct(ab) / ab2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            XYZ foot = a + ab.Multiply(t);
            return foot.DistanceTo(p);
        }

        private static double Cross2D(XYZ a, XYZ b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static void PatchDoorGaps(
            List<XYZ> nodes,
            List<Tuple<int, int>> edges,
            List<List<int>> adjacency,
            double doorGapMaxFt)
        {
            List<int> loose = Enumerable.Range(0, adjacency.Count)
                .Where(i => adjacency[i] != null && adjacency[i].Count == 1)
                .ToList();
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < loose.Count; i++)
            {
                int a = loose[i];
                if (used.Contains(a))
                {
                    continue;
                }

                int best = -1;
                double bestDist = double.MaxValue;
                for (int j = i + 1; j < loose.Count; j++)
                {
                    int b = loose[j];
                    if (used.Contains(b) || a == b)
                    {
                        continue;
                    }

                    double d = nodes[a].DistanceTo(nodes[b]);
                    if (d > doorGapMaxFt || d < 1e-9)
                    {
                        continue;
                    }

                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = b;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                int x = Math.Min(a, best);
                int y = Math.Max(a, best);
                if (!edges.Any(e => e.Item1 == x && e.Item2 == y))
                {
                    edges.Add(Tuple.Create(x, y));
                    adjacency[x].Add(y);
                    adjacency[y].Add(x);
                }

                used.Add(a);
                used.Add(best);
            }
        }

        private static void PatchSmallGaps(
            List<XYZ> nodes,
            List<Tuple<int, int>> edges,
            List<List<int>> adjacency,
            double smallGapMaxFt)
        {
            List<int> loose = Enumerable.Range(0, adjacency.Count)
                .Where(i => adjacency[i] != null && adjacency[i].Count == 1)
                .ToList();
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < loose.Count; i++)
            {
                int a = loose[i];
                if (used.Contains(a))
                {
                    continue;
                }

                int aRef = adjacency[a][0];
                XYZ aVec = nodes[a] - nodes[aRef];
                if (aVec.GetLength() < 1e-9)
                {
                    continue;
                }

                XYZ aDir = aVec.Normalize();
                int best = -1;
                double bestDist = double.MaxValue;
                for (int j = i + 1; j < loose.Count; j++)
                {
                    int b = loose[j];
                    if (used.Contains(b) || a == b)
                    {
                        continue;
                    }

                    double d = nodes[a].DistanceTo(nodes[b]);
                    if (d > smallGapMaxFt || d < 1e-9)
                    {
                        continue;
                    }

                    int bRef = adjacency[b][0];
                    XYZ bVec = nodes[b] - nodes[bRef];
                    if (bVec.GetLength() < 1e-9)
                    {
                        continue;
                    }

                    XYZ bDir = bVec.Normalize();
                    XYZ gapDir = (nodes[b] - nodes[a]).Normalize();
                    double collinear = aDir.DotProduct(bDir);
                    double alignA = Math.Abs(aDir.DotProduct(gapDir));
                    double alignB = Math.Abs(bDir.DotProduct(-gapDir));
                    if (collinear > -0.94 || alignA < 0.85 || alignB < 0.85)
                    {
                        continue;
                    }

                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = b;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                int x = Math.Min(a, best);
                int y = Math.Max(a, best);
                if (!edges.Any(e => e.Item1 == x && e.Item2 == y))
                {
                    edges.Add(Tuple.Create(x, y));
                    adjacency[x].Add(y);
                    adjacency[y].Add(x);
                }

                used.Add(a);
                used.Add(best);
            }
        }

        private static List<List<Tuple<int, int>>> SplitEdgeComponents(List<Tuple<int, int>> edges)
        {
            List<List<Tuple<int, int>>> result = new List<List<Tuple<int, int>>>();
            bool[] used = new bool[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                List<Tuple<int, int>> component = new List<Tuple<int, int>>();
                Queue<int> q = new Queue<int>();
                q.Enqueue(i);
                used[i] = true;
                while (q.Count > 0)
                {
                    int idx = q.Dequeue();
                    Tuple<int, int> cur = edges[idx];
                    component.Add(cur);
                    for (int j = 0; j < edges.Count; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        Tuple<int, int> other = edges[j];
                        if (other.Item1 == cur.Item1 || other.Item1 == cur.Item2 || other.Item2 == cur.Item1 || other.Item2 == cur.Item2)
                        {
                            used[j] = true;
                            q.Enqueue(j);
                        }
                    }
                }

                result.Add(component);
            }

            return result;
        }

        private static List<List<XYZ>> ExtractFaceLoops(List<Tuple<int, int>> component, List<XYZ> nodes, bool keepLargestFace)
        {
            List<List<XYZ>> result = new List<List<XYZ>>();
            if (component == null || component.Count == 0 || nodes == null || nodes.Count == 0)
            {
                return result;
            }

            Dictionary<int, List<int>> adjacency = BuildAdjacency(component);
            Dictionary<int, List<int>> sortedAdjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, List<int>> kv in adjacency)
            {
                int nodeId = kv.Key;
                sortedAdjacency[nodeId] = kv.Value
                    .Distinct()
                    .OrderBy(x => Math.Atan2(nodes[x].Y - nodes[nodeId].Y, nodes[x].X - nodes[nodeId].X))
                    .ToList();
            }

            HashSet<string> usedDirected = new HashSet<string>(StringComparer.Ordinal);
            List<List<int>> faceNodeLoops = new List<List<int>>();
            foreach (Tuple<int, int> edge in component)
            {
                int[] starts = { edge.Item1, edge.Item2 };
                int[] ends = { edge.Item2, edge.Item1 };
                for (int i = 0; i < 2; i++)
                {
                    int startU = starts[i];
                    int startV = ends[i];
                    string startKey = DirectedEdgeKey(startU, startV);
                    if (usedDirected.Contains(startKey))
                    {
                        continue;
                    }

                    List<int> loop = WalkFace(startU, startV, sortedAdjacency, usedDirected);
                    if (loop == null || loop.Count < 4)
                    {
                        continue;
                    }

                    double area = Math.Abs(ComputeArea(loop.Select(x => nodes[x]).ToList()));
                    if (area <= 1e-9)
                    {
                        continue;
                    }

                    faceNodeLoops.Add(loop);
                }
            }

            if (faceNodeLoops.Count == 0)
            {
                return result;
            }

            // By default the largest face is treated as the outer shell.
            // Local target-room retries can opt in to keeping it.
            List<Tuple<List<int>, double>> loopAreas = faceNodeLoops
                .Select(x => Tuple.Create(x, Math.Abs(ComputeArea(x.Select(n => nodes[n]).ToList()))))
                .OrderByDescending(x => x.Item2)
                .ToList();
            double maxArea = loopAreas[0].Item2;
            HashSet<string> dedupe = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < loopAreas.Count; i++)
            {
                List<int> loop = loopAreas[i].Item1;
                double area = loopAreas[i].Item2;
                if (!keepLargestFace && i == 0 && area >= maxArea * 0.999)
                {
                    continue;
                }

                string key = NormalizeLoopKey(loop);
                if (!dedupe.Add(key))
                {
                    continue;
                }

                List<XYZ> pts = loop.Select(n => nodes[n]).ToList();
                result.Add(EnsureClosed(Simplify(pts, 1e-6)));
            }

            return result;
        }

        private static Dictionary<int, List<int>> BuildAdjacency(List<Tuple<int, int>> component)
        {
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            foreach (Tuple<int, int> e in component)
            {
                if (!adjacency.ContainsKey(e.Item1))
                {
                    adjacency[e.Item1] = new List<int>();
                }

                if (!adjacency.ContainsKey(e.Item2))
                {
                    adjacency[e.Item2] = new List<int>();
                }

                adjacency[e.Item1].Add(e.Item2);
                adjacency[e.Item2].Add(e.Item1);
            }

            return adjacency;
        }

        private static List<int> WalkFace(
            int startU,
            int startV,
            Dictionary<int, List<int>> sortedAdjacency,
            HashSet<string> usedDirected)
        {
            List<int> loop = new List<int> { startU };
            int u = startU;
            int v = startV;
            int guard = 0;
            while (guard++ < 100000)
            {
                string edgeKey = DirectedEdgeKey(u, v);
                if (usedDirected.Contains(edgeKey))
                {
                    return null;
                }

                usedDirected.Add(edgeKey);
                loop.Add(v);

                List<int> outEdges;
                if (!sortedAdjacency.TryGetValue(v, out outEdges) || outEdges.Count == 0)
                {
                    return null;
                }

                int backIdx = outEdges.IndexOf(u);
                if (backIdx < 0)
                {
                    return null;
                }

                // Left-face traversal: choose the previous edge in CCW order.
                int nextIdx = (backIdx - 1 + outEdges.Count) % outEdges.Count;
                int w = outEdges[nextIdx];

                if (u == startU && v == startV && loop.Count > 2)
                {
                    break;
                }

                u = v;
                v = w;
                if (u == startU && v == startV)
                {
                    loop.Add(startU);
                    break;
                }
            }

            if (loop.Count < 4 || loop[0] != loop[loop.Count - 1])
            {
                return null;
            }

            return loop;
        }

        private static string NormalizeLoopKey(List<int> loop)
        {
            if (loop == null || loop.Count < 2)
            {
                return string.Empty;
            }

            List<int> ring = loop.Take(loop.Count - 1).ToList();
            int n = ring.Count;
            int minIdx = 0;
            for (int i = 1; i < n; i++)
            {
                if (ring[i] < ring[minIdx])
                {
                    minIdx = i;
                }
            }

            List<int> rotated = new List<int>();
            for (int i = 0; i < n; i++)
            {
                rotated.Add(ring[(minIdx + i) % n]);
            }

            return string.Join("_", rotated);
        }

        private static bool IsModelBoundaryDrivenLoopDetection(HashSet<string> layerSet)
        {
            if (layerSet == null || layerSet.Count == 0)
            {
                return false;
            }

            return layerSet.Contains(ModelBoundarySegmentBuilder.WallBoundaryLayerName) ||
                   layerSet.Contains(ModelBoundarySegmentBuilder.RoomSeparatorLayerName) ||
                   layerSet.Contains(ModelBoundarySegmentBuilder.DoorClosureLayerName);
        }

        private static List<RoomCandidate> RemoveShellLoops(List<RoomCandidate> candidates)
        {
            List<RoomCandidate> source = candidates ?? new List<RoomCandidate>();
            List<RoomCandidate> closed = source
                .Where(x => x != null && x.Status != RoomBoundaryStatus.NeedsFix && x.LoopPoints != null && x.LoopPoints.Count >= 4)
                .ToList();
            if (closed.Count <= 1)
            {
                return source;
            }

            List<double> areas = closed
                .Select(x => Math.Max(0.0, x.AreaM2))
                .OrderBy(x => x)
                .ToList();
            double median = areas[areas.Count / 2];
            double dynamicThreshold = Math.Max(10.0, median * 10.0);
            HashSet<string> removeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RoomCandidate c in closed)
            {
                if (c.AreaM2 > 1500.0 || c.AreaM2 > dynamicThreshold)
                {
                    removeKeys.Add(c.Key ?? string.Empty);
                    continue;
                }

                int containCount = closed.Count(x =>
                    x != null &&
                    !string.Equals(x.Key, c.Key, StringComparison.OrdinalIgnoreCase) &&
                    x.Centroid != null &&
                    c.LoopPoints != null &&
                    PointInPolygon.ContainsPointXY(c.LoopPoints, x.Centroid));
                if (containCount >= 5)
                {
                    removeKeys.Add(c.Key ?? string.Empty);
                }
            }

            return source
                .Where(x => x != null && !removeKeys.Contains(x.Key ?? string.Empty))
                .Where(x => !IsSlenderStripRoom(x))
                .ToList();
        }

        private static bool IsSlenderStripRoom(RoomCandidate candidate)
        {
            if (candidate == null || candidate.BBox == null)
            {
                return false;
            }

            double wFt = Math.Max(0.0, candidate.BBox.Max.X - candidate.BBox.Min.X);
            double hFt = Math.Max(0.0, candidate.BBox.Max.Y - candidate.BBox.Min.Y);
            double minFt = Math.Min(wFt, hFt);
            double maxFt = Math.Max(wFt, hFt);
            if (minFt <= 1e-9 || maxFt <= 1e-9)
            {
                return true;
            }

            double minMm = minFt * MmPerFt;
            double ratio = maxFt / minFt;
            return ratio > 8.0 && minMm < 400.0;
        }

        private static List<CadSegment> NormalizeBoundarySegmentsForRooms(List<CadSegment> source)
        {
            List<CadSegment> segments = source ?? new List<CadSegment>();
            if (segments.Count <= 1)
            {
                return segments;
            }

            double minDistFt = 80.0 / MmPerFt;
            double maxDistFt = 650.0 / MmPerFt;
            bool[] used = new bool[segments.Count];
            List<CadSegment> normalized = new List<CadSegment>();
            int nextId = segments.Count > 0 ? segments.Max(x => x != null ? x.SegmentId : 0) + 1 : 1;
            int pairCount = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                CadSegment a = segments[i];
                if (a == null || used[i])
                {
                    continue;
                }

                XYZ aVec = a.P1 - a.P0;
                double aLen = aVec.GetLength();
                if (aLen < 1e-9)
                {
                    used[i] = true;
                    continue;
                }

                XYZ aDir = aVec.Normalize();
                int bestIndex = -1;
                double bestScore = double.MaxValue;
                for (int j = i + 1; j < segments.Count; j++)
                {
                    CadSegment b = segments[j];
                    if (b == null || used[j])
                    {
                        continue;
                    }

                    XYZ bVec = b.P1 - b.P0;
                    double bLen = bVec.GetLength();
                    if (bLen < 1e-9)
                    {
                        continue;
                    }

                    XYZ bDir = bVec.Normalize();
                    double parallel = Math.Abs(aDir.DotProduct(bDir));
                    if (parallel < 0.995)
                    {
                        continue;
                    }

                    double lenRatio = aLen > bLen ? aLen / bLen : bLen / aLen;
                    if (lenRatio > 1.35)
                    {
                        continue;
                    }

                    double distFt = DistancePointToInfiniteLineFt(a.P0, aDir, b.MidPoint ?? ((b.P0 + b.P1) * 0.5));
                    if (distFt < minDistFt || distFt > maxDistFt)
                    {
                        continue;
                    }

                    if (!HasSufficientProjectedOverlap(a.P0, aDir, a.P0, a.P1, b.P0, b.P1, 0.60))
                    {
                        continue;
                    }

                    if (distFt < bestScore)
                    {
                        bestScore = distFt;
                        bestIndex = j;
                    }
                }

                if (bestIndex < 0)
                {
                    normalized.Add(a);
                    used[i] = true;
                    continue;
                }

                CadSegment pair = segments[bestIndex];
                XYZ b0 = pair.P0;
                XYZ b1 = pair.P1;
                if ((b1 - b0).DotProduct(aDir) < 0)
                {
                    XYZ tmp = b0;
                    b0 = b1;
                    b1 = tmp;
                }

                CadSegment center = new CadSegment
                {
                    SegmentId = nextId++,
                    NormalizedLayer = a.NormalizedLayer,
                    SemanticLayer = a.SemanticLayer,
                    LayerName = a.LayerName,
                    RawLayerName = a.RawLayerName,
                    SourceType = a.SourceType,
                    P0 = (a.P0 + b0) * 0.5,
                    P1 = (a.P1 + b1) * 0.5,
                    MidPoint = ((a.P0 + b0) * 0.5 + (a.P1 + b1) * 0.5) * 0.5,
                    IsArc = false
                };
                normalized.Add(center);
                used[i] = true;
                used[bestIndex] = true;
                pairCount++;
            }

            DiagnosticRecorder.AppendDebug(
                "[RoomBoundaryNormalize] SourceSegments=" + segments.Count +
                ", NormalizedSegments=" + normalized.Count +
                ", DoubleWallPairs=" + pairCount +
                ", DistRangeMm=[80,650]");

            return normalized;
        }

        private static bool HasSufficientProjectedOverlap(
            XYZ origin,
            XYZ dir,
            XYZ a0,
            XYZ a1,
            XYZ b0,
            XYZ b1,
            double minRatio)
        {
            double aT0 = (a0 - origin).DotProduct(dir);
            double aT1 = (a1 - origin).DotProduct(dir);
            double bT0 = (b0 - origin).DotProduct(dir);
            double bT1 = (b1 - origin).DotProduct(dir);
            double aMin = Math.Min(aT0, aT1);
            double aMax = Math.Max(aT0, aT1);
            double bMin = Math.Min(bT0, bT1);
            double bMax = Math.Max(bT0, bT1);
            double overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
            if (overlap <= 1e-9)
            {
                return false;
            }

            double lenA = aMax - aMin;
            double lenB = bMax - bMin;
            double minLen = Math.Min(lenA, lenB);
            if (minLen <= 1e-9)
            {
                return false;
            }

            return overlap / minLen >= minRatio;
        }

        private static double DistancePointToInfiniteLineFt(XYZ linePoint, XYZ lineDir, XYZ p)
        {
            XYZ v = p - linePoint;
            double proj = v.DotProduct(lineDir);
            XYZ foot = linePoint + lineDir * proj;
            return foot.DistanceTo(p);
        }

        private static List<XYZ> TraceChainForComponent(List<Tuple<int, int>> component, List<XYZ> nodes)
        {
            if (component == null || component.Count == 0)
            {
                return new List<XYZ>();
            }

            Dictionary<int, List<int>> adj = new Dictionary<int, List<int>>();
            foreach (Tuple<int, int> e in component)
            {
                if (!adj.ContainsKey(e.Item1)) adj[e.Item1] = new List<int>();
                if (!adj.ContainsKey(e.Item2)) adj[e.Item2] = new List<int>();
                adj[e.Item1].Add(e.Item2);
                adj[e.Item2].Add(e.Item1);
            }

            int start = adj.Where(x => x.Value.Count == 1).Select(x => x.Key).FirstOrDefault();
            if (!adj.ContainsKey(start))
            {
                start = adj.Keys.First();
            }

            List<XYZ> chain = new List<XYZ>();
            HashSet<string> usedEdge = new HashSet<string>(StringComparer.Ordinal);
            int prev = -1;
            int current = start;
            for (int guard = 0; guard < component.Count + 5; guard++)
            {
                chain.Add(nodes[current]);
                List<int> nextCandidates = adj[current];
                int next = -1;
                if (nextCandidates.Count == 0)
                {
                    break;
                }

                if (prev < 0)
                {
                    next = nextCandidates[0];
                }
                else
                {
                    XYZ baseDir = nodes[current] - nodes[prev];
                    double best = double.MaxValue;
                    foreach (int c in nextCandidates)
                    {
                        string key = EdgeKey(current, c);
                        if (usedEdge.Contains(key))
                        {
                            continue;
                        }

                        XYZ dir = nodes[c] - nodes[current];
                        double turn = Math.Abs(Math.Atan2(baseDir.X * dir.Y - baseDir.Y * dir.X, baseDir.X * dir.X + baseDir.Y * dir.Y));
                        if (turn < best)
                        {
                            best = turn;
                            next = c;
                        }
                    }
                }

                if (next < 0)
                {
                    break;
                }

                usedEdge.Add(EdgeKey(current, next));
                prev = current;
                current = next;
                if (current == start)
                {
                    chain.Add(nodes[start]);
                    break;
                }
            }

            return Simplify(chain, 1e-6);
        }

        private static List<XYZ> CloseLoop(
            List<XYZ> chain,
            double closeTolFt,
            double maxPatchFt,
            out RoomBoundaryStatus status,
            out double gapFt)
        {
            List<XYZ> points = chain.Select(ToPlanar).ToList();
            if (points.Count < 3)
            {
                status = RoomBoundaryStatus.NeedsFix;
                gapFt = 0;
                return points;
            }

            XYZ first = points[0];
            XYZ last = points[points.Count - 1];
            gapFt = first.DistanceTo(last);
            if (gapFt <= 1e-9)
            {
                status = RoomBoundaryStatus.Closed;
                return EnsureClosed(points);
            }

            if (gapFt <= closeTolFt)
            {
                points[points.Count - 1] = first;
                status = RoomBoundaryStatus.AutoClosed;
                return EnsureClosed(points);
            }

            if (gapFt <= maxPatchFt)
            {
                points.Add(first);
                status = RoomBoundaryStatus.Patched;
                return EnsureClosed(points);
            }

            status = RoomBoundaryStatus.NeedsFix;
            return points;
        }

        private static string EdgeKey(int a, int b)
        {
            int x = Math.Min(a, b);
            int y = Math.Max(a, b);
            return x + "_" + y;
        }

        private static string DirectedEdgeKey(int a, int b)
        {
            return a + ">" + b;
        }

        private static List<XYZ> EnsureClosed(List<XYZ> points)
        {
            List<XYZ> result = new List<XYZ>(points ?? new List<XYZ>());
            if (result.Count >= 2 && result[0].DistanceTo(result[result.Count - 1]) > 1e-9)
            {
                result.Add(result[0]);
            }

            return result;
        }

        private static List<XYZ> Simplify(List<XYZ> points, double tolFt)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (result.Count == 0 || result[result.Count - 1].DistanceTo(p) > tolFt)
                {
                    result.Add(p);
                }
            }

            return result;
        }

        private static double ComputeArea(List<XYZ> loop)
        {
            if (loop == null || loop.Count < 4)
            {
                return 0.0;
            }

            double a = 0.0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                a += loop[i].X * loop[i + 1].Y - loop[i + 1].X * loop[i].Y;
            }

            return a * 0.5;
        }

        private static XYZ ComputeCentroid(List<XYZ> loop)
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
                return new XYZ(loop.Take(loop.Count - 1).Average(x => x.X), loop.Take(loop.Count - 1).Average(x => x.Y), 0);
            }

            return new XYZ(cx / (3.0 * area2), cy / (3.0 * area2), 0);
        }

        private static BoundingBoxXYZ ComputeBBox(List<XYZ> points)
        {
            BoundingBoxXYZ box = new BoundingBoxXYZ();
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (minX == double.MaxValue)
            {
                minX = minY = maxX = maxY = 0;
            }

            box.Min = new XYZ(minX, minY, -1);
            box.Max = new XYZ(maxX, maxY, 1);
            return box;
        }

        private static XYZ ToPlanar(XYZ p)
        {
            return new XYZ(p.X, p.Y, 0.0);
        }

        private static int FindOrCreateNode(List<XYZ> nodes, XYZ point, double tolFt)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].DistanceTo(point) <= tolFt)
                {
                    nodes[i] = new XYZ((nodes[i].X + point.X) * 0.5, (nodes[i].Y + point.Y) * 0.5, 0);
                    return i;
                }
            }

            nodes.Add(point);
            return nodes.Count - 1;
        }
    }
}
