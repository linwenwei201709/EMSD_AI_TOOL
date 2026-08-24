using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class Room3DVisualizationGeometryBuilder
    {
        internal static Solid BuildRoomRegionSolid(
            RoomSemanticRecord room,
            ElementId materialId,
            out string failStage,
            out string failReason)
        {
            failStage = string.Empty;
            failReason = string.Empty;
            if (!TryBuildRegionBoundary(room, out List<XYZ> boundary, out double baseZFeet, out failStage, out failReason))
            {
                return null;
            }

            return BuildExtrusionSolid(
                boundary,
                Room3DVisualizationConstants.RegionThicknessMm,
                materialId,
                out failStage,
                out failReason);
        }

        internal static List<Solid> BuildRoomMarkerSolids(
            XYZ center,
            ElementId materialId,
            out string failReason)
        {
            failReason = string.Empty;
            if (center == null)
            {
                failReason = "MarkerCenterNull";
                return new List<Solid>();
            }

            double outerFeet = Room3DVisualizationConstants.MarkerOuterSizeMm * Room3DVisualizationConstants.MmToFeet;
            double barWidthFeet = Room3DVisualizationConstants.MarkerBarWidthMm * Room3DVisualizationConstants.MmToFeet;
            double thicknessMm = Room3DVisualizationConstants.MarkerThicknessMm;
            double halfLength = outerFeet * 0.5;
            double invSqrt2 = 1.0 / Math.Sqrt(2.0);

            XYZ dirA = new XYZ(invSqrt2, invSqrt2, 0.0);
            XYZ dirB = new XYZ(invSqrt2, -invSqrt2, 0.0);

            Solid barA = BuildMarkerBarSolid(center, dirA, halfLength, barWidthFeet, thicknessMm, materialId, out string barAReason);
            Solid barB = BuildMarkerBarSolid(center, dirB, halfLength, barWidthFeet, thicknessMm, materialId, out string barBReason);

            List<Solid> result = new List<Solid>();
            if (barA != null && barA.Faces != null && barA.Faces.Size > 0)
            {
                result.Add(barA);
            }

            if (barB != null && barB.Faces != null && barB.Faces.Size > 0)
            {
                result.Add(barB);
            }

            if (result.Count == 0)
            {
                failReason = "MarkerSolidFailed(" + barAReason + "|" + barBReason + ")";
            }

            return result;
        }

        internal static bool TryBuildRegionBoundary(
            RoomSemanticRecord room,
            out List<XYZ> boundary,
            out double baseZFeet,
            out string failStage,
            out string failReason)
        {
            boundary = new List<XYZ>();
            baseZFeet = 0.0;
            failStage = "PreCheck";
            failReason = string.Empty;

            if (room == null)
            {
                failReason = "RoomNull";
                return false;
            }

            if (room.LoopPoints == null || room.LoopPoints.Count < 4)
            {
                failReason = "LoopPointsTooFew";
                return false;
            }

            if (room.AreaM2 <= 0.0)
            {
                failReason = "AreaInvalid";
                return false;
            }

            if (room.CloseGapMm > Room3DVisualizationConstants.MaxCloseGapMm)
            {
                failReason = "CloseGapTooLarge(" + room.CloseGapMm.ToString("F1") + "mm)";
                return false;
            }

            baseZFeet = ResolveBaseZ(room);
            failStage = "Preprocess";
            List<XYZ> deDup = RemoveConsecutiveDuplicate(room.LoopPoints);
            if (deDup.Count < 3)
            {
                failReason = "DegeneratedAfterDedup";
                return false;
            }

            double minEdgeFeet = Room3DVisualizationConstants.MinEdgeLengthMm * Room3DVisualizationConstants.MmToFeet;
            deDup = RemoveShortStepPoints(deDup, minEdgeFeet);
            if (deDup.Count < 3)
            {
                failReason = "DegeneratedAfterShortEdgeFilter";
                return false;
            }

            failStage = "Closure";
            double zBase = baseZFeet;
            List<XYZ> flat = deDup.Select(p => new XYZ(p.X, p.Y, zBase)).ToList();
            if (flat[0].DistanceTo(flat[flat.Count - 1]) > minEdgeFeet)
            {
                flat.Add(new XYZ(flat[0].X, flat[0].Y, flat[0].Z));
            }

            if (flat.Count < 4)
            {
                failReason = "FlatPointsTooFew";
                return false;
            }

            failStage = "ProjectionValidate";
            int validEdges = 0;
            for (int i = 0; i < flat.Count - 1; i++)
            {
                if (flat[i].DistanceTo(flat[i + 1]) > minEdgeFeet)
                {
                    validEdges++;
                }
            }

            if (validEdges < 3)
            {
                failReason = "ValidEdgesTooFew";
                return false;
            }

            double absArea = Math.Abs(ComputePolygonArea(flat));
            if (absArea <= 1e-9)
            {
                failReason = "ProjectedAreaZero";
                return false;
            }

            boundary = flat;
            failStage = string.Empty;
            failReason = string.Empty;
            return true;
        }

        private static Solid BuildMarkerBarSolid(
            XYZ center,
            XYZ axis,
            double halfLengthFeet,
            double barWidthFeet,
            double thicknessMm,
            ElementId materialId,
            out string reason)
        {
            reason = string.Empty;
            try
            {
                XYZ dir = axis.Normalize();
                XYZ perp = new XYZ(-dir.Y, dir.X, 0.0);
                XYZ p0 = center - (dir * halfLengthFeet) - (perp * (barWidthFeet * 0.5));
                XYZ p1 = center + (dir * halfLengthFeet) - (perp * (barWidthFeet * 0.5));
                XYZ p2 = center + (dir * halfLengthFeet) + (perp * (barWidthFeet * 0.5));
                XYZ p3 = center - (dir * halfLengthFeet) + (perp * (barWidthFeet * 0.5));
                List<XYZ> rect = new List<XYZ> { p0, p1, p2, p3, p0 };
                return BuildExtrusionSolid(rect, thicknessMm, materialId, out _, out reason);
            }
            catch (Exception ex)
            {
                reason = "MarkerBarException(" + ex.Message + ")";
                return null;
            }
        }

        private static double ResolveBaseZ(RoomSemanticRecord room)
        {
            if (room != null && room.BBox != null && room.BBox.Min != null)
            {
                return room.BBox.Min.Z;
            }

            if (room != null && room.Centroid != null)
            {
                return room.Centroid.Z;
            }

            return 0.0;
        }

        private static Solid BuildExtrusionSolid(
            List<XYZ> boundary,
            double thicknessMm,
            ElementId materialId,
            out string failStage,
            out string failReason)
        {
            failStage = "CurveLoop";
            failReason = string.Empty;
            if (boundary == null || boundary.Count < 4)
            {
                failReason = "BoundaryTooFew";
                return null;
            }

            try
            {
                List<Curve> curves = new List<Curve>();
                for (int i = 0; i < boundary.Count - 1; i++)
                {
                    XYZ a = boundary[i];
                    XYZ b = boundary[i + 1];
                    if (a == null || b == null || a.DistanceTo(b) <= 1e-9)
                    {
                        continue;
                    }

                    curves.Add(Line.CreateBound(a, b));
                }

                if (curves.Count < 3)
                {
                    failReason = "CurvesTooFew";
                    return null;
                }

                CurveLoop loop = CurveLoop.Create(curves);
                IList<CurveLoop> loops = new List<CurveLoop> { loop };
                double thicknessFeet = thicknessMm * Room3DVisualizationConstants.MmToFeet;
                SolidOptions options = new SolidOptions(materialId, ElementId.InvalidElementId);
                failStage = "Solid";
                return GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, thicknessFeet, options);
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                return null;
            }
        }

        private static List<XYZ> RemoveConsecutiveDuplicate(List<XYZ> points)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p == null)
                {
                    continue;
                }

                if (result.Count == 0 || result[result.Count - 1].DistanceTo(p) > 1e-9)
                {
                    result.Add(new XYZ(p.X, p.Y, p.Z));
                }
            }

            return result;
        }

        private static List<XYZ> RemoveShortStepPoints(List<XYZ> points, double minEdgeFeet)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p == null)
                {
                    continue;
                }

                if (result.Count == 0 || result[result.Count - 1].DistanceTo(p) > minEdgeFeet)
                {
                    result.Add(p);
                }
            }

            if (result.Count >= 2 && result[0].DistanceTo(result[result.Count - 1]) <= minEdgeFeet)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static double ComputePolygonArea(List<XYZ> points)
        {
            if (points == null || points.Count < 4)
            {
                return 0.0;
            }

            double area = 0.0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                area += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y;
            }

            return area * 0.5;
        }
    }
}
