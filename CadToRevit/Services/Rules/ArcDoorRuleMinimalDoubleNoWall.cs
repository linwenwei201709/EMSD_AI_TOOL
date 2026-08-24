using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rules
{
    /// <summary>
    /// R3BD dedicated rule for minimal no-wall double doors.
    /// This rule uses independent candidate extraction and must not reuse R3C geometry.
    /// </summary>
    public sealed class ArcDoorRuleMinimalDoubleNoWall : IDoorCandidateRule
    {
        public string Name => "R3BD";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            List<CadSegment> segments = (doorSegments ?? new List<CadSegment>()).Where(x => x != null).ToList();
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            if (segments.Count == 0)
            {
                return result;
            }

            List<CadSegment> arcs = segments
                .Where(x => x.IsArc && x.P0 != null && x.P1 != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .Take(2)
                .ToList();
            List<CadSegment> lines = segments.Where(x => !x.IsArc && x.P0 != null && x.P1 != null).ToList();
            if (arcs.Count != 2)
            {
                return result;
            }

            CadSegment a0 = arcs[0];
            CadSegment a1 = arcs[1];
            List<XYZ> arcAnchors = new List<XYZ> { a0.P0, a0.P1, a1.P0, a1.P1 };
            List<XYZ> arcConvergePoints = new List<XYZ>
            {
                a0.MidPoint ?? Mid(a0.P0, a0.P1),
                a1.MidPoint ?? Mid(a1.P0, a1.P1)
            }.Where(x => x != null).ToList();
            XYZ arcAnchorCenter = new XYZ(
                arcAnchors.Average(x => x.X),
                arcAnchors.Average(x => x.Y),
                arcAnchors.Average(x => x.Z));
            XYZ innerReference = ResolveArcInnerReference(a0, a1) ?? (arcConvergePoints.Count > 0 ? AveragePoint(arcConvergePoints) : arcAnchorCenter);

            double minSideLenFt = MmToFt(220.0);
            double maxSideLenFt = MmToFt(1800.0);
            double nearArcFt = MmToFt(260.0);
            List<LineFeature> closingCandidates = BuildClosingLineCandidates(lines, arcAnchors, minSideLenFt, maxSideLenFt, nearArcFt, innerReference);
            XYZ openingCenter = (a0.Center != null && a1.Center != null)
                ? Mid(a0.Center, a1.Center)
                : arcAnchorCenter;
            XYZ openingDir = EstimateOpeningDirFromArcs(a0, a1, arcAnchors);
            if (openingDir == null)
            {
                return result;
            }
            double minWidthFt = MmToFt(Math.Max(effective.DoorWidthMinMm, 500.0));
            double openingWidthFt = EstimateOpeningWidthFtFromArcs(a0, a1, arcAnchors, openingDir);
            if (openingWidthFt < minWidthFt)
            {
                openingWidthFt = minWidthFt;
            }

            // Horizontal closing edges are enhancement only, not hard prerequisites.
            List<LineFeature> horizontalCandidates = closingCandidates
                .Where(x => IsNearHorizontal(x.Dir))
                .ToList();
            bool usedHorizontalEnhancement = false;
            XYZ leftInnerAnchor = null;
            XYZ rightInnerAnchor = null;
            int leftCount = 0;
            int rightCount = 0;
            if (horizontalCandidates.Count >= 2)
            {
                XYZ dominantClosingDir = ResolveDominantDirection(horizontalCandidates.Select(x => x.Dir).ToList(), 0.92);
                if (dominantClosingDir != null)
                {
                    double splitFt = MmToFt(100.0);
                    List<XYZ> leftFinal = new List<XYZ>();
                    List<XYZ> rightFinal = new List<XYZ>();
                    foreach (LineFeature lf in horizontalCandidates)
                    {
                        if (Math.Abs(lf.Dir.DotProduct(dominantClosingDir)) < 0.75)
                        {
                            continue;
                        }

                        double signed = openingDir.DotProduct(lf.InnerPoint - arcAnchorCenter);
                        if (signed > splitFt) leftFinal.Add(lf.InnerPoint);
                        if (signed < -splitFt) rightFinal.Add(lf.InnerPoint);
                    }

                    leftCount = leftFinal.Count;
                    rightCount = rightFinal.Count;
                    if (leftCount > 0 && rightCount > 0)
                    {
                        leftInnerAnchor = AveragePoint(leftFinal);
                        rightInnerAnchor = AveragePoint(rightFinal);
                        XYZ refinedDir = Normalize2D(rightInnerAnchor - leftInnerAnchor);
                        if (refinedDir != null)
                        {
                            openingDir = refinedDir;
                            double refinedWidthFt = Math.Abs((rightInnerAnchor - leftInnerAnchor).DotProduct(openingDir));
                            if (refinedWidthFt >= minWidthFt)
                            {
                                openingWidthFt = refinedWidthFt;
                            }
                            usedHorizontalEnhancement = true;
                        }
                    }
                }
            }

            double openingWidthMm = FtToMm(openingWidthFt);
            XYZ half = openingDir.Multiply(openingWidthFt * 0.5);
            XYZ baseStart = openingCenter - half;
            XYZ baseEnd = openingCenter + half;

            DiagnosticRecorder.AppendDebug(
                "[R3BDCandidateGroups] ComponentId=1" +
                ", RuleSource=R3BD" +
                ", UsedHorizontalEnhancement=" + usedHorizontalEnhancement +
                ", ClosingCandidateCount=" + closingCandidates.Count +
                ", HorizontalCandidateCount=" + horizontalCandidates.Count +
                ", LeftGroupCount=" + leftCount +
                ", RightGroupCount=" + rightCount +
                ", LeftInnerAnchor=" + FormatPoint(leftInnerAnchor) +
                ", RightInnerAnchor=" + FormatPoint(rightInnerAnchor));
            DiagnosticRecorder.AppendDebug(
                "[R3BDOpening] ComponentId=1" +
                ", OpeningDir=" + FormatVector2D(openingDir) +
                ", OpeningCenter=" + FormatPoint(openingCenter) +
                ", CombinedWidthMm=" + openingWidthMm.ToString("F1") +
                ", VirtualOpeningBaseStart=" + FormatPoint(baseStart) +
                ", VirtualOpeningBaseEnd=" + FormatPoint(baseEnd));

            DoorCandidate candidate = new DoorCandidate
            {
                CenterPoint = openingCenter,
                OpeningCenterPoint = openingCenter,
                OpeningBaseStartPoint = baseStart,
                OpeningBaseEndPoint = baseEnd,
                VirtualOpeningBaseStart = baseStart,
                VirtualOpeningBaseEnd = baseEnd,
                VirtualOpeningBaseCenter = openingCenter,
                WidthMm = openingWidthMm,
                OpeningWidthMm = openingWidthMm,
                VirtualOpeningWidthMm = openingWidthMm,
                RuleSource = Name,
                SymbolFamilyKind = DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing,
                SegmentIds = segments.Select(x => x.SegmentId).Distinct().ToList(),
                WallDirHint = openingDir,
                WidthSource = "R3BDIndependentOpening",
                PreferOpeningBaseHost = true,
                PreferVirtualOpeningHost = true,
                IsDoubleDoor = true,
                LeftEdgePoint = baseStart,
                RightEdgePoint = baseEnd,
                CombinedWidthMm = openingWidthMm,
                CombinedCenter = openingCenter,
                ArcRadiusMm = FtToMm((a0.RadiusFeet + a1.RadiusFeet) * 0.5),
                ArcSweepDeg = (Math.Abs(a0.SweepAngleRad) + Math.Abs(a1.SweepAngleRad)) * (180.0 / Math.PI) * 0.5,
                ArcMidPoint = Mid(a0.MidPoint ?? Mid(a0.P0, a0.P1), a1.MidPoint ?? Mid(a1.P0, a1.P1))
            };
            result.Add(candidate);
            return result;
        }

        private static List<LineFeature> BuildClosingLineCandidates(
            List<CadSegment> lines,
            List<XYZ> arcAnchors,
            double minSideLenFt,
            double maxSideLenFt,
            double nearArcFt,
            XYZ innerReference)
        {
            List<LineFeature> features = new List<LineFeature>();
            foreach (CadSegment line in lines ?? new List<CadSegment>())
            {
                if (line == null || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                double lenFt = line.P0.DistanceTo(line.P1);
                if (lenFt < minSideLenFt || lenFt > maxSideLenFt)
                {
                    continue;
                }

                bool nearAnyArc = (arcAnchors ?? new List<XYZ>()).Any(p =>
                    p != null && Math.Min(line.P0.DistanceTo(p), line.P1.DistanceTo(p)) <= nearArcFt);
                if (!nearAnyArc)
                {
                    continue;
                }

                XYZ dir = Normalize2D(line.P1 - line.P0);
                XYZ mid = Mid(line.P0, line.P1);
                if (dir == null || mid == null)
                {
                    continue;
                }

                XYZ innerPoint = ResolveInnerEndpoint(line.P0, line.P1, innerReference);
                if (innerPoint == null)
                {
                    continue;
                }

                features.Add(new LineFeature { Line = line, Dir = dir, Mid = mid, InnerPoint = innerPoint });
            }
            return features;
        }

        private static XYZ ResolveInnerEndpoint(XYZ p0, XYZ p1, XYZ innerReference)
        {
            if (p0 == null || p1 == null)
            {
                return null;
            }

            if (innerReference == null)
            {
                return p0;
            }

            double d0 = p0.DistanceTo(innerReference);
            double d1 = p1.DistanceTo(innerReference);
            return d0 <= d1 ? p0 : p1;
        }

        private static XYZ EstimateOpeningDirFromArcs(CadSegment a0, CadSegment a1, List<XYZ> arcAnchors)
        {
            if (a0?.Center != null && a1?.Center != null)
            {
                XYZ byCenters = Normalize2D(a1.Center - a0.Center);
                if (byCenters != null)
                {
                    return byCenters;
                }
            }

            List<XYZ> anchors = (arcAnchors ?? new List<XYZ>()).Where(x => x != null).ToList();
            if (anchors.Count >= 2)
            {
                XYZ pBest = null;
                XYZ qBest = null;
                double best = double.MinValue;
                for (int i = 0; i < anchors.Count; i++)
                {
                    for (int j = i + 1; j < anchors.Count; j++)
                    {
                        double d = anchors[i].DistanceTo(anchors[j]);
                        if (d > best)
                        {
                            best = d;
                            pBest = anchors[i];
                            qBest = anchors[j];
                        }
                    }
                }

                XYZ bySpan = Normalize2D(qBest - pBest);
                if (bySpan != null)
                {
                    return bySpan;
                }
            }

            return null;
        }

        private static double EstimateOpeningWidthFtFromArcs(CadSegment a0, CadSegment a1, List<XYZ> arcAnchors, XYZ openingDir)
        {
            if (openingDir == null)
            {
                return 0.0;
            }

            if (a0?.Center != null && a1?.Center != null)
            {
                double byCenters = Math.Abs((a1.Center - a0.Center).DotProduct(openingDir));
                if (byCenters > 1e-6)
                {
                    return byCenters;
                }
            }

            List<XYZ> anchors = (arcAnchors ?? new List<XYZ>()).Where(x => x != null).ToList();
            if (anchors.Count < 2)
            {
                return 0.0;
            }

            double minProj = double.MaxValue;
            double maxProj = double.MinValue;
            XYZ origin = anchors[0];
            foreach (XYZ p in anchors)
            {
                double t = (p - origin).DotProduct(openingDir);
                if (t < minProj) minProj = t;
                if (t > maxProj) maxProj = t;
            }

            return Math.Max(0.0, maxProj - minProj);
        }

        private static XYZ ResolveArcInnerReference(CadSegment a0, CadSegment a1)
        {
            if (a0 == null || a1 == null || a0.P0 == null || a0.P1 == null || a1.P0 == null || a1.P1 == null)
            {
                return null;
            }

            List<XYZ> e0 = new List<XYZ> { a0.P0, a0.P1 };
            List<XYZ> e1 = new List<XYZ> { a1.P0, a1.P1 };
            XYZ bestA = null;
            XYZ bestB = null;
            double best = double.MaxValue;
            foreach (XYZ p in e0)
            {
                foreach (XYZ q in e1)
                {
                    double d = p.DistanceTo(q);
                    if (d < best)
                    {
                        best = d;
                        bestA = p;
                        bestB = q;
                    }
                }
            }

            return Mid(bestA, bestB);
        }

        private static XYZ ResolveDominantDirection(List<XYZ> dirs, double parallelDotThreshold)
        {
            List<XYZ> all = (dirs ?? new List<XYZ>()).Where(x => x != null).ToList();
            if (all.Count == 0)
            {
                return null;
            }

            XYZ best = null;
            int bestSupport = -1;
            for (int i = 0; i < all.Count; i++)
            {
                XYZ seed = all[i];
                int support = 0;
                for (int j = 0; j < all.Count; j++)
                {
                    if (Math.Abs(seed.DotProduct(all[j])) >= parallelDotThreshold)
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

        private static bool IsNearHorizontal(XYZ dir)
        {
            if (dir == null)
            {
                return false;
            }

            return Math.Abs(dir.X) >= 0.75;
        }

        private static XYZ AveragePoint(List<XYZ> points)
        {
            List<XYZ> all = (points ?? new List<XYZ>()).Where(x => x != null).ToList();
            if (all.Count == 0)
            {
                return null;
            }

            return new XYZ(all.Average(x => x.X), all.Average(x => x.Y), all.Average(x => x.Z));
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

        private static double FtToMm(double feet)
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

        private static string FormatVector2D(XYZ v)
        {
            if (v == null)
            {
                return "(null)";
            }

            return "(" + v.X.ToString("F4") + "," + v.Y.ToString("F4") + ",0.0000)";
        }

        private sealed class LineFeature
        {
            public CadSegment Line { get; set; }
            public XYZ Mid { get; set; }
            public XYZ Dir { get; set; }
            public XYZ InnerPoint { get; set; }
        }
    }
}
