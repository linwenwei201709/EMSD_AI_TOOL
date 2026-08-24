using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class DoorClosureBuilder
    {
        public static List<Line> BuildDoorClosureLines(Document doc, ElementId levelId, XYZ seedCenter, double windowSizeMm)
        {
            List<Line> result = new List<Line>();
            if (doc == null || seedCenter == null)
            {
                return result;
            }

            double half = UnitUtils.ConvertToInternalUnits(Math.Max(1000.0, windowSizeMm) * 0.5, UnitTypeId.Millimeters);
            Outline outline = new Outline(
                new XYZ(seedCenter.X - half, seedCenter.Y - half, seedCenter.Z - 1000),
                new XYZ(seedCenter.X + half, seedCenter.Y + half, seedCenter.Z + 1000));
            BoundingBoxIntersectsFilter boxFilter = new BoundingBoxIntersectsFilter(outline);

            IEnumerable<FamilyInstance> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .WherePasses(boxFilter)
                .Cast<FamilyInstance>();

            foreach (FamilyInstance door in doors)
            {
                Wall hostWall = door.Host as Wall;
                if (hostWall == null || !IsOnLevel(hostWall, levelId))
                {
                    continue;
                }

                LocationCurve hostCurve = hostWall.Location as LocationCurve;
                Line hostLine = hostCurve != null ? hostCurve.Curve as Line : null;
                LocationPoint doorPoint = door.Location as LocationPoint;
                if (hostLine == null || doorPoint == null || doorPoint.Point == null)
                {
                    continue;
                }

                // Build a short segment across wall thickness to close the opening during flood fill.
                XYZ wallDir = hostLine.Direction.Normalize();
                XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
                double closeLength = Math.Max(hostWall.Width, UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters));
                XYZ p0 = doorPoint.Point - normal.Multiply(closeLength * 0.5);
                XYZ p1 = doorPoint.Point + normal.Multiply(closeLength * 0.5);
                if (p0.DistanceTo(p1) > 1e-6)
                {
                    result.Add(Line.CreateBound(p0, p1));
                }
            }

            return result;
        }

        private static bool IsOnLevel(Wall wall, ElementId levelId)
        {
            if (wall == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            Parameter p = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId id = p != null ? p.AsElementId() : ElementId.InvalidElementId;
            return id != null && id.IntegerValue == levelId.IntegerValue;
        }
    }
}
