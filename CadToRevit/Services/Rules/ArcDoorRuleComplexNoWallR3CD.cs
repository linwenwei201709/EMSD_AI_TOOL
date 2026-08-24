using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rules
{
    /// <summary>
    /// R3CD dedicated rule.
    /// Hard rules:
    /// 1) Routing/precheck and extraction must use the same direction-invariant structure logic.
    /// 2) Opening axis is only from bilateral side-group inner anchors.
    /// </summary>
    public sealed class ArcDoorRuleComplexNoWallR3CD : IDoorCandidateRule
    {
        public string Name => "R3CD";

        private const double MinDoorLineLenMm = 500.0;
        private const double MinSideLineLenMm = 500.0;
        private const double SideParallelThreshold = 0.92;
        private const double DoorLineAxisAlignThreshold = 0.90;
        private const double SideSplitToleranceMm = 80.0;
        private const double SideAttachToleranceMm = 260.0;
        private const double PrecheckSideAttachToleranceMm = 700.0;

        internal sealed class StructureSummary
        {
            public bool HasBilateralSideGroups { get; set; }
            public bool HasValidDoorLinesGe500 { get; set; }
            public bool ArcCountMatched { get; set; }
            public int SideCandidateCount { get; set; }
            public int LeftGroupCount { get; set; }
            public int RightGroupCount { get; set; }
            public int DoorLineCandidateCount { get; set; }
            public int ValidDoorLineCount { get; set; }
            public string RouteReason { get; set; }
            public XYZ LeftInnerAnchor { get; set; }
            public XYZ RightInnerAnchor { get; set; }
            public XYZ OpeningDir { get; set; }
            public XYZ OpeningCenter { get; set; }
            public double OpeningWidthMm { get; set; }
        }

        private sealed class SideLineSample
        {
            public CadSegment Segment { get; set; }
            public XYZ Mid { get; set; }
            public XYZ InnerPoint { get; set; }
            public double SignedFeet { get; set; }
        }

        private sealed class SideGroupResolve
        {
            public XYZ SideDir { get; set; }
            public XYZ SideNormal { get; set; }
            public XYZ InnerRef { get; set; }
            public List<SideLineSample> Left { get; set; } = new List<SideLineSample>();
            public List<SideLineSample> Right { get; set; } = new List<SideLineSample>();
            public List<CadSegment> SideCandidates { get; set; } = new List<CadSegment>();
        }

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            List<CadSegment> allSegments = (doorSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            if (allSegments.Count == 0)
            {
                return result;
            }

            ArcDoorRuleComplexNoWall baseRule = new ArcDoorRuleComplexNoWall();
            List<DoorCandidate> baseCandidates = baseRule.GenerateCandidates(allSegments, settings).ToList();
            if (baseCandidates.Count == 0)
            {
                return result;
            }

            Dictionary<int, CadSegment> segmentById = allSegments
                .Where(x => x != null)
                .GroupBy(x => x.SegmentId)
                .ToDictionary(g => g.Key, g => g.First());

            int componentId = 1;
            foreach (DoorCandidate seed in baseCandidates)
            {
                List<CadSegment> componentSegments = ResolveComponentSegments(seed, segmentById);
                StructureSummary summary;
                bool ok = TryEvaluateStructure(componentSegments, componentId, true, out summary);
                if (!ok)
                {
                    string reason = summary != null && !summary.HasBilateralSideGroups
                        ? "VerticalSideGroupsNotResolved"
                        : "NoValidDirectionalDoorLineGE500";
                    DiagnosticRecorder.AppendDebug(
                        "[R3CDRoute] ComponentId=" + componentId +
                        ", Accepted=False, Reason=" + reason);
                    componentId++;
                    continue;
                }

                bool isDouble = seed.IsDoubleDoor || (seed.SegmentIds ?? new List<int>()).Count >= 2;
                seed.RuleSource = Name;
                seed.SymbolFamilyKind = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD;
                seed.OpeningBaseStartPoint = summary.LeftInnerAnchor;
                seed.OpeningBaseEndPoint = summary.RightInnerAnchor;
                seed.VirtualOpeningBaseStart = summary.LeftInnerAnchor;
                seed.VirtualOpeningBaseEnd = summary.RightInnerAnchor;
                seed.OpeningCenterPoint = summary.OpeningCenter;
                seed.VirtualOpeningBaseCenter = summary.OpeningCenter;
                seed.CenterPoint = summary.OpeningCenter;
                seed.WallDirHint = summary.OpeningDir;
                seed.WidthMm = summary.OpeningWidthMm;
                seed.OpeningWidthMm = summary.OpeningWidthMm;
                seed.VirtualOpeningWidthMm = summary.OpeningWidthMm;
                seed.LeftEdgePoint = summary.LeftInnerAnchor;
                seed.RightEdgePoint = summary.RightInnerAnchor;
                seed.IsDoubleDoor = isDouble;
                seed.CombinedWidthMm = isDouble ? summary.OpeningWidthMm : seed.CombinedWidthMm;
                seed.CombinedCenter = isDouble ? summary.OpeningCenter : seed.CombinedCenter;
                seed.WidthSource = "R3CDSideInnerEndpoints";
                seed.PreferOpeningBaseHost = true;
                seed.PreferVirtualOpeningHost = true;

                DiagnosticRecorder.AppendDebug(
                    "[R3CDOpeningResolved] ComponentId=" + componentId +
                    ", LeftInnerAnchor=" + FormatPoint(summary.LeftInnerAnchor) +
                    ", RightInnerAnchor=" + FormatPoint(summary.RightInnerAnchor) +
                    ", OpeningDir=" + FormatVector(summary.OpeningDir) +
                    ", OpeningWidthMm=" + summary.OpeningWidthMm.ToString("F1") +
                    ", OpeningCenter=" + FormatPoint(summary.OpeningCenter));
                DiagnosticRecorder.AppendDebug(
                    "[R3CDRoute] ComponentId=" + componentId +
                    ", Accepted=True" +
                    ", LeftGroupCount=" + summary.LeftGroupCount +
                    ", RightGroupCount=" + summary.RightGroupCount +
                    ", ValidDoorLineCount=" + summary.ValidDoorLineCount);

                result.Add(seed);
                componentId++;
            }

            return result;
        }

        internal static bool TryEvaluateStructure(
            List<CadSegment> componentSegments,
            int componentId,
            bool writeLog,
            out StructureSummary summary)
        {
            summary = new StructureSummary();
            List<CadSegment> segments = (componentSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            List<CadSegment> lines = segments.Where(x => !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            List<CadSegment> arcs = SelectPrimaryArcs(segments);
            if (lines.Count == 0)
            {
                return false;
            }

            SideGroupResolve side;
            if (!TryResolveSideGroups(lines, arcs, componentId, writeLog, false, out side))
            {
                summary.SideCandidateCount = side?.SideCandidates?.Count ?? 0;
                summary.LeftGroupCount = side?.Left?.Count ?? 0;
                summary.RightGroupCount = side?.Right?.Count ?? 0;
                summary.HasBilateralSideGroups = false;
                return false;
            }

            summary.SideCandidateCount = side.SideCandidates.Count;
            summary.LeftGroupCount = side.Left.Count;
            summary.RightGroupCount = side.Right.Count;
            summary.HasBilateralSideGroups = true;

            XYZ leftAnchor = AveragePoint(side.Left.Select(x => x.InnerPoint).ToList());
            XYZ rightAnchor = AveragePoint(side.Right.Select(x => x.InnerPoint).ToList());
            XYZ openingDir = Normalize2D(rightAnchor - leftAnchor);
            if (leftAnchor == null || rightAnchor == null || openingDir == null)
            {
                summary.HasBilateralSideGroups = false;
                return false;
            }

            List<CadSegment> validDoorLines = new List<CadSegment>();
            foreach (CadSegment line in lines)
            {
                if (side.SideCandidates.Any(x => x.SegmentId == line.SegmentId))
                {
                    continue;
                }

                XYZ dir = Normalize2D(line.P1 - line.P0);
                if (dir == null)
                {
                    continue;
                }

                double lenMm = ToMm(line.P0.DistanceTo(line.P1));
                bool longEnough = lenMm >= MinDoorLineLenMm;
                bool alignToOpeningAxis = Math.Abs(Dot2D(dir, openingDir)) >= DoorLineAxisAlignThreshold;
                bool attachedToSides = IsAttachedToAnySideGroup(line, side.SideCandidates, PrecheckSideAttachToleranceMm, true);
                bool pass = longEnough && alignToOpeningAxis && attachedToSides;
                bool worldHorizontal = Math.Abs(Dot2D(dir, XYZ.BasisX)) >= 0.90;
                bool worldVertical = Math.Abs(Dot2D(dir, XYZ.BasisY)) >= 0.90;
                if (writeLog)
                {
                    string worldOri = worldHorizontal ? "Horizontal" : (worldVertical ? "Vertical" : "Oblique");
                    DiagnosticRecorder.AppendDebug(
                        "[R3CDDoorLineCandidate] ComponentId=" + componentId +
                        ", SegmentId=" + line.SegmentId +
                        ", LengthMm=" + lenMm.ToString("F1") +
                        ", WorldOrientation=" + worldOri +
                        ", AlignToOpeningAxis=" + Math.Abs(Dot2D(dir, openingDir)).ToString("F3") +
                        ", AttachedToSideGroups=" + attachedToSides +
                        ", PassedGE500AndRole=" + pass);
                }

                summary.DoorLineCandidateCount++;
                if (pass)
                {
                    summary.ValidDoorLineCount++;
                    validDoorLines.Add(line);
                }
            }

            if (writeLog)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3CDDoorLineSummary] ComponentId=" + componentId +
                    ", DoorLineCandidateCount=" + summary.DoorLineCandidateCount +
                    ", ValidDoorLineCount=" + summary.ValidDoorLineCount);
            }

            summary.HasValidDoorLinesGe500 = summary.ValidDoorLineCount > 0;
            if (!summary.HasValidDoorLinesGe500)
            {
                return false;
            }

            summary.LeftInnerAnchor = leftAnchor;
            summary.RightInnerAnchor = rightAnchor;
            summary.OpeningDir = openingDir;
            summary.OpeningCenter = Mid(leftAnchor, rightAnchor);
            summary.OpeningWidthMm = ToMm(leftAnchor.DistanceTo(rightAnchor));
            return true;
        }

        internal static bool TryEvaluateRoutingPrecheck(
            List<CadSegment> componentSegments,
            int componentId,
            bool writeLog,
            out StructureSummary summary)
        {
            summary = new StructureSummary();
            List<CadSegment> segments = (componentSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            List<CadSegment> lines = segments.Where(x => !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            List<CadSegment> arcs = SelectPrimaryArcs(segments);
            summary.ArcCountMatched = arcs.Count == 2;
            if (lines.Count == 0 || arcs.Count != 2)
            {
                summary.RouteReason = arcs.Count != 2 ? "PrimaryArcCountNotEqual2" : "NoBodyLines";
                return false;
            }

            SideGroupResolve side;
            if (!TryResolveSideGroups(lines, arcs, componentId, writeLog, true, out side))
            {
                summary.SideCandidateCount = side?.SideCandidates?.Count ?? 0;
                summary.LeftGroupCount = side?.Left?.Count ?? 0;
                summary.RightGroupCount = side?.Right?.Count ?? 0;
                summary.HasBilateralSideGroups = false;
                summary.RouteReason = "NoBilateralSideGroups";
                return false;
            }

            summary.SideCandidateCount = side.SideCandidates.Count;
            summary.LeftGroupCount = side.Left.Count;
            summary.RightGroupCount = side.Right.Count;
            summary.HasBilateralSideGroups = true;

            XYZ openingAxis = side.SideNormal;
            if (openingAxis == null)
            {
                summary.RouteReason = "OpeningAxisSeedMissing";
                return false;
            }

            int doorCandidateCount = 0;
            int validDoorCount = 0;
            foreach (CadSegment line in lines)
            {
                if (side.SideCandidates.Any(x => x.SegmentId == line.SegmentId))
                {
                    continue;
                }

                XYZ dir = Normalize2D(line.P1 - line.P0);
                if (dir == null)
                {
                    continue;
                }

                double lenMm = ToMm(line.P0.DistanceTo(line.P1));
                bool longEnough = lenMm >= MinDoorLineLenMm;
                bool alignToOpeningAxis = Math.Abs(Dot2D(dir, openingAxis)) >= DoorLineAxisAlignThreshold;
                bool attachedToSides = IsAttachedToAnySideGroup(line, side.SideCandidates, PrecheckSideAttachToleranceMm, true);
                bool pass = longEnough && alignToOpeningAxis && attachedToSides;
                if (alignToOpeningAxis)
                {
                    doorCandidateCount++;
                }
                if (pass)
                {
                    validDoorCount++;
                }
            }

            summary.DoorLineCandidateCount = doorCandidateCount;
            summary.ValidDoorLineCount = validDoorCount;
            summary.HasValidDoorLinesGe500 = validDoorCount > 0;
            summary.RouteReason = !summary.HasValidDoorLinesGe500 ? "NoDoorLineGE500" : "HardRouteAccepted";

            if (writeLog)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3CDRoutePrecheck] ComponentId=" + componentId +
                    ", ArcCount=" + arcs.Count +
                    ", SideCandidateCount=" + summary.SideCandidateCount +
                    ", LeftGroupCount=" + summary.LeftGroupCount +
                    ", RightGroupCount=" + summary.RightGroupCount +
                    ", DoorLineCandidateCount=" + summary.DoorLineCandidateCount +
                    ", ValidDoorLineCount=" + summary.ValidDoorLineCount +
                    ", Passed=" + (summary.HasBilateralSideGroups && summary.HasValidDoorLinesGe500) +
                    ", Reason=" + (summary.RouteReason ?? string.Empty) +
                    ", Mode=HardRouteFeatureOnly");
            }

            return summary.HasBilateralSideGroups && summary.HasValidDoorLinesGe500;
        }

        private static List<CadSegment> SelectPrimaryArcs(List<CadSegment> segments)
        {
            return (segments ?? new List<CadSegment>())
                .Where(x => x != null && x.IsArc)
                .OrderByDescending(GetArcRepresentativeSpanFeet)
                .ThenBy(x => x.SegmentId)
                .Take(2)
                .ToList();
        }

        private static double GetArcRepresentativeSpanFeet(CadSegment arc)
        {
            if (arc == null)
            {
                return 0.0;
            }

            double radiusSpan = arc.RadiusFeet > 1e-9 ? arc.RadiusFeet * 2.0 : 0.0;
            double chordSpan = arc.P0 != null && arc.P1 != null ? arc.P0.DistanceTo(arc.P1) : 0.0;

            List<XYZ> refs = new List<XYZ>();
            if (arc.P0 != null) refs.Add(arc.P0);
            if (arc.P1 != null) refs.Add(arc.P1);
            if (arc.MidPoint != null) refs.Add(arc.MidPoint);
            if (arc.Center != null) refs.Add(arc.Center);
            double bboxSpan = 0.0;
            if (refs.Count >= 2)
            {
                double minX = refs.Min(x => x.X);
                double maxX = refs.Max(x => x.X);
                double minY = refs.Min(x => x.Y);
                double maxY = refs.Max(x => x.Y);
                bboxSpan = Math.Sqrt(((maxX - minX) * (maxX - minX)) + ((maxY - minY) * (maxY - minY)));
            }

            return Math.Max(radiusSpan, Math.Max(chordSpan, bboxSpan));
        }

        private static bool TryResolveSideGroups(
            List<CadSegment> lines,
            List<CadSegment> arcs,
            int componentId,
            bool writeLog,
            bool relaxedForRoutingPrecheck,
            out SideGroupResolve resolve)
        {
            resolve = new SideGroupResolve();
            List<CadSegment> longLines = lines
                .Where(x => x != null && ToMm(x.P0.DistanceTo(x.P1)) >= MinSideLineLenMm)
                .ToList();
            if (longLines.Count < 2)
            {
                return false;
            }

            XYZ innerRef = ResolveInnerReference(arcs, lines);
            if (innerRef == null)
            {
                return false;
            }

            SideGroupResolve best = null;
            int bestScore = int.MinValue;

            foreach (CadSegment seedLine in longLines)
            {
                XYZ dominant = Normalize2D(seedLine.P1 - seedLine.P0);
                if (dominant == null)
                {
                    continue;
                }

                List<CadSegment> sideCandidates = longLines
                    .Where(x =>
                    {
                        XYZ d = Normalize2D(x.P1 - x.P0);
                        return d != null && Math.Abs(Dot2D(d, dominant)) >= SideParallelThreshold;
                    })
                    .ToList();
                if (sideCandidates.Count < 2)
                {
                    continue;
                }

                XYZ normal = new XYZ(-dominant.Y, dominant.X, 0);
                double splitTolFt = MmToFt(SideSplitToleranceMm);
                List<SideLineSample> left = new List<SideLineSample>();
                List<SideLineSample> right = new List<SideLineSample>();
                List<SideLineSample> allSamples = new List<SideLineSample>();
                foreach (CadSegment line in sideCandidates)
                {
                    XYZ mid = Mid(line.P0, line.P1);
                    XYZ inner = ResolveInnerEndpoint(line.P0, line.P1, innerRef);
                    if (mid == null || inner == null)
                    {
                        continue;
                    }

                    double signed = Dot2D(mid - innerRef, normal);
                    SideLineSample sample = new SideLineSample
                    {
                        Segment = line,
                        Mid = mid,
                        InnerPoint = inner,
                        SignedFeet = signed
                    };
                    allSamples.Add(sample);
                    if (signed > splitTolFt) right.Add(sample);
                    else if (signed < -splitTolFt) left.Add(sample);
                }

                if (relaxedForRoutingPrecheck && (left.Count == 0 || right.Count == 0) && allSamples.Count >= 2)
                {
                    double meanSigned = allSamples.Average(x => x.SignedFeet);
                    double recenterTolFt = MmToFt(20.0);
                    left = allSamples.Where(x => (x.SignedFeet - meanSigned) < -recenterTolFt).ToList();
                    right = allSamples.Where(x => (x.SignedFeet - meanSigned) > recenterTolFt).ToList();
                }

                int score = (Math.Min(left.Count, right.Count) * 1000) + (sideCandidates.Count * 10) + Math.Max(left.Count, right.Count);
                if (best == null || score > bestScore)
                {
                    bestScore = score;
                    best = new SideGroupResolve
                    {
                        SideDir = dominant,
                        SideNormal = normal,
                        InnerRef = innerRef,
                        Left = left,
                        Right = right,
                        SideCandidates = sideCandidates
                    };
                }
            }

            resolve = best ?? new SideGroupResolve();

            if (writeLog)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3CDSideGroupSummary] ComponentId=" + componentId +
                    ", SideCandidateCount=" + (resolve.SideCandidates?.Count ?? 0) +
                    ", LeftGroupCount=" + (resolve.Left?.Count ?? 0) +
                    ", RightGroupCount=" + (resolve.Right?.Count ?? 0) +
                    ", InnerRef=" + FormatPoint(resolve.InnerRef));
            }

            return resolve.Left != null && resolve.Right != null && resolve.Left.Count > 0 && resolve.Right.Count > 0;
        }

        private static XYZ ResolveDominantLineDirection(List<CadSegment> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            XYZ best = null;
            int bestSupport = -1;
            foreach (CadSegment line in lines)
            {
                XYZ seed = Normalize2D(line.P1 - line.P0);
                if (seed == null)
                {
                    continue;
                }

                int support = 0;
                foreach (CadSegment other in lines)
                {
                    XYZ d = Normalize2D(other.P1 - other.P0);
                    if (d != null && Math.Abs(Dot2D(seed, d)) >= SideParallelThreshold)
                    {
                        support++;
                    }
                }

                if (support > bestSupport)
                {
                    bestSupport = support;
                    best = seed;
                }
            }

            return best;
        }

        private static XYZ ResolveInnerReference(List<CadSegment> arcs, List<CadSegment> lines)
        {
            List<XYZ> arcCore = new List<XYZ>();
            foreach (CadSegment a in arcs ?? new List<CadSegment>())
            {
                if (a == null)
                {
                    continue;
                }

                if (a.MidPoint != null) arcCore.Add(a.MidPoint);
                else if (a.P0 != null && a.P1 != null) arcCore.Add(Mid(a.P0, a.P1));
                if (a.Center != null) arcCore.Add(a.Center);
            }

            if (arcCore.Count > 0)
            {
                return AveragePoint(arcCore);
            }

            List<XYZ> mids = (lines ?? new List<CadSegment>())
                .Select(x => x == null ? null : Mid(x.P0, x.P1))
                .Where(x => x != null)
                .ToList();
            return mids.Count == 0 ? null : AveragePoint(mids);
        }

        private static bool IsAttachedToAnySideGroup(
            CadSegment line,
            List<CadSegment> sideGroups,
            double toleranceMm,
            bool allowNearApproach)
        {
            if (line?.P0 == null || line.P1 == null || sideGroups == null || sideGroups.Count == 0)
            {
                return false;
            }

            double tolFt = MmToFt(toleranceMm);
            foreach (CadSegment side in sideGroups)
            {
                if (side?.P0 == null || side.P1 == null)
                {
                    continue;
                }

                if (DistancePointToSegment2D(line.P0, side.P0, side.P1) <= tolFt ||
                    DistancePointToSegment2D(line.P1, side.P0, side.P1) <= tolFt ||
                    DistancePointToSegment2D(side.P0, line.P0, line.P1) <= tolFt ||
                    DistancePointToSegment2D(side.P1, line.P0, line.P1) <= tolFt)
                {
                    return true;
                }

                if (allowNearApproach)
                {
                    double segmentGap = DistanceSegmentToSegment2D(line.P0, line.P1, side.P0, side.P1);
                    if (segmentGap <= tolFt)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static double DistanceSegmentToSegment2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1)
        {
            double d1 = DistancePointToSegment2D(a0, b0, b1);
            double d2 = DistancePointToSegment2D(a1, b0, b1);
            double d3 = DistancePointToSegment2D(b0, a0, a1);
            double d4 = DistancePointToSegment2D(b1, a0, a1);
            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        private static List<CadSegment> ResolveComponentSegments(
            DoorCandidate candidate,
            IReadOnlyDictionary<int, CadSegment> segmentById)
        {
            if (candidate?.SegmentIds == null || segmentById == null)
            {
                return new List<CadSegment>();
            }

            return candidate.SegmentIds
                .Distinct()
                .Where(segmentById.ContainsKey)
                .Select(id => segmentById[id])
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .ToList();
        }

        private static XYZ ResolveInnerEndpoint(XYZ p0, XYZ p1, XYZ innerRef)
        {
            if (p0 == null || p1 == null)
            {
                return null;
            }

            if (innerRef == null)
            {
                return p0;
            }

            return p0.DistanceTo(innerRef) <= p1.DistanceTo(innerRef) ? p0 : p1;
        }

        private static XYZ AveragePoint(List<XYZ> points)
        {
            List<XYZ> valid = (points ?? new List<XYZ>()).Where(x => x != null).ToList();
            if (valid.Count == 0)
            {
                return null;
            }

            return new XYZ(valid.Average(x => x.X), valid.Average(x => x.Y), valid.Average(x => x.Z));
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

        private static XYZ Mid(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static XYZ Normalize2D(XYZ v)
        {
            if (v == null)
            {
                return null;
            }

            double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
            if (len < 1e-9)
            {
                return null;
            }

            return new XYZ(v.X / len, v.Y / len, 0);
        }

        private static double ToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        private static double MmToFt(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        private static string FormatPoint(XYZ p)
        {
            if (p == null)
            {
                return "(null)";
            }

            return "(" + p.X.ToString("F3") + "," + p.Y.ToString("F3") + "," + p.Z.ToString("F3") + ")";
        }

        private static string FormatVector(XYZ v)
        {
            if (v == null)
            {
                return "(null)";
            }

            return "(" + v.X.ToString("F4") + "," + v.Y.ToString("F4") + ",0.0000)";
        }
    }
}
