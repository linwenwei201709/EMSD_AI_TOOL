using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Rules
{
    public class ArcDoorRule : IDoorCandidateRule
    {
        public string Name => "R3";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            if (doorSegments == null || settings == null)
            {
                return result;
            }

            List<CadSegment> arcs = doorSegments.Where(x => x != null && x.IsArc).ToList();
            List<CadSegment> lines = doorSegments.Where(x => x != null && !x.IsArc).ToList();

            double minSweep = DegToRad(settings.ArcMinSweepDeg);
            double maxSweep = DegToRad(settings.ArcMaxSweepDeg);
            double minRadiusFt = MmToFt(settings.ArcMinRadiusMm);
            double maxRadiusFt = MmToFt(settings.ArcMaxRadiusMm);
            double snapTolFt = MmToFt(settings.ArcEndpointSnapTolMm);
            double leafLenMinMm = settings.ArcLeafLineMinLengthMm;
            double leafLenMaxMm = settings.ArcLeafLineMaxLengthMm;

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

                XYZ hinge = arc.Center;
                XYZ arcMid = arc.MidPoint ?? Mid(arc.P0, arc.P1);
                List<CadSegment> nearStart = FindNearLines(lines, arc.P0, snapTolFt);
                List<CadSegment> nearEnd = FindNearLines(lines, arc.P1, snapTolFt);
                CadSegment startLine;
                CadSegment endLine;
                XYZ wallDirHint;
                bool hasWallDirHint = TryResolveWallDirFromPair(
                    nearStart,
                    nearEnd,
                    settings,
                    out startLine,
                    out endLine,
                    out wallDirHint);

                CadSegment leafLine = null;
                XYZ leafHinge = null;
                XYZ leafLatch = null;
                XYZ openingCenter = null;
                double widthMm = 0.0;
                string widthSource = string.Empty;

                XYZ arcConnectedEnd;
                XYZ arcFreeEnd;
                XYZ leafConnectedEnd;
                XYZ leafFreeEnd;
                if (!TryResolveSimpleOpeningByFreeEnds(
                    arc,
                    lines,
                    snapTolFt,
                    leafLenMinMm,
                    leafLenMaxMm,
                    out leafLine,
                    out arcConnectedEnd,
                    out arcFreeEnd,
                    out leafConnectedEnd,
                    out leafFreeEnd,
                    out openingCenter,
                    out widthMm))
                {
                    // R3 must not fall back to legacy hinge-only candidates.
                    continue;
                }

                leafHinge = leafConnectedEnd;
                leafLatch = leafFreeEnd;
                widthSource = "FreeEndOpening";

                XYZ center = openingCenter ?? arcMid;
                DoorCandidate candidate = new DoorCandidate
                {
                    CenterPoint = center,
                    WidthMm = widthMm,
                    RuleSource = Name,
                    SymbolFamilyKind = DoorSymbolFamilyKind.StandardArcDoor,
                    SegmentIds = BuildSegmentIds(arc, leafLine, startLine, endLine),
                    ArcRadiusMm = FtToMm(arc.RadiusFeet),
                    ArcSweepDeg = RadToDeg(arc.SweepAngleRad),
                    HingePoint = hinge,
                    LeafHinge = leafHinge,
                    LeafLatch = leafLatch,
                    OpeningBaseStartPoint = arcFreeEnd,
                    OpeningBaseEndPoint = leafFreeEnd,
                    OpeningCenterPoint = openingCenter,
                    PreferOpeningBaseHost = arcFreeEnd != null && leafFreeEnd != null,
                    PreferredHostPoint = openingCenter,
                    LeafLineSegmentId = leafLine == null ? 0 : leafLine.SegmentId,
                    ArcMidPoint = arcMid,
                    WallDirHint = wallDirHint,
                    WidthSource = widthSource
                };
                result.Add(candidate);
            }

            return result;
        }

        private static bool TryResolveWallDirFromPair(
            List<CadSegment> nearStart,
            List<CadSegment> nearEnd,
            DoorDetectSettings settings,
            out CadSegment startLine,
            out CadSegment endLine,
            out XYZ wallDirHint)
        {
            wallDirHint = null;
            bool ok = TryFindBestEndpointPair(nearStart, nearEnd, settings, out startLine, out endLine);
            if (!ok || startLine == null)
            {
                return false;
            }

            wallDirHint = Normalize2D(startLine.P1 - startLine.P0);
            return wallDirHint != null;
        }

        private static bool TryFindBestEndpointPair(
            List<CadSegment> nearStart,
            List<CadSegment> nearEnd,
            DoorDetectSettings settings,
            out CadSegment startLine,
            out CadSegment endLine)
        {
            startLine = null;
            endLine = null;
            double bestScore = double.MaxValue;
            double parallelTol = settings.ArcPairLineParallelTolDeg;

            foreach (CadSegment a in nearStart)
            {
                foreach (CadSegment b in nearEnd)
                {
                    if (!IsLengthInRange(a, settings) || !IsLengthInRange(b, settings))
                    {
                        continue;
                    }

                    double angle = AngleDeg(a, b);
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

        private static double ResolveSign(XYZ hinge, XYZ arcMid, XYZ wallDir)
        {
            if (hinge == null || arcMid == null || wallDir == null)
            {
                return 1.0;
            }

            double dot = (arcMid - hinge).DotProduct(wallDir);
            if (Math.Abs(dot) < 1e-9)
            {
                return 1.0;
            }

            return dot >= 0.0 ? 1.0 : -1.0;
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

        private static double AngleDeg(CadSegment a, CadSegment b)
        {
            XYZ da = a.P1 - a.P0;
            XYZ db = b.P1 - b.P0;
            if (da.GetLength() < 1e-9 || db.GetLength() < 1e-9)
            {
                return 180.0;
            }

            XYZ va = da.Normalize();
            XYZ vb = db.Normalize();
            double dot = Clamp(va.DotProduct(vb), -1.0, 1.0);
            return RadToDeg(Math.Acos(dot));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static List<CadSegment> FindNearLines(List<CadSegment> lines, XYZ point, double tolFt)
        {
            List<CadSegment> result = new List<CadSegment>();
            foreach (CadSegment line in lines)
            {
                double d0 = line.P0.DistanceTo(point);
                double d1 = line.P1.DistanceTo(point);
                if (Math.Min(d0, d1) <= tolFt)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static CadSegment FindBestLeafLine(
            List<CadSegment> lines,
            CadSegment arc,
            double hingeTolFt,
            double minLenMm,
            double maxLenMm)
        {
            FreeEndCandidateInfo selected;
            return TrySelectBestFreeEndCandidate(lines, arc, MmToFt(ArcEndpointSnapFallbackMm), minLenMm, maxLenMm, out selected)
                ? selected.Line
                : null;
        }

        private const double ArcEndpointSnapFallbackMm = 120.0;

        private static bool TryResolveSimpleOpeningByFreeEnds(
            CadSegment arc,
            List<CadSegment> lines,
            double endpointSnapTolFt,
            double minLenMm,
            double maxLenMm,
            out CadSegment bestLeafLine,
            out XYZ arcConnectedEnd,
            out XYZ arcFreeEnd,
            out XYZ leafConnectedEnd,
            out XYZ leafFreeEnd,
            out XYZ openingCenter,
            out double widthMm)
        {
            bestLeafLine = null;
            arcConnectedEnd = null;
            arcFreeEnd = null;
            leafConnectedEnd = null;
            leafFreeEnd = null;
            openingCenter = null;
            widthMm = 0.0;
            if (arc == null || lines == null || arc.P0 == null || arc.P1 == null)
            {
                return false;
            }

            FreeEndCandidateInfo selected;
            List<FreeEndCandidateInfo> candidateInfos;
            if (!TrySelectBestFreeEndCandidate(lines, arc, endpointSnapTolFt, minLenMm, maxLenMm, out selected, out candidateInfos))
            {
                return false;
            }

            bestLeafLine = selected.Line;
            arcConnectedEnd = selected.ArcConnectedEnd;
            arcFreeEnd = selected.ArcFreeEnd;
            leafConnectedEnd = selected.LeafConnectedEnd;
            leafFreeEnd = selected.LeafFreeEnd;
            openingCenter = Mid(selected.ArcFreeEnd, selected.LeafFreeEnd);
            widthMm = selected.WidthMm;

            DiagnosticRecorder.AppendDebug(
                "[R3FreeEndLeafSelection] ArcSegmentId=" + arc.SegmentId +
                ", CandidateCount=" + candidateInfos.Count +
                ", Candidates=" + BuildFreeEndCandidateLog(candidateInfos) +
                ", SelectedLeafSegmentId=" + selected.Line.SegmentId +
                ", ArcConnectedEnd=" + FormatPoint(selected.ArcConnectedEnd) +
                ", ArcFreeEnd=" + FormatPoint(selected.ArcFreeEnd) +
                ", LeafConnectedEnd=" + FormatPoint(selected.LeafConnectedEnd) +
                ", LeafFreeEnd=" + FormatPoint(selected.LeafFreeEnd) +
                ", OpeningWidthMm=" + selected.WidthMm.ToString("F1"));

            return true;
        }

        private static bool TrySelectBestFreeEndCandidate(
            List<CadSegment> lines,
            CadSegment arc,
            double endpointSnapTolFt,
            double minLenMm,
            double maxLenMm,
            out FreeEndCandidateInfo selected)
        {
            List<FreeEndCandidateInfo> infos;
            return TrySelectBestFreeEndCandidate(lines, arc, endpointSnapTolFt, minLenMm, maxLenMm, out selected, out infos);
        }

        private static bool TrySelectBestFreeEndCandidate(
            List<CadSegment> lines,
            CadSegment arc,
            double endpointSnapTolFt,
            double minLenMm,
            double maxLenMm,
            out FreeEndCandidateInfo selected,
            out List<FreeEndCandidateInfo> candidateInfos)
        {
            selected = null;
            candidateInfos = new List<FreeEndCandidateInfo>();
            if (arc == null || lines == null || arc.P0 == null || arc.P1 == null)
            {
                return false;
            }

            foreach (CadSegment line in lines)
            {
                if (!IsLeafLengthInRange(line, minLenMm, maxLenMm))
                {
                    continue;
                }

                XYZ candidateArcConnectedEnd;
                XYZ candidateArcFreeEnd;
                XYZ candidateLeafConnectedEnd;
                XYZ candidateLeafFreeEnd;
                if (!TryResolveConnectedAndFreeEnds(
                    arc,
                    line,
                    endpointSnapTolFt,
                    out candidateArcConnectedEnd,
                    out candidateArcFreeEnd,
                    out candidateLeafConnectedEnd,
                    out candidateLeafFreeEnd))
                {
                    continue;
                }

                double candidateWidthMm = FtToMm(candidateArcFreeEnd.DistanceTo(candidateLeafFreeEnd));
                if (candidateWidthMm <= 1e-6)
                {
                    continue;
                }

                candidateInfos.Add(new FreeEndCandidateInfo
                {
                    Line = line,
                    ArcConnectedEnd = candidateArcConnectedEnd,
                    ArcFreeEnd = candidateArcFreeEnd,
                    LeafConnectedEnd = candidateLeafConnectedEnd,
                    LeafFreeEnd = candidateLeafFreeEnd,
                    ConnectDistanceFt = candidateArcConnectedEnd.DistanceTo(candidateLeafConnectedEnd),
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

            selected = selectedGroup
                .OrderBy(x => x.WidthMm)
                .ThenBy(x => x.Line == null ? int.MaxValue : x.Line.SegmentId)
                .First();

            return selected != null;
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

        private static bool IsLeafLengthInRange(CadSegment line, double minLenMm, double maxLenMm)
        {
            if (line == null || line.IsArc || line.P0 == null || line.P1 == null)
            {
                return false;
            }

            double lenMm = FtToMm(line.P0.DistanceTo(line.P1));
            return lenMm >= minLenMm && lenMm <= maxLenMm;
        }

        private static double PointToSegmentDistance(XYZ point, XYZ a, XYZ b)
        {
            if (point == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            XYZ ab = b - a;
            double len2 = ab.DotProduct(ab);
            if (len2 < 1e-12)
            {
                return point.DistanceTo(a);
            }

            double t = (point - a).DotProduct(ab) / len2;
            t = Clamp(t, 0.0, 1.0);
            XYZ projected = a + ab.Multiply(t);
            return point.DistanceTo(projected);
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return string.Empty;
            }

            return "(" + FtToMm(point.X).ToString("F1") + "," + FtToMm(point.Y).ToString("F1") + "," + FtToMm(point.Z).ToString("F1") + ")";
        }

        private static void ResolveLeafEndpoints(CadSegment line, XYZ hinge, out XYZ leafHinge, out XYZ leafLatch)
        {
            leafHinge = null;
            leafLatch = null;
            if (line == null || line.P0 == null || line.P1 == null || hinge == null)
            {
                return;
            }

            double d0 = line.P0.DistanceTo(hinge);
            double d1 = line.P1.DistanceTo(hinge);
            if (d0 <= d1)
            {
                leafHinge = line.P0;
                leafLatch = line.P1;
            }
            else
            {
                leafHinge = line.P1;
                leafLatch = line.P0;
            }
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

        private static IList<int> BuildSegmentIds(CadSegment arc, CadSegment leaf, CadSegment startLine, CadSegment endLine)
        {
            return new[] { arc }
                .Concat(leaf == null ? new List<CadSegment>() : new List<CadSegment> { leaf })
                .Concat(startLine == null ? new List<CadSegment>() : new List<CadSegment> { startLine })
                .Concat(endLine == null ? new List<CadSegment>() : new List<CadSegment> { endLine })
                .Where(x => x != null)
                .Select(x => x.SegmentId)
                .Distinct()
                .ToList();
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

        private static double DegToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        private static double RadToDeg(double rad)
        {
            return rad * 180.0 / Math.PI;
        }
    }
}
