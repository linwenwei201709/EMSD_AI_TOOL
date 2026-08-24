using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rules
{
    /// <summary>
    /// R3C dedicated rule for complex standard no-wall-crossing door symbols.
    /// Phase-1 implementation focuses on component clustering + main opening baseline extraction.
    /// </summary>
    public sealed class ArcDoorRuleComplexNoWall : IDoorCandidateRule
    {
        public string Name => "R3C";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            if (doorSegments == null || settings == null)
            {
                return result;
            }

            List<CadSegment> segments = doorSegments.Where(x => x != null).ToList();
            if (segments.Count == 0 || !segments.Any(x => x.IsArc))
            {
                return result;
            }

            int componentId = 1;
            foreach (List<CadSegment> component in BuildComponents(segments, settings))
            {
                List<CadSegment> arcs = component.Where(x => x != null && x.IsArc).ToList();
                List<CadSegment> lines = component.Where(x => x != null && !x.IsArc && x.P0 != null && x.P1 != null).ToList();
                if (arcs.Count == 0 || lines.Count == 0)
                {
                    componentId++;
                    continue;
                }

                CadSegment openingBase = SelectMainOpeningBaseLine(lines, arcs, settings, componentId);
                if (openingBase == null || openingBase.P0 == null || openingBase.P1 == null)
                {
                    componentId++;
                    continue;
                }

                double widthMm = FtToMm(openingBase.P0.DistanceTo(openingBase.P1));
                double maxWidthMm = Math.Max(settings.DoorWidthMaxMm * 2.2, settings.DoorWidthMinMm + 200.0);
                if (widthMm < settings.DoorWidthMinMm || widthMm > maxWidthMm)
                {
                    continue;
                }

                XYZ openingDir = Normalize2D(openingBase.P1 - openingBase.P0);
                if (openingDir == null)
                {
                    continue;
                }

                XYZ openingCenter = Mid(openingBase.P0, openingBase.P1);
                bool isDouble = arcs.Count >= 2;

                DoorCandidate candidate = new DoorCandidate
                {
                    CenterPoint = openingCenter,
                    OpeningCenterPoint = openingCenter,
                    OpeningBaseStartPoint = openingBase.P0,
                    OpeningBaseEndPoint = openingBase.P1,
                    VirtualOpeningBaseStart = openingBase.P0,
                    VirtualOpeningBaseEnd = openingBase.P1,
                    VirtualOpeningBaseCenter = openingCenter,
                    WidthMm = widthMm,
                    OpeningWidthMm = widthMm,
                    VirtualOpeningWidthMm = widthMm,
                    RuleSource = Name,
                    SymbolFamilyKind = DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing,
                    SegmentIds = component.Where(x => x != null).Select(x => x.SegmentId).Distinct().ToList(),
                    WallDirHint = openingDir,
                    WidthSource = "R3CMainOpeningBase",
                    PreferOpeningBaseHost = true,
                    PreferVirtualOpeningHost = true,
                    IsDoubleDoor = isDouble,
                    LeftEdgePoint = openingBase.P0,
                    RightEdgePoint = openingBase.P1,
                    CombinedWidthMm = isDouble ? widthMm : 0.0,
                    CombinedCenter = isDouble ? openingCenter : null,
                    ArcRadiusMm = ResolveRepresentativeRadiusMm(arcs),
                    ArcSweepDeg = ResolveRepresentativeSweepDeg(arcs),
                    ArcMidPoint = ResolveRepresentativeArcMid(arcs)
                };
                result.Add(candidate);
                componentId++;
            }

            return result;
        }

        private static List<List<CadSegment>> BuildComponents(List<CadSegment> segments, DoorDetectSettings settings)
        {
            List<List<CadSegment>> components = new List<List<CadSegment>>();
            if (segments == null || segments.Count == 0)
            {
                return components;
            }

            double endpointTolMm = Math.Max(180.0, settings.DoorClusterTolMinMm * 0.6);
            double endpointTolFt = MmToFt(endpointTolMm);
            bool[] visited = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<CadSegment> component = new List<CadSegment>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    CadSegment current = segments[idx];
                    component.Add(current);

                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (visited[j])
                        {
                            continue;
                        }

                        CadSegment next = segments[j];
                        if (AreSegmentsNear(current, next, endpointTolFt))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                if (component.Count > 0)
                {
                    components.Add(component);
                }
            }

            return components;
        }

        private static bool AreSegmentsNear(CadSegment a, CadSegment b, double tolFt)
        {
            if (a == null || b == null)
            {
                return false;
            }

            foreach (XYZ pa in GetAnchorPoints(a))
            {
                foreach (XYZ pb in GetAnchorPoints(b))
                {
                    if (pa != null && pb != null && pa.DistanceTo(pb) <= tolFt)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<XYZ> GetAnchorPoints(CadSegment s)
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

        private static CadSegment SelectMainOpeningBaseLine(List<CadSegment> lines, List<CadSegment> arcs, DoorDetectSettings settings, int componentId)
        {
            CadSegment best = null;
            double bestScore = double.MaxValue;
            double minLenMm = Math.Max(settings.DoorWidthMinMm * 0.6, settings.SegmentLengthMinMm);
            double maxLenMm = Math.Max(settings.DoorWidthMaxMm * 2.2, settings.SegmentLengthMaxMm);
            CadSegment mainArc = arcs
                .Where(x => x != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .FirstOrDefault();
            XYZ chordDir = Normalize2D((mainArc?.P1) - (mainArc?.P0));
            XYZ arcCenter = mainArc?.Center;
            double projTolFt = MmToFt(180.0);

            foreach (CadSegment line in lines)
            {
                if (line == null || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                double lenMm = FtToMm(line.P0.DistanceTo(line.P1));
                if (lenMm < minLenMm || lenMm > maxLenMm)
                {
                    continue;
                }

                XYZ mid = Mid(line.P0, line.P1);
                if (mid == null)
                {
                    continue;
                }

                XYZ lineDir = Normalize2D(line.P1 - line.P0);
                if (lineDir == null)
                {
                    continue;
                }

                bool crossesOpening = false;
                bool singleSideAttached = false;
                bool radialLike = false;
                double chordAlign = chordDir == null ? 0.0 : Math.Abs(Dot2D(lineDir, chordDir));
                if (mainArc != null)
                {
                    ProjectionData p0 = ProjectPointToLineSegment(mainArc.P0, line.P0, line.P1);
                    ProjectionData p1 = ProjectPointToLineSegment(mainArc.P1, line.P0, line.P1);
                    crossesOpening = p0.IsInsideSegment && p1.IsInsideSegment &&
                                     p0.DistanceFeet <= projTolFt &&
                                     p1.DistanceFeet <= projTolFt;

                    double dStart = Math.Min(line.P0.DistanceTo(mainArc.P0), line.P1.DistanceTo(mainArc.P0));
                    double dEnd = Math.Min(line.P0.DistanceTo(mainArc.P1), line.P1.DistanceTo(mainArc.P1));
                    bool nearStart = dStart <= projTolFt;
                    bool nearEnd = dEnd <= projTolFt;
                    singleSideAttached = nearStart ^ nearEnd;
                }

                if (arcCenter != null)
                {
                    XYZ radial = Normalize2D(mid - arcCenter);
                    if (radial != null)
                    {
                        radialLike = Math.Abs(Dot2D(lineDir, radial)) >= 0.85;
                    }
                }

                double nearArcMidFt = arcs
                    .Select(x => x?.MidPoint ?? Mid(x?.P0, x?.P1))
                    .Where(x => x != null)
                    .DefaultIfEmpty()
                    .Select(x => x == null ? double.MaxValue : x.DistanceTo(mid))
                    .Min();

                double nearArcEndpointFt = arcs
                    .SelectMany(x => new[] { x?.P0, x?.P1 })
                    .Where(x => x != null)
                    .DefaultIfEmpty()
                    .Select(x => x == null ? double.MaxValue : Math.Min(line.P0.DistanceTo(x), line.P1.DistanceTo(x)))
                    .Min();

                // Prefer door-closing main base semantics over side-edge prominence.
                double score = (nearArcMidFt * 0.65) + (nearArcEndpointFt * 0.35);
                if (crossesOpening)
                {
                    score -= MmToFt(Math.Min(lenMm, 1200.0)) * 0.08;
                }

                // Strongly suppress single-side attached edge lines in single-arc components.
                if (arcs.Count == 1 && singleSideAttached && !crossesOpening)
                {
                    score += MmToFt(450.0);
                }

                // Opening base should be close to chord direction, not radial side-edge direction.
                if (chordDir != null)
                {
                    if (chordAlign < 0.40)
                    {
                        score += MmToFt(300.0);
                    }
                    else if (chordAlign > 0.80)
                    {
                        score -= MmToFt(80.0);
                    }
                }

                if (arcs.Count == 1 && radialLike && !crossesOpening)
                {
                    score += MmToFt(220.0);
                }

                DiagnosticRecorder.AppendDebug(
                    "[R3COpeningBaseCandidate] ComponentId=" + componentId +
                    ", SegmentId=" + line.SegmentId +
                    ", LenMm=" + lenMm.ToString("F1") +
                    ", ChordAlign=" + chordAlign.ToString("F3") +
                    ", NearArcMidMm=" + FtToMm(nearArcMidFt).ToString("F1") +
                    ", NearArcEndpointMm=" + FtToMm(nearArcEndpointFt).ToString("F1") +
                    ", CrossesOpening=" + crossesOpening +
                    ", SingleSideAttached=" + singleSideAttached +
                    ", RadialLike=" + radialLike +
                    ", Score=" + score.ToString("F4"));

                if (score < bestScore)
                {
                    bestScore = score;
                    best = line;
                }
            }

            if (best != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[R3COpeningBaseSelected] ComponentId=" + componentId +
                    ", SegmentId=" + best.SegmentId +
                    ", Score=" + bestScore.ToString("F4") +
                    ", Start=" + FormatPoint(best.P0) +
                    ", End=" + FormatPoint(best.P1));
            }

            return best;
        }

        private static string FormatPoint(XYZ p)
        {
            if (p == null)
            {
                return "(null)";
            }

            return "(" + p.X.ToString("F3") + "," + p.Y.ToString("F3") + "," + p.Z.ToString("F3") + ")";
        }

        private sealed class ProjectionData
        {
            public XYZ ProjectedPoint { get; set; }
            public bool IsInsideSegment { get; set; }
            public double DistanceFeet { get; set; }
        }

        private static ProjectionData ProjectPointToLineSegment(XYZ point, XYZ segStart, XYZ segEnd)
        {
            ProjectionData data = new ProjectionData
            {
                ProjectedPoint = null,
                IsInsideSegment = false,
                DistanceFeet = double.MaxValue
            };

            if (point == null || segStart == null || segEnd == null)
            {
                return data;
            }

            XYZ v = segEnd - segStart;
            double vv = Dot3D(v, v);
            if (vv < 1e-12)
            {
                data.ProjectedPoint = segStart;
                data.IsInsideSegment = true;
                data.DistanceFeet = point.DistanceTo(segStart);
                return data;
            }

            double tRaw = Dot3D(point - segStart, v) / vv;
            double t = Math.Max(0.0, Math.Min(1.0, tRaw));
            XYZ projected = segStart + v.Multiply(t);
            data.ProjectedPoint = projected;
            data.IsInsideSegment = tRaw >= 0.0 && tRaw <= 1.0;
            data.DistanceFeet = projected == null ? double.MaxValue : projected.DistanceTo(point);
            return data;
        }

        private static double Dot2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }

            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static double Dot3D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0.0;
            }

            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        private static double ResolveRepresentativeRadiusMm(List<CadSegment> arcs)
        {
            CadSegment main = arcs?
                .Where(x => x != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .ThenByDescending(x => x.RadiusFeet)
                .FirstOrDefault();
            return main == null ? 0.0 : FtToMm(main.RadiusFeet);
        }

        private static double ResolveRepresentativeSweepDeg(List<CadSegment> arcs)
        {
            CadSegment main = arcs?
                .Where(x => x != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .FirstOrDefault();
            return main == null ? 0.0 : RadToDeg(main.SweepAngleRad);
        }

        private static XYZ ResolveRepresentativeArcMid(List<CadSegment> arcs)
        {
            CadSegment main = arcs?
                .Where(x => x != null)
                .OrderByDescending(x => Math.Abs(x.SweepAngleRad))
                .FirstOrDefault();
            if (main == null)
            {
                return null;
            }

            return main.MidPoint ?? Mid(main.P0, main.P1);
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

        private static XYZ Mid(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;
    }
}
