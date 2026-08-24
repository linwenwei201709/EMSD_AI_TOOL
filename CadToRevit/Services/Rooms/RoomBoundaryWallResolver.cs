using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomBoundaryWallResolver
    {
        private const double SearchPaddingMm = 500.0;
        private const double MatchExtraToleranceMm = 300.0;
        private const double MinOverlapMm = 300.0;
        private const double ParallelAngleToleranceDeg = 8.0;

        public static List<RoomBoundaryWallReference> Resolve(
            Document doc,
            ElementId levelId,
            IList<XYZ> loopPoints)
        {
            List<RoomBoundaryWallReference> result = new List<RoomBoundaryWallReference>();
            if (doc == null || loopPoints == null || loopPoints.Count < 2)
            {
                return result;
            }

            List<XYZ> points = loopPoints.Where(p => p != null).ToList();
            if (points.Count < 2)
            {
                return result;
            }

            List<BoundaryEdgeInfo> edges = BuildEdges(points);
            if (edges.Count == 0)
            {
                return result;
            }

            Outline outline = BuildOutline(points, SearchPaddingMm);
            BoundingBoxIntersectsFilter boxFilter = new BoundingBoxIntersectsFilter(outline);

            List<WallMatchInfo> matches = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WherePasses(boxFilter)
                .Cast<Wall>()
                .Where(w => IsOnLevel(w, levelId))
                .Select(w => TryMatchWall(w, edges))
                .Where(m => m != null)
                .OrderBy(m => m.EdgeIndex)
                .ThenByDescending(m => m.OverlapFeet)
                .ToList();

            HashSet<int> usedWallIds = new HashSet<int>();
            int displayIndex = 1;
            foreach (WallMatchInfo match in matches)
            {
                int id = match.Wall.Id.IntegerValue;
                if (!usedWallIds.Add(id))
                {
                    continue;
                }

                LocationCurve locationCurve = match.Wall.Location as LocationCurve;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                double lengthMm = curve != null
                    ? UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters)
                    : 0.0;

                result.Add(new RoomBoundaryWallReference
                {
                    ElementId = id,
                    UniqueId = match.Wall.UniqueId ?? string.Empty,
                    DisplayName = "WALL-" + displayIndex.ToString("0000", CultureInfo.InvariantCulture),
                    RevitName = match.Wall.Name ?? string.Empty,
                    LengthMm = lengthMm
                });

                displayIndex++;
            }

            return result;
        }

        private static List<BoundaryEdgeInfo> BuildEdges(List<XYZ> points)
        {
            List<BoundaryEdgeInfo> edges = new List<BoundaryEdgeInfo>();
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                XYZ start = points[i];
                XYZ end = points[(i + 1) % count];
                if (start == null || end == null)
                {
                    continue;
                }

                XYZ delta = new XYZ(end.X - start.X, end.Y - start.Y, 0.0);
                double length = delta.GetLength();
                if (length <= 1e-6)
                {
                    continue;
                }

                XYZ direction = delta.Normalize();
                edges.Add(new BoundaryEdgeInfo
                {
                    Index = i,
                    Start = start,
                    End = end,
                    Direction = direction,
                    Length = length,
                    MidPoint = new XYZ((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5, (start.Z + end.Z) * 0.5)
                });
            }

            return edges;
        }

        private static Outline BuildOutline(List<XYZ> points, double paddingMm)
        {
            double padding = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, paddingMm), UnitTypeId.Millimeters);
            double minX = points.Min(p => p.X) - padding;
            double minY = points.Min(p => p.Y) - padding;
            double minZ = points.Min(p => p.Z) - 1000.0;
            double maxX = points.Max(p => p.X) + padding;
            double maxY = points.Max(p => p.Y) + padding;
            double maxZ = points.Max(p => p.Z) + 1000.0;
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        private static WallMatchInfo TryMatchWall(Wall wall, List<BoundaryEdgeInfo> edges)
        {
            if (wall == null || edges == null || edges.Count == 0)
            {
                return null;
            }

            LocationCurve locationCurve = wall.Location as LocationCurve;
            Line wallLine = locationCurve != null ? locationCurve.Curve as Line : null;
            if (wallLine == null)
            {
                return null;
            }

            XYZ wallStart = wallLine.GetEndPoint(0);
            XYZ wallEnd = wallLine.GetEndPoint(1);
            XYZ wallDelta = new XYZ(wallEnd.X - wallStart.X, wallEnd.Y - wallStart.Y, 0.0);
            double wallLength = wallDelta.GetLength();
            if (wallLength <= 1e-6)
            {
                return null;
            }

            XYZ wallDirection = wallDelta.Normalize();
            double cosTolerance = Math.Cos(ParallelAngleToleranceDeg * Math.PI / 180.0);
            double distanceTolerance = wall.Width * 0.5 + UnitUtils.ConvertToInternalUnits(MatchExtraToleranceMm, UnitTypeId.Millimeters);
            double minOverlap = UnitUtils.ConvertToInternalUnits(MinOverlapMm, UnitTypeId.Millimeters);

            WallMatchInfo best = null;
            foreach (BoundaryEdgeInfo edge in edges)
            {
                double parallel = Math.Abs(wallDirection.DotProduct(edge.Direction));
                if (parallel < cosTolerance)
                {
                    continue;
                }

                double distance = DistancePointToLine2D(edge.MidPoint, wallStart, wallDirection);
                if (distance > distanceTolerance)
                {
                    continue;
                }

                double overlap = ComputeOverlapAlongWall(wallStart, wallDirection, wallStart, wallEnd, edge.Start, edge.End);
                if (overlap < minOverlap)
                {
                    continue;
                }

                if (best == null || overlap > best.OverlapFeet)
                {
                    best = new WallMatchInfo
                    {
                        Wall = wall,
                        EdgeIndex = edge.Index,
                        OverlapFeet = overlap
                    };
                }
            }

            return best;
        }

        private static double DistancePointToLine2D(XYZ point, XYZ lineOrigin, XYZ lineDirection)
        {
            XYZ vector = new XYZ(point.X - lineOrigin.X, point.Y - lineOrigin.Y, 0.0);
            double cross = Math.Abs(vector.X * lineDirection.Y - vector.Y * lineDirection.X);
            return cross;
        }

        private static double ComputeOverlapAlongWall(
            XYZ origin,
            XYZ direction,
            XYZ wallStart,
            XYZ wallEnd,
            XYZ edgeStart,
            XYZ edgeEnd)
        {
            double wallA = ProjectScalar(wallStart, origin, direction);
            double wallB = ProjectScalar(wallEnd, origin, direction);
            double edgeA = ProjectScalar(edgeStart, origin, direction);
            double edgeB = ProjectScalar(edgeEnd, origin, direction);

            double wallMin = Math.Min(wallA, wallB);
            double wallMax = Math.Max(wallA, wallB);
            double edgeMin = Math.Min(edgeA, edgeB);
            double edgeMax = Math.Max(edgeA, edgeB);

            double min = Math.Max(wallMin, edgeMin);
            double max = Math.Min(wallMax, edgeMax);
            return Math.Max(0.0, max - min);
        }

        private static double ProjectScalar(XYZ point, XYZ origin, XYZ direction)
        {
            return (point.X - origin.X) * direction.X + (point.Y - origin.Y) * direction.Y;
        }

        private static bool IsOnLevel(Wall wall, ElementId levelId)
        {
            if (wall == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId wallLevelId = parameter != null ? parameter.AsElementId() : ElementId.InvalidElementId;
            return wallLevelId != null && wallLevelId.IntegerValue == levelId.IntegerValue;
        }

        private sealed class BoundaryEdgeInfo
        {
            public int Index { get; set; }
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public XYZ Direction { get; set; }
            public double Length { get; set; }
            public XYZ MidPoint { get; set; }
        }

        private sealed class WallMatchInfo
        {
            public Wall Wall { get; set; }
            public int EdgeIndex { get; set; }
            public double OverlapFeet { get; set; }
        }
    }
}
