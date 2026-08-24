using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class DoorOpeningWidthResolver
    {
        /// <summary>
        /// Resolve opening width from candidate geometry first.
        /// </summary>
        public static bool TryResolveOpeningWidthMm(
            Document doc,
            DoorCandidate candidate,
            Wall hostWall,
            IEnumerable<Wall> hostWalls,
            out double openingWidthMm,
            out XYZ openingCenter,
            out string reason)
        {
            openingWidthMm = 0.0;
            openingCenter = null;
            reason = "NoCandidate";
            if (candidate == null)
            {
                return false;
            }

            XYZ doorCenter = candidate.OpeningCenterPoint ?? candidate.CenterPoint ?? candidate.HingePoint;
            if (doorCenter == null)
            {
                reason = "DoorCenterMissing";
                return false;
            }

            Line hostLine;
            if (!TryGetWallLine(hostWall, out hostLine))
            {
                reason = "HostWallLineMissing";
                return false;
            }

            IList<Tuple<double, double>> intervals = BuildCollinearWallIntervals(hostLine, hostWall, hostWalls);
            if (intervals.Count < 1)
            {
                reason = "NoCollinearWalls";
                return false;
            }

            double c = hostLine.Project(doorCenter)?.Parameter ?? double.NaN;
            if (double.IsNaN(c) || double.IsInfinity(c))
            {
                reason = "CenterProjectFailed";
                return false;
            }

            intervals = MergeIntervals(intervals, 1e-6);
            Tuple<double, double> left = null;
            Tuple<double, double> right = null;
            foreach (Tuple<double, double> interval in intervals)
            {
                if (interval.Item2 <= c)
                {
                    left = interval;
                    continue;
                }

                if (interval.Item1 >= c)
                {
                    right = interval;
                    break;
                }

                reason = "CenterInsideWallInterval";
                return false;
            }

            if (left == null || right == null)
            {
                reason = "GapBoundsMissing";
                return false;
            }

            double gapFeet = right.Item1 - left.Item2;
            if (gapFeet <= 1e-9)
            {
                reason = "GapNotPositive";
                return false;
            }

            openingWidthMm = UnitUtils.ConvertFromInternalUnits(gapFeet, UnitTypeId.Millimeters);
            double centerParam = (left.Item2 + right.Item1) * 0.5;
            openingCenter = hostLine.Evaluate(centerParam, true);
            reason = "WallGap";
            return true;
        }

        // Build projected intervals for walls that are collinear with host wall.
        private static IList<Tuple<double, double>> BuildCollinearWallIntervals(Line hostLine, Wall hostWall, IEnumerable<Wall> hostWalls)
        {
            List<Tuple<double, double>> intervals = new List<Tuple<double, double>>();
            if (hostLine == null)
            {
                return intervals;
            }

            XYZ d = hostLine.Direction.Normalize();
            IEnumerable<Wall> source = (hostWalls ?? Enumerable.Empty<Wall>()).Concat(new[] { hostWall }).Where(x => x != null);
            HashSet<int> seen = new HashSet<int>();
            foreach (Wall w in source)
            {
                if (!seen.Add(w.Id.IntegerValue))
                {
                    continue;
                }

                Line wl;
                if (!TryGetWallLine(w, out wl))
                {
                    continue;
                }

                XYZ wd = wl.Direction.Normalize();
                double parallel = Math.Abs(wd.DotProduct(d));
                if (parallel < 0.9999)
                {
                    continue;
                }

                XYZ s = wl.GetEndPoint(0);
                XYZ e = wl.GetEndPoint(1);
                XYZ ps = hostLine.Project(s)?.XYZPoint;
                XYZ pe = hostLine.Project(e)?.XYZPoint;
                if (ps == null || pe == null)
                {
                    continue;
                }

                if (ps.DistanceTo(s) > 0.05 || pe.DistanceTo(e) > 0.05)
                {
                    continue;
                }

                double ts = hostLine.Project(s).Parameter;
                double te = hostLine.Project(e).Parameter;
                double lo = Math.Min(ts, te);
                double hi = Math.Max(ts, te);
                if (hi - lo > 1e-9)
                {
                    intervals.Add(Tuple.Create(lo, hi));
                }
            }

            return intervals;
        }

        private static IList<Tuple<double, double>> MergeIntervals(IList<Tuple<double, double>> raw, double tol)
        {
            List<Tuple<double, double>> merged = new List<Tuple<double, double>>();
            if (raw == null || raw.Count == 0)
            {
                return merged;
            }

            foreach (Tuple<double, double> seg in raw.OrderBy(x => x.Item1))
            {
                if (merged.Count == 0)
                {
                    merged.Add(Tuple.Create(seg.Item1, seg.Item2));
                    continue;
                }

                Tuple<double, double> last = merged[merged.Count - 1];
                if (seg.Item1 <= last.Item2 + tol)
                {
                    merged[merged.Count - 1] = Tuple.Create(last.Item1, Math.Max(last.Item2, seg.Item2));
                    continue;
                }

                merged.Add(Tuple.Create(seg.Item1, seg.Item2));
            }

            return merged;
        }

        private static bool TryGetWallLine(Wall wall, out Line line)
        {
            line = null;
            if (wall == null)
            {
                return false;
            }

            LocationCurve loc = wall.Location as LocationCurve;
            line = loc?.Curve as Line;
            return line != null;
        }
    }
}
