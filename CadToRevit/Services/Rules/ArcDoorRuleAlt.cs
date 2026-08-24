using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Rules
{
    /// <summary>
    /// Dedicated R3B rule for minimal arc-door symbols without wall-line crossing.
    /// This rule is an independent pipeline and does not supplement R3.
    /// </summary>
    public sealed class ArcDoorRuleAlt : IDoorCandidateRule
    {
        public string Name => "R3B";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            if (doorSegments == null || settings == null || !settings.EnableAltArcDoorRecognition)
            {
                return result;
            }

            List<CadSegment> arcs = doorSegments.Where(x => x != null && x.IsArc).ToList();
            List<CadSegment> lines = doorSegments.Where(x => x != null && !x.IsArc).ToList();
            if (arcs.Count == 0 || lines.Count == 0)
            {
                return result;
            }

            double minSweep = DegToRad(settings.ArcMinSweepDeg);
            double maxSweep = DegToRad(settings.ArcMaxSweepDeg);
            double minRadiusFt = MmToFt(settings.ArcMinRadiusMm);
            double maxRadiusFt = MmToFt(settings.ArcMaxRadiusMm);
            double lineSnapTolFt = MmToFt(settings.AltArcLineSnapTolMm);
            double projectionTolFt = MmToFt(settings.AltArcProjectionTolMm);
            double endpointSnapTolFt = MmToFt(settings.ArcEndpointSnapTolMm);

            foreach (CadSegment arc in arcs)
            {
                if (arc.RadiusFeet < minRadiusFt || arc.RadiusFeet > maxRadiusFt)
                {
                    continue;
                }

                if (arc.SweepAngleRad < minSweep || arc.SweepAngleRad > maxSweep)
                {
                    continue;
                }

                List<CadSegment> nearbyLines = FindNearbyLines(arc, lines, lineSnapTolFt);
                if (nearbyLines.Count == 0)
                {
                    continue;
                }

                CadSegment leafLine = FindSupportingLeafLine(arc, nearbyLines, endpointSnapTolFt, settings);
                if (leafLine == null)
                {
                    continue;
                }

                CadSegment supportLine;
                XYZ projectedStart;
                XYZ projectedEnd;
                XYZ leafBaseCenter = null;
                double openingWidthMm;
                bool resolvedByProjection = TryResolveOpeningByProjection(
                    arc,
                    nearbyLines,
                    projectionTolFt,
                    settings,
                    out supportLine,
                    out projectedStart,
                    out projectedEnd,
                    out openingWidthMm);
                if (!resolvedByProjection)
                {
                    if (!TryResolveSimpleOpeningByFreeEnds(
                        arc,
                        nearbyLines,
                        endpointSnapTolFt,
                        settings,
                        out leafLine,
                        out projectedStart,
                        out projectedEnd,
                        out leafBaseCenter,
                        out openingWidthMm))
                    {
                        continue;
                    }

                    supportLine = null;
                }

                XYZ arcMid = arc.MidPoint ?? Mid(arc.P0, arc.P1);
                XYZ openingCenter = Mid(projectedStart, projectedEnd);
                if (leafBaseCenter == null)
                {
                    leafBaseCenter = openingCenter;
                }
                XYZ wallDirHint = supportLine != null
                    ? Normalize2D(supportLine.P1 - supportLine.P0)
                    : Normalize2D(projectedEnd - projectedStart);

                result.Add(new DoorCandidate
                {
                    CenterPoint = openingCenter,
                    OpeningCenterPoint = openingCenter,
                    OpeningBaseStartPoint = projectedStart,
                    OpeningBaseEndPoint = projectedEnd,
                    DoorLeafBaseStart = projectedStart,
                    DoorLeafBaseEnd = projectedEnd,
                    DoorLeafBaseCenter = leafBaseCenter ?? openingCenter,
                    PreferredHostPoint = leafBaseCenter,
                    PreferOpeningBaseHost = true,
                    VirtualOpeningBaseStart = projectedStart,
                    VirtualOpeningBaseEnd = projectedEnd,
                    VirtualOpeningBaseCenter = leafBaseCenter,
                    VirtualOpeningWidthMm = openingWidthMm,
                    PreferVirtualOpeningHost = true,
                    WidthMm = openingWidthMm,
                    OpeningWidthMm = openingWidthMm,
                    RuleSource = Name,
                    SymbolFamilyKind = DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing,
                    SegmentIds = BuildSegmentIds(arc, supportLine, leafLine),
                    ArcRadiusMm = FtToMm(arc.RadiusFeet),
                    ArcSweepDeg = RadToDeg(arc.SweepAngleRad),
                    ArcMidPoint = arcMid,
                    WallDirHint = wallDirHint,
                    HingePoint = null,
                    LeafHinge = null,
                    LeafLatch = null,
                    LeafLineSegmentId = leafLine.SegmentId,
                    WidthSource = resolvedByProjection ? "AltArcProjection" : "FreeEndOpeningBase"
                });
            }

            return result;
        }

        private static List<CadSegment> FindNearbyLines(CadSegment arc, List<CadSegment> lines, double tolFt)
        {
            List<CadSegment> result = new List<CadSegment>();
            XYZ arcMid = arc.MidPoint ?? Mid(arc.P0, arc.P1);
            foreach (CadSegment line in lines)
            {
                if (line == null || line.P0 == null || line.P1 == null)
                {
                    continue;
                }

                if (MinDistanceToEndpoints(line, arc.P0, arc.P1, arcMid) <= tolFt)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static bool TryResolveOpeningByProjection(
            CadSegment arc,
            List<CadSegment> lines,
            double projectionTolFt,
            DoorDetectSettings settings,
            out CadSegment bestLine,
            out XYZ projectedStart,
            out XYZ projectedEnd,
            out double openingWidthMm)
        {
            bestLine = null;
            projectedStart = null;
            projectedEnd = null;
            openingWidthMm = 0.0;

            double bestScore = double.MaxValue;
            foreach (CadSegment line in lines)
            {
                if (!IsLengthInRange(line, settings))
                {
                    continue;
                }

                ProjectionData p0 = ProjectPointToLineSegment(arc.P0, line.P0, line.P1);
                ProjectionData p1 = ProjectPointToLineSegment(arc.P1, line.P0, line.P1);
                if (!p0.IsInsideSegment || !p1.IsInsideSegment)
                {
                    continue;
                }

                if (p0.DistanceFeet > projectionTolFt || p1.DistanceFeet > projectionTolFt)
                {
                    continue;
                }

                double widthMm = FtToMm(p0.ProjectedPoint.DistanceTo(p1.ProjectedPoint));
                if (widthMm < settings.DoorWidthMinMm || widthMm > settings.DoorWidthMaxMm)
                {
                    continue;
                }

                double score = p0.DistanceFeet + p1.DistanceFeet;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLine = line;
                    projectedStart = p0.ProjectedPoint;
                    projectedEnd = p1.ProjectedPoint;
                    openingWidthMm = widthMm;
                }
            }

            return bestLine != null;
        }

        private static CadSegment FindSupportingLeafLine(
            CadSegment arc,
            List<CadSegment> nearbyLines,
            double endpointSnapTolFt,
            DoorDetectSettings settings)
        {
            CadSegment best = null;
            double bestScore = double.MaxValue;
            XYZ arcMid = arc.MidPoint ?? Mid(arc.P0, arc.P1);

            foreach (CadSegment line in nearbyLines)
            {
                if (!IsLeafLengthInRange(line, settings))
                {
                    continue;
                }

                double nearStart = Math.Min(line.P0.DistanceTo(arc.P0), line.P1.DistanceTo(arc.P0));
                double nearEnd = Math.Min(line.P0.DistanceTo(arc.P1), line.P1.DistanceTo(arc.P1));
                double nearMid = PointToSegmentDistance(arcMid, line.P0, line.P1);

                bool touchingArcEndpoint = nearStart <= endpointSnapTolFt || nearEnd <= endpointSnapTolFt;
                if (!touchingArcEndpoint && nearMid > endpointSnapTolFt)
                {
                    continue;
                }

                double score = Math.Min(nearStart, nearEnd) + (0.2 * nearMid);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = line;
                }
            }

            return best;
        }

        private static bool TryResolveSimpleOpeningByFreeEnds(
            CadSegment arc,
            List<CadSegment> nearbyLines,
            double endpointSnapTolFt,
            DoorDetectSettings settings,
            out CadSegment bestLeafLine,
            out XYZ openingBaseStart,
            out XYZ openingBaseEnd,
            out XYZ openingCenter,
            out double widthMm)
        {
            bestLeafLine = null;
            openingBaseStart = null;
            openingBaseEnd = null;
            openingCenter = null;
            widthMm = 0.0;
            if (arc == null || nearbyLines == null || settings == null || arc.P0 == null || arc.P1 == null)
            {
                return false;
            }

            List<CadSegment> candidates = nearbyLines
                .Where(x => IsLeafLengthInRange(x, settings))
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            List<FreeEndCandidateInfo> candidateInfos = new List<FreeEndCandidateInfo>();
            foreach (CadSegment line in candidates)
            {
                XYZ arcConnectedEnd;
                XYZ arcFreeEnd;
                XYZ leafConnectedEnd;
                XYZ leafFreeEnd;
                if (!TryResolveConnectedAndFreeEnds(
                    arc,
                    line,
                    endpointSnapTolFt,
                    out arcConnectedEnd,
                    out arcFreeEnd,
                    out leafConnectedEnd,
                    out leafFreeEnd))
                {
                    continue;
                }

                double candidateWidthMm = FtToMm(arcFreeEnd.DistanceTo(leafFreeEnd));
                if (candidateWidthMm < settings.DoorWidthMinMm || candidateWidthMm > settings.DoorWidthMaxMm)
                {
                    continue;
                }

                candidateInfos.Add(new FreeEndCandidateInfo
                {
                    Line = line,
                    ArcConnectedEnd = arcConnectedEnd,
                    ArcFreeEnd = arcFreeEnd,
                    LeafConnectedEnd = leafConnectedEnd,
                    LeafFreeEnd = leafFreeEnd,
                    ConnectDistanceFt = arcConnectedEnd.DistanceTo(leafConnectedEnd),
                    WidthMm = candidateWidthMm,
                    LengthMm = FtToMm(line.P0.DistanceTo(line.P1))
                });
            }

            if (candidateInfos.Count == 0)
            {
                return false;
            }

            List<List<FreeEndCandidateInfo>> groups = BuildParallelLeafGroups(candidateInfos);
            if (groups.Count == 0)
            {
                return false;
            }

            int selectedGroupIndex = groups
                .Select((group, index) => new
                {
                    Group = group,
                    Index = index,
                    MinConnectDistanceFt = group.Min(x => x.ConnectDistanceFt)
                })
                .OrderBy(x => x.MinConnectDistanceFt)
                .ThenBy(x => x.Index)
                .First()
                .Index;

            List<FreeEndCandidateInfo> selectedGroup = groups[selectedGroupIndex];
            for (int i = 0; i < groups.Count; i++)
            {
                foreach (FreeEndCandidateInfo info in groups[i])
                {
                    info.GroupId = i + 1;
                    info.IsSameGroupAsSelected = i == selectedGroupIndex;
                }
            }

            FreeEndCandidateInfo selected = selectedGroup
                .OrderBy(x => x.WidthMm)
                .ThenBy(x => x.Line == null ? int.MaxValue : x.Line.SegmentId)
                .First();

            bestLeafLine = selected.Line;
            openingBaseStart = selected.ArcFreeEnd;
            openingBaseEnd = selected.LeafFreeEnd;
            openingCenter = Mid(selected.ArcFreeEnd, selected.LeafFreeEnd);
            widthMm = selected.WidthMm;

            DiagnosticRecorder.AppendDebug(
                "[R3BFreeEndLeafSelection] ArcSegmentId=" + arc.SegmentId +
                ", CandidateCount=" + candidateInfos.Count +
                ", Candidates=" + BuildFreeEndCandidateLog(candidateInfos) +
                ", SelectedLeafSegmentId=" + selected.Line.SegmentId +
                ", SelectedLeafP0=" + FormatPoint(selected.Line.P0) +
                ", SelectedLeafP1=" + FormatPoint(selected.Line.P1) +
                ", ArcConnectedEnd=" + FormatPoint(selected.ArcConnectedEnd) +
                ", ArcFreeEnd=" + FormatPoint(selected.ArcFreeEnd) +
                ", LeafConnectedEnd=" + FormatPoint(selected.LeafConnectedEnd) +
                ", LeafFreeEnd=" + FormatPoint(selected.LeafFreeEnd) +
                ", OpeningWidthMm=" + selected.WidthMm.ToString("F1"));

            return bestLeafLine != null && openingBaseStart != null && openingBaseEnd != null && openingCenter != null;
        }

        private sealed class FreeEndCandidateInfo
        {
            public CadSegment Line { get; set; }
            public XYZ ArcConnectedEnd { get; set; }
            public XYZ ArcFreeEnd { get; set; }
            public XYZ LeafConnectedEnd { get; set; }
            public XYZ LeafFreeEnd { get; set; }
            public double ConnectDistanceFt { get; set; }
            public double WidthMm { get; set; }
            public double LengthMm { get; set; }
            public int GroupId { get; set; }
            public bool IsSameGroupAsSelected { get; set; }
        }

        private static string BuildFreeEndCandidateLog(List<FreeEndCandidateInfo> infos)
        {
            if (infos == null || infos.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < infos.Count; i++)
            {
                FreeEndCandidateInfo info = infos[i];
                if (info == null || info.Line == null)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append("SegmentId=");
                sb.Append(info.Line.SegmentId);
                sb.Append(", GroupId=");
                sb.Append(info.GroupId);
                sb.Append(", IsSameGroup=");
                sb.Append(info.IsSameGroupAsSelected ? "true" : "false");
                sb.Append(", LengthMm=");
                sb.Append(info.LengthMm.ToString("F1"));
                sb.Append(", ConnectDistMm=");
                sb.Append(FtToMm(info.ConnectDistanceFt).ToString("F1"));
                sb.Append(", ArcFreeEnd=");
                sb.Append(FormatPoint(info.ArcFreeEnd));
                sb.Append(", LeafFreeEnd=");
                sb.Append(FormatPoint(info.LeafFreeEnd));
                sb.Append(", CandidateWidthMm=");
                sb.Append(info.WidthMm.ToString("F1"));
                sb.Append(", P0=");
                sb.Append(FormatPoint(info.Line.P0));
                sb.Append(", P1=");
                sb.Append(FormatPoint(info.Line.P1));
            }

            return sb.ToString();
        }

        private static List<List<FreeEndCandidateInfo>> BuildParallelLeafGroups(List<FreeEndCandidateInfo> infos)
        {
            List<List<FreeEndCandidateInfo>> groups = new List<List<FreeEndCandidateInfo>>();
            if (infos == null || infos.Count == 0)
            {
                return groups;
            }

            foreach (FreeEndCandidateInfo info in infos)
            {
                if (info == null || info.Line == null)
                {
                    continue;
                }

                List<FreeEndCandidateInfo> matchedGroup = null;
                foreach (List<FreeEndCandidateInfo> group in groups)
                {
                    if (group.Any(existing => AreParallelLeafLinesInSameGroup(existing, info)))
                    {
                        matchedGroup = group;
                        break;
                    }
                }

                if (matchedGroup == null)
                {
                    matchedGroup = new List<FreeEndCandidateInfo>();
                    groups.Add(matchedGroup);
                }

                matchedGroup.Add(info);
            }

            return groups;
        }

        private static bool AreParallelLeafLinesInSameGroup(FreeEndCandidateInfo a, FreeEndCandidateInfo b)
        {
            if (a == null || b == null || a.Line == null || b.Line == null)
            {
                return false;
            }

            XYZ dirA = Normalize2D(a.Line.P1 - a.Line.P0);
            XYZ dirB = Normalize2D(b.Line.P1 - b.Line.P0);
            if (dirA == null || dirB == null)
            {
                return false;
            }

            double parallelAbs = Math.Abs((dirA.X * dirB.X) + (dirA.Y * dirB.Y));
            if (parallelAbs < 0.98)
            {
                return false;
            }

            XYZ midA = Mid(a.Line.P0, a.Line.P1);
            XYZ midB = Mid(b.Line.P0, b.Line.P1);
            if (midA == null || midB == null)
            {
                return false;
            }

            XYZ perp = new XYZ(-dirA.Y, dirA.X, 0);
            double perpDistFt = Math.Abs(((midB.X - midA.X) * perp.X) + ((midB.Y - midA.Y) * perp.Y));
            double alongDistFt = Math.Abs(((midB.X - midA.X) * dirA.X) + ((midB.Y - midA.Y) * dirA.Y));
            double maxPerpDistFt = MmToFt(400.0);
            double maxAlongDistFt = MmToFt(1200.0);
            return perpDistFt <= maxPerpDistFt && alongDistFt <= maxAlongDistFt;
        }

        private static bool TryResolveConnectedAndFreeEnds(
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

            if (bestArcIndex < 0 || bestLeafIndex < 0)
            {
                return false;
            }

            if (bestPairDist > endpointSnapTolFt)
            {
                double arcToLeaf = Math.Min(PointToSegmentDistance(arcEnds[bestArcIndex], leafLine.P0, leafLine.P1), bestPairDist);
                double leafToArc = Math.Min(
                    Math.Min(leafEnds[bestLeafIndex].DistanceTo(arc.P0), leafEnds[bestLeafIndex].DistanceTo(arc.P1)),
                    bestPairDist);
                if (Math.Min(arcToLeaf, leafToArc) > endpointSnapTolFt)
                {
                    return false;
                }
            }

            arcConnectedEnd = arcEnds[bestArcIndex];
            arcFreeEnd = arcEnds[1 - bestArcIndex];
            leafConnectedEnd = leafEnds[bestLeafIndex];
            leafFreeEnd = leafEnds[1 - bestLeafIndex];
            return true;
        }

        private static IList<int> BuildSegmentIds(CadSegment arc, CadSegment support, CadSegment leaf)
        {
            return new[] { arc, support, leaf }
                .Where(x => x != null)
                .Select(x => x.SegmentId)
                .Distinct()
                .ToList();
        }

        private static bool IsLengthInRange(CadSegment line, DoorDetectSettings settings)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = FtToMm(line.P0.DistanceTo(line.P1));
            return lenMm >= settings.SegmentLengthMinMm && lenMm <= settings.SegmentLengthMaxMm;
        }

        private static bool IsLeafLengthInRange(CadSegment line, DoorDetectSettings settings)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = FtToMm(line.P0.DistanceTo(line.P1));
            return lenMm >= settings.ArcLeafLineMinLengthMm && lenMm <= settings.ArcLeafLineMaxLengthMm;
        }

        private static double MinDistanceToEndpoints(CadSegment line, XYZ p0, XYZ p1, XYZ pmid)
        {
            double d0 = PointToSegmentDistance(p0, line.P0, line.P1);
            double d1 = PointToSegmentDistance(p1, line.P0, line.P1);
            double d2 = PointToSegmentDistance(pmid, line.P0, line.P1);
            return Math.Min(d0, Math.Min(d1, d2));
        }

        private static double PointToSegmentDistance(XYZ point, XYZ a, XYZ b)
        {
            return ProjectPointToLineSegment(point, a, b).DistanceFeet;
        }

        private static ProjectionData ProjectPointToLineSegment(XYZ point, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double ab2 = ab.DotProduct(ab);
            if (ab2 <= 1e-9)
            {
                return new ProjectionData
                {
                    ProjectedPoint = a,
                    DistanceFeet = point.DistanceTo(a),
                    IsInsideSegment = false
                };
            }

            double t = (point - a).DotProduct(ab) / ab2;
            double clamped = Math.Max(0.0, Math.Min(1.0, t));
            XYZ projected = a + ab.Multiply(clamped);
            return new ProjectionData
            {
                ProjectedPoint = projected,
                DistanceFeet = point.DistanceTo(projected),
                IsInsideSegment = t >= -1e-6 && t <= 1.0 + 1e-6
            };
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
            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double MmToFt(double mm)
        {
            return mm / 304.8;
        }

        private static double FtToMm(double ft)
        {
            return UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return string.Empty;
            }

            return "(" + FtToMm(point.X).ToString("F1") + "," + FtToMm(point.Y).ToString("F1") + "," + FtToMm(point.Z).ToString("F1") + ")";
        }

        private static double DegToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        private static double RadToDeg(double rad)
        {
            return rad * 180.0 / Math.PI;
        }

        private sealed class ProjectionData
        {
            public XYZ ProjectedPoint { get; set; }
            public double DistanceFeet { get; set; }
            public bool IsInsideSegment { get; set; }
        }
    }
}
