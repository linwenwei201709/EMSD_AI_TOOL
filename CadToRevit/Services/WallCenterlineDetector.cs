using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WallCenterlineDetector
    {
        private const double EpsilonFeet = 1e-8;
        private static readonly double OverlapThresholdToleranceFt =
            UnitUtils.ConvertToInternalUnits(1.5, UnitTypeId.Millimeters);
        private static readonly double AdaptiveLengthDiffThresholdFt =
            UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);

        public static WallDetectResult Detect(
            List<CadSegment> wallSegments,
            WallDetectSettings settings)
        {
            WallDetectResult result = new WallDetectResult();
            if (wallSegments == null || wallSegments.Count == 0)
            {
                return result;
            }

            WallDetectSettings effectiveSettings = settings ?? new WallDetectSettings();
            List<SegmentInfo> infos = wallSegments
                .Where(IsValidSegment)
                .Select((x, i) => CreateSegmentInfo(x, i))
                .ToList();

            result.InputWallSegmentCount = infos.Count;
            result.DirectionGroupCount = infos
                .Select(x => BuildDirectionGroupKey(x.Direction))
                .Distinct()
                .Count();

            List<PairCandidate> candidates = new List<PairCandidate>();
            double cosTol = Math.Cos(DegreeToRadian(effectiveSettings.ParallelAngleTolDeg));

            for (int i = 0; i < infos.Count; i++)
            {
                for (int j = i + 1; j < infos.Count; j++)
                {
                    result.PairCandidateCount++;
                    SegmentInfo a = infos[i];
                    SegmentInfo b = infos[j];

                    double dot = Math.Abs(Dot2D(a.Direction, b.Direction));
                    if (dot < cosTol)
                    {
                        continue;
                    }

                    result.PassedParallelCount++;

                    double signedDistanceFeet = SignedDistanceFeet(a, b);
                    double thicknessFeet = Math.Abs(signedDistanceFeet);
                    if (Math.Abs(thicknessFeet - effectiveSettings.TargetThicknessFt) > effectiveSettings.ThicknessTolFt)
                    {
                        continue;
                    }

                    result.PassedThicknessCount++;

                    double overlapFeet;
                    double overlapStartFeet;
                    double overlapEndFeet;
                    SpanData span = ComputePairSpanFeet(a, b, effectiveSettings, out overlapFeet, out overlapStartFeet, out overlapEndFeet);
                    if (overlapFeet + OverlapThresholdToleranceFt < effectiveSettings.MinOverlapFt)
                    {
                        continue;
                    }

                    result.PassedOverlapCount++;

                    Line centerLine = BuildCenterLine(a, signedDistanceFeet, span.StartFt, span.EndFt);
                    if (centerLine == null)
                    {
                        continue;
                    }

                    double a0 = Dot2D(a.P0, a.Direction);
                    double a1 = Dot2D(a.P1, a.Direction);
                    if (a0 > a1) Swap(ref a0, ref a1);
                    double b0 = Dot2D(b.P0, a.Direction);
                    double b1 = Dot2D(b.P1, a.Direction);
                    if (b0 > b1) Swap(ref b0, ref b1);
                    ClipInterval(span.StartFt, span.EndFt, a0, a1, out double aStart, out double aEnd);
                    ClipInterval(span.StartFt, span.EndFt, b0, b1, out double bStart, out double bEnd);

                    candidates.Add(new PairCandidate
                    {
                        A = a,
                        B = b,
                        ThicknessMm = ToMm(thicknessFeet),
                        OverlapMm = ToMm(overlapFeet),
                        CenterLine = centerLine,
                        AStartFt = aStart,
                        AEndFt = aEnd,
                        BStartFt = bStart,
                        BEndFt = bEnd
                    });
                }
            }

            // Interval allocation prevents duplicated overlapping pair acceptance.
            Dictionary<int, List<OccupiedInterval>> occupied = new Dictionary<int, List<OccupiedInterval>>();
            HashSet<int> used = new HashSet<int>();
            double intervalTolFt = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            foreach (PairCandidate candidate in candidates.OrderByDescending(x => x.OverlapMm))
            {
                int idA = candidate.A.Index;
                int idB = candidate.B.Index;
                if (HasIntervalConflict(occupied, idA, candidate.AStartFt, candidate.AEndFt, intervalTolFt) ||
                    HasIntervalConflict(occupied, idB, candidate.BStartFt, candidate.BEndFt, intervalTolFt))
                {
                    continue;
                }

                AddOccupiedInterval(occupied, idA, candidate.AStartFt, candidate.AEndFt);
                AddOccupiedInterval(occupied, idB, candidate.BStartFt, candidate.BEndFt);
                used.Add(idA);
                used.Add(idB);
                result.Centerlines.Add(new WallCenterlineCandidate
                {
                    CenterLine = candidate.CenterLine,
                    ThicknessMm = candidate.ThicknessMm,
                    SideA = candidate.A.Source,
                    SideB = candidate.B.Source,
                    OverlapLengthMm = candidate.OverlapMm
                });
            }

            result.UnmatchedWallSegmentCount = infos.Count - used.Count;
            return result;
        }

        public static List<PairMeasurement> ScanPairMeasurements(
            List<CadSegment> wallSegments,
            double parallelAngleTolDeg,
            double minThicknessFt,
            double maxThicknessFt,
            double minOverlapFt)
        {
            List<PairMeasurement> result = new List<PairMeasurement>();
            if (wallSegments == null || wallSegments.Count == 0)
            {
                return result;
            }

            List<SegmentInfo> infos = wallSegments
                .Where(IsValidSegment)
                .Select((x, i) => CreateSegmentInfo(x, i))
                .ToList();
            double cosTol = Math.Cos(DegreeToRadian(parallelAngleTolDeg));
            for (int i = 0; i < infos.Count; i++)
            {
                for (int j = i + 1; j < infos.Count; j++)
                {
                    SegmentInfo a = infos[i];
                    SegmentInfo b = infos[j];
                    if (Math.Abs(Dot2D(a.Direction, b.Direction)) < cosTol)
                    {
                        continue;
                    }

                    double thicknessFt = Math.Abs(SignedDistanceFeet(a, b));
                    if (thicknessFt < minThicknessFt || thicknessFt > maxThicknessFt)
                    {
                        continue;
                    }

                    double overlapFeet;
                    double overlapStartFeet;
                    double overlapEndFeet;
                    ComputePairSpanFeet(a, b, null, out overlapFeet, out overlapStartFeet, out overlapEndFeet);
                    if (overlapFeet + OverlapThresholdToleranceFt < minOverlapFt)
                    {
                        continue;
                    }

                    result.Add(new PairMeasurement
                    {
                        ThicknessMm = ToMm(thicknessFt),
                        OverlapMm = ToMm(overlapFeet)
                    });
                }
            }

            return result;
        }

        private static bool IsValidSegment(CadSegment segment)
        {
            if (segment == null || segment.P0 == null || segment.P1 == null)
            {
                return false;
            }

            return segment.P0.DistanceTo(segment.P1) > EpsilonFeet;
        }

        private static SegmentInfo CreateSegmentInfo(CadSegment segment, int index)
        {
            XYZ p0 = segment.P0;
            XYZ p1 = segment.P1;
            if (IsGreater(p0, p1))
            {
                XYZ temp = p0;
                p0 = p1;
                p1 = temp;
            }

            XYZ direction = Normalize2D(p1 - p0);
            if (direction.X < 0 || (Math.Abs(direction.X) <= EpsilonFeet && direction.Y < 0))
            {
                direction = new XYZ(-direction.X, -direction.Y, 0);
            }

            return new SegmentInfo
            {
                Index = index,
                Source = segment,
                P0 = p0,
                P1 = p1,
                Direction = direction
            };
        }

        private static string BuildDirectionGroupKey(XYZ direction)
        {
            double angle = Math.Atan2(direction.Y, direction.X);
            double bucket = Math.Round(angle / DegreeToRadian(5.0));
            return bucket.ToString("F0");
        }

        private static double SignedDistanceFeet(SegmentInfo a, SegmentInfo b)
        {
            XYZ normal = new XYZ(-a.Direction.Y, a.Direction.X, 0);
            XYZ delta = b.P0 - a.P0;
            return Dot2D(delta, normal);
        }

        private static SpanData ComputePairSpanFeet(
            SegmentInfo a,
            SegmentInfo b,
            WallDetectSettings settings,
            out double overlapFeet,
            out double overlapStartFeet,
            out double overlapEndFeet)
        {
            XYZ u = a.Direction;
            double a0 = Dot2D(a.P0, u);
            double a1 = Dot2D(a.P1, u);
            if (a0 > a1)
            {
                Swap(ref a0, ref a1);
            }

            double b0 = Dot2D(b.P0, u);
            double b1 = Dot2D(b.P1, u);
            if (b0 > b1)
            {
                Swap(ref b0, ref b1);
            }

            overlapStartFeet = Math.Max(a0, b0);
            overlapEndFeet = Math.Min(a1, b1);
            overlapFeet = Math.Max(0.0, overlapEndFeet - overlapStartFeet);

            SpanData overlapSpan = new SpanData
            {
                StartFt = overlapStartFeet,
                EndFt = overlapEndFeet
            };
            SpanData unionSpan = new SpanData
            {
                StartFt = Math.Min(a0, b0),
                EndFt = Math.Max(a1, b1)
            };

            if (settings == null || settings.DoubleLineLengthPolicy == WallDoubleLineLengthPolicy.Overlap)
            {
                return overlapSpan;
            }

            if (settings.DoubleLineLengthPolicy == WallDoubleLineLengthPolicy.Union)
            {
                return unionSpan;
            }

            bool aIsLonger = (a1 - a0) >= (b1 - b0);
            SpanData longer = aIsLonger
                ? new SpanData { StartFt = a0, EndFt = a1 }
                : new SpanData { StartFt = b0, EndFt = b1 };
            SpanData shorter = aIsLonger
                ? new SpanData { StartFt = b0, EndFt = b1 }
                : new SpanData { StartFt = a0, EndFt = a1 };

            if (settings.DoubleLineLengthPolicy == WallDoubleLineLengthPolicy.LongerSide)
            {
                return longer;
            }

            double lenA = a1 - a0;
            double lenB = b1 - b0;
            if (Math.Abs(lenA - lenB) < AdaptiveLengthDiffThresholdFt)
            {
                return overlapSpan;
            }

            double containTolFt = Math.Max(0.0, settings.AdaptiveContainTolFt);
            bool isContained =
                shorter.StartFt >= (longer.StartFt - containTolFt) &&
                shorter.EndFt <= (longer.EndFt + containTolFt);
            if (!isContained)
            {
                return overlapSpan;
            }

            double extendStart = Math.Max(0.0, overlapStartFeet - longer.StartFt);
            double extendEnd = Math.Max(0.0, longer.EndFt - overlapEndFeet);
            double extendMaxFt = Math.Max(0.0, settings.AdaptiveExtendMaxFt);
            if (extendStart <= extendMaxFt && extendEnd <= extendMaxFt)
            {
                return longer;
            }

            return overlapSpan;
        }

        private static void ClipInterval(double srcStart, double srcEnd, double min, double max, out double start, out double end)
        {
            double a = Math.Min(srcStart, srcEnd);
            double b = Math.Max(srcStart, srcEnd);
            start = Math.Max(a, min);
            end = Math.Min(b, max);
            if (start > end)
            {
                start = min;
                end = max;
            }
        }

        private static Line BuildCenterLine(
            SegmentInfo a,
            double signedDistanceFeet,
            double overlapStartFeet,
            double overlapEndFeet)
        {
            XYZ u = a.Direction;
            XYZ n = new XYZ(-u.Y, u.X, 0);
            XYZ offset = n.Multiply(signedDistanceFeet / 2.0);

            XYZ c0 = PointOnLineByProjection(a.P0, u, overlapStartFeet).Add(offset);
            XYZ c1 = PointOnLineByProjection(a.P0, u, overlapEndFeet).Add(offset);
            if (c0.DistanceTo(c1) <= EpsilonFeet)
            {
                return null;
            }

            return Line.CreateBound(c0, c1);
        }

        private static XYZ PointOnLineByProjection(XYZ anchor, XYZ direction, double projectionScalar)
        {
            double anchorProjection = Dot2D(anchor, direction);
            double delta = projectionScalar - anchorProjection;
            return anchor.Add(direction.Multiply(delta));
        }

        private static double ToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        private static double Dot2D(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static XYZ Normalize2D(XYZ vector)
        {
            double length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            if (length <= EpsilonFeet)
            {
                return new XYZ(1, 0, 0);
            }

            return new XYZ(vector.X / length, vector.Y / length, 0);
        }

        private static bool IsGreater(XYZ a, XYZ b)
        {
            if (a.X > b.X + EpsilonFeet)
            {
                return true;
            }

            if (Math.Abs(a.X - b.X) <= EpsilonFeet && a.Y > b.Y + EpsilonFeet)
            {
                return true;
            }

            return false;
        }

        private static double DegreeToRadian(double degree)
        {
            return degree * Math.PI / 180.0;
        }

        private static void Swap(ref double a, ref double b)
        {
            double tmp = a;
            a = b;
            b = tmp;
        }

        private static bool HasIntervalConflict(
            Dictionary<int, List<OccupiedInterval>> occupied,
            int index,
            double startFt,
            double endFt,
            double tolFt)
        {
            List<OccupiedInterval> list;
            if (occupied == null || !occupied.TryGetValue(index, out list) || list == null || list.Count == 0)
            {
                return false;
            }

            double min = Math.Min(startFt, endFt);
            double max = Math.Max(startFt, endFt);
            foreach (OccupiedInterval item in list)
            {
                double overlap = Math.Min(max, item.EndFt) - Math.Max(min, item.StartFt);
                if (overlap > tolFt)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddOccupiedInterval(
            Dictionary<int, List<OccupiedInterval>> occupied,
            int index,
            double startFt,
            double endFt)
        {
            if (occupied == null)
            {
                return;
            }

            List<OccupiedInterval> list;
            if (!occupied.TryGetValue(index, out list))
            {
                list = new List<OccupiedInterval>();
                occupied[index] = list;
            }

            list.Add(new OccupiedInterval
            {
                StartFt = Math.Min(startFt, endFt),
                EndFt = Math.Max(startFt, endFt)
            });
        }

        private class SegmentInfo
        {
            public int Index { get; set; }

            public CadSegment Source { get; set; }

            public XYZ P0 { get; set; }

            public XYZ P1 { get; set; }

            public XYZ Direction { get; set; }
        }

        private class PairCandidate
        {
            public SegmentInfo A { get; set; }

            public SegmentInfo B { get; set; }

            public Line CenterLine { get; set; }

            public double ThicknessMm { get; set; }

            public double OverlapMm { get; set; }

            public double AStartFt { get; set; }

            public double AEndFt { get; set; }

            public double BStartFt { get; set; }

            public double BEndFt { get; set; }
        }

        private sealed class SpanData
        {
            public double StartFt { get; set; }

            public double EndFt { get; set; }
        }

        private class OccupiedInterval
        {
            public double StartFt { get; set; }

            public double EndFt { get; set; }
        }

        public sealed class PairMeasurement
        {
            public double ThicknessMm { get; set; }

            public double OverlapMm { get; set; }
        }
    }
}
