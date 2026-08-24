using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Services.Rooms
{
    public static class PointInPolygon
    {
        public static bool ContainsPointXY(IList<XYZ> polygon, XYZ point)
        {
            if (polygon == null || point == null || polygon.Count < 4)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                XYZ pi = polygon[i];
                XYZ pj = polygon[j];
                bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                                  (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-12) + pi.X);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
