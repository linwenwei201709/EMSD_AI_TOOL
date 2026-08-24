using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class ModelFloodFillService
    {
        private const int MinVisitedCellCount = 4;

        public sealed class FloodFillResult
        {
            public bool Success { get; set; }
            public string Reason { get; set; }
            public double AreaM2 { get; set; }
            public XYZ Centroid { get; set; }
            public BoundingBoxXYZ BBox { get; set; }
            public List<XYZ> Polygon { get; set; } = new List<XYZ>();
        }

        public static FloodFillResult DetectRoomPolygon(
            XYZ seed,
            IList<Line> boundaryLines,
            double windowSizeMm,
            double cellSizeMm = 150.0)
        {
            FloodFillResult result = new FloodFillResult();
            if (seed == null)
            {
                result.Reason = "Seed is null.";
                return result;
            }

            double halfFt = UnitUtils.ConvertToInternalUnits(Math.Max(1000.0, windowSizeMm) * 0.5, UnitTypeId.Millimeters);
            double cellFt = UnitUtils.ConvertToInternalUnits(Math.Max(80.0, cellSizeMm), UnitTypeId.Millimeters);
            int gridCount = Math.Max(20, (int)Math.Ceiling((halfFt * 2.0) / cellFt));
            int[,] mask = new int[gridCount, gridCount];

            double minX = seed.X - halfFt;
            double minY = seed.Y - halfFt;
            // Rasterize boundary segments into blocked cells.
            foreach (Line line in boundaryLines ?? new List<Line>())
            {
                if (line == null)
                {
                    continue;
                }

                XYZ a = line.GetEndPoint(0);
                XYZ b = line.GetEndPoint(1);
                double len = a.DistanceTo(b);
                int steps = Math.Max(2, (int)Math.Ceiling(len / (cellFt * 0.5)));
                for (int i = 0; i <= steps; i++)
                {
                    double t = i / (double)steps;
                    double x = a.X + ((b.X - a.X) * t);
                    double y = a.Y + ((b.Y - a.Y) * t);
                    int ix = (int)Math.Floor((x - minX) / cellFt);
                    int iy = (int)Math.Floor((y - minY) / cellFt);
                    if (ix >= 0 && ix < gridCount && iy >= 0 && iy < gridCount)
                    {
                        mask[ix, iy] = 1;
                    }
                }
            }

            int sx = (int)Math.Floor((seed.X - minX) / cellFt);
            int sy = (int)Math.Floor((seed.Y - minY) / cellFt);
            if (sx < 0 || sy < 0 || sx >= gridCount || sy >= gridCount)
            {
                result.Reason = "Seed out of local window.";
                return result;
            }

            if (mask[sx, sy] == 1)
            {
                result.Reason = "Seed is on blocked boundary.";
                return result;
            }

            bool[,] visited = new bool[gridCount, gridCount];
            Queue<(int X, int Y)> q = new Queue<(int X, int Y)>();
            q.Enqueue((sx, sy));
            visited[sx, sy] = true;
            int count = 0;
            long sumX = 0;
            long sumY = 0;
            int minIx = sx;
            int maxIx = sx;
            int minIy = sy;
            int maxIy = sy;
            bool touchesWindowBoundary = false;
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            while (q.Count > 0)
            {
                (int X, int Y) node = q.Dequeue();
                count++;
                sumX += node.X;
                sumY += node.Y;
                if (node.X == 0 || node.Y == 0 || node.X == gridCount - 1 || node.Y == gridCount - 1)
                {
                    touchesWindowBoundary = true;
                }

                if (node.X < minIx) minIx = node.X;
                if (node.X > maxIx) maxIx = node.X;
                if (node.Y < minIy) minIy = node.Y;
                if (node.Y > maxIy) maxIy = node.Y;

                for (int k = 0; k < 4; k++)
                {
                    int nx = node.X + dx[k];
                    int ny = node.Y + dy[k];
                    if (nx < 0 || ny < 0 || nx >= gridCount || ny >= gridCount)
                    {
                        continue;
                    }

                    if (visited[nx, ny] || mask[nx, ny] == 1)
                    {
                        continue;
                    }

                    visited[nx, ny] = true;
                    q.Enqueue((nx, ny));
                }
            }

            if (count < MinVisitedCellCount)
            {
                result.Reason = "Flood fill area too small.";
                return result;
            }

            if (touchesWindowBoundary)
            {
                result.Reason = "FloodFillLeak";
                return result;
            }

            List<XYZ> polygon = GridContourExtractor.ExtractOuterContour(visited, minX, minY, cellFt, seed.Z);
            if (polygon == null || polygon.Count < 4)
            {
                result.Reason = "ContourExtractionFailed";
                return result;
            }

            double cxFt = minX + (((sumX / (double)count) + 0.5) * cellFt);
            double cyFt = minY + (((sumY / (double)count) + 0.5) * cellFt);
            result.Centroid = new XYZ(cxFt, cyFt, seed.Z);
            result.AreaM2 = ComputePolygonAreaM2(polygon);

            result.BBox = BuildBoundingBox(polygon, seed.Z);
            result.Polygon = polygon;
            result.Success = true;
            return result;
        }

        private static BoundingBoxXYZ BuildBoundingBox(IList<XYZ> polygon, double z)
        {
            if (polygon == null || polygon.Count == 0)
            {
                return null;
            }

            double minX = polygon.Min(x => x.X);
            double minY = polygon.Min(x => x.Y);
            double maxX = polygon.Max(x => x.X);
            double maxY = polygon.Max(x => x.Y);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, z),
                Max = new XYZ(maxX, maxY, z)
            };
        }

        // Compute area from extracted polygon instead of fill-cell count for better geometric accuracy.
        private static double ComputePolygonAreaM2(IList<XYZ> polygon)
        {
            if (polygon == null || polygon.Count < 4)
            {
                return 0.0;
            }

            double signedAreaFt2 = 0.0;
            for (int i = 0; i < polygon.Count - 1; i++)
            {
                XYZ a = polygon[i];
                XYZ b = polygon[i + 1];
                signedAreaFt2 += (a.X * b.Y) - (b.X * a.Y);
            }

            double areaFt2 = Math.Abs(signedAreaFt2) * 0.5;
            return UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
        }
    }
}
