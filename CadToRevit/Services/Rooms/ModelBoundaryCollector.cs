using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class ModelBoundaryCollector
    {
        public static List<Line> CollectBoundaryLines(Document doc, ElementId levelId, XYZ seedCenter, double windowSizeMm)
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

            // Collect model walls as primary enclosure boundaries near the seed.
            IEnumerable<Wall> walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WherePasses(boxFilter)
                .Cast<Wall>()
                .Where(x => IsOnLevel(x, levelId));
            foreach (Wall wall in walls)
            {
                LocationCurve lc = wall.Location as LocationCurve;
                Line line = lc != null ? lc.Curve as Line : null;
                if (line != null)
                {
                    result.Add(line);
                }
            }

            // Add column footprints. Columns can replace a short piece of wall at wall ends;
            // without these rectangle edges the flood-fill fallback can leak through column gaps.
            IEnumerable<Element> columns = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(boxFilter)
                .ToElements()
                .Where(x => IsColumnLike(x) && IsColumnOnLevel(x, doc, levelId));
            foreach (Element column in columns)
            {
                AddColumnFootprintLines(result, column, seedCenter.Z);
            }

            // Add room separation lines so user-authored partitions can close loops.
            IEnumerable<CurveElement> separators = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WherePasses(boxFilter)
                .Cast<CurveElement>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomSeparationLines);
            foreach (CurveElement c in separators)
            {
                Line line = c.GeometryCurve as Line;
                if (line != null)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static void AddColumnFootprintLines(List<Line> result, Element column, double z)
        {
            if (result == null || column == null)
            {
                return;
            }

            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            double minX = Math.Min(box.Min.X, box.Max.X);
            double minY = Math.Min(box.Min.Y, box.Max.Y);
            double maxX = Math.Max(box.Min.X, box.Max.X);
            double maxY = Math.Max(box.Min.Y, box.Max.Y);
            if ((maxX - minX) <= 1e-6 || (maxY - minY) <= 1e-6)
            {
                return;
            }

            XYZ a = new XYZ(minX, minY, z);
            XYZ b = new XYZ(maxX, minY, z);
            XYZ c = new XYZ(maxX, maxY, z);
            XYZ d = new XYZ(minX, maxY, z);
            result.Add(Line.CreateBound(a, b));
            result.Add(Line.CreateBound(b, c));
            result.Add(Line.CreateBound(c, d));
            result.Add(Line.CreateBound(d, a));
        }

        private static bool IsColumnLike(Element element)
        {
            if (element == null || element.Category == null)
            {
                return false;
            }

            int categoryId = element.Category.Id.IntegerValue;
            return categoryId == (int)BuiltInCategory.OST_StructuralColumns ||
                   categoryId == (int)BuiltInCategory.OST_Columns;
        }

        private static bool IsColumnOnLevel(Element column, Document doc, ElementId levelId)
        {
            if (column == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            if (column.LevelId != null && column.LevelId != ElementId.InvalidElementId && column.LevelId.IntegerValue == levelId.IntegerValue)
            {
                return true;
            }

            if (HasMatchingLevelParameter(column, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, levelId) ||
                HasMatchingLevelParameter(column, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, levelId))
            {
                return true;
            }

            Level level = doc != null ? doc.GetElement(levelId) as Level : null;
            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (level == null || box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            double toleranceFt = UnitUtils.ConvertToInternalUnits(500.0, UnitTypeId.Millimeters);
            double elevation = level.Elevation;
            double minZ = Math.Min(box.Min.Z, box.Max.Z) - toleranceFt;
            double maxZ = Math.Max(box.Min.Z, box.Max.Z) + toleranceFt;
            return elevation >= minZ && elevation <= maxZ;
        }

        private static bool HasMatchingLevelParameter(Element element, BuiltInParameter parameterId, ElementId levelId)
        {
            Parameter parameter = element != null ? element.get_Parameter(parameterId) : null;
            ElementId value = parameter != null && parameter.StorageType == StorageType.ElementId
                ? parameter.AsElementId()
                : ElementId.InvalidElementId;
            return value != null && value != ElementId.InvalidElementId && levelId != null && value.IntegerValue == levelId.IntegerValue;
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
