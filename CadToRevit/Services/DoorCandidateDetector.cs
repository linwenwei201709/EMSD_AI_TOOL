using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class DoorCandidateDetector
    {
        public static DoorDetectResult Detect(
            Document doc,
            ImportInstance importInstance,
            DoorDetectSettings settings)
        {
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            if (doc == null || importInstance == null)
            {
                return new DoorDetectResult();
            }

            HashSet<string> rawLayerFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DOOR" };
            List<CadSegment> doorSegments = BuildDoorSegments(doc, importInstance, rawLayerFilter, null);
            return DetectCore(doc, importInstance, effective, doorSegments);
        }

        public static DoorDetectResult DetectByRawLayer(
            Document doc,
            ImportInstance importInstance,
            DoorDetectSettings settings,
            string rawLayerName)
        {
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            if (doc == null || importInstance == null)
            {
                return new DoorDetectResult();
            }

            HashSet<string> rawLayerFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DOOR" };
            if (!string.IsNullOrWhiteSpace(rawLayerName))
            {
                rawLayerFilter.Add(rawLayerName);
            }

            List<CadSegment> doorSegments = BuildDoorSegments(doc, importInstance, rawLayerFilter, rawLayerName);
            return DetectCore(doc, importInstance, effective, doorSegments);
        }

        public static DoorDetectResult DetectByRawLayerFromSegments(
            IList<CadSegment> segments,
            DoorDetectSettings settings,
            string rawLayerName)
        {
            return DetectByRawLayerFromSegments(segments, settings, rawLayerName, null);
        }

        public static DoorDetectResult DetectByRawLayerFromSegments(
            IList<CadSegment> segments,
            DoorDetectSettings settings,
            string rawLayerName,
            IList<Wall> hostWalls)
        {
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            List<CadSegment> doorSegments = (segments ?? new List<CadSegment>())
                .Where(x => x != null &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                            string.Equals(x.RawLayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DoorDetectResult result = new DoorDetectResult();
            result.DoorSegmentsTotal = doorSegments.Count;
            result.ArcSegmentsTotal = doorSegments.Count(x => x.IsArc);
            if (doorSegments.Count == 0)
            {
                return result;
            }

            List<CadSegment> routeContextSegments = (segments ?? new List<CadSegment>())
                .Where(x => x != null)
                .ToList();
            List<WallHostLine> wallHosts = GetWallHosts(hostWalls);
            List<DoorCandidate> rawCandidates = GenerateCandidatesByComponentRouting(
                doorSegments,
                result,
                effective,
                routeContextSegments,
                wallHosts);

            List<DoorCandidate> merged = MergeCandidates(rawCandidates, effective);
            foreach (DoorCandidate candidate in merged)
            {
                if (wallHosts.Count > 0)
                {
                    MatchToWall(candidate, wallHosts, new List<WallCenterlineCandidate>(), effective);
                }
            }

            merged = DoorPairingService.BuildCandidates(merged, effective);
            merged = PruneCandidates(merged, effective);
            int id = 1;
            foreach (DoorCandidate candidate in merged)
            {
                candidate.CandidateId = id++;
                CountWidthRange(result, candidate.WidthMm);
            }

            result.Candidates = merged;
            result.MergedCandidateCount = merged.Count;
            result.MatchedCount = merged.Count(x => x.MatchedWallId != null || x.MatchedWall != null);
            result.UnmatchedCount = merged.Count - result.MatchedCount;
            return result;
        }

        private static DoorDetectResult DetectCore(
            Document doc,
            ImportInstance importInstance,
            DoorDetectSettings settings,
            List<CadSegment> doorSegments)
        {
            DoorDetectResult result = new DoorDetectResult();
            result.DoorSegmentsTotal = doorSegments.Count;
            result.ArcSegmentsTotal = doorSegments.Count(x => x != null && x.IsArc);
            if (doorSegments.Count == 0)
            {
                return result;
            }

            List<WallHostLine> wallHosts = GetWallHosts(doc);
            List<DoorCandidate> rawCandidates = GenerateCandidatesByComponentRouting(
                doorSegments,
                result,
                settings,
                doorSegments,
                wallHosts);

            List<DoorCandidate> merged = MergeCandidates(rawCandidates, settings);
            result.MergedCandidateCount = merged.Count;

            List<WallCenterlineCandidate> fallbackCenterlines = GetWallCenterlineFallback(doc, importInstance);

            foreach (DoorCandidate candidate in merged)
            {
                MatchToWall(candidate, wallHosts, fallbackCenterlines, settings);
            }

            merged = DoorPairingService.BuildCandidates(merged, settings);
            merged = PruneCandidates(merged, settings);
            int id = 1;
            foreach (DoorCandidate candidate in merged)
            {
                candidate.CandidateId = id++;
                CountWidthRange(result, candidate.WidthMm);
            }

            result.Candidates = merged;
            result.MatchedCount = merged.Count(x => x.MatchedWallId != null || x.MatchedWall != null);
            result.UnmatchedCount = merged.Count - result.MatchedCount;
            return result;
        }

        private static List<CadSegment> BuildDoorSegments(
            Document doc,
            ImportInstance importInstance,
            ISet<string> rawLayerFilter,
            string selectedRawLayer)
        {
            CadSegmentBuildResult build = CadSegmentBuilder.BuildSegments(doc, importInstance, rawLayerFilter);
            IEnumerable<CadSegment> all = build.Segments ?? new List<CadSegment>();

            if (!string.IsNullOrWhiteSpace(selectedRawLayer))
            {
                return all.Where(x => x != null &&
                                      !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                                      string.Equals(x.RawLayerName, selectedRawLayer, StringComparison.OrdinalIgnoreCase))
                          .ToList();
            }

            return all.Where(x => x != null &&
                                  (string.Equals(x.SemanticLayer, "DOOR", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(x.NormalizedLayer, "DOOR", StringComparison.OrdinalIgnoreCase) ||
                                   (!string.IsNullOrWhiteSpace(x.RawLayerName) && x.RawLayerName.IndexOf("DOOR", StringComparison.OrdinalIgnoreCase) >= 0)))
                      .ToList();
        }

        private static List<DoorCandidate> GenerateCandidatesByComponentRouting(
            List<CadSegment> doorSegments,
            DoorDetectResult result,
            DoorDetectSettings settings)
        {
            return GenerateCandidatesByComponentRouting(doorSegments, result, settings, doorSegments, null);
        }

        private static List<DoorCandidate> GenerateCandidatesByComponentRouting(
            List<CadSegment> doorSegments,
            DoorDetectResult result,
            DoorDetectSettings settings,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext)
        {
            List<DoorCandidate> rawCandidates = new List<DoorCandidate>();
            List<CadSegment> segments = (doorSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            List<CadSegment> contextSegments = (routeContextSegments ?? doorSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            result.ArcCountOnDoorLayer = segments.Count(x => x.IsArc);

            List<List<CadSegment>> components = BuildDoorComponents(segments, settings);
            HashSet<string> enabledRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int componentId = 1;

            foreach (List<CadSegment> component in components)
            {
                List<CadSegment> componentSegments = (component ?? new List<CadSegment>()).Where(x => x != null).ToList();
                if (componentSegments.Count == 0)
                {
                    continue;
                }

                int arcCount = componentSegments.Count(x => x.IsArc);
                int lineCount = componentSegments.Count - arcCount;
                BoundingBoxXYZ bbox = ComputeComponentBBox(componentSegments);
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteComponent] ComponentId=" + componentId +
                    ", Segments=" + componentSegments.Count +
                    ", Arcs=" + arcCount +
                    ", Lines=" + lineCount +
                    ", ContextSegments=" + contextSegments.Count +
                    ", WallContextCount=" + (wallContext == null ? 0 : wallContext.Count) +
                    ", BBoxMin=" + FormatPoint(bbox?.Min) +
                    ", BBoxMax=" + FormatPoint(bbox?.Max) +
                    ", SegmentIds=" + string.Join("|", componentSegments.Select(x => x.SegmentId).OrderBy(x => x)));

                TripleArcDoorWithWallCrossingSummary r3tSummary;
                DoorSymbolFamilyKind routedKind;
                if (TryResolveTripleArcDoorWithWallCrossing(componentSegments, contextSegments, wallContext, settings, componentId, out r3tSummary))
                {
                    routedKind = DoorSymbolFamilyKind.TripleArcDoorWithWallCrossing;
                }
                else
                {
                    routedKind = ResolveDoorSymbolFamilyKind(componentSegments, contextSegments, wallContext, settings, componentId);
                }
                HardWallContactMatch preferredHardWallMatch = null;
                if (routedKind == DoorSymbolFamilyKind.StandardArcDoor ||
                    routedKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing)
                {
                    TryResolveHardWallCrossingContact(componentSegments, contextSegments, wallContext, settings, componentId, out preferredHardWallMatch);
                }

                if (routedKind == DoorSymbolFamilyKind.TripleArcDoorWithWallCrossing)
                {
                    enabledRules.Add("R3T");
                    DiagnosticRecorder.AppendDebug(
                        "[DoorRouteComponentKind] ComponentId=" + componentId +
                        ", RoutedKind=" + routedKind +
                        ", SelectedRule=R3T");

                    DoorCandidate r3tCandidate = BuildR3TCandidate(componentSegments, r3tSummary, componentId);
                    if (r3tCandidate != null)
                    {
                        AccumulateRuleCount(result, "R3T", 1);
                        DiagnosticRecorder.AppendDebug(
                            "[DoorRouteCandidate] ComponentId=" + componentId +
                            ", RuleSource=" + (r3tCandidate.RuleSource ?? string.Empty) +
                            ", DoorKind=Single" +
                            ", OpeningCenter=" + FormatPoint(r3tCandidate.OpeningCenterPoint ?? r3tCandidate.CenterPoint) +
                            ", OpeningDir=" + FormatVector2D(ResolveCandidateOpeningDirection(r3tCandidate)) +
                            ", OpeningWidthMm=" + r3tCandidate.OpeningWidthMm.ToString("F1"));
                        rawCandidates.Add(r3tCandidate);
                    }

                    componentId++;
                    continue;
                }

                List<IDoorCandidateRule> rules = BuildRulesForDoorComponent(componentSegments, routedKind);
                string selectedRule = rules.Count == 0 ? "<none>" : string.Join("|", rules.Select(x => x.Name));
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteComponentKind] ComponentId=" + componentId +
                    ", RoutedKind=" + routedKind +
                    ", SelectedRule=" + selectedRule);

                foreach (IDoorCandidateRule rule in rules)
                {
                    enabledRules.Add(rule.Name);
                    bool usedR3CDFallback = false;
                    List<DoorCandidate> byRule = rule.GenerateCandidates(componentSegments, settings).ToList();

                    if (preferredHardWallMatch != null &&
                        (string.Equals(rule.Name, "R3", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(rule.Name, "R3D", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (DoorCandidate c in byRule)
                        {
                            if (c == null)
                            {
                                continue;
                            }

                            c.PreferredHostWallId = preferredHardWallMatch.PreferredWallId;
                            if (c.PreferredHostPoint == null)
                            {
                                c.PreferredHostPoint = c.HingePoint ?? c.OpeningCenterPoint ?? c.ArcMidPoint ?? c.CenterPoint;
                            }

                            DiagnosticRecorder.AppendDebug(
                                "[DoorRoutePreferredHostWallBinding] ComponentId=" + componentId +
                                ", Rule=" + rule.Name +
                                ", CandidateRuleSource=" + (c.RuleSource ?? string.Empty) +
                                ", PreferredWallId=" + (preferredHardWallMatch.PreferredWallId == null ? 0 : preferredHardWallMatch.PreferredWallId.IntegerValue) +
                                ", SourceWallSegmentId=" + preferredHardWallMatch.WallSegmentId +
                                ", ArcSegmentId=" + preferredHardWallMatch.ArcSegmentId +
                                ", LeafSegmentId=" + preferredHardWallMatch.LeafSegmentId +
                                ", PreferredHostPoint=" + FormatPoint(c.PreferredHostPoint));
                        }
                    }

                    if (byRule.Count == 0 &&
                        routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD &&
                        string.Equals(rule.Name, "R3CD", StringComparison.OrdinalIgnoreCase))
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[R3CDRouteFallback] ComponentId=" + componentId +
                            ", From=R3CD, To=<disabled>, Reason=R3CDReturnedZeroCandidates");
                    }

                    if (!usedR3CDFallback)
                    {
                        AccumulateRuleCount(result, rule.Name, byRule.Count);
                        AssignSymbolFamilyKind(byRule, rule, routedKind);
                    }

                    foreach (DoorCandidate c in byRule)
                    {
                        if (c == null)
                        {
                            continue;
                        }

                        XYZ dir = ResolveCandidateOpeningDirection(c);
                        string doorKind = c.IsDoubleDoor ? "Double" : "Single";
                        double openingWidthMm = c.VirtualOpeningWidthMm > 1e-6
                            ? c.VirtualOpeningWidthMm
                            : (c.OpeningWidthMm > 1e-6 ? c.OpeningWidthMm : c.WidthMm);
                        DiagnosticRecorder.AppendDebug(
                            "[DoorRouteCandidate] ComponentId=" + componentId +
                            ", RuleSource=" + (c.RuleSource ?? string.Empty) +
                            ", DoorKind=" + doorKind +
                            ", OpeningCenter=" + FormatPoint(c.OpeningCenterPoint ?? c.CenterPoint) +
                            ", OpeningDir=" + FormatVector2D(dir) +
                            ", OpeningWidthMm=" + openingWidthMm.ToString("F1"));
                    }

                    rawCandidates.AddRange(byRule);
                }

                componentId++;
            }

            result.EnabledRules = enabledRules.OrderBy(x => x).ToList();
            return rawCandidates;
        }

        private static List<List<CadSegment>> BuildDoorComponents(List<CadSegment> segments, DoorDetectSettings settings)
        {
            List<List<CadSegment>> components = new List<List<CadSegment>>();
            List<CadSegment> all = (segments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            if (all.Count == 0)
            {
                return components;
            }

            double clusterTolMm = 60.0;
            double clusterTolFt = UnitUtils.ConvertToInternalUnits(clusterTolMm, UnitTypeId.Millimeters);
            DiagnosticRecorder.AppendDebug("[DoorComponentCluster] ClusterTolMm=" + clusterTolMm.ToString("F1") + ", Rule=EndpointOnly");
            bool[] visited = new bool[all.Count];

            for (int i = 0; i < all.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<CadSegment> component = new List<CadSegment>();
                Queue<int> q = new Queue<int>();
                q.Enqueue(i);
                visited[i] = true;

                while (q.Count > 0)
                {
                    int idx = q.Dequeue();
                    CadSegment current = all[idx];
                    component.Add(current);

                    for (int j = 0; j < all.Count; j++)
                    {
                        if (visited[j])
                        {
                            continue;
                        }

                        ConnectionEval eval = EvaluateSegmentConnectionForRouting(current, all[j], all, clusterTolFt, settings);
                        if (eval.IsEndpointNear && eval.IsBlockedByBarrier)
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[DoorComponentBlock] A=" + current.SegmentId +
                                ", B=" + all[j].SegmentId +
                                ", EndpointDistMm=" + UnitUtils.ConvertFromInternalUnits(eval.EndpointDistanceFt, UnitTypeId.Millimeters).ToString("F1") +
                                ", ClusterTolMm=" + clusterTolMm.ToString("F1") +
                                ", BarrierSegmentId=" + eval.BarrierSegmentId);
                        }

                        if (eval.ShouldConnect)
                        {
                            visited[j] = true;
                            q.Enqueue(j);
                            DiagnosticRecorder.AppendDebug(
                                "[DoorComponentMerge] A=" + current.SegmentId +
                                ", B=" + all[j].SegmentId +
                                ", Reason=EndpointNear" +
                                ", EndpointDistMm=" + UnitUtils.ConvertFromInternalUnits(eval.EndpointDistanceFt, UnitTypeId.Millimeters).ToString("F1") +
                                ", ClusterTolMm=" + clusterTolMm.ToString("F1") +
                                ", BarrierBlocked=False");
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private sealed class ConnectionEval
        {
            public bool IsEndpointNear { get; set; }
            public bool IsBlockedByBarrier { get; set; }
            public bool ShouldConnect => IsEndpointNear && !IsBlockedByBarrier;
            public double EndpointDistanceFt { get; set; }
            public int BarrierSegmentId { get; set; } = 0;
        }

        private sealed class HardWallContextLine
        {
            public CadSegment Segment { get; set; }
            public ElementId WallId { get; set; }
            public bool IsFromHostWall { get; set; }
        }

        private sealed class HardWallContactMatch
        {
            public ElementId PreferredWallId { get; set; }
            public int WallSegmentId { get; set; }
            public double WallLengthMm { get; set; }
            public int LeafSegmentId { get; set; }
            public int ArcSegmentId { get; set; }
            public double Score { get; set; }
            public double CenterDistMm { get; set; }
            public double JunctionDistMm { get; set; }
            public double LeafDistMm { get; set; }
            public double DirectionPenalty { get; set; }
            public int ArcSupportCount { get; set; }
            public bool IsFromHostWall { get; set; }
        }

        private sealed class TripleArcDoorWithWallCrossingSummary
        {
            public List<R3TArcOpeningInfo> ArcInfos { get; } = new List<R3TArcOpeningInfo>();
            public ElementId PreferredHostWallId { get; set; }
            public XYZ OpeningBaseStartPoint { get; set; }
            public XYZ OpeningBaseEndPoint { get; set; }
            public XYZ OpeningCenterPoint { get; set; }
            public XYZ OpeningDirection { get; set; }
            public double TotalWidthMm { get; set; }
            public double Gap12Mm { get; set; }
            public double Gap23Mm { get; set; }
            public bool OpeningSideConsistent { get; set; }
        }

        private sealed class R3TArcOpeningInfo
        {
            public CadSegment Arc { get; set; }
            public CadSegment LeafLine { get; set; }
            public XYZ ArcConnectedEnd { get; set; }
            public XYZ ArcFreeEnd { get; set; }
            public XYZ LeafConnectedEnd { get; set; }
            public XYZ LeafFreeEnd { get; set; }
            public XYZ OpeningBaseStart { get; set; }
            public XYZ OpeningBaseEnd { get; set; }
            public XYZ OpeningCenter { get; set; }
            public XYZ OpeningDir { get; set; }
            public double WidthMm { get; set; }
            public double StartCoordFt { get; set; }
            public double EndCoordFt { get; set; }
            public double SideSign { get; set; }
        }

        private sealed class R3TSubgroupInfo
        {
            public string StructureKind { get; set; }
            public List<R3TArcOpeningInfo> Members { get; } = new List<R3TArcOpeningInfo>();
            public XYZ OpeningBaseStart { get; set; }
            public XYZ OpeningBaseEnd { get; set; }
            public XYZ OpeningCenter { get; set; }
            public XYZ OpeningDir { get; set; }
            public double WidthMm { get; set; }
            public double StartCoordFt { get; set; }
            public double EndCoordFt { get; set; }
            public double SideSign { get; set; }
        }

        private static bool TryResolveTripleArcDoorWithWallCrossing(
            List<CadSegment> segments,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings,
            int componentId,
            out TripleArcDoorWithWallCrossingSummary summary)
        {
            summary = null;
            List<CadSegment> arcs = (segments ?? new List<CadSegment>())
                .Where(x => x != null && x.IsArc && x.P0 != null && x.P1 != null && x.Center != null)
                .Where(x => IsArcInConfiguredRange(x, settings))
                .ToList();

            DiagnosticRecorder.AppendDebug(
                "[R3TPrecheck] ComponentId=" + componentId +
                ", MainArcCount=" + arcs.Count +
                ", RouteContextCount=" + (routeContextSegments == null ? 0 : routeContextSegments.Count) +
                ", WallContextCount=" + (wallContext == null ? 0 : wallContext.Count));

            if (arcs.Count != 3)
            {
                return false;
            }

            List<CadSegment> lines = (segments ?? new List<CadSegment>())
                .Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null)
                .ToList();
            if (lines.Count == 0 || wallContext == null || wallContext.Count == 0)
            {
                return false;
            }

            List<R3TArcOpeningInfo> arcInfos = new List<R3TArcOpeningInfo>();
            foreach (CadSegment arc in arcs)
            {
                R3TArcOpeningInfo info;
                if (!TryResolveR3TArcOpeningInfo(arc, lines, settings, out info))
                {
                    return false;
                }

                if (info.WidthMm < 500.0)
                {
                    return false;
                }

                arcInfos.Add(info);
            }

            List<R3TSubgroupInfo> subgroups;
            string structureKind;
            if (!TryBuildR3TSubgroups(arcInfos, settings, out subgroups, out structureKind))
            {
                return false;
            }

            XYZ dominantDir = ResolveDominantR3TSubgroupDirection(subgroups);
            if (dominantDir == null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3TRejectReason] ComponentId=" + componentId +
                    ", Reason=NoDominantDirection" +
                    ", Structure=" + (structureKind ?? string.Empty));
                return false;
            }

            for (int subgroupIndex = 0; subgroupIndex < subgroups.Count; subgroupIndex++)
            {
                R3TSubgroupInfo subgroup = subgroups[subgroupIndex];
                double parallel = subgroup.OpeningDir == null ? 0.0 : Math.Abs(dominantDir.DotProduct(subgroup.OpeningDir));
                if (parallel < 0.965925826)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[R3TRejectReason] ComponentId=" + componentId +
                        ", Reason=SubgroupDirectionNotParallel" +
                        ", Structure=" + (structureKind ?? string.Empty) +
                        ", SubgroupIndex=" + subgroupIndex +
                        ", ParallelAbs=" + parallel.ToString("F4"));
                    return false;
                }
            }

            foreach (R3TSubgroupInfo subgroup in subgroups)
            {
                if (subgroup.OpeningDir != null && subgroup.OpeningDir.DotProduct(dominantDir) < 0.0)
                {
                    XYZ tmpPoint = subgroup.OpeningBaseStart;
                    subgroup.OpeningBaseStart = subgroup.OpeningBaseEnd;
                    subgroup.OpeningBaseEnd = tmpPoint;
                    subgroup.OpeningDir = Normalize2D(subgroup.OpeningBaseEnd - subgroup.OpeningBaseStart);
                    subgroup.SideSign = -subgroup.SideSign;
                }

                subgroup.StartCoordFt = Dot2D(subgroup.OpeningBaseStart, dominantDir);
                subgroup.EndCoordFt = Dot2D(subgroup.OpeningBaseEnd, dominantDir);
                if (subgroup.StartCoordFt > subgroup.EndCoordFt)
                {
                    double tmpCoord = subgroup.StartCoordFt;
                    subgroup.StartCoordFt = subgroup.EndCoordFt;
                    subgroup.EndCoordFt = tmpCoord;
                    XYZ tmpPoint = subgroup.OpeningBaseStart;
                    subgroup.OpeningBaseStart = subgroup.OpeningBaseEnd;
                    subgroup.OpeningBaseEnd = tmpPoint;
                    subgroup.SideSign = -subgroup.SideSign;
                }
            }

            bool sideConsistent;
            if (string.Equals(structureKind, "2+1", StringComparison.OrdinalIgnoreCase))
            {
                // For 2+1 (double subgroup + single subgroup), subgroup SideSign values are not
                // guaranteed to be globally same-signed, because the double subgroup is built from
                // mirrored leaves while the single subgroup is built from a single arc.
                // The old all-same-sign rule only fits 1+1+1 and would incorrectly reject valid 2+1.
                sideConsistent = true;
                DiagnosticRecorder.AppendDebug(
                    "[R3TSideConsistencyBypass] ComponentId=" + componentId +
                    ", Structure=2+1" +
                    ", Reason=SubgroupSideSignRuleDisabledForTwoPlusOne");
            }
            else
            {
                sideConsistent = subgroups.All(x => x.SideSign > 0) || subgroups.All(x => x.SideSign < 0);
                if (!sideConsistent)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[R3TRejectReason] ComponentId=" + componentId +
                        ", Reason=SideInconsistent" +
                        ", Structure=" + (structureKind ?? string.Empty));
                    return false;
                }
            }

            foreach (R3TArcOpeningInfo info in arcInfos)
            {
                info.StartCoordFt = Dot2D(info.OpeningBaseStart, dominantDir);
                info.EndCoordFt = Dot2D(info.OpeningBaseEnd, dominantDir);
                if (info.StartCoordFt > info.EndCoordFt)
                {
                    double tmp = info.StartCoordFt;
                    info.StartCoordFt = info.EndCoordFt;
                    info.EndCoordFt = tmp;
                    XYZ tmpPoint = info.OpeningBaseStart;
                    info.OpeningBaseStart = info.OpeningBaseEnd;
                    info.OpeningBaseEnd = tmpPoint;
                }
            }

            List<R3TSubgroupInfo> orderedSubgroups = subgroups.OrderBy(x => x.StartCoordFt).ToList();
            List<double> gapsMm = new List<double>();
            for (int i = 1; i < orderedSubgroups.Count; i++)
            {
                gapsMm.Add(FtToMm(Math.Max(0.0, orderedSubgroups[i].StartCoordFt - orderedSubgroups[i - 1].EndCoordFt)));
            }

            if (gapsMm.Any(x => x > 150.0))
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3TRejectReason] ComponentId=" + componentId +
                    ", Reason=GapTooLarge" +
                    ", Structure=" + (structureKind ?? string.Empty) +
                    ", GapValuesMm=" + string.Join("|", gapsMm.Select(x => x.ToString("F1"))));
                return false;
            }

            double gap12Mm = gapsMm.Count > 0 ? gapsMm[0] : 0.0;
            double gap23Mm = gapsMm.Count > 1 ? gapsMm[1] : 0.0;

            XYZ totalStart = orderedSubgroups[0].OpeningBaseStart;
            XYZ totalEnd = orderedSubgroups[orderedSubgroups.Count - 1].OpeningBaseEnd;
            double totalWidthMm = FtToMm(totalStart.DistanceTo(totalEnd));
            if (totalWidthMm < 1800.0 || totalWidthMm > 3600.0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3TRejectReason] ComponentId=" + componentId +
                    ", Reason=TotalWidthOutOfRange" +
                    ", Structure=" + (structureKind ?? string.Empty) +
                    ", TotalWidthMm=" + totalWidthMm.ToString("F1"));
                return false;
            }

            ElementId sharedWallId;
            XYZ projectedStart;
            XYZ projectedEnd;
            XYZ projectedCenter;
            bool hasSharedHostBand = TryResolveR3TSharedHostWall(orderedSubgroups, wallContext, settings, dominantDir, out sharedWallId, out projectedStart, out projectedEnd, out projectedCenter);
            if (!hasSharedHostBand)
            {
                projectedStart = totalStart;
                projectedEnd = totalEnd;
                projectedCenter = Mid(projectedStart, projectedEnd);
            }

            summary = new TripleArcDoorWithWallCrossingSummary
            {
                PreferredHostWallId = sharedWallId,
                OpeningBaseStartPoint = projectedStart,
                OpeningBaseEndPoint = projectedEnd,
                OpeningCenterPoint = projectedCenter,
                OpeningDirection = dominantDir,
                TotalWidthMm = FtToMm(projectedStart.DistanceTo(projectedEnd)),
                Gap12Mm = gap12Mm,
                Gap23Mm = gap23Mm,
                OpeningSideConsistent = true
            };
            summary.ArcInfos.AddRange(arcInfos.OrderBy(x => x.StartCoordFt).ToList());

            DiagnosticRecorder.AppendDebug(
                "[R3TResolved] ComponentId=" + componentId +
                ", MainArcCount=" + arcInfos.Count +
                ", Structure=" + structureKind +
                ", SubgroupCount=" + orderedSubgroups.Count +
                ", SubWidth1Mm=" + orderedSubgroups[0].WidthMm.ToString("F1") +
                ", SubWidth2Mm=" + (orderedSubgroups.Count > 1 ? orderedSubgroups[1].WidthMm.ToString("F1") : "0.0") +
                ", SubWidth3Mm=" + (orderedSubgroups.Count > 2 ? orderedSubgroups[2].WidthMm.ToString("F1") : "0.0") +
                ", Gap12Mm=" + gap12Mm.ToString("F1") +
                ", Gap23Mm=" + gap23Mm.ToString("F1") +
                ", TotalWidthMm=" + summary.TotalWidthMm.ToString("F1") +
                ", SideConsistent=True" +
                ", PreferredHostWallId=" + (sharedWallId == null ? 0 : sharedWallId.IntegerValue) +
                ", HasSharedHostBand=" + hasSharedHostBand);

            return true;
        }

        private static DoorCandidate BuildR3TCandidate(
            List<CadSegment> componentSegments,
            TripleArcDoorWithWallCrossingSummary summary,
            int componentId)
        {
            if (summary == null || summary.OpeningBaseStartPoint == null || summary.OpeningBaseEndPoint == null || summary.OpeningCenterPoint == null)
            {
                return null;
            }

            List<int> segmentIds = new List<int>();
            foreach (R3TArcOpeningInfo info in summary.ArcInfos)
            {
                if (info?.Arc != null)
                {
                    segmentIds.Add(info.Arc.SegmentId);
                }

                if (info?.LeafLine != null)
                {
                    segmentIds.Add(info.LeafLine.SegmentId);
                }
            }

            DoorCandidate candidate = new DoorCandidate
            {
                CenterPoint = summary.OpeningCenterPoint,
                RuleSource = "R3T",
                SymbolFamilyKind = DoorSymbolFamilyKind.TripleArcDoorWithWallCrossing,
                IsDoubleDoor = false,
                CombinedWidthMm = summary.TotalWidthMm,
                WidthMm = summary.TotalWidthMm,
                OpeningWidthMm = summary.TotalWidthMm,
                OpeningBaseStartPoint = summary.OpeningBaseStartPoint,
                OpeningBaseEndPoint = summary.OpeningBaseEndPoint,
                OpeningCenterPoint = summary.OpeningCenterPoint,
                PreferOpeningBaseHost = true,
                PreferredHostWallId = summary.PreferredHostWallId,
                PreferredHostPoint = summary.OpeningCenterPoint,
                WallDirHint = summary.OpeningDirection,
                WidthSource = "R3TCombinedOpening",
                SegmentIds = segmentIds.Distinct().ToList()
            };

            DiagnosticRecorder.AppendDebug(
                "[R3TCandidateBuilt] ComponentId=" + componentId +
                ", PreferredHostWallId=" + (candidate.PreferredHostWallId == null ? 0 : candidate.PreferredHostWallId.IntegerValue) +
                ", OpeningBaseStart=" + FormatPoint(candidate.OpeningBaseStartPoint) +
                ", OpeningBaseEnd=" + FormatPoint(candidate.OpeningBaseEndPoint) +
                ", OpeningCenter=" + FormatPoint(candidate.OpeningCenterPoint) +
                ", TotalWidthMm=" + candidate.WidthMm.ToString("F1"));

            return candidate;
        }

        private static bool TryResolveR3TArcOpeningInfo(
            CadSegment arc,
            List<CadSegment> lines,
            DoorDetectSettings settings,
            out R3TArcOpeningInfo info)
        {
            info = null;
            if (arc == null || lines == null)
            {
                return false;
            }

            double endpointSnapTolFt = MmToFt(settings?.ArcEndpointSnapTolMm ?? 120.0);
            double minLenMm = settings?.ArcLeafLineMinLengthMm ?? 300.0;
            double maxLenMm = settings?.ArcLeafLineMaxLengthMm ?? 1600.0;
            List<R3TArcOpeningInfo> candidates = new List<R3TArcOpeningInfo>();

            foreach (CadSegment line in lines)
            {
                if (!IsR3TLeafLengthInRange(line, minLenMm, maxLenMm))
                {
                    continue;
                }

                XYZ arcConnectedEnd;
                XYZ arcFreeEnd;
                XYZ leafConnectedEnd;
                XYZ leafFreeEnd;
                if (!TryResolveConnectedAndFreeEndsForR3T(arc, line, endpointSnapTolFt, out arcConnectedEnd, out arcFreeEnd, out leafConnectedEnd, out leafFreeEnd))
                {
                    continue;
                }

                XYZ openingDir = Normalize2D(leafFreeEnd - arcFreeEnd);
                if (openingDir == null)
                {
                    continue;
                }

                double widthMm = FtToMm(arcFreeEnd.DistanceTo(leafFreeEnd));
                if (widthMm < 500.0)
                {
                    continue;
                }

                XYZ normal = new XYZ(-openingDir.Y, openingDir.X, 0);
                double sideSign = Cross2D(openingDir, (arc.MidPoint ?? Mid(arc.P0, arc.P1)) - arcFreeEnd);
                candidates.Add(new R3TArcOpeningInfo
                {
                    Arc = arc,
                    LeafLine = line,
                    ArcConnectedEnd = arcConnectedEnd,
                    ArcFreeEnd = arcFreeEnd,
                    LeafConnectedEnd = leafConnectedEnd,
                    LeafFreeEnd = leafFreeEnd,
                    OpeningBaseStart = arcFreeEnd,
                    OpeningBaseEnd = leafFreeEnd,
                    OpeningCenter = Mid(arcFreeEnd, leafFreeEnd),
                    OpeningDir = openingDir,
                    WidthMm = widthMm,
                    SideSign = Math.Abs(sideSign) < 1e-9 ? Dot2D((arc.Center ?? arc.MidPoint) - arcFreeEnd, normal) : sideSign
                });
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            List<List<R3TArcOpeningInfo>> groups = BuildParallelR3TLeafGroups(candidates);
            if (groups.Count == 0)
            {
                return false;
            }

            List<R3TArcOpeningInfo> selectedGroup = groups
                .OrderBy(x => x.Min(y => y.ArcConnectedEnd.DistanceTo(y.LeafConnectedEnd)))
                .ThenBy(x => x.Min(y => y.WidthMm))
                .First();

            info = selectedGroup
                .OrderBy(x => x.ArcConnectedEnd.DistanceTo(x.LeafConnectedEnd))
                .ThenBy(x => x.WidthMm)
                .ThenBy(x => x.LeafLine == null ? int.MaxValue : x.LeafLine.SegmentId)
                .FirstOrDefault();
            return info != null;
        }

        private static bool TryResolveR3TSharedHostWall(
            List<R3TSubgroupInfo> ordered,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings,
            XYZ dominantDir,
            out ElementId wallId,
            out XYZ projectedStart,
            out XYZ projectedEnd,
            out XYZ projectedCenter)
        {
            wallId = null;
            projectedStart = null;
            projectedEnd = null;
            projectedCenter = null;
            if (ordered == null || ordered.Count < 2 || wallContext == null || wallContext.Count == 0 || dominantDir == null)
            {
                return false;
            }

            double tolFt = MmToFt(settings?.WallMatchDistTolMm ?? 500.0);
            double mergeGapFt = MmToFt(150.0);
            double bandPerpTolFt = MmToFt(160.0);
            double bestScore = double.MaxValue;
            Line bestBandLine = null;
            XYZ totalStart = ordered[0].OpeningBaseStart;
            XYZ totalEnd = ordered[ordered.Count - 1].OpeningBaseEnd;
            List<R3TWallBand> bands = BuildR3TWallBands(wallContext, dominantDir, mergeGapFt, bandPerpTolFt);
            foreach (R3TWallBand band in bands)
            {
                if (band == null || band.RepresentativeWallId == null || band.RepresentativeWallId == ElementId.InvalidElementId || band.Line == null)
                {
                    continue;
                }

                ProjectionData ps = ProjectPointToLineSegment(totalStart, band.Line);
                ProjectionData pe = ProjectPointToLineSegment(totalEnd, band.Line);
                if (!ps.IsInsideSegment || !pe.IsInsideSegment || ps.DistanceFeet > tolFt || pe.DistanceFeet > tolFt)
                {
                    continue;
                }

                double totalDist = ps.DistanceFeet + pe.DistanceFeet;
                bool allCovered = true;
                foreach (R3TSubgroupInfo info in ordered)
                {
                    ProjectionData pc = ProjectPointToLineSegment(info.OpeningCenter, band.Line);
                    if (!pc.IsInsideSegment || pc.DistanceFeet > tolFt)
                    {
                        allCovered = false;
                        break;
                    }

                    totalDist += pc.DistanceFeet;
                }

                if (!allCovered)
                {
                    continue;
                }

                if (totalDist < bestScore)
                {
                    bestScore = totalDist;
                    bestBandLine = band.Line;
                    wallId = band.RepresentativeWallId;
                    projectedStart = ps.ProjectedPoint;
                    projectedEnd = pe.ProjectedPoint;
                    projectedCenter = Mid(projectedStart, projectedEnd);
                }
            }

            if (wallId != null && wallId != ElementId.InvalidElementId)
            {
                XYZ originalStart = projectedStart;
                XYZ originalEnd = projectedEnd;
                if (ApplyR3THostEdgeReserve(bestBandLine, dominantDir, ref projectedStart, ref projectedEnd))
                {
                    projectedCenter = Mid(projectedStart, projectedEnd);
                    DiagnosticRecorder.AppendDebug(
                        "[R3THardWallEdgeReserveApplied] PreferredHostWallId=" + wallId.IntegerValue +
                        ", OriginalStart=" + FormatPoint(originalStart) +
                        ", OriginalEnd=" + FormatPoint(originalEnd) +
                        ", AdjustedStart=" + FormatPoint(projectedStart) +
                        ", AdjustedEnd=" + FormatPoint(projectedEnd));
                }

                DiagnosticRecorder.AppendDebug(
                    "[R3THardWallHit] PreferredHostWallId=" + wallId.IntegerValue +
                    ", OpeningBaseStart=" + FormatPoint(projectedStart) +
                    ", OpeningBaseEnd=" + FormatPoint(projectedEnd) +
                    ", OpeningCenter=" + FormatPoint(projectedCenter));
                return true;
            }

            R3TWallBand softBand = null;
            double softScore = double.MaxValue;
            XYZ totalCenter = Mid(totalStart, totalEnd);
            foreach (R3TWallBand band in bands)
            {
                if (band == null || band.RepresentativeWallId == null || band.RepresentativeWallId == ElementId.InvalidElementId || band.Line == null)
                {
                    continue;
                }

                ProjectionData pc = ProjectPointToLineSegment(totalCenter, band.Line);
                if (!pc.IsInsideSegment)
                {
                    continue;
                }

                double score = pc.DistanceFeet;
                foreach (R3TSubgroupInfo info in ordered)
                {
                    if (info?.OpeningCenter == null)
                    {
                        continue;
                    }

                    ProjectionData psi = ProjectPointToLineSegment(info.OpeningCenter, band.Line);
                    if (!psi.IsInsideSegment)
                    {
                        score = double.MaxValue;
                        break;
                    }

                    score += psi.DistanceFeet;
                }

                if (score < softScore)
                {
                    softScore = score;
                    softBand = band;
                }
            }

            if (softBand != null)
            {
                wallId = softBand.RepresentativeWallId;
                bestBandLine = softBand.Line;
                projectedStart = ProjectPointToInfiniteLine(totalStart, softBand.Line);
                projectedEnd = ProjectPointToInfiniteLine(totalEnd, softBand.Line);
                if (projectedStart != null && projectedEnd != null && Dot2D(projectedEnd - projectedStart, dominantDir) < 0.0)
                {
                    XYZ tmp = projectedStart;
                    projectedStart = projectedEnd;
                    projectedEnd = tmp;
                }

                projectedCenter = Mid(projectedStart, projectedEnd);

                XYZ originalStart = projectedStart;
                XYZ originalEnd = projectedEnd;
                if (ApplyR3THostEdgeReserve(bestBandLine, dominantDir, ref projectedStart, ref projectedEnd))
                {
                    projectedCenter = Mid(projectedStart, projectedEnd);
                    DiagnosticRecorder.AppendDebug(
                        "[R3THardWallEdgeReserveApplied] PreferredHostWallId=" + wallId.IntegerValue +
                        ", OriginalStart=" + FormatPoint(originalStart) +
                        ", OriginalEnd=" + FormatPoint(originalEnd) +
                        ", AdjustedStart=" + FormatPoint(projectedStart) +
                        ", AdjustedEnd=" + FormatPoint(projectedEnd));
                }

                DiagnosticRecorder.AppendDebug(
                    "[R3THardWallSoftHit] PreferredHostWallId=" + wallId.IntegerValue +
                    ", OpeningBaseStart=" + FormatPoint(projectedStart) +
                    ", OpeningBaseEnd=" + FormatPoint(projectedEnd) +
                    ", OpeningCenter=" + FormatPoint(projectedCenter) +
                    ", SoftScoreFt=" + softScore.ToString("F4"));
                return true;
            }

            DiagnosticRecorder.AppendDebug(
                "[R3THardWallHitMiss] OrderedSubgroupCount=" + ordered.Count +
                ", TotalStart=" + FormatPoint(totalStart) +
                ", TotalEnd=" + FormatPoint(totalEnd) +
                ", DominantDir=" + FormatVector2D(dominantDir));
            return false;
        }

        private static bool ApplyR3THostEdgeReserve(
            Line bandLine,
            XYZ dominantDir,
            ref XYZ projectedStart,
            ref XYZ projectedEnd)
        {
            if (bandLine == null || dominantDir == null || projectedStart == null || projectedEnd == null)
            {
                return false;
            }

            XYZ alongDir = Normalize2D(dominantDir);
            if (alongDir == null)
            {
                return false;
            }

            XYZ lineP0 = bandLine.GetEndPoint(0);
            XYZ lineP1 = bandLine.GetEndPoint(1);
            if (lineP0 == null || lineP1 == null)
            {
                return false;
            }

            XYZ bandStart = lineP0;
            XYZ bandEnd = lineP1;
            if (Dot2D(lineP1 - lineP0, alongDir) < 0.0)
            {
                bandStart = lineP1;
                bandEnd = lineP0;
            }

            double reserveFt = MmToFt(15.0);
            double triggerFt = MmToFt(25.0);
            double minRemainSpanFt = MmToFt(1600.0);

            double startGapFt = Math.Abs(Dot2D(projectedStart - bandStart, alongDir));
            double endGapFt = Math.Abs(Dot2D(bandEnd - projectedEnd, alongDir));
            double currentSpanFt = Math.Abs(Dot2D(projectedEnd - projectedStart, alongDir));

            bool nearStartEdge = startGapFt <= triggerFt;
            bool nearEndEdge = endGapFt <= triggerFt;
            if (!nearStartEdge && !nearEndEdge)
            {
                return false;
            }

            XYZ adjustedStart = projectedStart;
            XYZ adjustedEnd = projectedEnd;

            if (nearStartEdge && startGapFt < reserveFt)
            {
                adjustedStart = projectedStart + alongDir.Multiply(reserveFt - startGapFt);
            }

            if (nearEndEdge && endGapFt < reserveFt)
            {
                adjustedEnd = projectedEnd - alongDir.Multiply(reserveFt - endGapFt);
            }

            double adjustedSpanFt = Math.Abs(Dot2D(adjustedEnd - adjustedStart, alongDir));
            if (adjustedSpanFt < minRemainSpanFt || adjustedSpanFt >= currentSpanFt - 1e-9)
            {
                return false;
            }

            projectedStart = adjustedStart;
            projectedEnd = adjustedEnd;
            return true;
        }

        private static bool TryBuildR3TSubgroups(
            List<R3TArcOpeningInfo> arcInfos,
            DoorDetectSettings settings,
            out List<R3TSubgroupInfo> subgroups,
            out string structureKind)
        {
            subgroups = null;
            structureKind = null;
            if (arcInfos == null || arcInfos.Count != 3)
            {
                return false;
            }

            List<R3TSubgroupInfo> twoPlusOne;
            if (TryBuildR3TTwoPlusOneSubgroups(arcInfos, settings, out twoPlusOne))
            {
                subgroups = twoPlusOne;
                structureKind = "2+1";
                return true;
            }

            subgroups = arcInfos
                .Select(x => BuildR3TSingleSubgroup(x))
                .Where(x => x != null)
                .ToList();
            if (subgroups.Count != 3)
            {
                return false;
            }

            structureKind = "1+1+1";
            return true;
        }

        private static bool TryBuildR3TTwoPlusOneSubgroups(
            List<R3TArcOpeningInfo> arcInfos,
            DoorDetectSettings settings,
            out List<R3TSubgroupInfo> subgroups)
        {
            subgroups = null;
            if (arcInfos == null || arcInfos.Count != 3)
            {
                return false;
            }

            double junctionTolFt = MmToFt(settings?.ArcEndpointSnapTolMm ?? 120.0);
            double bestScore = double.MaxValue;
            List<R3TSubgroupInfo> best = null;

            for (int i = 0; i < arcInfos.Count; i++)
            {
                for (int j = i + 1; j < arcInfos.Count; j++)
                {
                    R3TSubgroupInfo doubleGroup;
                    if (!TryBuildR3TDoubleSubgroup(arcInfos[i], arcInfos[j], junctionTolFt, out doubleGroup))
                    {
                        continue;
                    }

                    int singleIndex = Enumerable.Range(0, arcInfos.Count).First(x => x != i && x != j);
                    R3TSubgroupInfo singleGroup = BuildR3TSingleSubgroup(arcInfos[singleIndex]);
                    if (singleGroup == null)
                    {
                        continue;
                    }

                    double sharedScore = ResolveR3TSharedFreeEndDistance(arcInfos[i], arcInfos[j]);
                    double totalScore = sharedScore + Math.Abs(doubleGroup.WidthMm - (arcInfos[i].WidthMm + arcInfos[j].WidthMm));
                    if (totalScore < bestScore)
                    {
                        bestScore = totalScore;
                        best = new List<R3TSubgroupInfo> { doubleGroup, singleGroup };
                    }
                }
            }

            if (best == null)
            {
                return false;
            }

            subgroups = best;
            return true;
        }

        private static R3TSubgroupInfo BuildR3TSingleSubgroup(R3TArcOpeningInfo info)
        {
            if (info == null || info.OpeningBaseStart == null || info.OpeningBaseEnd == null || info.OpeningCenter == null || info.OpeningDir == null)
            {
                return null;
            }

            R3TSubgroupInfo subgroup = new R3TSubgroupInfo
            {
                StructureKind = "Single",
                OpeningBaseStart = info.OpeningBaseStart,
                OpeningBaseEnd = info.OpeningBaseEnd,
                OpeningCenter = info.OpeningCenter,
                OpeningDir = info.OpeningDir,
                WidthMm = info.WidthMm,
                SideSign = info.SideSign
            };
            subgroup.Members.Add(info);
            return subgroup;
        }

        private static bool TryBuildR3TDoubleSubgroup(
            R3TArcOpeningInfo first,
            R3TArcOpeningInfo second,
            double junctionTolFt,
            out R3TSubgroupInfo subgroup)
        {
            subgroup = null;
            if (first == null || second == null)
            {
                return false;
            }

            XYZ sharedJunction = ResolveR3TSharedPairJunction(first, second, junctionTolFt);
            if (sharedJunction == null)
            {
                return false;
            }

            if (first.ArcFreeEnd == null || second.ArcFreeEnd == null ||
                first.LeafFreeEnd == null || second.LeafFreeEnd == null)
            {
                return false;
            }

            if (first.ArcFreeEnd.DistanceTo(sharedJunction) > junctionTolFt ||
                second.ArcFreeEnd.DistanceTo(sharedJunction) > junctionTolFt)
            {
                return false;
            }

            double dirDot = first.OpeningDir == null || second.OpeningDir == null
                ? 0.0
                : first.OpeningDir.DotProduct(second.OpeningDir);
            if (dirDot > -0.965925826)
            {
                return false;
            }

            XYZ subgroupDir = Normalize2D(second.LeafFreeEnd - first.LeafFreeEnd);
            if (subgroupDir == null)
            {
                return false;
            }

            XYZ subgroupStart = first.LeafFreeEnd;
            XYZ subgroupEnd = second.LeafFreeEnd;
            double startCoord = Dot2D(subgroupStart, subgroupDir);
            double endCoord = Dot2D(subgroupEnd, subgroupDir);
            if (startCoord > endCoord)
            {
                XYZ tmp = subgroupStart;
                subgroupStart = subgroupEnd;
                subgroupEnd = tmp;
                subgroupDir = Normalize2D(subgroupEnd - subgroupStart);
            }

            XYZ subgroupCenter = Mid(subgroupStart, subgroupEnd);
            double subgroupWidthMm = FtToMm(subgroupStart.DistanceTo(subgroupEnd));
            if (subgroupWidthMm < 1000.0)
            {
                return false;
            }

            XYZ normal = new XYZ(-subgroupDir.Y, subgroupDir.X, 0.0);
            XYZ arcMidFirst = first.Arc?.MidPoint ?? Mid(first.Arc?.P0, first.Arc?.P1);
            XYZ arcMidSecond = second.Arc?.MidPoint ?? Mid(second.Arc?.P0, second.Arc?.P1);
            XYZ samplePoint = AveragePoint(new[] { arcMidFirst, arcMidSecond }.Where(x => x != null).ToList());
            if (samplePoint == null)
            {
                samplePoint = AveragePoint(new[] { first.OpeningCenter, second.OpeningCenter }.Where(x => x != null).ToList());
            }

            double sideSign = samplePoint == null ? 0.0 : Dot2D(samplePoint - subgroupCenter, normal);
            if (Math.Abs(sideSign) < 1e-9)
            {
                sideSign = first.SideSign != 0.0 ? first.SideSign : second.SideSign;
            }

            subgroup = new R3TSubgroupInfo
            {
                StructureKind = "Double",
                OpeningBaseStart = subgroupStart,
                OpeningBaseEnd = subgroupEnd,
                OpeningCenter = subgroupCenter,
                OpeningDir = subgroupDir,
                WidthMm = subgroupWidthMm,
                SideSign = sideSign
            };
            subgroup.Members.Add(first);
            subgroup.Members.Add(second);
            return true;
        }

        private static XYZ ResolveR3TSharedPairJunction(
            R3TArcOpeningInfo first,
            R3TArcOpeningInfo second,
            double junctionTolFt)
        {
            List<CadSegment> pairArcs = new List<CadSegment> { first?.Arc, second?.Arc }
                .Where(x => x != null)
                .ToList();
            XYZ shared = TryResolveSharedArcJunctionPoint(pairArcs, junctionTolFt);
            if (shared != null)
            {
                return shared;
            }

            if (first?.ArcFreeEnd != null && second?.ArcFreeEnd != null && first.ArcFreeEnd.DistanceTo(second.ArcFreeEnd) <= junctionTolFt)
            {
                return Mid(first.ArcFreeEnd, second.ArcFreeEnd);
            }

            return null;
        }

        private static double ResolveR3TSharedFreeEndDistance(R3TArcOpeningInfo first, R3TArcOpeningInfo second)
        {
            if (first?.ArcFreeEnd == null || second?.ArcFreeEnd == null)
            {
                return double.MaxValue;
            }

            return FtToMm(first.ArcFreeEnd.DistanceTo(second.ArcFreeEnd));
        }

        private static XYZ ResolveDominantR3TSubgroupDirection(List<R3TSubgroupInfo> subgroups)
        {
            List<R3TSubgroupInfo> list = (subgroups ?? new List<R3TSubgroupInfo>())
                .Where(x => x != null && x.OpeningDir != null)
                .ToList();
            if (list.Count == 0)
            {
                return null;
            }

            XYZ seed = list.OrderByDescending(x => x.WidthMm).Select(x => x.OpeningDir).FirstOrDefault();
            if (seed == null)
            {
                return null;
            }

            double sx = 0.0;
            double sy = 0.0;
            foreach (R3TSubgroupInfo subgroup in list)
            {
                XYZ dir = subgroup.OpeningDir;
                if (dir.DotProduct(seed) < 0.0)
                {
                    dir = dir.Negate();
                }

                sx += dir.X;
                sy += dir.Y;
            }

            return Normalize2D(new XYZ(sx, sy, 0.0));
        }

        private sealed class R3TWallBand
        {
            public Line Line { get; set; }
            public ElementId RepresentativeWallId { get; set; }
        }

        private static List<R3TWallBand> BuildR3TWallBands(
            IList<WallHostLine> wallContext,
            XYZ dominantDir,
            double mergeGapFt,
            double bandPerpTolFt)
        {
            List<R3TWallBand> bands = new List<R3TWallBand>();
            XYZ alongDir = Normalize2D(dominantDir);
            if (wallContext == null || wallContext.Count == 0 || alongDir == null)
            {
                return bands;
            }

            XYZ origin = wallContext
                .Where(x => x != null && x.Line != null)
                .Select(x => x.Line.GetEndPoint(0))
                .FirstOrDefault();
            if (origin == null)
            {
                return bands;
            }

            XYZ perp = new XYZ(-alongDir.Y, alongDir.X, 0);
            List<R3TWallBandSeed> seeds = new List<R3TWallBandSeed>();
            foreach (WallHostLine wall in wallContext)
            {
                if (wall == null || wall.Line == null || wall.WallId == null || wall.WallId == ElementId.InvalidElementId)
                {
                    continue;
                }

                XYZ wallDir = Normalize2D(wall.Line.GetEndPoint(1) - wall.Line.GetEndPoint(0));
                if (wallDir == null || Math.Abs(wallDir.DotProduct(alongDir)) < 0.965925826)
                {
                    continue;
                }

                XYZ p0 = wall.Line.GetEndPoint(0);
                XYZ p1 = wall.Line.GetEndPoint(1);
                double s0 = Dot2D(p0 - origin, alongDir);
                double s1 = Dot2D(p1 - origin, alongDir);
                double start = Math.Min(s0, s1);
                double end = Math.Max(s0, s1);
                XYZ mid = Mid(p0, p1);
                double perpOffset = Dot2D(mid - origin, perp);
                seeds.Add(new R3TWallBandSeed
                {
                    WallId = wall.WallId,
                    StartCoordFt = start,
                    EndCoordFt = end,
                    PerpOffsetFt = perpOffset
                });
            }

            foreach (IGrouping<int, R3TWallBandSeed> group in seeds
                .GroupBy(x => (int)Math.Round(x.PerpOffsetFt / Math.Max(bandPerpTolFt, 1e-6))))
            {
                List<R3TWallBandSeed> ordered = group.OrderBy(x => x.StartCoordFt).ToList();
                if (ordered.Count == 0)
                {
                    continue;
                }

                R3TWallBandSeed current = ordered[0];
                for (int i = 1; i < ordered.Count; i++)
                {
                    R3TWallBandSeed next = ordered[i];
                    if (next.StartCoordFt - current.EndCoordFt <= mergeGapFt)
                    {
                        current.EndCoordFt = Math.Max(current.EndCoordFt, next.EndCoordFt);
                        continue;
                    }

                    bands.Add(BuildR3TWallBand(origin, alongDir, current));
                    current = next;
                }

                bands.Add(BuildR3TWallBand(origin, alongDir, current));
            }

            return bands.Where(x => x != null && x.Line != null).ToList();
        }

        private sealed class R3TWallBandSeed
        {
            public ElementId WallId { get; set; }
            public double StartCoordFt { get; set; }
            public double EndCoordFt { get; set; }
            public double PerpOffsetFt { get; set; }
        }

        private static R3TWallBand BuildR3TWallBand(XYZ origin, XYZ dominantDir, R3TWallBandSeed seed)
        {
            XYZ alongDir = Normalize2D(dominantDir);
            if (origin == null || alongDir == null || seed == null || seed.WallId == null || seed.WallId == ElementId.InvalidElementId)
            {
                return null;
            }

            XYZ perp = new XYZ(-alongDir.Y, alongDir.X, 0);
            XYZ offset = perp.Multiply(seed.PerpOffsetFt);
            XYZ start = origin + offset + alongDir.Multiply(seed.StartCoordFt);
            XYZ end = origin + offset + alongDir.Multiply(seed.EndCoordFt);
            if (start == null || end == null || start.DistanceTo(end) < 1e-9)
            {
                return null;
            }

            return new R3TWallBand
            {
                RepresentativeWallId = seed.WallId,
                Line = Line.CreateBound(start, end)
            };
        }

        private static XYZ ResolveDominantR3TOpeningDirection(List<R3TArcOpeningInfo> arcInfos)
        {
            if (arcInfos == null || arcInfos.Count == 0)
            {
                return null;
            }

            XYZ first = arcInfos.Select(x => x.OpeningDir).FirstOrDefault(x => x != null);
            if (first == null)
            {
                return null;
            }

            double sx = 0.0;
            double sy = 0.0;
            foreach (R3TArcOpeningInfo info in arcInfos)
            {
                XYZ dir = info?.OpeningDir;
                if (dir == null)
                {
                    continue;
                }

                if (dir.DotProduct(first) < 0.0)
                {
                    dir = new XYZ(-dir.X, -dir.Y, 0);
                }

                sx += dir.X;
                sy += dir.Y;
            }

            return Normalize2D(new XYZ(sx, sy, 0));
        }

        private static bool TryResolveConnectedAndFreeEndsForR3T(
            CadSegment arc,
            CadSegment leafLine,
            double endpointSnapTolFt,
            out XYZ arcConnectedEnd,
            out XYZ arcFreeEnd,
            out XYZ leafConnectedEnd,
            out XYZ leafFreeEnd)
        {
            arcConnectedEnd = null;
            arcFreeEnd = null;
            leafConnectedEnd = null;
            leafFreeEnd = null;
            if (arc == null || leafLine == null || arc.P0 == null || arc.P1 == null || leafLine.P0 == null || leafLine.P1 == null)
            {
                return false;
            }

            XYZ[] arcEnds = new[] { arc.P0, arc.P1 };
            XYZ[] leafEnds = new[] { leafLine.P0, leafLine.P1 };
            double bestPairDist = double.MaxValue;
            int bestArcIndex = -1;
            int bestLeafIndex = -1;
            for (int arcIndex = 0; arcIndex < arcEnds.Length; arcIndex++)
            {
                for (int leafIndex = 0; leafIndex < leafEnds.Length; leafIndex++)
                {
                    double dist = arcEnds[arcIndex].DistanceTo(leafEnds[leafIndex]);
                    if (dist < bestPairDist)
                    {
                        bestPairDist = dist;
                        bestArcIndex = arcIndex;
                        bestLeafIndex = leafIndex;
                    }
                }
            }

            if (bestArcIndex < 0 || bestLeafIndex < 0 || bestPairDist > endpointSnapTolFt)
            {
                return false;
            }

            arcConnectedEnd = arcEnds[bestArcIndex];
            arcFreeEnd = arcEnds[1 - bestArcIndex];
            leafConnectedEnd = leafEnds[bestLeafIndex];
            leafFreeEnd = leafEnds[1 - bestLeafIndex];
            return true;
        }

        private static List<List<R3TArcOpeningInfo>> BuildParallelR3TLeafGroups(List<R3TArcOpeningInfo> infos)
        {
            List<List<R3TArcOpeningInfo>> groups = new List<List<R3TArcOpeningInfo>>();
            foreach (R3TArcOpeningInfo info in infos ?? new List<R3TArcOpeningInfo>())
            {
                if (info == null || info.LeafLine == null)
                {
                    continue;
                }

                List<R3TArcOpeningInfo> matched = groups.FirstOrDefault(g => g.Any(x => AreR3TLeafLinesInSameGroup(x, info)));
                if (matched == null)
                {
                    matched = new List<R3TArcOpeningInfo>();
                    groups.Add(matched);
                }

                matched.Add(info);
            }

            return groups;
        }

        private static bool AreR3TLeafLinesInSameGroup(R3TArcOpeningInfo a, R3TArcOpeningInfo b)
        {
            if (a == null || b == null || a.LeafLine == null || b.LeafLine == null)
            {
                return false;
            }

            XYZ dirA = Normalize2D(a.LeafLine.P1 - a.LeafLine.P0);
            XYZ dirB = Normalize2D(b.LeafLine.P1 - b.LeafLine.P0);
            if (dirA == null || dirB == null || Math.Abs(dirA.DotProduct(dirB)) < 0.98)
            {
                return false;
            }

            XYZ midA = Mid(a.LeafLine.P0, a.LeafLine.P1);
            XYZ midB = Mid(b.LeafLine.P0, b.LeafLine.P1);
            XYZ perp = new XYZ(-dirA.Y, dirA.X, 0);
            double perpDistFt = Math.Abs(Dot2D(midB - midA, perp));
            double alongDistFt = Math.Abs(Dot2D(midB - midA, dirA));
            return perpDistFt <= MmToFt(400.0) && alongDistFt <= MmToFt(1200.0);
        }

        private static bool IsArcInConfiguredRange(CadSegment arc, DoorDetectSettings settings)
        {
            if (arc == null || !arc.IsArc)
            {
                return false;
            }

            double minSweep = DegToRad(settings?.ArcMinSweepDeg ?? 45.0);
            double maxSweep = DegToRad(settings?.ArcMaxSweepDeg ?? 135.0);
            double minRadiusFt = MmToFt(settings?.ArcMinRadiusMm ?? 400.0);
            double maxRadiusFt = MmToFt(settings?.ArcMaxRadiusMm ?? 2000.0);
            return arc.RadiusFeet >= minRadiusFt &&
                   arc.RadiusFeet <= maxRadiusFt &&
                   arc.SweepAngleRad >= minSweep &&
                   arc.SweepAngleRad <= maxSweep;
        }

        private static bool IsR3TLeafLengthInRange(CadSegment line, double minLenMm, double maxLenMm)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = FtToMm(line.P0.DistanceTo(line.P1));
            return lenMm >= minLenMm && lenMm <= maxLenMm;
        }

        private static ConnectionEval EvaluateSegmentConnectionForRouting(
            CadSegment a,
            CadSegment b,
            List<CadSegment> all,
            double tolFt,
            DoorDetectSettings settings)
        {
            ConnectionEval eval = new ConnectionEval
            {
                IsEndpointNear = false,
                IsBlockedByBarrier = false,
                EndpointDistanceFt = double.MaxValue,
                BarrierSegmentId = 0
            };
            if (a == null || b == null || all == null)
            {
                return eval;
            }

            XYZ bestA = null;
            XYZ bestB = null;
            double bestDist = double.MaxValue;
            foreach (XYZ pa in EnumerateEndpointPoints(a))
            {
                foreach (XYZ pb in EnumerateEndpointPoints(b))
                {
                    if (pa == null || pb == null)
                    {
                        continue;
                    }

                    double d = pa.DistanceTo(pb);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestA = pa;
                        bestB = pb;
                    }
                }
            }

            eval.EndpointDistanceFt = bestDist;
            eval.IsEndpointNear = bestA != null && bestB != null && bestDist <= tolFt;
            if (!eval.IsEndpointNear)
            {
                return eval;
            }

            if (TryFindBarrierSegmentBetweenEndpoints(bestA, bestB, a, b, all, tolFt, settings, out int barrierSegmentId))
            {
                eval.IsBlockedByBarrier = true;
                eval.BarrierSegmentId = barrierSegmentId;
            }

            return eval;
        }

        private static bool TryFindBarrierSegmentBetweenEndpoints(
            XYZ endpointA,
            XYZ endpointB,
            CadSegment segmentA,
            CadSegment segmentB,
            List<CadSegment> all,
            double clusterTolFt,
            DoorDetectSettings settings,
            out int barrierSegmentId)
        {
            barrierSegmentId = 0;
            if (endpointA == null || endpointB == null || all == null)
            {
                return false;
            }

            XYZ mid = Mid(endpointA, endpointB);
            if (mid == null)
            {
                return false;
            }

            double barrierMinLenMm = Math.Max(700.0, settings?.DoorWidthMinMm ?? 650.0);
            double barrierMinLenFt = UnitUtils.ConvertToInternalUnits(barrierMinLenMm, UnitTypeId.Millimeters);
            double barrierBandFt = UnitUtils.ConvertToInternalUnits(80.0, UnitTypeId.Millimeters);

            foreach (CadSegment c in all)
            {
                if (c == null || c.P0 == null || c.P1 == null)
                {
                    continue;
                }

                if (segmentA != null && c.SegmentId == segmentA.SegmentId)
                {
                    continue;
                }

                if (segmentB != null && c.SegmentId == segmentB.SegmentId)
                {
                    continue;
                }

                if (c.P0.DistanceTo(c.P1) < barrierMinLenFt)
                {
                    continue;
                }

                Line line = Line.CreateBound(c.P0, c.P1);
                ProjectionData pm = ProjectPointToLineSegment(mid, line);
                if (!pm.IsInsideSegment || pm.DistanceFeet > barrierBandFt)
                {
                    continue;
                }

                double da = DistancePointToSegment2D(endpointA, c.P0, c.P1);
                double db = DistancePointToSegment2D(endpointB, c.P0, c.P1);
                if (da <= clusterTolFt * 0.4 || db <= clusterTolFt * 0.4)
                {
                    // If endpoints almost touch the candidate barrier, treat as same local structure, not a separator.
                    continue;
                }

                double sideA = Cross2D(c.P1 - c.P0, endpointA - c.P0);
                double sideB = Cross2D(c.P1 - c.P0, endpointB - c.P0);
                if (sideA * sideB >= 0)
                {
                    continue;
                }

                barrierSegmentId = c.SegmentId;
                return true;
            }

            return false;
        }

        private static IEnumerable<XYZ> EnumerateEndpointPoints(CadSegment s)
        {
            if (s == null)
            {
                yield break;
            }

            if (s.P0 != null) yield return s.P0;
            if (s.P1 != null) yield return s.P1;
        }

        private static IEnumerable<XYZ> EnumerateAnchorPoints(CadSegment s)
        {
            if (s == null)
            {
                yield break;
            }

            if (s.P0 != null) yield return s.P0;
            if (s.P1 != null) yield return s.P1;
            if (s.MidPoint != null) yield return s.MidPoint;
            if (s.Center != null) yield return s.Center;
        }

        private static BoundingBoxXYZ ComputeComponentBBox(List<CadSegment> component)
        {
            List<XYZ> points = (component ?? new List<CadSegment>())
                .SelectMany(x => EnumerateAnchorPoints(x))
                .Where(x => x != null)
                .ToList();
            if (points.Count == 0)
            {
                return null;
            }

            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double minZ = points.Min(p => p.Z);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);
            double maxZ = points.Max(p => p.Z);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private static List<IDoorCandidateRule> BuildRulesForDoorComponent(
            List<CadSegment> componentSegments,
            DoorSymbolFamilyKind routedKind)
        {
            List<IDoorCandidateRule> rules = new List<IDoorCandidateRule>();
            int arcCount = (componentSegments ?? new List<CadSegment>()).Count(x => x != null && x.IsArc);

            if (arcCount > 0)
            {
                if (routedKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleAlt());
                }
                else if (routedKind == DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleMinimalDoubleNoWall());
                }
                else if (routedKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing)
                {
                    rules.Add(new ArcDoorRuleDoubleWithWallCrossing());
                }
                else if (routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleComplexNoWall());
                }
                else if (routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD)
                {
                    rules.Add(new ArcDoorRuleComplexNoWallR3CD());
                }
                else
                {
                    rules.Add(new ArcDoorRule());
                }
            }
            else
            {
                rules.Add(new ParallelPairDoorRule());
                rules.Add(new SingleSegmentDoorRule());
            }

            return rules;
        }

        private static void AccumulateRuleCount(DoorDetectResult result, string ruleName, int count)
        {
            if (result == null || count <= 0)
            {
                return;
            }

            if (string.Equals(ruleName, "R1", StringComparison.OrdinalIgnoreCase))
            {
                result.Rule1Count += count;
            }
            else if (string.Equals(ruleName, "R2", StringComparison.OrdinalIgnoreCase))
            {
                result.Rule2Count += count;
            }
            else if (string.Equals(ruleName, "R3", StringComparison.OrdinalIgnoreCase))
            {
                result.Rule3Count += count;
            }
        }

        private static XYZ ResolveCandidateOpeningDirection(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            XYZ s = candidate.VirtualOpeningBaseStart ?? candidate.OpeningBaseStartPoint;
            XYZ e = candidate.VirtualOpeningBaseEnd ?? candidate.OpeningBaseEndPoint;
            if (s != null && e != null)
            {
                XYZ d = e - s;
                double len = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
                if (len > 1e-9)
                {
                    return new XYZ(d.X / len, d.Y / len, 0);
                }
            }

            return candidate.WallDirHint;
        }

        private static string FormatPoint(XYZ p)
        {
            if (p == null)
            {
                return "(null)";
            }

            return "(" + p.X.ToString("F3") + "," + p.Y.ToString("F3") + "," + p.Z.ToString("F3") + ")";
        }

        private static string FormatVector2D(XYZ v)
        {
            if (v == null)
            {
                return "(null)";
            }

            return "(" + v.X.ToString("F4") + "," + v.Y.ToString("F4") + ",0.0000)";
        }

        private static XYZ Mid(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double MmToFt(double valueMm)
        {
            return UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters);
        }

        private static double FtToMm(double valueFt)
        {
            return UnitUtils.ConvertFromInternalUnits(valueFt, UnitTypeId.Millimeters);
        }

        private static double DegToRad(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        private static XYZ Normalize2D(XYZ vector)
        {
            if (vector == null)
            {
                return null;
            }

            double len = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            if (len < 1e-9)
            {
                return null;
            }

            return new XYZ(vector.X / len, vector.Y / len, 0.0);
        }

        private static double Cross2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }

            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static double Dot2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }

            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static double DistancePointToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            XYZ ab = new XYZ(b.X - a.X, b.Y - a.Y, 0);
            double ab2 = (ab.X * ab.X) + (ab.Y * ab.Y);
            if (ab2 < 1e-12)
            {
                double dx = p.X - a.X;
                double dy = p.Y - a.Y;
                return Math.Sqrt((dx * dx) + (dy * dy));
            }

            double t = (((p.X - a.X) * ab.X) + ((p.Y - a.Y) * ab.Y)) / ab2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double x = a.X + (ab.X * t);
            double y = a.Y + (ab.Y * t);
            double ddx = p.X - x;
            double ddy = p.Y - y;
            return Math.Sqrt((ddx * ddx) + (ddy * ddy));
        }


        private static List<IDoorCandidateRule> BuildRulesForDoorLayer(
            List<CadSegment> doorSegments,
            DoorDetectResult result,
            DoorDetectSettings settings)
        {
            int arcCount = (doorSegments ?? new List<CadSegment>()).Count(x => x != null && x.IsArc);
            if (result != null)
            {
                result.ArcCountOnDoorLayer = arcCount;
            }

            List<IDoorCandidateRule> rules = new List<IDoorCandidateRule>();
            DoorSymbolFamilyKind routedKind = ResolveDoorSymbolFamilyKind(doorSegments, settings, 0);
            if (arcCount > 0)
            {
                // Hard routing:
                // - minimal-arc-no-wall-crossing => R3B dedicated rule
                // - minimal-double-arc-no-wall-crossing => R3BD dedicated rule
                // - complex-standard-no-wall-crossing => R3C dedicated rule
                if (routedKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleAlt());
                }
                else if (routedKind == DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleMinimalDoubleNoWall());
                }
                else if (routedKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing)
                {
                    rules.Add(new ArcDoorRuleDoubleWithWallCrossing());
                }
                else if (routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing)
                {
                    rules.Add(new ArcDoorRuleComplexNoWall());
                }
                else if (routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD)
                {
                    rules.Add(new ArcDoorRuleComplexNoWallR3CD());
                }
                else
                {
                    rules.Add(new ArcDoorRule());
                }
            }

            if (arcCount == 0)
            {
                rules.Add(new ParallelPairDoorRule());
                rules.Add(new SingleSegmentDoorRule());
            }

            if (result != null)
            {
                result.EnabledRules = rules.Select(x => x.Name).ToList();
            }

            return rules;
        }

        private static DoorSymbolFamilyKind ResolveDoorSymbolFamilyKind(
            List<CadSegment> doorSegments,
            DoorDetectSettings settings,
            int componentId = 0)
        {
            return ResolveDoorSymbolFamilyKind(doorSegments, doorSegments, null, settings, componentId);
        }

        private static DoorSymbolFamilyKind ResolveDoorSymbolFamilyKind(
            List<CadSegment> doorSegments,
            List<CadSegment> routeContextSegments,
            DoorDetectSettings settings,
            int componentId = 0)
        {
            return ResolveDoorSymbolFamilyKind(doorSegments, routeContextSegments, null, settings, componentId);
        }

        private static DoorSymbolFamilyKind ResolveDoorSymbolFamilyKind(
            List<CadSegment> doorSegments,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings,
            int componentId = 0)
        {
            List<CadSegment> segments = (doorSegments ?? new List<CadSegment>())
                .Where(x => x != null)
                .ToList();

            List<CadSegment> contextSegments = (routeContextSegments ?? doorSegments ?? new List<CadSegment>())
                .Where(x => x != null)
                .ToList();
            if (segments.Count == 0 || !segments.Any(x => x.IsArc))
            {
                return DoorSymbolFamilyKind.Unknown;
            }

            bool hasBodyLines = segments.Any(x => !x.IsArc);
            if (!hasBodyLines)
            {
                return DoorSymbolFamilyKind.StandardArcDoor;
            }

            HardWallContactMatch hardWallContactMatch;
            bool hasHardWallCrossingContact = TryResolveHardWallCrossingContact(segments, contextSegments, wallContext, settings, componentId, out hardWallContactMatch);
            MinimalDoubleNoWallFeatureSummary minimalDoubleFeatures;
            bool isMinimalDoubleNoWall = IsMinimalDoubleNoWallDoorSymbol(segments, settings, out minimalDoubleFeatures);
            ComplexNoWallFeatureSummary features;
            bool isComplexNoWall = IsComplexStandardNoWallDoorSymbol(segments, settings, out features);
            ArcDoorRuleComplexNoWallR3CD.StructureSummary r3cdStructure;
            bool hasR3CDRoutePrecheck = ArcDoorRuleComplexNoWallR3CD.TryEvaluateRoutingPrecheck(segments, componentId, false, out r3cdStructure);
            if (r3cdStructure == null)
            {
                r3cdStructure = new ArcDoorRuleComplexNoWallR3CD.StructureSummary();
            }
            bool isR3CD = r3cdStructure.ArcCountMatched && hasR3CDRoutePrecheck;

            DoorSymbolFamilyKind resolved;
            if (hasHardWallCrossingContact && features.ArcCount == 2)
            {
                resolved = DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing;
            }
            else if (hasHardWallCrossingContact && features.ArcCount == 1 && !isMinimalDoubleNoWall && !isComplexNoWall)
            {
                resolved = DoorSymbolFamilyKind.StandardArcDoor;
            }
            else if (isMinimalDoubleNoWall)
            {
                resolved = DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing;
            }
            else if (isR3CD)
            {
                resolved = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD;
            }
            else if (isComplexNoWall)
            {
                resolved = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing;
            }
            else
            {
                resolved = DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing;
            }

            DiagnosticRecorder.AppendDebug(
                "[DoorRouteDecision] ComponentId=" + componentId +
                ", ArcCount=" + features.ArcCount +
                ", LineCount=" + features.LineCount +
                ", ShortCount=" + features.ShortCount +
                ", HasHardWallCrossingContact=" + hasHardWallCrossingContact +
                ", RouteContextCount=" + contextSegments.Count +
                ", WallContextCount=" + (wallContext == null ? 0 : wallContext.Count) +
                ", IsMinimalDoubleNoWallDoorSymbol=" + isMinimalDoubleNoWall +
                ", RadiusClose=" + minimalDoubleFeatures.RadiusClose +
                ", SymmetryLike=" + minimalDoubleFeatures.SymmetryLike +
                ", LeftSideGroupCount=" + minimalDoubleFeatures.LeftSideGroupCount +
                ", RightSideGroupCount=" + minimalDoubleFeatures.RightSideGroupCount +
                ", LightWeightStructure=" + minimalDoubleFeatures.LightWeightStructure +
                ", HasStrongMainBaseLikeLine=" + features.HasStrongMainBaseLikeLine +
                ", HasLeafSideLikeLine=" + features.HasLeafSideLikeLine +
                ", HasStandardDoorStructure=" + features.HasStandardDoorStructure +
                ", R3CDArcCountGatePassed=" + r3cdStructure.ArcCountMatched +
                ", HasR3CDRoutePrecheck=" + hasR3CDRoutePrecheck +
                ", HasValidR3CDDoorLineGE500=" + r3cdStructure.HasValidDoorLinesGe500 +
                ", R3CDDoorLineCandidateCount=" + r3cdStructure.DoorLineCandidateCount +
                ", R3CDValidDoorLineCount=" + r3cdStructure.ValidDoorLineCount +
                ", R3CDSideCandidateCount=" + r3cdStructure.SideCandidateCount +
                ", R3CDPrecheckLeftGroupCount=" + r3cdStructure.LeftGroupCount +
                ", R3CDPrecheckRightGroupCount=" + r3cdStructure.RightGroupCount +
                ", HasBilateralR3CDSideGroups=" + r3cdStructure.HasBilateralSideGroups +
                ", IsComplexStandardNoWallDoorSymbol=" + isComplexNoWall +
                ", ResolvedKind=" + resolved);
            DiagnosticRecorder.AppendDebug(
                "[R3CDHardRouteDecision] ComponentId=" + componentId +
                ", ArcCountMatched=" + r3cdStructure.ArcCountMatched +
                ", HasDoorLineGE500=" + r3cdStructure.HasValidDoorLinesGe500 +
                ", HasBilateralSideGroups=" + r3cdStructure.HasBilateralSideGroups +
                ", Reason=" + (r3cdStructure.RouteReason ?? string.Empty) +
                ", RoutedTo=" + (resolved == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD ? "R3CD" : "R3C"));

            return resolved;
        }

        private static bool IsComplexStandardNoWallDoorSymbol(List<CadSegment> segments, DoorDetectSettings settings)
        {
            ComplexNoWallFeatureSummary _;
            return IsComplexStandardNoWallDoorSymbol(segments, settings, out _);
        }

        private static bool IsComplexStandardNoWallDoorSymbol(
            List<CadSegment> segments,
            DoorDetectSettings settings,
            out ComplexNoWallFeatureSummary features)
        {
            List<CadSegment> arcs = (segments ?? new List<CadSegment>()).Where(x => x != null && x.IsArc).ToList();
            List<CadSegment> lines = (segments ?? new List<CadSegment>()).Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            features = BuildComplexNoWallFeatureSummary(arcs, lines, settings);
            if (arcs.Count == 0 || lines.Count == 0)
            {
                return false;
            }

            if (arcs.Count >= 2 &&
                lines.Count >= 6 &&
                (features.HasStrongMainBaseLikeLine || features.HasLeafSideLikeLine || features.ShortCount >= 2))
            {
                return true;
            }

            if (arcs.Count == 1 &&
                features.HasStandardDoorStructure &&
                lines.Count >= 5 &&
                (features.ShortCount >= 2 || lines.Count >= 7))
            {
                return true;
            }

            return false;
        }

        private sealed class ComplexNoWallFeatureSummary
        {
            public int ArcCount { get; set; }
            public int LineCount { get; set; }
            public int ShortCount { get; set; }
            public bool HasStrongMainBaseLikeLine { get; set; }
            public bool HasLeafSideLikeLine { get; set; }
            public bool HasStandardDoorStructure { get; set; }
        }

        private sealed class MinimalDoubleNoWallFeatureSummary
        {
            public int ArcCount { get; set; }
            public int LineCount { get; set; }
            public int LeftSideGroupCount { get; set; }
            public int RightSideGroupCount { get; set; }
            public bool RadiusClose { get; set; }
            public bool SymmetryLike { get; set; }
            public bool LightWeightStructure { get; set; }
        }

        private static bool IsMinimalDoubleNoWallDoorSymbol(
            List<CadSegment> segments,
            DoorDetectSettings settings,
            out MinimalDoubleNoWallFeatureSummary summary)
        {
            List<CadSegment> arcs = (segments ?? new List<CadSegment>()).Where(x => x != null && x.IsArc && x.P0 != null && x.P1 != null).ToList();
            List<CadSegment> lines = (segments ?? new List<CadSegment>()).Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            summary = new MinimalDoubleNoWallFeatureSummary
            {
                ArcCount = arcs.Count,
                LineCount = lines.Count,
                LeftSideGroupCount = 0,
                RightSideGroupCount = 0,
                RadiusClose = false,
                SymmetryLike = false,
                LightWeightStructure = false
            };

            if (arcs.Count != 2 || lines.Count < 4)
            {
                return false;
            }

            CadSegment a0 = arcs[0];
            CadSegment a1 = arcs[1];
            double r0 = UnitUtils.ConvertFromInternalUnits(a0.RadiusFeet, UnitTypeId.Millimeters);
            double r1 = UnitUtils.ConvertFromInternalUnits(a1.RadiusFeet, UnitTypeId.Millimeters);
            double rMin = Math.Max(1.0, Math.Min(r0, r1));
            summary.RadiusClose = Math.Abs(r0 - r1) <= Math.Max(150.0, rMin * 0.25);

            List<XYZ> arcAnchors = new List<XYZ> { a0.P0, a0.P1, a1.P0, a1.P1 }.Where(x => x != null).ToList();
            XYZ openingCenter = new XYZ(
                arcAnchors.Average(x => x.X),
                arcAnchors.Average(x => x.Y),
                arcAnchors.Average(x => x.Z));

            double nearArcFt = UnitUtils.ConvertToInternalUnits(260.0, UnitTypeId.Millimeters);
            double minSideLenFt = UnitUtils.ConvertToInternalUnits(220.0, UnitTypeId.Millimeters);
            double maxSideLenFt = UnitUtils.ConvertToInternalUnits(1800.0, UnitTypeId.Millimeters);
            double splitFt = UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);

            List<(CadSegment Line, XYZ Mid, XYZ Dir)> closingCandidates = new List<(CadSegment, XYZ, XYZ)>();
            foreach (CadSegment line in lines)
            {
                double lenFt = line.P0.DistanceTo(line.P1);
                if (lenFt < minSideLenFt || lenFt > maxSideLenFt)
                {
                    continue;
                }

                bool nearAnyArc = arcAnchors.Any(p =>
                    Math.Min(line.P0.DistanceTo(p), line.P1.DistanceTo(p)) <= nearArcFt);
                if (!nearAnyArc)
                {
                    continue;
                }

                XYZ lineDir = Normalize2D(line.P1 - line.P0);
                XYZ mid = Mid(line.P0, line.P1);
                if (lineDir == null || mid == null)
                {
                    continue;
                }

                closingCandidates.Add((line, mid, lineDir));
            }

            if (closingCandidates.Count < 2)
            {
                return false;
            }

            // Determine dominant closing-edge direction first (do not depend on openingDir yet).
            XYZ dominantClosingDir = null;
            int bestSupport = -1;
            for (int i = 0; i < closingCandidates.Count; i++)
            {
                XYZ seed = closingCandidates[i].Dir;
                int support = 0;
                for (int j = 0; j < closingCandidates.Count; j++)
                {
                    if (Math.Abs(seed.DotProduct(closingCandidates[j].Dir)) >= 0.92)
                    {
                        support++;
                    }
                }

                if (support > bestSupport)
                {
                    bestSupport = support;
                    dominantClosingDir = seed;
                }
            }

            if (dominantClosingDir == null)
            {
                return false;
            }

            // openingDir must be derived from left/right closing-edge groups.
            XYZ roughOpeningDir = Normalize2D(new XYZ(-dominantClosingDir.Y, dominantClosingDir.X, 0));
            if (roughOpeningDir == null)
            {
                return false;
            }

            List<XYZ> leftMids = new List<XYZ>();
            List<XYZ> rightMids = new List<XYZ>();
            foreach ((CadSegment Line, XYZ Mid, XYZ Dir) item in closingCandidates)
            {
                if (Math.Abs(item.Dir.DotProduct(dominantClosingDir)) < 0.75)
                {
                    continue;
                }

                double signed = roughOpeningDir.DotProduct(item.Mid - openingCenter);
                if (signed > splitFt) leftMids.Add(item.Mid);
                if (signed < -splitFt) rightMids.Add(item.Mid);
            }

            XYZ openingDir = null;
            if (leftMids.Count > 0 && rightMids.Count > 0)
            {
                XYZ leftCenter = new XYZ(leftMids.Average(x => x.X), leftMids.Average(x => x.Y), leftMids.Average(x => x.Z));
                XYZ rightCenter = new XYZ(rightMids.Average(x => x.X), rightMids.Average(x => x.Y), rightMids.Average(x => x.Z));
                openingDir = Normalize2D(rightCenter - leftCenter);
            }

            if (openingDir == null)
            {
                openingDir = roughOpeningDir;
            }

            XYZ d0 = Normalize2D(a0.P1 - a0.P0);
            XYZ d1 = Normalize2D(a1.P1 - a1.P0);
            XYZ chordBasedDir = null;
            if (d0 != null && d1 != null)
            {
                XYZ d1Aligned = d0.DotProduct(d1) < 0 ? new XYZ(-d1.X, -d1.Y, 0) : d1;
                chordBasedDir = Normalize2D(new XYZ(d0.X + d1Aligned.X, d0.Y + d1Aligned.Y, 0));
            }
            if (openingDir == null)
            {
                openingDir = chordBasedDir ?? d0 ?? d1;
            }
            if (openingDir == null)
            {
                return false;
            }

            int leftCount = 0;
            int rightCount = 0;
            foreach ((CadSegment Line, XYZ Mid, XYZ Dir) item in closingCandidates)
            {
                // Side-group lines are expected to be near-perpendicular to opening direction.
                if (Math.Abs(item.Dir.DotProduct(openingDir)) > 0.50)
                {
                    continue;
                }

                if (Math.Abs(item.Dir.DotProduct(dominantClosingDir)) < 0.75)
                {
                    continue;
                }

                // Left/right grouping must follow door expansion direction, not its normal.
                double signed = openingDir.DotProduct(item.Mid - openingCenter);
                if (signed > splitFt) leftCount++;
                if (signed < -splitFt) rightCount++;
            }

            summary.LeftSideGroupCount = leftCount;
            summary.RightSideGroupCount = rightCount;
            summary.SymmetryLike = leftCount >= 1 && rightCount >= 1 && Math.Abs(leftCount - rightCount) <= 2;
            summary.LightWeightStructure = lines.Count <= 24;

            DiagnosticRecorder.AppendDebug(
                "[R3BDFeature] ArcCount=" + summary.ArcCount +
                ", LineCount=" + summary.LineCount +
                ", RadiusClose=" + summary.RadiusClose +
                ", SymmetryLike=" + summary.SymmetryLike +
                ", LeftSideGroupCount=" + summary.LeftSideGroupCount +
                ", RightSideGroupCount=" + summary.RightSideGroupCount +
                ", OpeningDirFromGroup=True" +
                ", LightWeightStructure=" + summary.LightWeightStructure);

            return summary.RadiusClose && summary.SymmetryLike && summary.LightWeightStructure;
        }

        private static ComplexNoWallFeatureSummary BuildComplexNoWallFeatureSummary(
            List<CadSegment> arcs,
            List<CadSegment> lines,
            DoorDetectSettings settings)
        {
            ComplexNoWallFeatureSummary summary = new ComplexNoWallFeatureSummary
            {
                ArcCount = (arcs ?? new List<CadSegment>()).Count,
                LineCount = (lines ?? new List<CadSegment>()).Count,
                ShortCount = 0,
                HasStrongMainBaseLikeLine = false,
                HasLeafSideLikeLine = false,
                HasStandardDoorStructure = false
            };

            if (arcs == null || lines == null || arcs.Count == 0 || lines.Count == 0)
            {
                return summary;
            }

            double shortMm = Math.Max(400.0, (settings?.DoorWidthMinMm ?? 650.0) * 0.65);
            double shortFt = UnitUtils.ConvertToInternalUnits(shortMm, UnitTypeId.Millimeters);
            summary.ShortCount = lines.Count(x => x != null && x.P0 != null && x.P1 != null && x.P0.DistanceTo(x.P1) <= shortFt);

            summary.HasStrongMainBaseLikeLine = HasStrongMainBaseLikeLine(arcs, lines, settings);
            summary.HasLeafSideLikeLine = HasLeafSideLikeLine(arcs, lines, settings);
            summary.HasStandardDoorStructure = summary.HasStrongMainBaseLikeLine && summary.HasLeafSideLikeLine;
            return summary;
        }

        private static bool HasStrongMainBaseLikeLine(
            List<CadSegment> arcs,
            List<CadSegment> lines,
            DoorDetectSettings settings)
        {
            if (arcs == null || lines == null || arcs.Count == 0 || lines.Count == 0)
            {
                return false;
            }

            CadSegment mainArc = arcs
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .FirstOrDefault();
            if (mainArc == null)
            {
                return false;
            }

            XYZ chordDir = Normalize2D(mainArc.P1 - mainArc.P0);
            if (chordDir == null)
            {
                return false;
            }

            double minLenMm = Math.Max((settings?.DoorWidthMinMm ?? 650.0) * 0.60, 380.0);
            double maxLenMm = Math.Max((settings?.DoorWidthMaxMm ?? 1200.0) * 2.10, 2200.0);
            double projTolFt = UnitUtils.ConvertToInternalUnits(280.0, UnitTypeId.Millimeters);

            foreach (CadSegment line in lines)
            {
                if (line == null || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                double lenMm = UnitUtils.ConvertFromInternalUnits(line.P0.DistanceTo(line.P1), UnitTypeId.Millimeters);
                if (lenMm < minLenMm || lenMm > maxLenMm)
                {
                    continue;
                }

                XYZ lineDir = Normalize2D(line.P1 - line.P0);
                if (lineDir == null)
                {
                    continue;
                }

                double align = Math.Abs(chordDir.DotProduct(lineDir));
                if (align < 0.58)
                {
                    continue;
                }

                XYZ projP0 = ClosestPointOnSegment(mainArc.P0, line.P0, line.P1);
                XYZ projP1 = ClosestPointOnSegment(mainArc.P1, line.P0, line.P1);
                if (projP0 == null || projP1 == null)
                {
                    continue;
                }

                bool distOk = projP0.DistanceTo(mainArc.P0) <= projTolFt &&
                              projP1.DistanceTo(mainArc.P1) <= projTolFt;
                if (!distOk)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool HasLeafSideLikeLine(
            List<CadSegment> arcs,
            List<CadSegment> lines,
            DoorDetectSettings settings)
        {
            if (arcs == null || lines == null || arcs.Count == 0 || lines.Count == 0)
            {
                return false;
            }

            CadSegment mainArc = arcs
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .FirstOrDefault();
            if (mainArc == null)
            {
                return false;
            }

            XYZ chordDir = Normalize2D(mainArc.P1 - mainArc.P0);
            if (chordDir == null)
            {
                return false;
            }

            double minLenMm = Math.Max(260.0, (settings?.DoorWidthMinMm ?? 650.0) * 0.35);
            double maxLenMm = Math.Max(1600.0, (settings?.DoorWidthMaxMm ?? 1200.0) * 1.60);
            double nearEndpointFt = UnitUtils.ConvertToInternalUnits(220.0, UnitTypeId.Millimeters);

            foreach (CadSegment line in lines)
            {
                if (line == null || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                double lenMm = UnitUtils.ConvertFromInternalUnits(line.P0.DistanceTo(line.P1), UnitTypeId.Millimeters);
                if (lenMm < minLenMm || lenMm > maxLenMm)
                {
                    continue;
                }

                XYZ lineDir = Normalize2D(line.P1 - line.P0);
                if (lineDir == null)
                {
                    continue;
                }

                double alignToChord = Math.Abs(chordDir.DotProduct(lineDir));
                if (alignToChord > 0.45)
                {
                    continue;
                }

                bool nearArcStart = Math.Min(line.P0.DistanceTo(mainArc.P0), line.P1.DistanceTo(mainArc.P0)) <= nearEndpointFt;
                bool nearArcEnd = Math.Min(line.P0.DistanceTo(mainArc.P1), line.P1.DistanceTo(mainArc.P1)) <= nearEndpointFt;
                if (nearArcStart || nearArcEnd)
                {
                    return true;
                }
            }

            return false;
        }


        private static bool TryResolveHardWallCrossingContact(
            List<CadSegment> componentSegments,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings,
            int componentId,
            out HardWallContactMatch match)
        {
            match = null;

            List<CadSegment> arcs = (componentSegments ?? new List<CadSegment>())
                .Where(x => x != null && x.IsArc && x.P0 != null && x.P1 != null)
                .ToList();
            if (arcs.Count != 1 && arcs.Count != 2)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteHardWallContact] ComponentId=" + componentId +
                    ", ArcCount=" + arcs.Count +
                    ", Result=False, Reason=ArcCountNotSupported");
                return false;
            }

            List<CadSegment> primaryArcs = arcs
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .Take(Math.Min(2, arcs.Count))
                .ToList();
            List<CadSegment> componentLines = (componentSegments ?? new List<CadSegment>())
                .Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null)
                .ToList();

            double touchTolMm = Math.Max(120.0, settings?.ArcEndpointSnapTolMm ?? 120.0);
            double touchTolFt = UnitUtils.ConvertToInternalUnits(touchTolMm, UnitTypeId.Millimeters);
            List<CadSegment> leafCandidates = componentLines
                .Where(x => IsLeafLengthInRangeForHardR3(x, settings))
                .Where(x => primaryArcs.Any(arc => DoesLineTouchArc(x, arc, touchTolFt)))
                .ToList();

            List<HardWallContextLine> hardContextLines = BuildHardWallCrossingContextLines(
                componentSegments,
                routeContextSegments,
                wallContext,
                settings);

            XYZ referenceCenter = ResolveHardWallReferenceCenter(primaryArcs);
            XYZ sharedJunction = TryResolveSharedArcJunctionPoint(primaryArcs, touchTolFt);
            XYZ expectedWallDir = ResolveExpectedHardWallDirection(primaryArcs, leafCandidates);

            DiagnosticRecorder.AppendDebug(
                "[DoorRouteHardWallContact] ComponentId=" + componentId +
                ", ArcCount=" + primaryArcs.Count +
                ", ArcSegmentIds=" + string.Join("|", primaryArcs.Select(x => x.SegmentId)) +
                ", LeafCandidateCount=" + leafCandidates.Count +
                ", ContextCandidateCount=" + hardContextLines.Count +
                ", TouchTolMm=" + touchTolMm.ToString("F1") +
                ", ReferenceCenter=" + FormatPoint(referenceCenter) +
                ", SharedJunction=" + FormatPoint(sharedJunction) +
                ", ExpectedWallDir=" + FormatVector2D(expectedWallDir));

            if (leafCandidates.Count == 0 || hardContextLines.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteHardWallContact] ComponentId=" + componentId +
                    ", Result=False, Reason=NoLeafOrNoContext");
                return false;
            }

            List<HardWallContactMatch> matches = new List<HardWallContactMatch>();
            double supportTolFt = UnitUtils.ConvertToInternalUnits(45.0, UnitTypeId.Millimeters);

            foreach (CadSegment leaf in leafCandidates.OrderBy(x => x.SegmentId))
            {
                foreach (HardWallContextLine wallLine in hardContextLines.OrderBy(x => x.Segment == null ? int.MaxValue : x.Segment.SegmentId))
                {
                    CadSegment wallSeg = wallLine?.Segment;
                    if (wallSeg == null)
                    {
                        continue;
                    }

                    if (leaf.SegmentId == wallSeg.SegmentId)
                    {
                        continue;
                    }

                    if (!IsHardWallLikeLengthEnough(wallSeg, settings))
                    {
                        continue;
                    }

                    CadSegment matchedArc = primaryArcs.FirstOrDefault(arc => DoesLineTouchArc(wallSeg, arc, touchTolFt));
                    bool touchesArc = matchedArc != null;
                    double leafDistFt = DistanceBetweenSegmentsBidirectional(wallSeg, leaf);
                    bool touchesLeaf = leafDistFt <= touchTolFt;
                    if (!touchesArc || !touchesLeaf)
                    {
                        continue;
                    }

                    XYZ wallDir = Normalize2D(wallSeg.P1 - wallSeg.P0);
                    double directionPenalty = 1.0;
                    if (wallDir != null && expectedWallDir != null)
                    {
                        directionPenalty = 1.0 - Math.Abs(Dot(wallDir, expectedWallDir));
                    }

                    double centerDistFt = referenceCenter == null
                        ? double.MaxValue
                        : DistancePointToSegment(referenceCenter, wallSeg.P0, wallSeg.P1);
                    double junctionDistFt = sharedJunction == null
                        ? centerDistFt
                        : DistancePointToSegment(sharedJunction, wallSeg.P0, wallSeg.P1);
                    int arcSupportCount = CountArcSupportPointsNearWall(primaryArcs, wallSeg, supportTolFt);

                    double centerDistMm = UnitUtils.ConvertFromInternalUnits(centerDistFt, UnitTypeId.Millimeters);
                    double junctionDistMm = UnitUtils.ConvertFromInternalUnits(junctionDistFt, UnitTypeId.Millimeters);
                    double leafDistMm = UnitUtils.ConvertFromInternalUnits(leafDistFt, UnitTypeId.Millimeters);

                    double score = 0.0;
                    score += centerDistMm * 1.0;
                    score += junctionDistMm * (sharedJunction == null ? 0.8 : 2.5);
                    score += leafDistMm * 0.8;
                    score += directionPenalty * 180.0;
                    score += (Math.Max(0, 4 - arcSupportCount) * 30.0);
                    if (wallLine.IsFromHostWall)
                    {
                        score -= 25.0;
                    }

                    HardWallContactMatch candidateMatch = new HardWallContactMatch
                    {
                        PreferredWallId = wallLine.WallId,
                        WallSegmentId = wallSeg.SegmentId,
                        WallLengthMm = UnitUtils.ConvertFromInternalUnits(wallSeg.P0.DistanceTo(wallSeg.P1), UnitTypeId.Millimeters),
                        LeafSegmentId = leaf.SegmentId,
                        ArcSegmentId = matchedArc.SegmentId,
                        Score = score,
                        CenterDistMm = centerDistMm,
                        JunctionDistMm = junctionDistMm,
                        LeafDistMm = leafDistMm,
                        DirectionPenalty = directionPenalty,
                        ArcSupportCount = arcSupportCount,
                        IsFromHostWall = wallLine.IsFromHostWall
                    };
                    matches.Add(candidateMatch);

                    DiagnosticRecorder.AppendDebug(
                        "[DoorRouteHardWallContactCandidate] ComponentId=" + componentId +
                        ", ArcSegmentId=" + matchedArc.SegmentId +
                        ", LeafSegmentId=" + leaf.SegmentId +
                        ", WallSegmentId=" + wallSeg.SegmentId +
                        ", PreferredWallId=" + (candidateMatch.PreferredWallId == null ? 0 : candidateMatch.PreferredWallId.IntegerValue) +
                        ", IsFromHostWall=" + candidateMatch.IsFromHostWall +
                        ", WallLengthMm=" + candidateMatch.WallLengthMm.ToString("F1") +
                        ", CenterDistMm=" + candidateMatch.CenterDistMm.ToString("F1") +
                        ", JunctionDistMm=" + candidateMatch.JunctionDistMm.ToString("F1") +
                        ", LeafDistMm=" + candidateMatch.LeafDistMm.ToString("F1") +
                        ", DirectionPenalty=" + candidateMatch.DirectionPenalty.ToString("F4") +
                        ", ArcSupportCount=" + candidateMatch.ArcSupportCount +
                        ", Score=" + candidateMatch.Score.ToString("F2"));
                }
            }

            if (matches.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteHardWallContact] ComponentId=" + componentId +
                    ", Result=False, Reason=NoWallLineTouchingArcAndLeaf");
                return false;
            }

            match = matches
                .OrderBy(x => x.Score)
                .ThenByDescending(x => x.ArcSupportCount)
                .ThenBy(x => x.JunctionDistMm)
                .ThenBy(x => x.CenterDistMm)
                .ThenBy(x => x.LeafDistMm)
                .ThenBy(x => x.WallSegmentId)
                .First();

            DiagnosticRecorder.AppendDebug(
                "[DoorRouteHardWallContactHit] ComponentId=" + componentId +
                ", ArcSegmentId=" + match.ArcSegmentId +
                ", LeafSegmentId=" + match.LeafSegmentId +
                ", WallSegmentId=" + match.WallSegmentId +
                ", PreferredWallId=" + (match.PreferredWallId == null ? 0 : match.PreferredWallId.IntegerValue) +
                ", IsFromHostWall=" + match.IsFromHostWall +
                ", WallLengthMm=" + match.WallLengthMm.ToString("F1") +
                ", CenterDistMm=" + match.CenterDistMm.ToString("F1") +
                ", JunctionDistMm=" + match.JunctionDistMm.ToString("F1") +
                ", LeafDistMm=" + match.LeafDistMm.ToString("F1") +
                ", DirectionPenalty=" + match.DirectionPenalty.ToString("F4") +
                ", ArcSupportCount=" + match.ArcSupportCount +
                ", Score=" + match.Score.ToString("F2") +
                ", Result=True");
            return true;
        }

        private static XYZ ResolveHardWallReferenceCenter(List<CadSegment> arcs)
        {
            List<XYZ> points = new List<XYZ>();
            foreach (CadSegment arc in arcs ?? new List<CadSegment>())
            {
                if (arc == null)
                {
                    continue;
                }

                if (arc.P0 != null) points.Add(arc.P0);
                if (arc.P1 != null) points.Add(arc.P1);
                if (arc.MidPoint != null) points.Add(arc.MidPoint);
            }

            if (points.Count == 0)
            {
                return null;
            }

            return new XYZ(
                points.Average(x => x.X),
                points.Average(x => x.Y),
                points.Average(x => x.Z));
        }

        private static XYZ TryResolveSharedArcJunctionPoint(List<CadSegment> arcs, double tolFt)
        {
            List<CadSegment> list = (arcs ?? new List<CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
            if (list.Count < 2)
            {
                return null;
            }

            XYZ[] aEnds = new[] { list[0].P0, list[0].P1 };
            XYZ[] bEnds = new[] { list[1].P0, list[1].P1 };
            XYZ best = null;
            double bestDist = double.MaxValue;

            foreach (XYZ a in aEnds)
            {
                foreach (XYZ b in bEnds)
                {
                    if (a == null || b == null)
                    {
                        continue;
                    }

                    double d = a.DistanceTo(b);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = Mid(a, b);
                    }
                }
            }

            return bestDist <= tolFt ? best : null;
        }

        private static XYZ AveragePoint(List<XYZ> points)
        {
            List<XYZ> list = (points ?? new List<XYZ>())
                .Where(x => x != null)
                .ToList();
            if (list.Count == 0)
            {
                return null;
            }

            return new XYZ(
                list.Average(x => x.X),
                list.Average(x => x.Y),
                list.Average(x => x.Z));
        }

        private static XYZ ResolveExpectedHardWallDirection(List<CadSegment> arcs, List<CadSegment> leafCandidates)
        {
            XYZ dominantLeafDir = ResolveDominantLeafDirection(leafCandidates);
            if (dominantLeafDir != null)
            {
                return Normalize2D(new XYZ(-dominantLeafDir.Y, dominantLeafDir.X, 0.0));
            }

            List<CadSegment> list = (arcs ?? new List<CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
            if (list.Count == 1)
            {
                return Normalize2D(list[0].P1 - list[0].P0);
            }

            XYZ shared = TryResolveSharedArcJunctionPoint(list, UnitUtils.ConvertToInternalUnits(120.0, UnitTypeId.Millimeters));
            if (shared != null)
            {
                List<XYZ> nonSharedEnds = new List<XYZ>();
                foreach (CadSegment arc in list)
                {
                    if (arc.P0 != null && arc.P0.DistanceTo(shared) > UnitUtils.ConvertToInternalUnits(120.0, UnitTypeId.Millimeters))
                    {
                        nonSharedEnds.Add(arc.P0);
                    }
                    if (arc.P1 != null && arc.P1.DistanceTo(shared) > UnitUtils.ConvertToInternalUnits(120.0, UnitTypeId.Millimeters))
                    {
                        nonSharedEnds.Add(arc.P1);
                    }
                }

                if (nonSharedEnds.Count >= 2)
                {
                    XYZ a = nonSharedEnds[0];
                    XYZ b = nonSharedEnds[1];
                    return Normalize2D(b - a);
                }
            }

            return null;
        }

        private static XYZ ResolveDominantLeafDirection(List<CadSegment> leafCandidates)
        {
            List<CadSegment> list = (leafCandidates ?? new List<CadSegment>())
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
            if (list.Count == 0)
            {
                return null;
            }

            XYZ seed = null;
            int bestSupport = -1;
            foreach (CadSegment line in list)
            {
                XYZ dir = Normalize2D(line.P1 - line.P0);
                if (dir == null)
                {
                    continue;
                }

                int support = 0;
                foreach (CadSegment other in list)
                {
                    XYZ otherDir = Normalize2D(other.P1 - other.P0);
                    if (otherDir != null && Math.Abs(Dot(dir, otherDir)) >= 0.92)
                    {
                        support++;
                    }
                }

                if (support > bestSupport)
                {
                    bestSupport = support;
                    seed = dir;
                }
            }

            return seed;
        }

        private static int CountArcSupportPointsNearWall(List<CadSegment> arcs, CadSegment wallSeg, double tolFt)
        {
            int count = 0;
            foreach (CadSegment arc in arcs ?? new List<CadSegment>())
            {
                if (arc == null)
                {
                    continue;
                }

                foreach (XYZ p in new[] { arc.P0, arc.P1, arc.MidPoint })
                {
                    if (p == null)
                    {
                        continue;
                    }

                    if (DistancePointToSegment(p, wallSeg.P0, wallSeg.P1) <= tolFt)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static double DistanceBetweenSegmentsBidirectional(CadSegment a, CadSegment b)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || b.P0 == null || b.P1 == null)
            {
                return double.MaxValue;
            }

            return Math.Min(
                Math.Min(DistancePointToSegment(a.P0, b.P0, b.P1), DistancePointToSegment(a.P1, b.P0, b.P1)),
                Math.Min(DistancePointToSegment(b.P0, a.P0, a.P1), DistancePointToSegment(b.P1, a.P0, a.P1)));
        }

        private static List<HardWallContextLine> BuildHardWallCrossingContextLines(
            List<CadSegment> componentSegments,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings)
        {
            HashSet<int> componentIds = new HashSet<int>(
                (componentSegments ?? new List<CadSegment>())
                .Where(x => x != null)
                .Select(x => x.SegmentId));

            Dictionary<int, HardWallContextLine> result = new Dictionary<int, HardWallContextLine>();

            foreach (CadSegment line in routeContextSegments ?? new List<CadSegment>())
            {
                if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                if (componentIds.Contains(line.SegmentId))
                {
                    continue;
                }

                if (!IsWallLikeContextLine(line))
                {
                    continue;
                }

                result[line.SegmentId] = new HardWallContextLine
                {
                    Segment = line,
                    WallId = ElementId.InvalidElementId,
                    IsFromHostWall = false
                };
            }

            int syntheticId = -2000000;
            foreach (WallHostLine wall in wallContext ?? new List<WallHostLine>())
            {
                if (wall == null || wall.Line == null)
                {
                    continue;
                }

                XYZ p0 = wall.Line.GetEndPoint(0);
                XYZ p1 = wall.Line.GetEndPoint(1);
                if (p0 == null || p1 == null)
                {
                    continue;
                }

                int segmentId = syntheticId--;
                result[segmentId] = new HardWallContextLine
                {
                    Segment = new CadSegment
                    {
                        SegmentId = segmentId,
                        P0 = p0,
                        P1 = p1,
                        IsArc = false,
                        RawLayerName = "__HOST_WALL_HARD_R3__",
                        SemanticLayer = "WALL",
                        NormalizedLayer = "WALL"
                    },
                    WallId = wall.WallId,
                    IsFromHostWall = true
                };
            }

            return result.Values.ToList();
        }


        private static bool IsWallLikeContextLine(CadSegment line)
        {
            if (line == null)
            {
                return false;
            }

            string raw = line.RawLayerName ?? string.Empty;
            string semantic = line.SemanticLayer ?? string.Empty;
            string normalized = line.NormalizedLayer ?? string.Empty;

            if (string.Equals(semantic, "WALL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "WALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (raw.IndexOf("WALL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("CORE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("HEAD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("__HOST_WALL__", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("__HOST_WALL_HARD_R3__", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsHardWallLikeLengthEnough(CadSegment line, DoorDetectSettings settings)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = UnitUtils.ConvertFromInternalUnits(line.P0.DistanceTo(line.P1), UnitTypeId.Millimeters);
            return lenMm > 600.0;
        }

        private static bool IsLeafLengthInRangeForHardR3(CadSegment line, DoorDetectSettings settings)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = UnitUtils.ConvertFromInternalUnits(line.P0.DistanceTo(line.P1), UnitTypeId.Millimeters);
            double minMm = settings?.ArcLeafLineMinLengthMm ?? 500.0;
            double maxMm = settings?.ArcLeafLineMaxLengthMm ?? 2000.0;
            return lenMm >= minMm && lenMm <= maxMm;
        }

        private static bool DoesLineTouchArc(CadSegment line, CadSegment arc, double tolFt)
        {
            if (line == null || arc == null || line.P0 == null || line.P1 == null || arc.P0 == null || arc.P1 == null)
            {
                return false;
            }

            List<XYZ> arcPoints = new List<XYZ> { arc.P0, arc.P1 };
            if (arc.MidPoint != null)
            {
                arcPoints.Add(arc.MidPoint);
            }

            foreach (XYZ p in arcPoints)
            {
                if (p == null)
                {
                    continue;
                }

                if (DistancePointToSegment(p, line.P0, line.P1) <= tolFt)
                {
                    return true;
                }
            }

            if (DistancePointToSegment(line.P0, arc.P0, arc.P1) <= tolFt ||
                DistancePointToSegment(line.P1, arc.P0, arc.P1) <= tolFt)
            {
                return true;
            }

            return false;
        }

        private static bool DoSegmentsTouchOrNear(CadSegment a, CadSegment b, double tolFt)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || b.P0 == null || b.P1 == null)
            {
                return false;
            }

            double best = Math.Min(
                Math.Min(DistancePointToSegment(a.P0, b.P0, b.P1), DistancePointToSegment(a.P1, b.P0, b.P1)),
                Math.Min(DistancePointToSegment(b.P0, a.P0, a.P1), DistancePointToSegment(b.P1, a.P0, a.P1)));

            return best <= tolFt;
        }


        private static bool HasOldR3StyleWallHint(
            List<CadSegment> componentSegments,
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings,
            int componentId)
        {
            List<CadSegment> arcs = (componentSegments ?? new List<CadSegment>())
                .Where(x => x != null && x.IsArc && x.P0 != null && x.P1 != null)
                .ToList();
            List<CadSegment> lines = BuildLegacyR3HintContextLines(routeContextSegments, wallContext);

            if (arcs.Count == 0 || lines.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteOldR3Precheck] ComponentId=" + componentId +
                    ", ArcCount=" + arcs.Count +
                    ", ContextLineCount=" + lines.Count +
                    ", WallContextCount=" + (wallContext == null ? 0 : wallContext.Count) +
                    ", Result=False, Reason=NoArcsOrNoLines");
                return false;
            }

            double snapTolMm = settings?.ArcEndpointSnapTolMm ?? 120.0;
            double snapTolFt = UnitUtils.ConvertToInternalUnits(snapTolMm, UnitTypeId.Millimeters);

            foreach (CadSegment arc in arcs)
            {
                List<CadSegment> nearStart = FindNearLinesForLegacyR3Hint(lines, arc.P0, snapTolFt);
                List<CadSegment> nearEnd = FindNearLinesForLegacyR3Hint(lines, arc.P1, snapTolFt);

                CadSegment startLine;
                CadSegment endLine;
                XYZ wallDirHint;
                bool ok = TryResolveWallDirFromPairForLegacyR3Hint(
                    nearStart,
                    nearEnd,
                    settings,
                    out startLine,
                    out endLine,
                    out wallDirHint);

                bool hostWallCrossingLike = false;
                if (!ok)
                {
                    hostWallCrossingLike = HasHostWallCrossingLikeHint(arc, wallContext, settings);
                    ok = hostWallCrossingLike;
                    if (hostWallCrossingLike && wallDirHint == null)
                    {
                        wallDirHint = ResolveHostWallDirectionNearArc(arc, wallContext, settings);
                    }
                }

                DiagnosticRecorder.AppendDebug(
                    "[DoorRouteOldR3PrecheckArc] ComponentId=" + componentId +
                    ", ArcSegmentId=" + arc.SegmentId +
                    ", NearStartCount=" + nearStart.Count +
                    ", NearEndCount=" + nearEnd.Count +
                    ", NearStartIds=" + string.Join("|", nearStart.Select(x => x.SegmentId).OrderBy(x => x)) +
                    ", NearEndIds=" + string.Join("|", nearEnd.Select(x => x.SegmentId).OrderBy(x => x)) +
                    ", SelectedStartLineId=" + (startLine == null ? 0 : startLine.SegmentId) +
                    ", SelectedEndLineId=" + (endLine == null ? 0 : endLine.SegmentId) +
                    ", HostWallCrossingLike=" + hostWallCrossingLike +
                    ", WallDirHint=" + FormatVector2D(wallDirHint) +
                    ", Result=" + ok);

                if (ok)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DoorRouteOldR3Precheck] ComponentId=" + componentId +
                        ", Result=True, ArcSegmentId=" + arc.SegmentId);
                    return true;
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[DoorRouteOldR3Precheck] ComponentId=" + componentId +
                ", ArcCount=" + arcs.Count +
                ", ContextLineCount=" + lines.Count +
                ", WallContextCount=" + (wallContext == null ? 0 : wallContext.Count) +
                ", Result=False, Reason=NoValidEndpointPair");
            return false;
        }

        private static List<CadSegment> BuildLegacyR3HintContextLines(
            List<CadSegment> routeContextSegments,
            IList<WallHostLine> wallContext)
        {
            List<CadSegment> result = (routeContextSegments ?? new List<CadSegment>())
                .Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null)
                .ToList();

            int syntheticId = -1000000;
            foreach (WallHostLine wall in wallContext ?? new List<WallHostLine>())
            {
                if (wall == null || wall.Line == null)
                {
                    continue;
                }

                XYZ p0 = wall.Line.GetEndPoint(0);
                XYZ p1 = wall.Line.GetEndPoint(1);
                if (p0 == null || p1 == null)
                {
                    continue;
                }

                result.Add(new CadSegment
                {
                    SegmentId = syntheticId--,
                    P0 = p0,
                    P1 = p1,
                    IsArc = false,
                    RawLayerName = "__HOST_WALL__",
                    SemanticLayer = "WALL",
                    NormalizedLayer = "WALL"
                });
            }

            return result;
        }

        private static List<CadSegment> FindNearLinesForLegacyR3Hint(List<CadSegment> lines, XYZ point, double tolFt)
        {
            List<CadSegment> result = new List<CadSegment>();
            foreach (CadSegment line in lines ?? new List<CadSegment>())
            {
                if (line == null || line.IsArc || line.P0 == null || line.P1 == null || point == null)
                {
                    continue;
                }

                double d0 = line.P0.DistanceTo(point);
                double d1 = line.P1.DistanceTo(point);
                double ds = DistancePointToSegment(point, line.P0, line.P1);
                if (Math.Min(Math.Min(d0, d1), ds) <= tolFt)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static bool TryResolveWallDirFromPairForLegacyR3Hint(
            List<CadSegment> nearStart,
            List<CadSegment> nearEnd,
            DoorDetectSettings settings,
            out CadSegment startLine,
            out CadSegment endLine,
            out XYZ wallDirHint)
        {
            wallDirHint = null;
            bool ok = TryFindBestEndpointPairForLegacyR3Hint(nearStart, nearEnd, settings, out startLine, out endLine);
            if (!ok || startLine == null)
            {
                return false;
            }

            wallDirHint = Normalize2D(startLine.P1 - startLine.P0);
            return wallDirHint != null;
        }

        private static bool TryFindBestEndpointPairForLegacyR3Hint(
            List<CadSegment> nearStart,
            List<CadSegment> nearEnd,
            DoorDetectSettings settings,
            out CadSegment startLine,
            out CadSegment endLine)
        {
            startLine = null;
            endLine = null;
            double bestScore = double.MaxValue;
            double parallelTol = settings?.ArcPairLineParallelTolDeg ?? 8.0;

            foreach (CadSegment a in nearStart ?? new List<CadSegment>())
            {
                foreach (CadSegment b in nearEnd ?? new List<CadSegment>())
                {
                    if (!IsLengthInRangeForLegacyR3Hint(a, settings) || !IsLengthInRangeForLegacyR3Hint(b, settings))
                    {
                        continue;
                    }

                    double angle = AngleDegForLegacyR3Hint(a, b);
                    double parallelDelta = Math.Min(Math.Abs(angle), Math.Abs(180.0 - angle));
                    if (parallelDelta > parallelTol)
                    {
                        continue;
                    }

                    double score = parallelDelta;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        startLine = a;
                        endLine = b;
                    }
                }
            }

            return startLine != null && endLine != null;
        }


        private static bool IsLengthInRangeForLegacyR3Hint(CadSegment line, DoorDetectSettings settings)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = UnitUtils.ConvertFromInternalUnits(line.P0.DistanceTo(line.P1), UnitTypeId.Millimeters);
            double minMm = settings?.SegmentLengthMinMm ?? 200.0;
            double maxMm = settings?.SegmentLengthMaxMm ?? 4000.0;
            return lenMm >= minMm && lenMm <= maxMm;
        }

        private static double AngleDegForLegacyR3Hint(CadSegment a, CadSegment b)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || b.P0 == null || b.P1 == null)
            {
                return 180.0;
            }

            XYZ da = a.P1 - a.P0;
            XYZ db = b.P1 - b.P0;
            if (da.GetLength() < 1e-9 || db.GetLength() < 1e-9)
            {
                return 180.0;
            }

            XYZ va = da.Normalize();
            XYZ vb = db.Normalize();
            double dot = va.DotProduct(vb);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot) * (180.0 / Math.PI);
        }

        private static bool HasHostWallCrossingLikeHint(
            CadSegment arc,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings)
        {
            if (arc == null || arc.P0 == null || arc.P1 == null || wallContext == null || wallContext.Count == 0)
            {
                return false;
            }

            double tolFt = UnitUtils.ConvertToInternalUnits(
                Math.Max(160.0, settings?.ArcEndpointSnapTolMm ?? 120.0),
                UnitTypeId.Millimeters);
            XYZ chordMid = Mid(arc.P0, arc.P1);
            foreach (WallHostLine wall in wallContext)
            {
                if (wall == null || wall.Line == null)
                {
                    continue;
                }

                ProjectionData pm = ProjectPointToLineSegment(chordMid, wall.Line);
                ProjectionData p0 = ProjectPointToLineSegment(arc.P0, wall.Line);
                ProjectionData p1 = ProjectPointToLineSegment(arc.P1, wall.Line);
                bool midNear = pm.IsInsideSegment && pm.DistanceFeet <= tolFt;
                bool endpointNear = (p0.IsInsideSegment && p0.DistanceFeet <= tolFt) || (p1.IsInsideSegment && p1.DistanceFeet <= tolFt);
                if (midNear && endpointNear)
                {
                    return true;
                }
            }

            return false;
        }

        private static XYZ ResolveHostWallDirectionNearArc(
            CadSegment arc,
            IList<WallHostLine> wallContext,
            DoorDetectSettings settings)
        {
            if (arc == null || wallContext == null)
            {
                return null;
            }

            double tolFt = UnitUtils.ConvertToInternalUnits(
                Math.Max(160.0, settings?.ArcEndpointSnapTolMm ?? 120.0),
                UnitTypeId.Millimeters);
            XYZ chordMid = Mid(arc.P0, arc.P1);
            foreach (WallHostLine wall in wallContext)
            {
                if (wall == null || wall.Line == null)
                {
                    continue;
                }

                ProjectionData pm = ProjectPointToLineSegment(chordMid, wall.Line);
                if (!pm.IsInsideSegment || pm.DistanceFeet > tolFt)
                {
                    continue;
                }

                XYZ dir = Normalize2D(wall.Line.GetEndPoint(1) - wall.Line.GetEndPoint(0));
                if (dir != null)
                {
                    return dir;
                }
            }

            return null;
        }

        private static bool HasContinuousWallCrossingLine(List<CadSegment> segments, DoorDetectSettings settings)
        {
            List<CadSegment> arcs = segments.Where(x => x != null && x.IsArc).ToList();
            List<CadSegment> lines = segments.Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            if (arcs.Count == 0 || lines.Count == 0)
            {
                return false;
            }

            double minWallLikeLengthMm = Math.Max(600.0, (settings?.DoorWidthMinMm ?? 650.0) * 0.9);
            double minWallLikeLengthFeet = UnitUtils.ConvertToInternalUnits(minWallLikeLengthMm, UnitTypeId.Millimeters);
            double crossTolFeet = UnitUtils.ConvertToInternalUnits(160.0, UnitTypeId.Millimeters);

            foreach (CadSegment arc in arcs)
            {
                if (arc == null || arc.P0 == null || arc.P1 == null)
                {
                    continue;
                }

                XYZ arcStart = arc.P0;
                XYZ arcEnd = arc.P1;
                XYZ chordMid = new XYZ(
                    (arcStart.X + arcEnd.X) * 0.5,
                    (arcStart.Y + arcEnd.Y) * 0.5,
                    (arcStart.Z + arcEnd.Z) * 0.5);

                foreach (CadSegment line in lines)
                {
                    if (line == null || line.P0 == null || line.P1 == null)
                    {
                        continue;
                    }

                    if (line.P0.DistanceTo(line.P1) < minWallLikeLengthFeet)
                    {
                        continue;
                    }

                    XYZ projectedMid = ClosestPointOnSegment(chordMid, line.P0, line.P1);
                    if (projectedMid == null || projectedMid.DistanceTo(chordMid) > crossTolFeet)
                    {
                        continue;
                    }

                    double d0 = DistancePointToSegment(arcStart, line.P0, line.P1);
                    double d1 = DistancePointToSegment(arcEnd, line.P0, line.P1);
                    if (d0 <= crossTolFeet && d1 <= crossTolFeet)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static double DistancePointToSegment(XYZ point, XYZ segStart, XYZ segEnd)
        {
            XYZ projected = ClosestPointOnSegment(point, segStart, segEnd);
            return projected == null ? double.MaxValue : projected.DistanceTo(point);
        }

        private static XYZ ClosestPointOnSegment(XYZ point, XYZ segStart, XYZ segEnd)
        {
            if (point == null || segStart == null || segEnd == null)
            {
                return null;
            }

            XYZ v = segEnd - segStart;
            double vv = v.DotProduct(v);
            if (vv < 1e-12)
            {
                return segStart;
            }

            double t = (point - segStart).DotProduct(v) / vv;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return segStart + (v * t);
        }

        private static XYZ ProjectPointToInfiniteLine(XYZ point, Line line)
        {
            if (point == null || line == null)
            {
                return null;
            }

            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            if (a == null || b == null)
            {
                return null;
            }

            XYZ v = b - a;
            double vv = v.DotProduct(v);
            if (vv < 1e-12)
            {
                return a;
            }

            double t = (point - a).DotProduct(v) / vv;
            return a + (v * t);
        }

        private static List<DoorCandidate> MergeCandidates(List<DoorCandidate> raw, DoorDetectSettings settings)
        {
            List<DoorCandidate> merged = new List<DoorCandidate>();
            double centerTolFeet = UnitUtils.ConvertToInternalUnits(settings.MergeCenterTolMm, UnitTypeId.Millimeters);
            foreach (DoorCandidate current in raw)
            {
                DoorCandidate existing = merged.FirstOrDefault(x =>
                    CanMergeTogether(x, current) &&
                    x.CenterPoint != null &&
                    current.CenterPoint != null &&
                    x.CenterPoint.DistanceTo(current.CenterPoint) <= centerTolFeet &&
                    Math.Abs(x.WidthMm - current.WidthMm) <= settings.MergeWidthTolMm);
                if (existing == null)
                {
                    merged.Add(CloneCandidate(current));
                    continue;
                }

                existing.SegmentIds = existing.SegmentIds
                    .Union(current.SegmentIds ?? new List<int>())
                    .ToList();
                if (Prefer(current, existing))
                {
                    existing.RuleSource = current.RuleSource;
                    existing.SymbolFamilyKind = current.SymbolFamilyKind;
                    existing.WidthMm = current.WidthMm;
                    existing.CenterPoint = current.CenterPoint;
                    existing.ArcRadiusMm = current.ArcRadiusMm;
                    existing.ArcSweepDeg = current.ArcSweepDeg;
                    existing.HingePoint = current.HingePoint;
                    existing.LeafHinge = current.LeafHinge;
                    existing.LeafLatch = current.LeafLatch;
                    existing.OpeningCenterPoint = current.OpeningCenterPoint;
                    existing.LeafLineSegmentId = current.LeafLineSegmentId;
                    existing.ArcMidPoint = current.ArcMidPoint;
                    existing.WallDirHint = current.WallDirHint;
                    existing.WidthSource = current.WidthSource;
                    existing.IsDoubleDoor = current.IsDoubleDoor;
                    existing.LeftEdgePoint = current.LeftEdgePoint;
                    existing.RightEdgePoint = current.RightEdgePoint;
                    existing.CombinedWidthMm = current.CombinedWidthMm;
                    existing.CombinedCenter = current.CombinedCenter;
                    existing.PreferredHostWallId = current.PreferredHostWallId;
                    existing.PreferredHostPoint = current.PreferredHostPoint;
                    existing.OpeningBaseStartPoint = current.OpeningBaseStartPoint;
                    existing.OpeningBaseEndPoint = current.OpeningBaseEndPoint;
                    existing.PreferOpeningBaseHost = current.PreferOpeningBaseHost;
                    existing.DoorLeafBaseStart = current.DoorLeafBaseStart;
                    existing.DoorLeafBaseEnd = current.DoorLeafBaseEnd;
                    existing.DoorLeafBaseCenter = current.DoorLeafBaseCenter;
                    existing.VirtualOpeningBaseStart = current.VirtualOpeningBaseStart;
                    existing.VirtualOpeningBaseEnd = current.VirtualOpeningBaseEnd;
                    existing.VirtualOpeningBaseCenter = current.VirtualOpeningBaseCenter;
                    existing.VirtualOpeningWidthMm = current.VirtualOpeningWidthMm;
                    existing.PreferVirtualOpeningHost = current.PreferVirtualOpeningHost;
                }
            }

            return merged;
        }

        private static bool Prefer(DoorCandidate a, DoorCandidate b)
        {
            if (a != null && b != null && a.SymbolFamilyKind != b.SymbolFamilyKind)
            {
                return a.SymbolFamilyKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing;
            }

            bool aVirtual = IsVirtualOpeningPreferredCandidate(a);
            bool bVirtual = IsVirtualOpeningPreferredCandidate(b);
            if (aVirtual != bVirtual)
            {
                return aVirtual;
            }

            return Rank(a.RuleSource) > Rank(b.RuleSource);
        }

        private static int Rank(string rule)
        {
            if (string.Equals(rule, "R3T", StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(rule, "R3", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(rule, "R3D", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(rule, "R3BD", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(rule, "R3C", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(rule, "R3CD", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(rule, "R3B", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(rule, "R1", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static DoorCandidate CloneCandidate(DoorCandidate source)
        {
            return new DoorCandidate
            {
                CenterPoint = source.CenterPoint,
                WidthMm = source.WidthMm,
                RuleSource = source.RuleSource,
                SymbolFamilyKind = source.SymbolFamilyKind,
                SegmentIds = (source.SegmentIds ?? new List<int>()).ToList(),
                ArcRadiusMm = source.ArcRadiusMm,
                ArcSweepDeg = source.ArcSweepDeg,
                HingePoint = source.HingePoint,
                LeafHinge = source.LeafHinge,
                LeafLatch = source.LeafLatch,
                OpeningCenterPoint = source.OpeningCenterPoint,
                LeafLineSegmentId = source.LeafLineSegmentId,
                ArcMidPoint = source.ArcMidPoint,
                WallDirHint = source.WallDirHint,
                WidthSource = source.WidthSource,
                OpeningWidthMm = source.OpeningWidthMm,
                IsDoubleDoor = source.IsDoubleDoor,
                LeftEdgePoint = source.LeftEdgePoint,
                RightEdgePoint = source.RightEdgePoint,
                CombinedWidthMm = source.CombinedWidthMm,
                CombinedCenter = source.CombinedCenter,
                PreferredHostWallId = source.PreferredHostWallId,
                PreferredHostPoint = source.PreferredHostPoint,
                OpeningBaseStartPoint = source.OpeningBaseStartPoint,
                OpeningBaseEndPoint = source.OpeningBaseEndPoint,
                PreferOpeningBaseHost = source.PreferOpeningBaseHost,
                DoorLeafBaseStart = source.DoorLeafBaseStart,
                DoorLeafBaseEnd = source.DoorLeafBaseEnd,
                DoorLeafBaseCenter = source.DoorLeafBaseCenter,
                VirtualOpeningBaseStart = source.VirtualOpeningBaseStart,
                VirtualOpeningBaseEnd = source.VirtualOpeningBaseEnd,
                VirtualOpeningBaseCenter = source.VirtualOpeningBaseCenter,
                VirtualOpeningWidthMm = source.VirtualOpeningWidthMm,
                PreferVirtualOpeningHost = source.PreferVirtualOpeningHost
            };
        }

        private static void MatchToWall(
            DoorCandidate candidate,
            List<WallHostLine> walls,
            List<WallCenterlineCandidate> fallbackCenterlines,
            DoorDetectSettings settings)
        {
            candidate.UnmatchedReason = "No host wall matched.";

            if (TryMatchPreferredHostWall(candidate, walls, settings))
            {
                return;
            }

            // Keep opening-base-first host matching only for AltArc candidates.
            if (IsOpeningBasePreferredCandidate(candidate))
            {
                MatchHit openingBaseHit = FindBestOpeningBaseWallHit(candidate, walls, settings.WallMatchDistTolMm);
                if (openingBaseHit != null)
                {
                    candidate.PreferredHostWallId = openingBaseHit.WallId;
                    candidate.PreferredHostPoint = openingBaseHit.ProjectedPoint;
                    candidate.MatchedWallId = openingBaseHit.WallId;
                    candidate.DistToWallMm = openingBaseHit.DistMm;
                    candidate.ProjectedPointOnWall = openingBaseHit.ProjectedPoint;
                    candidate.DeltaAlongWallMm = openingBaseHit.AlongWallMm;
                    candidate.UnmatchedReason = null;
                    return;
                }
            }

            XYZ matchPoint = ResolveMatchPoint(candidate);
            if (matchPoint == null)
            {
                candidate.UnmatchedReason = "Candidate center is null.";
                return;
            }

            MatchHit hit = FindBestWallHit(matchPoint, walls, settings.WallMatchDistTolMm);
            if (hit != null)
            {
                candidate.MatchedWallId = hit.WallId;
                candidate.DistToWallMm = hit.DistMm;
                candidate.ProjectedPointOnWall = hit.ProjectedPoint;
                candidate.DeltaAlongWallMm = hit.AlongWallMm;
                candidate.UnmatchedReason = null;
                return;
            }

            MatchHit fallback = FindBestCenterlineHit(matchPoint, fallbackCenterlines, settings.WallMatchDistTolMm);
            if (fallback != null)
            {
                candidate.MatchedWall = fallback.Centerline;
                candidate.DistToWallMm = fallback.DistMm;
                candidate.ProjectedPointOnWall = fallback.ProjectedPoint;
                candidate.DeltaAlongWallMm = fallback.AlongWallMm;
                candidate.UnmatchedReason = null;
                return;
            }

            candidate.UnmatchedReason = "Distance or projection out of tolerance.";
        }

        private static bool TryMatchPreferredHostWall(DoorCandidate candidate, List<WallHostLine> walls, DoorDetectSettings settings)
        {
            if (candidate == null ||
                walls == null ||
                walls.Count == 0 ||
                candidate.PreferredHostWallId == null ||
                candidate.PreferredHostWallId == ElementId.InvalidElementId)
            {
                return false;
            }

            WallHostLine preferred = walls.FirstOrDefault(x =>
                x != null &&
                x.WallId != null &&
                x.WallId != ElementId.InvalidElementId &&
                x.WallId.IntegerValue == candidate.PreferredHostWallId.IntegerValue);

            if (preferred == null || preferred.Line == null)
            {
                return false;
            }

            XYZ anchor = candidate.PreferredHostPoint ?? ResolveMatchPoint(candidate) ?? candidate.CenterPoint;
            if (anchor == null)
            {
                anchor = preferred.Line.Evaluate(0.5, true);
            }

            XYZ projected = ClosestPointOnSegment(anchor, preferred.Line.GetEndPoint(0), preferred.Line.GetEndPoint(1));
            if (projected == null)
            {
                projected = preferred.Line.Evaluate(0.5, true);
            }

            double distToWallMm = UnitUtils.ConvertFromInternalUnits(anchor.DistanceTo(projected), UnitTypeId.Millimeters);
            const double maxPreferredHostDistanceMm = 500.0;
            if (distToWallMm > maxPreferredHostDistanceMm)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorPreferredHostWallRejected] CandidateId=" + candidate.CandidateId +
                    ", RuleSource=" + (candidate.RuleSource ?? string.Empty) +
                    ", PreferredWallId=" + preferred.WallId.IntegerValue +
                    ", Anchor=" + FormatPoint(anchor) +
                    ", ProjectedPoint=" + FormatPoint(projected) +
                    ", DistToWallMm=" + distToWallMm.ToString("F1") +
                    ", MaxAllowedMm=" + maxPreferredHostDistanceMm.ToString("F1"));
                return false;
            }

            candidate.MatchedWallId = preferred.WallId;
            candidate.DistToWallMm = distToWallMm;
            candidate.ProjectedPointOnWall = projected;
            candidate.DeltaAlongWallMm = ToAlongWallMm(preferred.Line, projected);
            candidate.UnmatchedReason = null;

            DiagnosticRecorder.AppendDebug(
                "[DoorPreferredHostWallMatched] CandidateId=" + candidate.CandidateId +
                ", RuleSource=" + (candidate.RuleSource ?? string.Empty) +
                ", PreferredWallId=" + preferred.WallId.IntegerValue +
                ", Anchor=" + FormatPoint(anchor) +
                ", ProjectedPoint=" + FormatPoint(projected) +
                ", DistToWallMm=" + candidate.DistToWallMm.ToString("F1"));

            return true;
        }

        private static XYZ ResolveMatchPoint(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            if (IsOpeningBasePreferredCandidate(candidate))
            {
                return ResolveOpeningBaseCenter(candidate) ?? candidate.PreferredHostPoint ?? candidate.OpeningCenterPoint ?? candidate.ArcMidPoint ?? candidate.CenterPoint;
            }

            if (string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.HingePoint ?? candidate.ArcMidPoint ?? candidate.CenterPoint;
            }

            if (string.Equals(candidate.RuleSource, "R3B", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R3BD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R4", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.OpeningCenterPoint ?? candidate.ArcMidPoint ?? candidate.CenterPoint;
            }

            return candidate.OpeningCenterPoint ?? candidate.CenterPoint;
        }

        private static bool IsOpeningBasePreferredCandidate(DoorCandidate candidate)
        {
            if (candidate == null || !(candidate.PreferOpeningBaseHost || candidate.PreferVirtualOpeningHost))
            {
                return false;
            }

            return IsR3BDedicated(candidate) ||
                   string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "AltArc", StringComparison.OrdinalIgnoreCase);
        }

        private static XYZ ResolveOpeningBaseCenter(DoorCandidate candidate)
        {
            if (candidate?.VirtualOpeningBaseCenter != null)
            {
                return candidate.VirtualOpeningBaseCenter;
            }

            if (candidate?.VirtualOpeningBaseStart != null && candidate.VirtualOpeningBaseEnd != null)
            {
                XYZ vs = candidate.VirtualOpeningBaseStart;
                XYZ ve = candidate.VirtualOpeningBaseEnd;
                return new XYZ((vs.X + ve.X) * 0.5, (vs.Y + ve.Y) * 0.5, (vs.Z + ve.Z) * 0.5);
            }

            if (candidate?.OpeningBaseStartPoint == null || candidate.OpeningBaseEndPoint == null)
            {
                return null;
            }

            XYZ s = candidate.OpeningBaseStartPoint;
            XYZ e = candidate.OpeningBaseEndPoint;
            return new XYZ((s.X + e.X) * 0.5, (s.Y + e.Y) * 0.5, (s.Z + e.Z) * 0.5);
        }

        private static MatchHit FindBestOpeningBaseWallHit(DoorCandidate candidate, List<WallHostLine> walls, double tolMm)
        {
            XYZ s = candidate?.VirtualOpeningBaseStart ?? candidate?.OpeningBaseStartPoint;
            XYZ e = candidate?.VirtualOpeningBaseEnd ?? candidate?.OpeningBaseEndPoint;
            if (s == null || e == null || walls == null || walls.Count == 0)
            {
                return null;
            }
            XYZ d = e - s;
            double dLen = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
            if (dLen < 1e-9)
            {
                return null;
            }

            XYZ openingDir = new XYZ(d.X / dLen, d.Y / dLen, 0);
            const double minParallelCos = 0.965925826; // 15 degrees.

            MatchHit best = null;
            double bestOverlapRatio = double.MinValue;
            double bestAvgDistMm = double.MaxValue;

            foreach (WallHostLine wall in walls)
            {
                if (wall?.Line == null)
                {
                    continue;
                }

                XYZ wallDirRaw = wall.Line.Direction;
                double wallLen = Math.Sqrt((wallDirRaw.X * wallDirRaw.X) + (wallDirRaw.Y * wallDirRaw.Y));
                if (wallLen < 1e-9)
                {
                    continue;
                }

                XYZ wallDir = new XYZ(wallDirRaw.X / wallLen, wallDirRaw.Y / wallLen, 0);
                double parallelAbs = Math.Abs(Dot(openingDir, wallDir));
                if (parallelAbs < minParallelCos)
                {
                    continue;
                }

                ProjectionData ps = ProjectPointToLineSegment(s, wall.Line);
                ProjectionData pe = ProjectPointToLineSegment(e, wall.Line);
                if (!ps.IsInsideSegment || !pe.IsInsideSegment)
                {
                    continue;
                }

                double dsMm = UnitUtils.ConvertFromInternalUnits(ps.DistanceFeet, UnitTypeId.Millimeters);
                double deMm = UnitUtils.ConvertFromInternalUnits(pe.DistanceFeet, UnitTypeId.Millimeters);
                double avgDistMm = (dsMm + deMm) * 0.5;
                if (avgDistMm > tolMm)
                {
                    continue;
                }

                double baseLenMm = UnitUtils.ConvertFromInternalUnits(s.DistanceTo(e), UnitTypeId.Millimeters);
                double projectedLenMm = UnitUtils.ConvertFromInternalUnits(ps.ProjectedPoint.DistanceTo(pe.ProjectedPoint), UnitTypeId.Millimeters);
                double overlapRatio = baseLenMm > 1e-6 ? Math.Min(1.0, projectedLenMm / baseLenMm) : 0.0;
                XYZ center = new XYZ(
                    (ps.ProjectedPoint.X + pe.ProjectedPoint.X) * 0.5,
                    (ps.ProjectedPoint.Y + pe.ProjectedPoint.Y) * 0.5,
                    (ps.ProjectedPoint.Z + pe.ProjectedPoint.Z) * 0.5);

                if (overlapRatio > bestOverlapRatio + 1e-6 ||
                    (Math.Abs(overlapRatio - bestOverlapRatio) <= 1e-6 && avgDistMm < bestAvgDistMm))
                {
                    bestOverlapRatio = overlapRatio;
                    bestAvgDistMm = avgDistMm;
                    best = new MatchHit
                    {
                        DistMm = avgDistMm,
                        ProjectedPoint = center,
                        AlongWallMm = ToAlongWallMm(wall.Line, center),
                        WallId = wall.WallId
                    };
                }
            }

            return best;
        }

        private static bool IsVirtualOpeningPreferredCandidate(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate.PreferVirtualOpeningHost || candidate.PreferOpeningBaseHost;
        }

        private static List<DoorCandidate> PruneCandidates(List<DoorCandidate> candidates, DoorDetectSettings settings)
        {
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            List<DoorCandidate> ordered = (candidates ?? new List<DoorCandidate>())
                .Where(x => x != null)
                .OrderByDescending(x => CandidateQualityScore(x))
                .ToList();
            List<DoorCandidate> kept = new List<DoorCandidate>();

            foreach (DoorCandidate current in ordered)
            {
                bool skip = false;
                foreach (DoorCandidate existing in kept)
                {
                    if (IsR3T(current) != IsR3T(existing))
                    {
                        continue;
                    }

                    if (IsR3BDedicated(current) != IsR3BDedicated(existing))
                    {
                        // Hard isolation: never cross-prune between R3B and non-R3B.
                        continue;
                    }

                    if (!IsSameWall(current, existing))
                    {
                        continue;
                    }

                    XYZ pa = ResolveCandidatePoint(current);
                    XYZ pb = ResolveCandidatePoint(existing);
                    if (pa == null || pb == null)
                    {
                        continue;
                    }

                    double distanceMm = UnitUtils.ConvertFromInternalUnits(pa.DistanceTo(pb), UnitTypeId.Millimeters);
                    double tolMm = Math.Max(ResolveClusterTolMm(current, effective), ResolveClusterTolMm(existing, effective));
                    bool near = distanceMm <= tolMm;
                    bool shareSegments = HasSharedSegments(current, existing);
                    bool currentIsR3 = IsR3(current);
                    bool existingIsR3 = IsR3(existing);
                    bool currentIsR3T = IsR3T(current);
                    bool existingIsR3T = IsR3T(existing);

                    if (currentIsR3T && existingIsR3T && (shareSegments || near))
                    {
                        skip = true;
                        break;
                    }

                    if (!IsR3BDedicated(current) &&
                        effective.PreferR3OverOthers &&
                        !currentIsR3 &&
                        existingIsR3 &&
                        near)
                    {
                        skip = true;
                        break;
                    }

                    if (currentIsR3 && existingIsR3 && (shareSegments || near))
                    {
                        skip = true;
                        break;
                    }

                    if (shareSegments && near)
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                {
                    kept.Add(current);
                }
            }

            return kept;
        }

        private static bool IsSameWall(DoorCandidate a, DoorCandidate b)
        {
            if (a == null || b == null || a.MatchedWallId == null || b.MatchedWallId == null)
            {
                return false;
            }

            return a.MatchedWallId.IntegerValue == b.MatchedWallId.IntegerValue;
        }

        private static XYZ ResolveCandidatePoint(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            return candidate.ProjectedPointOnWall ?? candidate.OpeningCenterPoint ?? candidate.CenterPoint ?? candidate.HingePoint;
        }

        private static double ResolveClusterTolMm(DoorCandidate candidate, DoorDetectSettings settings)
        {
            double width = candidate?.WidthMm ?? 0.0;
            if (width <= 1e-6)
            {
                width = 600.0;
            }

            double byWidth = width * settings.DoorClusterTolFactor;
            return Math.Max(settings.DoorClusterTolMinMm, Math.Min(byWidth, settings.DoorClusterTolMaxMm));
        }

        private static bool HasSharedSegments(DoorCandidate a, DoorCandidate b)
        {
            if (a?.SegmentIds == null || b?.SegmentIds == null || a.SegmentIds.Count == 0 || b.SegmentIds.Count == 0)
            {
                return false;
            }

            HashSet<int> set = new HashSet<int>(a.SegmentIds);
            return b.SegmentIds.Any(set.Contains);
        }

        private static bool IsR3(DoorCandidate candidate)
        {
            return candidate != null && string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsR3T(DoorCandidate candidate)
        {
            return candidate != null && string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsR3BDedicated(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate.SymbolFamilyKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD ||
                   string.Equals(candidate.RuleSource, "R3B", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3BD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanMergeTogether(DoorCandidate a, DoorCandidate b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (IsR3T(a) != IsR3T(b))
            {
                return false;
            }

            bool aR3B = IsR3BDedicated(a);
            bool bR3B = IsR3BDedicated(b);
            return aR3B == bR3B;
        }

        private static void AssignSymbolFamilyKind(
            IEnumerable<DoorCandidate> candidates,
            IDoorCandidateRule rule,
            DoorSymbolFamilyKind routedKind)
        {
            if (candidates == null)
            {
                return;
            }

            bool isR3BRule = rule != null && string.Equals(rule.Name, "R3B", StringComparison.OrdinalIgnoreCase);
            bool isR3DRule = rule != null && string.Equals(rule.Name, "R3D", StringComparison.OrdinalIgnoreCase);
            bool isR3BDRule = rule != null && string.Equals(rule.Name, "R3BD", StringComparison.OrdinalIgnoreCase);
            bool isR3CRule = rule != null && string.Equals(rule.Name, "R3C", StringComparison.OrdinalIgnoreCase);
            bool isR3CDRule = rule != null && string.Equals(rule.Name, "R3CD", StringComparison.OrdinalIgnoreCase);
            bool isR3Rule = rule != null && string.Equals(rule.Name, "R3", StringComparison.OrdinalIgnoreCase);

            foreach (DoorCandidate c in candidates)
            {
                if (c == null)
                {
                    continue;
                }

                if (isR3BRule || routedKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing;
                    continue;
                }

                if (isR3DRule || routedKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing;
                    c.IsDoubleDoor = true;
                    continue;
                }

                if (isR3BDRule || routedKind == DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing;
                    c.IsDoubleDoor = true;
                    continue;
                }

                if (isR3CRule || routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing;
                    continue;
                }

                if (isR3CDRule || routedKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD;
                    continue;
                }

                if (isR3Rule || routedKind == DoorSymbolFamilyKind.StandardArcDoor)
                {
                    c.SymbolFamilyKind = DoorSymbolFamilyKind.StandardArcDoor;
                }
            }
        }

        private static int CandidateQualityScore(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0;
            }

            int score = 0;
            if (IsVirtualOpeningPreferredCandidate(candidate)) score += 120;
            if (candidate.PreferVirtualOpeningHost && candidate.PreferredHostWallId != null && candidate.PreferredHostWallId != ElementId.InvalidElementId) score += 300;
            if (IsR3T(candidate)) score += 110;
            else if (IsR3(candidate)) score += 100;
            else if (string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase)) score += 75;
            else if (string.Equals(candidate.RuleSource, "R3BD", StringComparison.OrdinalIgnoreCase)) score += 75;
            else if (string.Equals(candidate.RuleSource, "R3B", StringComparison.OrdinalIgnoreCase)) score += 70;
            else if (string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase)) score += 70;
            else if (string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase)) score += 70;
            else if (string.Equals(candidate.RuleSource, "R1", StringComparison.OrdinalIgnoreCase)) score += 60;
            else if (string.Equals(candidate.RuleSource, "R2", StringComparison.OrdinalIgnoreCase)) score += 40;

            if (candidate.WallDirHint != null) score += 30;
            if (candidate.OpeningCenterPoint != null) score += 20;
            if (candidate.HingePoint != null) score += 10;
            if (candidate.MatchedWallId != null) score += 10;
            return score;
        }

        private static MatchHit FindBestWallHit(XYZ point, List<WallHostLine> walls, double tolMm)
        {
            if (walls == null || walls.Count == 0)
            {
                return null;
            }

            MatchHit best = null;
            foreach (WallHostLine wall in walls)
            {
                ProjectionData p = ProjectPointToLineSegment(point, wall.Line);
                if (!p.IsInsideSegment)
                {
                    continue;
                }

                double distMm = UnitUtils.ConvertFromInternalUnits(p.DistanceFeet, UnitTypeId.Millimeters);
                if (distMm > tolMm)
                {
                    continue;
                }

                if (best == null || distMm < best.DistMm)
                {
                    best = new MatchHit
                    {
                        DistMm = distMm,
                        ProjectedPoint = p.ProjectedPoint,
                        AlongWallMm = ToAlongWallMm(wall.Line, p.ProjectedPoint),
                        WallId = wall.WallId
                    };
                }
            }

            return best;
        }

        private static MatchHit FindBestCenterlineHit(XYZ point, List<WallCenterlineCandidate> centerlines, double tolMm)
        {
            if (centerlines == null || centerlines.Count == 0)
            {
                return null;
            }

            MatchHit best = null;
            foreach (WallCenterlineCandidate c in centerlines)
            {
                if (c == null || c.CenterLine == null)
                {
                    continue;
                }

                ProjectionData p = ProjectPointToLineSegment(point, c.CenterLine);
                if (!p.IsInsideSegment)
                {
                    continue;
                }

                double distMm = UnitUtils.ConvertFromInternalUnits(p.DistanceFeet, UnitTypeId.Millimeters);
                if (distMm > tolMm)
                {
                    continue;
                }

                if (best == null || distMm < best.DistMm)
                {
                    best = new MatchHit
                    {
                        DistMm = distMm,
                        ProjectedPoint = p.ProjectedPoint,
                        AlongWallMm = ToAlongWallMm(c.CenterLine, p.ProjectedPoint),
                        Centerline = c
                    };
                }
            }

            return best;
        }

        private static ProjectionData ProjectPointToLineSegment(XYZ point, Line line)
        {
            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            XYZ ab = b - a;
            double len2 = (ab.X * ab.X) + (ab.Y * ab.Y) + (ab.Z * ab.Z);
            if (len2 < 1e-12)
            {
                return new ProjectionData
                {
                    IsInsideSegment = false,
                    DistanceFeet = point.DistanceTo(a),
                    ProjectedPoint = a
                };
            }

            double t = Dot(point - a, ab) / len2;
            XYZ projected = a + ab.Multiply(t);
            double tol = 0.02;
            bool inside = t >= -tol && t <= 1.0 + tol;
            double dist = point.DistanceTo(projected);

            return new ProjectionData
            {
                IsInsideSegment = inside,
                ProjectedPoint = projected,
                DistanceFeet = dist
            };
        }

        private static double Dot(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        // Use projected point to build a stable position scalar on wall, in mm.
        private static double ToAlongWallMm(Line line, XYZ projectedPoint)
        {
            if (line == null || projectedPoint == null)
            {
                return 0.0;
            }

            XYZ start = line.GetEndPoint(0);
            double feet = start.DistanceTo(projectedPoint);
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        private static List<WallHostLine> GetWallHosts(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Select(x => new
                {
                    Wall = x,
                    LocationCurve = x.Location as LocationCurve
                })
                .Where(x => x.LocationCurve != null && x.LocationCurve.Curve is Line)
                .Select(x => new WallHostLine
                {
                    WallId = x.Wall.Id,
                    Line = (Line)x.LocationCurve.Curve
                })
                .ToList();
        }

        private static List<WallHostLine> GetWallHosts(IList<Wall> hostWalls)
        {
            return (hostWalls ?? new List<Wall>())
                .Where(x => x != null)
                .Select(x => new
                {
                    Wall = x,
                    LocationCurve = x.Location as LocationCurve
                })
                .Where(x => x.LocationCurve != null && x.LocationCurve.Curve is Line)
                .Select(x => new WallHostLine
                {
                    WallId = x.Wall.Id,
                    Line = (Line)x.LocationCurve.Curve
                })
                .ToList();
        }

        private static List<WallCenterlineCandidate> GetWallCenterlineFallback(Document doc, ImportInstance importInstance)
        {
            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WALL" };
            CadSegmentBuildResult wallBuild = CadSegmentBuilder.BuildSegments(doc, importInstance, filter);
            List<CadSegment> wallSegments = wallBuild.Segments
                .Where(x => !x.IsArc && string.Equals(x.SemanticLayer, "WALL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return WallCenterlineDetector.Detect(wallSegments, new WallDetectSettings()).Centerlines;
        }

        private static void CountWidthRange(DoorDetectResult result, double widthMm)
        {
            if (widthMm >= 650.0 && widthMm < 800.0)
            {
                result.WidthRange650To800++;
                return;
            }

            if (widthMm >= 800.0 && widthMm < 1000.0)
            {
                result.WidthRange800To1000++;
                return;
            }

            if (widthMm >= 1000.0 && widthMm <= 1200.0)
            {
                result.WidthRange1000To1200++;
            }
        }

        private class WallHostLine
        {
            public ElementId WallId { get; set; }

            public Line Line { get; set; }
        }

        private class ProjectionData
        {
            public bool IsInsideSegment { get; set; }

            public double DistanceFeet { get; set; }

            public XYZ ProjectedPoint { get; set; }
        }

        private class MatchHit
        {
            public double DistMm { get; set; }

            public XYZ ProjectedPoint { get; set; }

            public double AlongWallMm { get; set; }

            public ElementId WallId { get; set; }

            public WallCenterlineCandidate Centerline { get; set; }
        }
    }
}
