using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Columns
{
    public sealed class RevitWallDirectionProvider : IWallDirectionProvider
    {
        private const double MmPerFt = 304.8;
        private readonly List<Line> _wallCenterLines;

        public RevitWallDirectionProvider(Document doc)
        {
            _wallCenterLines = new List<Line>();
            if (doc == null)
            {
                return;
            }

            _wallCenterLines = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Select(x => x.Location as LocationCurve)
                .Where(x => x != null)
                .Select(x => x.Curve as Line)
                .Where(x => x != null)
                .ToList();
        }

        public XYZ TryGetNearestDirection(XYZ point, double radiusMm)
        {
            if (point == null || _wallCenterLines.Count == 0)
            {
                return null;
            }

            double radiusFt = radiusMm / MmPerFt;
            double nearest = double.MaxValue;
            Line nearestLine = null;
            foreach (Line line in _wallCenterLines)
            {
                XYZ projected = ProjectPointToLine(point, line);
                double dist = projected.DistanceTo(point);
                if (dist < nearest)
                {
                    nearest = dist;
                    nearestLine = line;
                }
            }

            if (nearestLine == null || nearest > radiusFt)
            {
                return null;
            }

            XYZ dir = nearestLine.GetEndPoint(1) - nearestLine.GetEndPoint(0);
            XYZ dir2 = new XYZ(dir.X, dir.Y, 0);
            if (dir2.GetLength() <= 1e-9)
            {
                return null;
            }

            return dir2.Normalize();
        }

        private static XYZ ProjectPointToLine(XYZ point, Line line)
        {
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ v = p1 - p0;
            double len2 = v.DotProduct(v);
            if (len2 <= 1e-9)
            {
                return p0;
            }

            double t = (point - p0).DotProduct(v) / len2;
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return p0 + v.Multiply(t);
        }
    }
}
