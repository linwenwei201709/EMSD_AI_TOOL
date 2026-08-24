using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class DoorPairingService
    {
        public static List<DoorCandidate> BuildCandidates(
            IList<DoorCandidate> source,
            DoorDetectSettings settings)
        {
            DoorDetectSettings effective = settings ?? new DoorDetectSettings();
            List<DoorCandidate> input = (source ?? new List<DoorCandidate>())
                .Where(x => x != null)
                .ToList();
            if (!effective.EnableDoubleDoorRecognition || input.Count < 2)
            {
                return input;
            }

            List<DoorCandidate> output = new List<DoorCandidate>();
            HashSet<int> paired = new HashSet<int>();
            List<int> pairable = input
                .Select((c, idx) => new { Candidate = c, Index = idx })
                .Where(x => IsPairableArcCandidate(x.Candidate))
                .Select(x => x.Index)
                .ToList();

            Dictionary<int, int> bestMatch = new Dictionary<int, int>();
            Dictionary<int, PairEvaluation> bestEval = new Dictionary<int, PairEvaluation>();

            // Build nearest valid partner for each pairable candidate first,
            // then only accept mutually-nearest pairs to avoid cross-opening chaining.
            foreach (int i in pairable)
            {
                DoorCandidate a = input[i];
                int bestIndex = -1;
                PairEvaluation localBest = default(PairEvaluation);
                double bestScore = double.MaxValue;

                foreach (int j in pairable)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    DoorCandidate b = input[j];
                    PairEvaluation eval;
                    if (!TryEvaluatePair(input, i, a, j, b, effective, out eval))
                    {
                        continue;
                    }

                    if (eval.Score < bestScore - 1e-9 ||
                        (Math.Abs(eval.Score - bestScore) <= 1e-9 && j < bestIndex))
                    {
                        bestScore = eval.Score;
                        bestIndex = j;
                        localBest = eval;
                    }
                }

                if (bestIndex >= 0)
                {
                    bestMatch[i] = bestIndex;
                    bestEval[i] = localBest;
                }
            }

            foreach (int i in pairable.OrderBy(x => x))
            {
                if (paired.Contains(i) || !bestMatch.ContainsKey(i))
                {
                    continue;
                }

                int j = bestMatch[i];
                if (i == j || paired.Contains(j) || !bestMatch.ContainsKey(j))
                {
                    continue;
                }

                if (bestMatch[j] != i)
                {
                    continue;
                }

                PairEvaluation eval = bestEval[i];
                DoorCandidate merged = MergeToDoubleDoor(input[i], input[j], eval.LeftEdge, eval.RightEdge, eval.CombinedWidthMm);
                output.Add(merged);
                paired.Add(i);
                paired.Add(j);

                DiagnosticRecorder.AppendDebug(
                    "[DoubleDoorPair] "
                    + "A=Candidate" + input[i].CandidateId
                    + ", B=Candidate" + input[j].CandidateId
                    + ", LeftEdge=" + FormatPointMm(eval.LeftEdge)
                    + ", RightEdge=" + FormatPointMm(eval.RightEdge)
                    + ", CombinedWidth=" + eval.CombinedWidthMm.ToString("F1") + "mm"
                    + ", Center=" + FormatPointMm(merged.CombinedCenter)
                    + ", AxisSource=" + (eval.AxisSource ?? string.Empty));
            }

            for (int i = 0; i < input.Count; i++)
            {
                if (!paired.Contains(i))
                {
                    output.Add(input[i]);
                }
            }

            return output;
        }

        private struct PairEvaluation
        {
            public double Score;
            public XYZ LeftEdge;
            public XYZ RightEdge;
            public double CombinedWidthMm;
            public string AxisSource;
        }

        private static bool TryEvaluatePair(
            IList<DoorCandidate> all,
            int indexA,
            DoorCandidate a,
            int indexB,
            DoorCandidate b,
            DoorDetectSettings settings,
            out PairEvaluation eval)
        {
            eval = default(PairEvaluation);
            double score;
            XYZ leftEdge;
            XYZ rightEdge;
            double combinedWidthMm;
            string axisSource;
            if (!CanPair(all, indexA, a, indexB, b, settings, out score, out leftEdge, out rightEdge, out combinedWidthMm, out axisSource))
            {
                return false;
            }

            eval = new PairEvaluation
            {
                Score = score,
                LeftEdge = leftEdge,
                RightEdge = rightEdge,
                CombinedWidthMm = combinedWidthMm,
                AxisSource = axisSource
            };
            return true;
        }

        private static bool IsPairableArcCandidate(DoorCandidate c)
        {
            return c != null &&
                   string.Equals(c.RuleSource, "R3", StringComparison.OrdinalIgnoreCase) &&
                   c.HingePoint != null &&
                   c.MatchedWallId != null;
        }

        private static bool CanPair(
            IList<DoorCandidate> all,
            int indexA,
            DoorCandidate a,
            int indexB,
            DoorCandidate b,
            DoorDetectSettings settings,
            out double score,
            out XYZ leftEdge,
            out XYZ rightEdge,
            out double combinedWidthMm,
            out string axisSource)
        {
            score = double.MaxValue;
            leftEdge = null;
            rightEdge = null;
            combinedWidthMm = 0.0;
            axisSource = string.Empty;
            if (a.MatchedWallId.IntegerValue != b.MatchedWallId.IntegerValue)
            {
                return false;
            }

            double alongA = ResolveAlongWallMm(a);
            double alongB = ResolveAlongWallMm(b);
            if (Math.Abs(alongA - alongB) > Math.Max(1.0, settings.DoorPairSpacingMaxMm))
            {
                return false;
            }

            if (!TryResolvePairEdges(a, b, out leftEdge, out rightEdge, out combinedWidthMm, out axisSource))
            {
                return false;
            }

            if (combinedWidthMm < 1000.0 || combinedWidthMm > 3000.0)
            {
                return false;
            }

            double widthA = ResolveLeafWidthMm(a);
            double widthB = ResolveLeafWidthMm(b);
            if (Math.Abs(widthA - widthB) >= 400.0)
            {
                return false;
            }

            if (widthA > 1e-6 && widthB > 1e-6)
            {
                // Reject pair if the resolved combined span is inconsistent with leaf widths.
                // This blocks accidental cross-opening pairing that produces oversized combined doors.
                double expectedCombined = widthA + widthB;
                if (Math.Abs(combinedWidthMm - expectedCombined) > 250.0)
                {
                    return false;
                }
            }

            if (HasCandidateBetween(all, indexA, indexB, a, b, alongA, alongB))
            {
                return false;
            }

            // Keep deterministic nearest-neighbor scoring for mutual-match selection.
            score = Math.Abs(alongA - alongB) + (Math.Abs(widthA - widthB) * 0.1);
            return true;
        }

        private static bool HasCandidateBetween(
            IList<DoorCandidate> all,
            int indexA,
            int indexB,
            DoorCandidate a,
            DoorCandidate b,
            double alongA,
            double alongB)
        {
            if (all == null || all.Count == 0 || a == null || b == null)
            {
                return false;
            }

            if (a.MatchedWallId == null || b.MatchedWallId == null ||
                a.MatchedWallId.IntegerValue != b.MatchedWallId.IntegerValue)
            {
                return false;
            }

            if (Math.Abs(alongA - alongB) <= 1e-6)
            {
                return false;
            }

            double minAlong = Math.Min(alongA, alongB) + 50.0;
            double maxAlong = Math.Max(alongA, alongB) - 50.0;
            if (minAlong > maxAlong)
            {
                return false;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (i == indexA || i == indexB)
                {
                    continue;
                }

                DoorCandidate c = all[i];
                if (!IsPairableArcCandidate(c) || c.MatchedWallId == null)
                {
                    continue;
                }

                if (c.MatchedWallId.IntegerValue != a.MatchedWallId.IntegerValue)
                {
                    continue;
                }

                double along = ResolveAlongWallMm(c);
                if (along > minAlong && along < maxAlong)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePairEdges(
            DoorCandidate a,
            DoorCandidate b,
            out XYZ leftEdge,
            out XYZ rightEdge,
            out double combinedWidthMm,
            out string axisSource)
        {
            leftEdge = null;
            rightEdge = null;
            combinedWidthMm = 0.0;
            axisSource = string.Empty;

            XYZ axis = ResolveWallAxis(a, b, out axisSource);
            List<XYZ> points = new List<XYZ>();
            AddBoundaryPoints(points, a);
            AddBoundaryPoints(points, b);
            if (points.Count < 2)
            {
                return false;
            }

            XYZ minPoint = null;
            XYZ maxPoint = null;
            double minProj = double.MaxValue;
            double maxProj = double.MinValue;
            foreach (XYZ p in points)
            {
                if (p == null)
                {
                    continue;
                }

                double proj = p.DotProduct(axis);
                if (proj < minProj)
                {
                    minProj = proj;
                    minPoint = p;
                }

                if (proj > maxProj)
                {
                    maxProj = proj;
                    maxPoint = p;
                }
            }

            if (minPoint == null || maxPoint == null || minPoint.DistanceTo(maxPoint) < 1e-6)
            {
                return false;
            }

            leftEdge = minPoint;
            rightEdge = maxPoint;
            combinedWidthMm = FtToMm(leftEdge.DistanceTo(rightEdge));
            return combinedWidthMm > 1e-6;
        }

        private static XYZ ResolveWallAxis(DoorCandidate a, DoorCandidate b, out string axisSource)
        {
            axisSource = "FallbackX";
            Line line = a?.MatchedWall?.CenterLine ?? b?.MatchedWall?.CenterLine;
            if (line != null && line.Direction != null && line.Direction.GetLength() > 1e-9)
            {
                XYZ axis2D = new XYZ(line.Direction.X, line.Direction.Y, 0.0);
                if (axis2D.GetLength() > 1e-9)
                {
                    axisSource = "MatchedWallCenterLine";
                    return axis2D.Normalize();
                }
            }

            XYZ hint = a?.WallDirHint ?? b?.WallDirHint;
            if (hint != null && hint.GetLength() > 1e-9)
            {
                XYZ axis2D = new XYZ(hint.X, hint.Y, 0.0);
                if (axis2D.GetLength() > 1e-9)
                {
                    axisSource = "WallDirHint";
                    return axis2D.Normalize();
                }
            }

            XYZ projectedAxis = TryResolveAxisFromPoints(a?.ProjectedPointOnWall, b?.ProjectedPointOnWall);
            if (projectedAxis != null)
            {
                axisSource = "ProjectedPair";
                return projectedAxis;
            }

            XYZ hingeAxis = TryResolveAxisFromPoints(a?.HingePoint, b?.HingePoint);
            if (hingeAxis != null)
            {
                axisSource = "HingePair";
                return hingeAxis;
            }

            // Fallback axis keeps behavior deterministic when wall axis is unavailable.
            return XYZ.BasisX;
        }

        private static XYZ TryResolveAxisFromPoints(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            XYZ v = new XYZ(b.X - a.X, b.Y - a.Y, 0.0);
            if (v.GetLength() <= 1e-9)
            {
                return null;
            }

            return v.Normalize();
        }

        private static void AddBoundaryPoints(List<XYZ> points, DoorCandidate c)
        {
            if (points == null || c == null)
            {
                return;
            }

            AddUniquePoint(points, c.LeftEdgePoint);
            AddUniquePoint(points, c.RightEdgePoint);
            AddUniquePoint(points, c.LeafHinge);
            AddUniquePoint(points, c.LeafLatch);
            AddUniquePoint(points, c.HingePoint);
        }

        private static void AddUniquePoint(List<XYZ> points, XYZ point)
        {
            if (points == null || point == null)
            {
                return;
            }

            if (points.Any(x => x != null && x.DistanceTo(point) < 1e-6))
            {
                return;
            }

            points.Add(point);
        }

        private static double ResolveAlongWallMm(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0.0;
            }

            if (candidate.DeltaAlongWallMm > 1e-6)
            {
                return candidate.DeltaAlongWallMm;
            }

            return 0.0;
        }

        private static double ResolveLeafWidthMm(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0.0;
            }

            if (candidate.WidthMm > 1e-6)
            {
                return candidate.WidthMm;
            }

            if (candidate.OpeningWidthMm > 1e-6)
            {
                return candidate.OpeningWidthMm;
            }

            return 0.0;
        }

        private static DoorCandidate MergeToDoubleDoor(DoorCandidate a, DoorCandidate b, XYZ leftEdge, XYZ rightEdge, double combinedWidthMm)
        {
            XYZ left = leftEdge ?? a.HingePoint;
            XYZ right = rightEdge ?? b.HingePoint;
            if (!IsLeftToRight(left, right))
            {
                XYZ tmp = left;
                left = right;
                right = tmp;
            }

            double widthMm = combinedWidthMm > 1e-6 ? combinedWidthMm : FtToMm(left.DistanceTo(right));
            XYZ center = Mid(left, right);
            List<int> segments = (a.SegmentIds ?? new List<int>())
                .Union(b.SegmentIds ?? new List<int>())
                .ToList();

            return new DoorCandidate
            {
                RuleSource = "R3",
                IsDoubleDoor = true,
                LeftEdgePoint = left,
                RightEdgePoint = right,
                CombinedWidthMm = widthMm,
                CombinedCenter = center,
                WidthMm = widthMm,
                OpeningWidthMm = widthMm,
                WidthSource = "Combined",
                CenterPoint = center,
                OpeningCenterPoint = center,
                HingePoint = center,
                MatchedWallId = a.MatchedWallId,
                MatchedWall = a.MatchedWall ?? b.MatchedWall,
                ProjectedPointOnWall = Mid(a.ProjectedPointOnWall, b.ProjectedPointOnWall) ?? a.ProjectedPointOnWall ?? b.ProjectedPointOnWall,
                DistToWallMm = Math.Min(a.DistToWallMm, b.DistToWallMm),
                SegmentIds = segments,
                UnmatchedReason = null
            };
        }

        private static bool IsLeftToRight(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return true;
            }

            if (Math.Abs(a.X - b.X) > 1e-6)
            {
                return a.X < b.X;
            }

            return a.Y < b.Y;
        }

        private static XYZ Mid(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double FtToMm(double ft)
        {
            return UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        }

        private static string FormatPointMm(XYZ p)
        {
            if (p == null)
            {
                return string.Empty;
            }

            return "("
                   + FtToMm(p.X).ToString("F1") + ","
                   + FtToMm(p.Y).ToString("F1") + ","
                   + FtToMm(p.Z).ToString("F1") + ")";
        }
    }
}
