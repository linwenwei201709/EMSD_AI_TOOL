using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class BeamCreatorService
    {
        private sealed class BeamCreateStats
        {
            public int RawSegments { get; set; }
            public int AfterLengthFilter { get; set; }
            public int AfterMerge { get; set; }
            public int Created { get; set; }
            public int SkippedArcs { get; set; }
            public int SkippedShort { get; set; }
        }

        public static int CreateByRawLayer(
            Document doc,
            IReadOnlyList<CadSegment> sourceSegments,
            string rawLayerName,
            AdvancedSettingsRow settings,
            FamilySymbol beamSymbol,
            Level level,
            List<string> failureMessages)
        {
            return CreateByRawLayerWithResult(
                doc,
                sourceSegments,
                rawLayerName,
                settings,
                beamSymbol,
                level,
                failureMessages).CreatedCount;
        }

        public static BeamCreateResult CreateByRawLayerWithResult(
            Document doc,
            IReadOnlyList<CadSegment> sourceSegments,
            string rawLayerName,
            AdvancedSettingsRow settings,
            FamilySymbol beamSymbol,
            Level level,
            List<string> failureMessages)
        {
            BeamCreateResult result = new BeamCreateResult();
            if (doc == null || sourceSegments == null || string.IsNullOrWhiteSpace(rawLayerName) || beamSymbol == null || level == null)
            {
                return result;
            }

            BeamCreateStats stats = new BeamCreateStats();
            bool allowArc = settings != null && settings.BeamAllowArc.HasValue && settings.BeamAllowArc.Value;
            bool enableMerge = settings == null || !settings.BeamEnableMergeCollinear.HasValue || settings.BeamEnableMergeCollinear.Value;
            double minLengthMm = settings != null && settings.BeamMinLengthMm.HasValue && settings.BeamMinLengthMm.Value > 0
                ? settings.BeamMinLengthMm.Value
                : 800.0;
            double endpointTolMm = settings != null && settings.BeamEndpointMergeTolMm.HasValue && settings.BeamEndpointMergeTolMm.Value > 0
                ? settings.BeamEndpointMergeTolMm.Value
                : 10.0;
            double angleTolDeg = settings != null && settings.BeamParallelAngleTolDeg.HasValue && settings.BeamParallelAngleTolDeg.Value > 0
                ? settings.BeamParallelAngleTolDeg.Value
                : 3.0;
            double offsetMm = settings != null && settings.BeamElevationOffsetMm.HasValue
                ? settings.BeamElevationOffsetMm.Value
                : 3000.0;

            List<CadSegment> layerSegments = sourceSegments
                .Where(x => x != null && string.Equals(x.RawLayerName ?? string.Empty, rawLayerName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            stats.RawSegments = layerSegments.Count;

            double minLengthFt = UnitUtils.ConvertToInternalUnits(minLengthMm, UnitTypeId.Millimeters);
            List<Curve> curves = new List<Curve>();
            foreach (CadSegment segment in layerSegments)
            {
                if (segment == null || segment.P0 == null || segment.P1 == null)
                {
                    continue;
                }

                if (segment.IsArc && !allowArc)
                {
                    stats.SkippedArcs++;
                    continue;
                }

                double lengthFt = segment.P0.DistanceTo(segment.P1);
                if (lengthFt < minLengthFt)
                {
                    stats.SkippedShort++;
                    continue;
                }

                Curve curve = TryBuildCurve(segment, allowArc);
                if (curve == null)
                {
                    continue;
                }

                curves.Add(curve);
            }

            stats.AfterLengthFilter = curves.Count;
            if (enableMerge)
            {
                curves = MergeCollinearLines(curves, endpointTolMm, angleTolDeg);
            }

            stats.AfterMerge = curves.Count;
            if (curves.Count == 0)
            {
                WriteStats(rawLayerName, stats);
                return result;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Create Beams"))
                {
                    tx.Start();
                    if (!beamSymbol.IsActive)
                    {
                        beamSymbol.Activate();
                        doc.Regenerate();
                    }

                    foreach (Curve curve in curves)
                    {
                        if (curve == null)
                        {
                            continue;
                        }

                        try
                        {
                            FamilyInstance fi = doc.Create.NewFamilyInstance(
                                curve,
                                beamSymbol,
                                level,
                                StructuralType.Beam);
                            if (fi != null)
                            {
                                TryApplyBeamOffset(fi, offsetMm, rawLayerName, failureMessages);
                                stats.Created++;
                                result.CreatedElementIds.Add(fi.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (failureMessages != null && failureMessages.Count < 30)
                            {
                                failureMessages.Add("Beam create failed: " + ex.Message);
                            }

                            if (result.Errors.Count < 30)
                            {
                                result.Errors.Add("Beam create failed: " + ex.Message);
                            }
                        }
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                if (failureMessages != null && failureMessages.Count < 30)
                {
                    failureMessages.Add("Beam transaction failed: " + ex.Message);
                }

                if (result.Errors.Count < 30)
                {
                    result.Errors.Add("Beam transaction failed: " + ex.Message);
                }
            }

            WriteStats(rawLayerName, stats);
            result.CreatedCount = stats.Created;
            return result;
        }

        private static Curve TryBuildCurve(CadSegment segment, bool allowArc)
        {
            if (segment == null)
            {
                return null;
            }

            if (segment.IsArc && allowArc)
            {
                try
                {
                    if (segment.MidPoint != null)
                    {
                        return Arc.Create(segment.P0, segment.P1, segment.MidPoint);
                    }
                }
                catch
                {
                }
            }

            try
            {
                return Line.CreateBound(segment.P0, segment.P1);
            }
            catch
            {
                return null;
            }
        }

        private static List<Curve> MergeCollinearLines(List<Curve> source, double endpointTolMm, double angleTolDeg)
        {
            List<Curve> current = (source ?? new List<Curve>()).Where(x => x != null).ToList();
            if (current.Count <= 1)
            {
                return current;
            }

            double tolFt = UnitUtils.ConvertToInternalUnits(Math.Max(endpointTolMm, 1.0), UnitTypeId.Millimeters);
            double angTol = Math.Max(angleTolDeg, 0.1);
            bool changed = true;
            int guard = 0;
            while (changed && guard < 2000)
            {
                guard++;
                changed = false;
                for (int i = 0; i < current.Count; i++)
                {
                    Line a = current[i] as Line;
                    if (a == null)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < current.Count; j++)
                    {
                        Line b = current[j] as Line;
                        if (b == null)
                        {
                            continue;
                        }

                        Line merged;
                        if (!TryMergeTwoLines(a, b, tolFt, angTol, out merged))
                        {
                            continue;
                        }

                        current[i] = merged;
                        current.RemoveAt(j);
                        changed = true;
                        break;
                    }

                    if (changed)
                    {
                        break;
                    }
                }
            }

            return current;
        }

        private static bool TryMergeTwoLines(Line a, Line b, double endpointTolFt, double angleTolDeg, out Line merged)
        {
            merged = null;
            if (a == null || b == null)
            {
                return false;
            }

            XYZ da = a.Direction;
            XYZ db = b.Direction;
            if (da == null || db == null)
            {
                return false;
            }

            double angle = da.AngleTo(db) * 180.0 / Math.PI;
            angle = Math.Min(angle, Math.Abs(180.0 - angle));
            if (angle > angleTolDeg)
            {
                return false;
            }

            if (!HasNearbyEndpoint(a, b, endpointTolFt))
            {
                return false;
            }

            XYZ origin = a.GetEndPoint(0);
            XYZ axis = a.Direction;
            List<XYZ> points = new List<XYZ>
            {
                a.GetEndPoint(0),
                a.GetEndPoint(1),
                b.GetEndPoint(0),
                b.GetEndPoint(1)
            };

            // 共线性校验：四个点到基线的距离都应接近 0。
            if (points.Any(p => DistanceToLine(p, origin, axis) > endpointTolFt))
            {
                return false;
            }

            XYZ minPoint = points.OrderBy(p => axis.DotProduct(p - origin)).First();
            XYZ maxPoint = points.OrderByDescending(p => axis.DotProduct(p - origin)).First();
            if (minPoint.DistanceTo(maxPoint) <= endpointTolFt)
            {
                return false;
            }

            try
            {
                merged = Line.CreateBound(minPoint, maxPoint);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasNearbyEndpoint(Line a, Line b, double tolFt)
        {
            XYZ a0 = a.GetEndPoint(0);
            XYZ a1 = a.GetEndPoint(1);
            XYZ b0 = b.GetEndPoint(0);
            XYZ b1 = b.GetEndPoint(1);
            return a0.DistanceTo(b0) <= tolFt ||
                   a0.DistanceTo(b1) <= tolFt ||
                   a1.DistanceTo(b0) <= tolFt ||
                   a1.DistanceTo(b1) <= tolFt;
        }

        private static double DistanceToLine(XYZ point, XYZ linePoint, XYZ direction)
        {
            if (point == null || linePoint == null || direction == null)
            {
                return double.MaxValue;
            }

            XYZ v = point - linePoint;
            XYZ proj = direction.Multiply(v.DotProduct(direction));
            return (v - proj).GetLength();
        }

        private static void TryApplyBeamOffset(FamilyInstance fi, double offsetMm, string rawLayerName, List<string> failureMessages)
        {
            double offsetFt = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
            if (TrySetDouble(fi, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, offsetFt))
            {
                TrySetDouble(fi, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, offsetFt);
                return;
            }

            if (TrySetDouble(fi, BuiltInParameter.Z_OFFSET_VALUE, offsetFt))
            {
                return;
            }

            if (TrySetDouble(fi, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, offsetFt))
            {
                return;
            }

            string msg = "Beam offset parameter not found, layer=" + (rawLayerName ?? string.Empty);
            DiagnosticRecorder.AppendDebug("[BeamOffset] " + msg);
            if (failureMessages != null && failureMessages.Count < 30)
            {
                failureMessages.Add(msg);
            }
        }

        private static bool TrySetDouble(Element element, BuiltInParameter bip, double value)
        {
            if (element == null)
            {
                return false;
            }

            try
            {
                Parameter p = element.get_Parameter(bip);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double)
                {
                    return false;
                }

                p.Set(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteStats(string rawLayerName, BeamCreateStats stats)
        {
            DiagnosticRecorder.AppendDebug("Beam Layer: " + (rawLayerName ?? string.Empty));
            DiagnosticRecorder.AppendDebug("Raw segments: " + stats.RawSegments);
            DiagnosticRecorder.AppendDebug("After length filter: " + stats.AfterLengthFilter);
            DiagnosticRecorder.AppendDebug("After merge: " + stats.AfterMerge);
            DiagnosticRecorder.AppendDebug("Created beams: " + stats.Created);
            DiagnosticRecorder.AppendDebug("Skipped arcs: " + stats.SkippedArcs);
            DiagnosticRecorder.AppendDebug("Skipped short: " + stats.SkippedShort);
        }
    }
}
