using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal sealed class TargetRoomLocalDetectResult
    {
        public RoomCandidate MatchedLoop { get; set; }

        public List<Services.CadSegment> DebugSegments { get; set; } = new List<Services.CadSegment>();

        public int WindowSegments { get; set; }

        public int LocalLoops { get; set; }

        public int ValidLoops { get; set; }

        public int ContainsCount { get; set; }

        public double RadiusM { get; set; }

        public double NearestLoopDistM { get; set; } = double.MaxValue;

        public string AttemptName { get; set; } = string.Empty;

        public List<string> RejectedLoopDiagnostics { get; set; } = new List<string>();
    }

    internal static class TargetRoomLocalDetector
    {
        private const double MmPerFt = 304.8;
        private const double LocalWindowRadiusM = 10.0;
        private const double RetryWindowRadiusM = 16.0;
        private const double RetryWindowRadiusWideM = 20.0;
        private const double MinAreaM2ForContain = 10.0;
        private const double MinLoopMinSideM = 1.2;
        private const double SegmentConnectTolMm = 50.0;

        public static TargetRoomLocalDetectResult DetectLocalRoomForLabel(
            CadDataset dataset,
            HashSet<string> wallLayers,
            RoomLabel label,
            RoomSemanticConfig cfg)
        {
            TargetRoomLocalDetectResult first = DetectLocalRoom(dataset, wallLayers, label, cfg, LocalWindowRadiusM, "base-10m");
            LogLocalRoomDetect(label, first);
            if (first.MatchedLoop != null)
            {
                return first;
            }

            TargetRoomLocalDetectResult second = DetectLocalRoom(dataset, wallLayers, label, cfg, RetryWindowRadiusM, "retry-16m");
            LogLocalRoomDetect(label, second);
            if (second.MatchedLoop != null)
            {
                return second;
            }

            TargetRoomLocalDetectResult third = DetectLocalRoom(dataset, wallLayers, label, RelaxConfig(cfg), RetryWindowRadiusWideM, "relax-20m", false, false, false, true);
            LogLocalRoomDetect(label, third);
            if (third.MatchedLoop != null)
            {
                return third;
            }

            if (ShouldRunComplexClosureRetry(first, second, third))
            {
                // Retry once more with relaxed closure-only parameters for complex local environments.
                TargetRoomLocalDetectResult fourth = DetectLocalRoom(
                    dataset,
                    wallLayers,
                    label,
                    RelaxConfigForComplexEnvironment(cfg),
                    RetryWindowRadiusWideM,
                    "complex-closure-20m",
                    false,
                    false,
                    true,
                    true);
                LogLocalRoomDetect(label, fourth);
                if (fourth.MatchedLoop != null)
                {
                    return fourth;
                }

                if (ShouldRunBridgeClosureRetry(fourth))
                {
                    // Retry once with a bridge-oriented config for split local loops.
                    TargetRoomLocalDetectResult fifth = DetectLocalRoom(
                        dataset,
                        wallLayers,
                        label,
                        BridgeConfigForSplitRoom(cfg),
                        RetryWindowRadiusWideM,
                        "bridge-closure-20m",
                        true,
                        true,
                        true,
                        true);
                    LogLocalRoomDetect(label, fifth);
                    if (fifth.MatchedLoop != null)
                    {
                        return fifth;
                    }

                    LogRejectedLoops(label, fifth);
                    return fifth;
                }

                LogRejectedLoops(label, fourth);
                return fourth;
            }

            LogRejectedLoops(label, third);
            return third;
        }

        private static TargetRoomLocalDetectResult DetectLocalRoom(
            CadDataset dataset,
            HashSet<string> wallLayers,
            RoomLabel label,
            RoomSemanticConfig cfg,
            double radiusM,
            string attemptName,
            bool keepLargestFace = false,
            bool preferLargestContainingLoop = false,
            bool excludeArcSegments = false,
            bool pruneDanglingBranches = false)
        {
            TargetRoomLocalDetectResult result = new TargetRoomLocalDetectResult
            {
                RadiusM = radiusM,
                AttemptName = attemptName ?? string.Empty
            };
            if (dataset == null || label == null || label.Position == null || wallLayers == null || wallLayers.Count == 0)
            {
                return result;
            }

            double radiusFt = radiusM * 1000.0 / MmPerFt;
            double minX = label.Position.X - radiusFt;
            double maxX = label.Position.X + radiusFt;
            double minY = label.Position.Y - radiusFt;
            double maxY = label.Position.Y + radiusFt;

            List<Services.CadSegment> localSegments = (dataset.Segments ?? new List<Services.CadSegment>())
                .Where(s => s != null &&
                            s.P0 != null &&
                            s.P1 != null &&
                            !string.IsNullOrWhiteSpace(s.RawLayerName) &&
                            (!excludeArcSegments || !s.IsArc) &&
                            wallLayers.Contains(s.RawLayerName) &&
                            IsSegmentInWindow(s, minX, minY, maxX, maxY))
                .ToList();
            localSegments = ClipSegmentsToWindow(localSegments, minX, minY, maxX, maxY);
            localSegments = FilterSegmentsByLabelDistance(localSegments, label.Position, radiusM);
            localSegments = SelectBestConnectedComponent(localSegments, label.Position);
            if (pruneDanglingBranches)
            {
                localSegments = PruneDanglingSegments(localSegments, label.Position);
            }
            result.DebugSegments = new List<Services.CadSegment>(localSegments);
            result.WindowSegments = localSegments.Count;
            if (localSegments.Count == 0)
            {
                return result;
            }

            CadDataset localDataset = new CadDataset
            {
                Segments = localSegments
            };

            List<RoomCandidate> loops = RoomBoundaryLoopService.DetectMulti(
                localDataset,
                wallLayers,
                cfg != null ? cfg.CloseTolMm : 10.0,
                cfg != null ? cfg.MaxPatchMm : 300.0,
                cfg != null ? cfg.MinAreaM2 : 1.0,
                cfg != null ? cfg.DoorGapMaxMm : 1200.0,
                cfg != null ? cfg.SmallGapPatchMaxMm : 350.0,
                keepLargestFace);
            result.LocalLoops = loops.Count;
            result.RejectedLoopDiagnostics = loops
                .Where(x => x != null && !IsValidRoomLoop(x))
                .Take(8)
                .Select(BuildRejectedLoopDiagnostic)
                .ToList();

            List<RoomCandidate> validLoops = loops
                .Where(IsValidRoomLoop)
                .ToList();
            result.ValidLoops = validLoops.Count;

            List<RoomCandidate> contains = validLoops
                .Where(x => x != null && x.LoopPoints != null && PointInPolygon.ContainsPointXY(x.LoopPoints, label.Position))
                .OrderBy(x => preferLargestContainingLoop ? -x.AreaM2 : x.AreaM2)
                .ToList();
            result.ContainsCount = contains.Count;
            if (contains.Count > 0)
            {
                result.MatchedLoop = contains[0];
                return result;
            }

            double nearestFt = double.MaxValue;
            foreach (RoomCandidate loop in validLoops)
            {
                if (loop == null || loop.LoopPoints == null || loop.LoopPoints.Count < 2)
                {
                    continue;
                }

                double d = DistancePointToLoopFt(label.Position, loop.LoopPoints);
                if (d < nearestFt)
                {
                    nearestFt = d;
                }
            }

            result.NearestLoopDistM = nearestFt < double.MaxValue ? nearestFt * MmPerFt / 1000.0 : double.MaxValue;
            return result;
        }

        private static List<Services.CadSegment> ClipSegmentsToWindow(
            List<Services.CadSegment> segments,
            double minX,
            double minY,
            double maxX,
            double maxY)
        {
            List<Services.CadSegment> result = new List<Services.CadSegment>();
            foreach (Services.CadSegment segment in segments ?? new List<Services.CadSegment>())
            {
                if (segment == null || segment.P0 == null || segment.P1 == null)
                {
                    continue;
                }

                if (segment.IsArc)
                {
                    result.Add(segment);
                    continue;
                }

                XYZ clippedP0;
                XYZ clippedP1;
                if (!TryClipLineToRect(segment.P0, segment.P1, minX, minY, maxX, maxY, out clippedP0, out clippedP1))
                {
                    continue;
                }

                if (clippedP0 == null || clippedP1 == null || clippedP0.DistanceTo(clippedP1) <= 1e-6)
                {
                    continue;
                }

                result.Add(CloneSegmentWithEndpoints(segment, clippedP0, clippedP1));
            }

            return result;
        }

        private static RoomSemanticConfig RelaxConfig(RoomSemanticConfig cfg)
        {
            RoomSemanticConfig source = cfg ?? new RoomSemanticConfig();
            return new RoomSemanticConfig
            {
                TargetKeywords = source.TargetKeywords != null ? new List<string>(source.TargetKeywords) : new List<string>(),
                CloseTolMm = Math.Max(source.CloseTolMm, 50.0),
                MaxPatchMm = Math.Max(source.MaxPatchMm, 450.0),
                MinAreaM2 = source.MinAreaM2,
                DoorGapMaxMm = Math.Max(source.DoorGapMaxMm, 1200.0),
                SmallGapPatchMaxMm = Math.Max(source.SmallGapPatchMaxMm, 450.0)
            };
        }

        private static RoomSemanticConfig RelaxConfigForComplexEnvironment(RoomSemanticConfig cfg)
        {
            RoomSemanticConfig source = cfg ?? new RoomSemanticConfig();
            return new RoomSemanticConfig
            {
                TargetKeywords = source.TargetKeywords != null ? new List<string>(source.TargetKeywords) : new List<string>(),
                // Keep the complex retry tighter than the broad relax pass to avoid oversized loops.
                CloseTolMm = Math.Max(source.CloseTolMm, 40.0),
                MaxPatchMm = Math.Max(source.MaxPatchMm, 320.0),
                MinAreaM2 = source.MinAreaM2,
                DoorGapMaxMm = Math.Max(source.DoorGapMaxMm, 1200.0),
                SmallGapPatchMaxMm = Math.Max(source.SmallGapPatchMaxMm, 220.0)
            };
        }

        private static RoomSemanticConfig BridgeConfigForSplitRoom(RoomSemanticConfig cfg)
        {
            RoomSemanticConfig source = cfg ?? new RoomSemanticConfig();
            return new RoomSemanticConfig
            {
                TargetKeywords = source.TargetKeywords != null ? new List<string>(source.TargetKeywords) : new List<string>(),
                // Use a moderate bridge config to reconnect split loops without reopening the broad oversized case.
                CloseTolMm = Math.Max(source.CloseTolMm, 55.0),
                MaxPatchMm = Math.Max(source.MaxPatchMm, 380.0),
                MinAreaM2 = source.MinAreaM2,
                DoorGapMaxMm = Math.Max(source.DoorGapMaxMm, 1250.0),
                SmallGapPatchMaxMm = Math.Max(source.SmallGapPatchMaxMm, 300.0)
            };
        }

        private static bool ShouldRunComplexClosureRetry(
            TargetRoomLocalDetectResult first,
            TargetRoomLocalDetectResult second,
            TargetRoomLocalDetectResult third)
        {
            TargetRoomLocalDetectResult latest = third ?? second ?? first;
            if (latest == null || latest.MatchedLoop != null)
            {
                return false;
            }

            return latest.WindowSegments >= 20 || latest.LocalLoops > 0 || latest.ValidLoops == 0;
        }

        private static bool ShouldRunBridgeClosureRetry(TargetRoomLocalDetectResult result)
        {
            if (result == null || result.RejectedLoopDiagnostics == null || result.RejectedLoopDiagnostics.Count == 0)
            {
                return false;
            }

            bool hasTinyClosedLoop = result.RejectedLoopDiagnostics.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                x.IndexOf("|Status=Closed|", StringComparison.OrdinalIgnoreCase) >= 0 &&
                x.IndexOf("|Reason=Area<10m2", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasNeedsFixLoop = result.RejectedLoopDiagnostics.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                x.IndexOf("|Status=NeedsFix|", StringComparison.OrdinalIgnoreCase) >= 0);
            return hasTinyClosedLoop && hasNeedsFixLoop;
        }

        private static List<Services.CadSegment> FilterSegmentsByLabelDistance(
            List<Services.CadSegment> segments,
            XYZ labelPosition,
            double radiusM)
        {
            List<Services.CadSegment> source = segments ?? new List<Services.CadSegment>();
            if (labelPosition == null || source.Count == 0)
            {
                return source;
            }

            double keepDistM = radiusM <= LocalWindowRadiusM ? 8.0 : 12.0;
            double keepDistFt = keepDistM * 1000.0 / MmPerFt;
            List<Services.CadSegment> filtered = source
                .Where(x => x != null &&
                            x.P0 != null &&
                            x.P1 != null &&
                            DistancePointToSegmentFt(labelPosition, x.P0, x.P1) <= keepDistFt)
                .ToList();
            return filtered.Count > 0 ? filtered : source;
        }

        private static List<Services.CadSegment> SelectBestConnectedComponent(
            List<Services.CadSegment> segments,
            XYZ labelPosition)
        {
            List<Services.CadSegment> source = segments ?? new List<Services.CadSegment>();
            if (labelPosition == null || source.Count <= 1)
            {
                return source;
            }

            double tolFt = SegmentConnectTolMm / MmPerFt;
            List<List<int>> components = BuildSegmentComponents(source, tolFt);
            if (components.Count <= 1)
            {
                return source;
            }

            List<Services.CadSegment> best = null;
            double bestScore = double.MaxValue;
            foreach (List<int> component in components)
            {
                List<Services.CadSegment> part = component
                    .Where(i => i >= 0 && i < source.Count)
                    .Select(i => source[i])
                    .Where(x => x != null)
                    .ToList();
                if (part.Count == 0)
                {
                    continue;
                }

                BoundingBoxXYZ box = ComputeSegmentBBox(part);
                double boxDist = DistancePointToBoxFt(labelPosition, box);
                double segDist = part.Min(x => DistancePointToSegmentFt(labelPosition, x.P0, x.P1));
                double score = boxDist * 0.7 + segDist * 0.3;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = part;
                }
            }

            return best != null && best.Count > 0 ? best : source;
        }

        private static List<Services.CadSegment> PruneDanglingSegments(
            List<Services.CadSegment> segments,
            XYZ labelPosition)
        {
            List<Services.CadSegment> working = (segments ?? new List<Services.CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
            if (labelPosition == null || working.Count < 3)
            {
                return working;
            }

            for (int pass = 0; pass < 6; pass++)
            {
                List<NodeRef> nodes = BuildNodeRefs(working, SegmentConnectTolMm / MmPerFt);
                Dictionary<int, int> degreeByNode = nodes
                    .GroupBy(x => x.NodeId)
                    .ToDictionary(g => g.Key, g => g.Count());
                List<Services.CadSegment> toRemove = new List<Services.CadSegment>();

                foreach (Services.CadSegment segment in working)
                {
                    if (segment.IsArc)
                    {
                        continue;
                    }

                    NodeRef node0 = nodes.FirstOrDefault(x => x.Segment == segment && x.IsStart);
                    NodeRef node1 = nodes.FirstOrDefault(x => x.Segment == segment && !x.IsStart);
                    if (node0 == null || node1 == null)
                    {
                        continue;
                    }

                    int degree0 = degreeByNode.ContainsKey(node0.NodeId) ? degreeByNode[node0.NodeId] : 0;
                    int degree1 = degreeByNode.ContainsKey(node1.NodeId) ? degreeByNode[node1.NodeId] : 0;

                    if (degree0 == 1 && degree1 >= 2 && ShouldTrimDanglingSegment(segment, segment.P0, segment.P1, labelPosition))
                    {
                        toRemove.Add(segment);
                        continue;
                    }

                    if (degree1 == 1 && degree0 >= 2 && ShouldTrimDanglingSegment(segment, segment.P1, segment.P0, labelPosition))
                    {
                        toRemove.Add(segment);
                    }
                }

                if (toRemove.Count == 0)
                {
                    break;
                }

                working = working.Except(toRemove).ToList();
                if (working.Count < 3)
                {
                    break;
                }
            }

            return working;
        }

        private static bool ShouldTrimDanglingSegment(
            Services.CadSegment segment,
            XYZ danglingEnd,
            XYZ anchorEnd,
            XYZ labelPosition)
        {
            if (segment == null || danglingEnd == null || anchorEnd == null || labelPosition == null)
            {
                return false;
            }

            double lengthM = segment.P0.DistanceTo(segment.P1) * MmPerFt / 1000.0;
            if (lengthM < 1.5)
            {
                return false;
            }

            double danglingDistM = danglingEnd.DistanceTo(labelPosition) * MmPerFt / 1000.0;
            double anchorDistM = anchorEnd.DistanceTo(labelPosition) * MmPerFt / 1000.0;
            if (danglingDistM < anchorDistM + 1.0)
            {
                return false;
            }

            XYZ mid = new XYZ(
                (segment.P0.X + segment.P1.X) * 0.5,
                (segment.P0.Y + segment.P1.Y) * 0.5,
                (segment.P0.Z + segment.P1.Z) * 0.5);
            double midDistM = mid.DistanceTo(labelPosition) * MmPerFt / 1000.0;
            return midDistM >= 3.0;
        }

        private static List<NodeRef> BuildNodeRefs(List<Services.CadSegment> segments, double tolFt)
        {
            List<XYZ> nodePoints = new List<XYZ>();
            List<NodeRef> refs = new List<NodeRef>();
            foreach (Services.CadSegment segment in segments ?? new List<Services.CadSegment>())
            {
                if (segment == null || segment.P0 == null || segment.P1 == null)
                {
                    continue;
                }

                int node0 = GetOrCreateNode(nodePoints, segment.P0, tolFt);
                int node1 = GetOrCreateNode(nodePoints, segment.P1, tolFt);
                refs.Add(new NodeRef { Segment = segment, NodeId = node0, IsStart = true });
                refs.Add(new NodeRef { Segment = segment, NodeId = node1, IsStart = false });
            }

            return refs;
        }

        private static int GetOrCreateNode(List<XYZ> nodePoints, XYZ point, double tolFt)
        {
            for (int i = 0; i < nodePoints.Count; i++)
            {
                if (nodePoints[i] != null && nodePoints[i].DistanceTo(point) <= tolFt)
                {
                    return i;
                }
            }

            nodePoints.Add(point);
            return nodePoints.Count - 1;
        }

        private sealed class NodeRef
        {
            public Services.CadSegment Segment { get; set; }

            public int NodeId { get; set; }

            public bool IsStart { get; set; }
        }

        private static List<List<int>> BuildSegmentComponents(List<Services.CadSegment> segments, double tolFt)
        {
            List<List<int>> result = new List<List<int>>();
            bool[] visited = new bool[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                if (visited[i] || segments[i] == null)
                {
                    continue;
                }

                List<int> component = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (visited[j] || segments[j] == null)
                        {
                            continue;
                        }

                        if (!AreSegmentsConnected(segments[cur], segments[j], tolFt))
                        {
                            continue;
                        }

                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }

                result.Add(component);
            }

            return result;
        }

        private static bool AreSegmentsConnected(Services.CadSegment a, Services.CadSegment b, double tolFt)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || b.P0 == null || b.P1 == null)
            {
                return false;
            }

            return a.P0.DistanceTo(b.P0) <= tolFt ||
                   a.P0.DistanceTo(b.P1) <= tolFt ||
                   a.P1.DistanceTo(b.P0) <= tolFt ||
                   a.P1.DistanceTo(b.P1) <= tolFt;
        }

        private static BoundingBoxXYZ ComputeSegmentBBox(List<Services.CadSegment> segments)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (Services.CadSegment s in segments ?? new List<Services.CadSegment>())
            {
                if (s == null || s.P0 == null || s.P1 == null)
                {
                    continue;
                }

                minX = Math.Min(minX, Math.Min(s.P0.X, s.P1.X));
                minY = Math.Min(minY, Math.Min(s.P0.Y, s.P1.Y));
                maxX = Math.Max(maxX, Math.Max(s.P0.X, s.P1.X));
                maxY = Math.Max(maxY, Math.Max(s.P0.Y, s.P1.Y));
            }

            if (minX == double.MaxValue)
            {
                minX = minY = maxX = maxY = 0.0;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, -1.0),
                Max = new XYZ(maxX, maxY, 1.0)
            };
        }

        private static double DistancePointToBoxFt(XYZ p, BoundingBoxXYZ box)
        {
            if (p == null || box == null || box.Min == null || box.Max == null)
            {
                return double.MaxValue;
            }

            double dx = Math.Max(Math.Max(box.Min.X - p.X, 0.0), p.X - box.Max.X);
            double dy = Math.Max(Math.Max(box.Min.Y - p.Y, 0.0), p.Y - box.Max.Y);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsValidRoomLoop(RoomCandidate loop)
        {
            if (loop == null || loop.LoopPoints == null)
            {
                return false;
            }

            if (loop.AreaM2 < MinAreaM2ForContain)
            {
                return false;
            }

            if (loop.LoopPoints.Count < 4)
            {
                return false;
            }

            if (loop.BBox == null || loop.BBox.Min == null || loop.BBox.Max == null)
            {
                return false;
            }

            double widthM = (loop.BBox.Max.X - loop.BBox.Min.X) * 0.3048;
            double heightM = (loop.BBox.Max.Y - loop.BBox.Min.Y) * 0.3048;
            double minSideM = Math.Min(Math.Abs(widthM), Math.Abs(heightM));
            if (minSideM < MinLoopMinSideM)
            {
                return false;
            }

            return true;
        }

        private static string BuildRejectedLoopDiagnostic(RoomCandidate loop)
        {
            double minSideM = GetLoopMinSideM(loop);
            return (loop != null ? (loop.Key ?? "-") : "-") +
                   "|Area=" + (loop != null ? loop.AreaM2.ToString("F2") : "0") +
                   "|Pts=" + (loop != null && loop.LoopPoints != null ? loop.LoopPoints.Count.ToString() : "0") +
                   "|MinSide=" + minSideM.ToString("F2") + "m" +
                   "|Gap=" + (loop != null ? loop.CloseGapMm.ToString("F0") : "0") + "mm" +
                   "|Status=" + (loop != null ? loop.Status.ToString() : "-") +
                   "|Reason=" + GetLoopRejectReason(loop, minSideM);
        }

        private static string GetLoopRejectReason(RoomCandidate loop, double minSideM)
        {
            if (loop == null)
            {
                return "NullLoop";
            }

            List<string> reasons = new List<string>();
            if (loop.LoopPoints == null)
            {
                reasons.Add("NoPoints");
            }
            else if (loop.LoopPoints.Count < 4)
            {
                reasons.Add("Vertex<4");
            }

            if (loop.AreaM2 < MinAreaM2ForContain)
            {
                reasons.Add("Area<" + MinAreaM2ForContain.ToString("F0") + "m2");
            }

            if (loop.BBox == null || loop.BBox.Min == null || loop.BBox.Max == null)
            {
                reasons.Add("BBoxMissing");
            }
            else if (minSideM < MinLoopMinSideM)
            {
                reasons.Add("MinSide<" + MinLoopMinSideM.ToString("F1") + "m");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("RejectedByCustomRule");
            }

            return string.Join("+", reasons);
        }

        private static double GetLoopMinSideM(RoomCandidate loop)
        {
            if (loop == null || loop.BBox == null || loop.BBox.Min == null || loop.BBox.Max == null)
            {
                return 0.0;
            }

            double widthM = (loop.BBox.Max.X - loop.BBox.Min.X) * 0.3048;
            double heightM = (loop.BBox.Max.Y - loop.BBox.Min.Y) * 0.3048;
            return Math.Min(Math.Abs(widthM), Math.Abs(heightM));
        }

        private static void LogLocalRoomDetect(RoomLabel label, TargetRoomLocalDetectResult result)
        {
            string labelText = label != null ? (label.RoomName ?? string.Empty) : string.Empty;
            if (result == null)
            {
                DiagnosticRecorder.AppendDebug("[LocalRoomDetect] Label=" + labelText + ", Result=null");
                return;
            }

            if (result.MatchedLoop != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LocalRoomDetect] Label=" + labelText +
                    ", Attempt=" + result.AttemptName +
                    ", Radius=" + result.RadiusM.ToString("F0") + "m" +
                    ", WindowSegments=" + result.WindowSegments +
                    ", Loops=" + result.LocalLoops +
                    ", ValidLoops=" + result.ValidLoops +
                    ", Contains=" + result.ContainsCount +
                    ", Area=" + result.MatchedLoop.AreaM2.ToString("F2") + "m2");
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[LocalRoomDetect] Label=" + labelText +
                ", Attempt=" + result.AttemptName +
                ", Radius=" + result.RadiusM.ToString("F0") + "m" +
                ", WindowSegments=" + result.WindowSegments +
                ", Loops=" + result.LocalLoops +
                ", ValidLoops=" + result.ValidLoops +
                ", Contains=" + result.ContainsCount +
                ", NoContainingLoop" +
                ", RejectCount=" + (result.RejectedLoopDiagnostics != null ? result.RejectedLoopDiagnostics.Count : 0) +
                ", NearestLoopDist=" + (result.NearestLoopDistM < double.MaxValue
                    ? result.NearestLoopDistM.ToString("F2") + "m"
                    : "N/A"));
        }

        private static void LogRejectedLoops(RoomLabel label, TargetRoomLocalDetectResult result)
        {
            if (label == null || result == null || result.RejectedLoopDiagnostics == null || result.RejectedLoopDiagnostics.Count == 0)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[LocalRoomReject] Label=" + (label.RoomName ?? string.Empty) +
                ", Attempt=" + result.AttemptName +
                ", Radius=" + result.RadiusM.ToString("F0") + "m" +
                ", Rejected={" + string.Join("; ", result.RejectedLoopDiagnostics) + "}");
        }

        private static bool IsSegmentInWindow(Services.CadSegment s, double minX, double minY, double maxX, double maxY)
        {
            double sxMin = Math.Min(s.P0.X, s.P1.X);
            double sxMax = Math.Max(s.P0.X, s.P1.X);
            double syMin = Math.Min(s.P0.Y, s.P1.Y);
            double syMax = Math.Max(s.P0.Y, s.P1.Y);
            if (sxMax < minX || sxMin > maxX || syMax < minY || syMin > maxY)
            {
                return false;
            }

            return true;
        }

        // Clip long local boundary lines to the active window so they cannot pull the room
        // topology into unrelated external networks.
        private static bool TryClipLineToRect(
            XYZ p0,
            XYZ p1,
            double minX,
            double minY,
            double maxX,
            double maxY,
            out XYZ clippedP0,
            out XYZ clippedP1)
        {
            clippedP0 = null;
            clippedP1 = null;
            if (p0 == null || p1 == null)
            {
                return false;
            }

            double x0 = p0.X;
            double y0 = p0.Y;
            double x1 = p1.X;
            double y1 = p1.Y;

            int code0 = ComputeClipCode(x0, y0, minX, minY, maxX, maxY);
            int code1 = ComputeClipCode(x1, y1, minX, minY, maxX, maxY);

            while (true)
            {
                if ((code0 | code1) == 0)
                {
                    clippedP0 = new XYZ(x0, y0, p0.Z);
                    clippedP1 = new XYZ(x1, y1, p1.Z);
                    return true;
                }

                if ((code0 & code1) != 0)
                {
                    return false;
                }

                int codeOut = code0 != 0 ? code0 : code1;
                double x = 0.0;
                double y = 0.0;

                if ((codeOut & 8) != 0)
                {
                    x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0);
                    y = maxY;
                }
                else if ((codeOut & 4) != 0)
                {
                    x = x0 + (x1 - x0) * (minY - y0) / (y1 - y0);
                    y = minY;
                }
                else if ((codeOut & 2) != 0)
                {
                    y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0);
                    x = maxX;
                }
                else
                {
                    y = y0 + (y1 - y0) * (minX - x0) / (x1 - x0);
                    x = minX;
                }

                if (codeOut == code0)
                {
                    x0 = x;
                    y0 = y;
                    code0 = ComputeClipCode(x0, y0, minX, minY, maxX, maxY);
                }
                else
                {
                    x1 = x;
                    y1 = y;
                    code1 = ComputeClipCode(x1, y1, minX, minY, maxX, maxY);
                }
            }
        }

        private static int ComputeClipCode(double x, double y, double minX, double minY, double maxX, double maxY)
        {
            int code = 0;
            if (x < minX)
            {
                code |= 1;
            }
            else if (x > maxX)
            {
                code |= 2;
            }

            if (y < minY)
            {
                code |= 4;
            }
            else if (y > maxY)
            {
                code |= 8;
            }

            return code;
        }

        private static Services.CadSegment CloneSegmentWithEndpoints(Services.CadSegment source, XYZ p0, XYZ p1)
        {
            return new Services.CadSegment
            {
                SegmentId = source.SegmentId,
                NormalizedLayer = source.NormalizedLayer,
                SemanticLayer = source.SemanticLayer,
                LayerName = source.LayerName,
                RawLayerName = source.RawLayerName,
                SourceType = source.SourceType,
                P0 = p0,
                P1 = p1,
                IsArc = source.IsArc,
                Center = source.Center,
                RadiusFeet = source.RadiusFeet,
                SweepAngleRad = source.SweepAngleRad,
                MidPoint = p0 != null && p1 != null ? new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, (p0.Z + p1.Z) * 0.5) : source.MidPoint
            };
        }

        private static double DistancePointToLoopFt(XYZ p, List<XYZ> loop)
        {
            double best = double.MaxValue;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                XYZ a = loop[i];
                XYZ b = loop[i + 1];
                if (a == null || b == null)
                {
                    continue;
                }

                double d = DistancePointToSegmentFt(p, a, b);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        private static double DistancePointToSegmentFt(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double ab2 = ab.DotProduct(ab);
            if (ab2 < 1e-12)
            {
                return p.DistanceTo(a);
            }

            double t = (p - a).DotProduct(ab) / ab2;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            XYZ proj = a + ab * t;
            return p.DistanceTo(proj);
        }
    }
}
