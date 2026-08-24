using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class GridContourExtractor
    {
        public static List<XYZ> ExtractOuterContour(bool[,] visited, double minX, double minY, double cellFt, double z)
        {
            if (visited == null)
            {
                return new List<XYZ>();
            }

            int width = visited.GetLength(0);
            int height = visited.GetLength(1);
            if (width <= 0 || height <= 0)
            {
                return new List<XYZ>();
            }

            List<Edge> edges = BuildBoundaryEdges(visited, width, height);
            if (edges.Count == 0)
            {
                return new List<XYZ>();
            }

            List<List<IntPoint>> loops = BuildLoops(edges);
            if (loops.Count == 0)
            {
                return new List<XYZ>();
            }

            List<IntPoint> outerLoop = loops
                .OrderByDescending(x => Math.Abs(ComputeSignedArea(x)))
                .FirstOrDefault() ?? new List<IntPoint>();
            outerLoop = RemoveCollinearPoints(outerLoop);
            if (outerLoop.Count < 4)
            {
                return new List<XYZ>();
            }

            if (ComputeSignedArea(outerLoop) > 0)
            {
                outerLoop.Reverse();
            }

            return outerLoop
                .Select(p => new XYZ(minX + (p.X * cellFt), minY + (p.Y * cellFt), z))
                .ToList();
        }

        // Build directed boundary edges around each visited cell in clockwise orientation.
        private static List<Edge> BuildBoundaryEdges(bool[,] visited, int width, int height)
        {
            List<Edge> edges = new List<Edge>();
            for (int ix = 0; ix < width; ix++)
            {
                for (int iy = 0; iy < height; iy++)
                {
                    if (!visited[ix, iy])
                    {
                        continue;
                    }

                    bool n = iy + 1 < height && visited[ix, iy + 1];
                    bool e = ix + 1 < width && visited[ix + 1, iy];
                    bool s = iy - 1 >= 0 && visited[ix, iy - 1];
                    bool w = ix - 1 >= 0 && visited[ix - 1, iy];

                    if (!n)
                    {
                        edges.Add(new Edge(new IntPoint(ix, iy + 1), new IntPoint(ix + 1, iy + 1)));
                    }

                    if (!e)
                    {
                        edges.Add(new Edge(new IntPoint(ix + 1, iy + 1), new IntPoint(ix + 1, iy)));
                    }

                    if (!s)
                    {
                        edges.Add(new Edge(new IntPoint(ix + 1, iy), new IntPoint(ix, iy)));
                    }

                    if (!w)
                    {
                        edges.Add(new Edge(new IntPoint(ix, iy), new IntPoint(ix, iy + 1)));
                    }
                }
            }

            return edges;
        }

        private static List<List<IntPoint>> BuildLoops(List<Edge> edges)
        {
            Dictionary<IntPoint, List<int>> byStart = new Dictionary<IntPoint, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                if (!byStart.TryGetValue(edges[i].Start, out List<int> list))
                {
                    list = new List<int>();
                    byStart[edges[i].Start] = list;
                }

                list.Add(i);
            }

            bool[] used = new bool[edges.Count];
            List<List<IntPoint>> loops = new List<List<IntPoint>>();
            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                List<IntPoint> loop = new List<IntPoint>();
                int current = i;
                int guard = 0;
                while (current >= 0 && !used[current] && guard < edges.Count + 5)
                {
                    used[current] = true;
                    Edge edge = edges[current];
                    loop.Add(edge.Start);

                    IntPoint nextStart = edge.End;
                    current = -1;
                    if (byStart.TryGetValue(nextStart, out List<int> candidates))
                    {
                        int next = candidates.Where(x => !used[x]).DefaultIfEmpty(-1).First();
                        if (next >= 0)
                        {
                            current = next;
                        }
                    }

                    guard++;
                }

                if (loop.Count >= 3)
                {
                    if (loop[0] != loop[loop.Count - 1])
                    {
                        loop.Add(loop[0]);
                    }

                    loops.Add(loop);
                }
            }

            return loops;
        }

        // Remove redundant vertices so display lines stay clean.
        private static List<IntPoint> RemoveCollinearPoints(List<IntPoint> loop)
        {
            if (loop == null || loop.Count < 4)
            {
                return loop ?? new List<IntPoint>();
            }

            List<IntPoint> pts = new List<IntPoint>(loop);
            if (pts[0] != pts[pts.Count - 1])
            {
                pts.Add(pts[0]);
            }

            List<IntPoint> simplified = new List<IntPoint> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                IntPoint prev = simplified[simplified.Count - 1];
                IntPoint curr = pts[i];
                IntPoint next = pts[i + 1];
                if (IsCollinear(prev, curr, next))
                {
                    continue;
                }

                simplified.Add(curr);
            }

            if (simplified.Count == 0 || simplified[0] != pts[pts.Count - 1])
            {
                simplified.Add(pts[pts.Count - 1]);
            }

            return simplified.Count >= 4 ? simplified : pts;
        }

        private static bool IsCollinear(IntPoint a, IntPoint b, IntPoint c)
        {
            int abx = b.X - a.X;
            int aby = b.Y - a.Y;
            int bcx = c.X - b.X;
            int bcy = c.Y - b.Y;
            return (abx == 0 && bcx == 0) || (aby == 0 && bcy == 0);
        }

        private static double ComputeSignedArea(IList<IntPoint> loop)
        {
            if (loop == null || loop.Count < 3)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                IntPoint a = loop[i];
                IntPoint b = loop[i + 1];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            return sum * 0.5;
        }

        private struct Edge
        {
            public Edge(IntPoint start, IntPoint end)
            {
                Start = start;
                End = end;
            }

            public IntPoint Start { get; }

            public IntPoint End { get; }
        }

        private struct IntPoint : IEquatable<IntPoint>
        {
            public IntPoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }

            public int Y { get; }

            public bool Equals(IntPoint other)
            {
                return X == other.X && Y == other.Y;
            }

            public override bool Equals(object obj)
            {
                return obj is IntPoint other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Y;
                }
            }

            public static bool operator ==(IntPoint left, IntPoint right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(IntPoint left, IntPoint right)
            {
                return !left.Equals(right);
            }
        }
    }
}
