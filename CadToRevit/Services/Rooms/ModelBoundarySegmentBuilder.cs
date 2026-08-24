using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal sealed class ModelBoundaryDatasetBuildResult
    {
        public CadDataset Dataset { get; set; } = new CadDataset();
        public int WallSegments { get; set; }
        public int SeparatorSegments { get; set; }
        public int DoorClosureSegments { get; set; }
        public int ColumnSegments { get; set; }
        public int SkippedCurvedWalls { get; set; }
        public int DirectWallCount { get; set; }
        public int GroupWallCount { get; set; }
        public int WallLocationCurveNull { get; set; }
        public int WallLocationCurveNotLine { get; set; }
    }

    internal static class ModelBoundarySegmentBuilder
    {
        internal const string WallBoundaryLayerName = "MODEL_WALL_BOUNDARY";
        internal const string RoomSeparatorLayerName = "ROOM_SEPARATION";
        internal const string DoorClosureLayerName = "MODEL_DOOR_CLOSURE";

        public static ModelBoundaryDatasetBuildResult BuildLocalDataset(
            Document doc,
            ElementId levelId,
            XYZ seedCenter,
            double windowSizeMm)
        {
            ModelBoundaryDatasetBuildResult result = new ModelBoundaryDatasetBuildResult();
            if (doc == null || seedCenter == null)
            {
                return result;
            }

            double halfFt = UnitUtils.ConvertToInternalUnits(Math.Max(1000.0, windowSizeMm) * 0.5, UnitTypeId.Millimeters);
            double minX = seedCenter.X - halfFt;
            double minY = seedCenter.Y - halfFt;
            double maxX = seedCenter.X + halfFt;
            double maxY = seedCenter.Y + halfFt;
            Outline outline = new Outline(
                new XYZ(minX, minY, seedCenter.Z - 1000.0),
                new XYZ(maxX, maxY, seedCenter.Z + 1000.0));
            BoundingBoxIntersectsFilter boxFilter = new BoundingBoxIntersectsFilter(outline);

            int nextSegmentId = 1;

            List<Wall> directWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WherePasses(boxFilter)
                .Cast<Wall>()
                .Where(x => IsOnLevel(x, levelId))
                .ToList();
            HashSet<int> wallIds = new HashSet<int>();
            List<Wall> walls = new List<Wall>();
            foreach (Wall wall in directWalls)
            {
                if (wall != null && wallIds.Add(wall.Id.IntegerValue))
                {
                    walls.Add(wall);
                    result.DirectWallCount++;
                }
            }

            List<Group> groups = new FilteredElementCollector(doc)
                .OfClass(typeof(Group))
                .WherePasses(boxFilter)
                .Cast<Group>()
                .Where(x => x != null)
                .ToList();
            foreach (Group group in groups)
            {
                foreach (ElementId memberId in group.GetMemberIds())
                {
                    Wall memberWall = doc.GetElement(memberId) as Wall;
                    if (memberWall == null || !IsOnLevel(memberWall, levelId) || !wallIds.Add(memberWall.Id.IntegerValue))
                    {
                        continue;
                    }

                    walls.Add(memberWall);
                    result.GroupWallCount++;
                }
            }

            DiagnosticRecorder.AppendDebug("[ModelBoundarySegmentBuilder] DirectWallCount=" + result.DirectWallCount.ToString(CultureInfo.InvariantCulture) +
                ", GroupCount=" + groups.Count.ToString(CultureInfo.InvariantCulture) +
                ", GroupWallCount=" + result.GroupWallCount.ToString(CultureInfo.InvariantCulture));
            foreach (Wall wall in walls)
            {
                if (!TryBuildWallBoundarySegments(wall, minX, minY, maxX, maxY, ref nextSegmentId, out List<CadSegment> segments, out string skipReason))
                {
                    if (string.Equals(skipReason, "LocationCurveNull", StringComparison.OrdinalIgnoreCase))
                    {
                        result.WallLocationCurveNull++;
                    }
                    else if (string.Equals(skipReason, "LocationCurveNotLine", StringComparison.OrdinalIgnoreCase))
                    {
                        result.WallLocationCurveNotLine++;
                    }

                    result.SkippedCurvedWalls++;
                    continue;
                }

                result.WallSegments += AddSegments(result.Dataset, segments);
            }

            IEnumerable<Element> columns = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(boxFilter)
                .ToElements()
                .Where(x => IsColumnLike(x) && IsColumnOnLevel(x, doc, levelId));
            foreach (Element column in columns)
            {
                if (TryBuildColumnBoundarySegments(column, seedCenter.Z, minX, minY, maxX, maxY, ref nextSegmentId, out List<CadSegment> segments))
                {
                    result.ColumnSegments += AddSegments(result.Dataset, segments);
                }
            }

            IEnumerable<CurveElement> separators = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WherePasses(boxFilter)
                .Cast<CurveElement>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomSeparationLines);
            foreach (CurveElement separator in separators)
            {
                Line line = separator.GeometryCurve as Line;
                if (line == null)
                {
                    continue;
                }

                if (TryClipLineToRect(line.GetEndPoint(0), line.GetEndPoint(1), minX, minY, maxX, maxY, out XYZ clippedP0, out XYZ clippedP1))
                {
                    CadSegment segment = CreateSegment(nextSegmentId++, RoomSeparatorLayerName, clippedP0, clippedP1);
                    result.SeparatorSegments += AddSegments(result.Dataset, new List<CadSegment> { segment });
                }
            }

            foreach (Line line in DoorClosureBuilder.BuildDoorClosureLines(doc, levelId, seedCenter, windowSizeMm))
            {
                if (line == null)
                {
                    continue;
                }

                if (TryClipLineToRect(line.GetEndPoint(0), line.GetEndPoint(1), minX, minY, maxX, maxY, out XYZ clippedP0, out XYZ clippedP1))
                {
                    CadSegment segment = CreateSegment(nextSegmentId++, DoorClosureLayerName, clippedP0, clippedP1);
                    result.DoorClosureSegments += AddSegments(result.Dataset, new List<CadSegment> { segment });
                }
            }

            return result;
        }

        private static bool TryBuildColumnBoundarySegments(
            Element column,
            double z,
            double minX,
            double minY,
            double maxX,
            double maxY,
            ref int nextSegmentId,
            out List<CadSegment> segments)
        {
            segments = new List<CadSegment>();
            if (column == null)
            {
                return false;
            }

            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            double colMinX = Math.Min(box.Min.X, box.Max.X);
            double colMinY = Math.Min(box.Min.Y, box.Max.Y);
            double colMaxX = Math.Max(box.Min.X, box.Max.X);
            double colMaxY = Math.Max(box.Min.Y, box.Max.Y);
            if ((colMaxX - colMinX) <= 1e-6 || (colMaxY - colMinY) <= 1e-6)
            {
                return false;
            }

            XYZ a = new XYZ(colMinX, colMinY, z);
            XYZ b = new XYZ(colMaxX, colMinY, z);
            XYZ c = new XYZ(colMaxX, colMaxY, z);
            XYZ d = new XYZ(colMinX, colMaxY, z);

            // Treat column footprints as room enclosure edges.  They intentionally use the
            // same layer as wall boundaries so the existing loop detector can consume them
            // without changing the recognition routing logic.
            TryAddBoundarySegment(segments, ref nextSegmentId, a, b, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, b, c, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, c, d, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, d, a, minX, minY, maxX, maxY);
            return segments.Count > 0;
        }

        private static bool TryBuildWallBoundarySegments(
            Wall wall,
            double minX,
            double minY,
            double maxX,
            double maxY,
            ref int nextSegmentId,
            out List<CadSegment> segments,
            out string skipReason)
        {
            segments = new List<CadSegment>();
            skipReason = string.Empty;
            if (wall == null)
            {
                skipReason = "WallNull";
                return false;
            }

            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve == null)
            {
                skipReason = "LocationCurveNull";
                return false;
            }

            Line hostLine = locationCurve != null ? locationCurve.Curve as Line : null;
            if (hostLine == null)
            {
                skipReason = "LocationCurveNotLine";
                return false;
            }

            XYZ p0 = hostLine.GetEndPoint(0);
            XYZ p1 = hostLine.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            XYZ normal = new XYZ(-dir.Y, dir.X, 0.0);
            double halfWidth = wall.Width * 0.5;

            XYZ a = p0 + normal.Multiply(halfWidth);
            XYZ b = p1 + normal.Multiply(halfWidth);
            XYZ c = p1 - normal.Multiply(halfWidth);
            XYZ d = p0 - normal.Multiply(halfWidth);

            TryAddBoundarySegment(segments, ref nextSegmentId, a, b, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, b, c, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, c, d, minX, minY, maxX, maxY);
            TryAddBoundarySegment(segments, ref nextSegmentId, d, a, minX, minY, maxX, maxY);
            return true;
        }

        private static void TryAddBoundarySegment(
            List<CadSegment> segments,
            ref int nextSegmentId,
            XYZ start,
            XYZ end,
            double minX,
            double minY,
            double maxX,
            double maxY)
        {
            if (!TryClipLineToRect(start, end, minX, minY, maxX, maxY, out XYZ clippedP0, out XYZ clippedP1))
            {
                return;
            }

            segments.Add(CreateSegment(nextSegmentId++, WallBoundaryLayerName, clippedP0, clippedP1));
        }

        private static CadSegment CreateSegment(int segmentId, string rawLayerName, XYZ p0, XYZ p1)
        {
            return new CadSegment
            {
                SegmentId = segmentId,
                RawLayerName = rawLayerName ?? string.Empty,
                LayerName = rawLayerName ?? string.Empty,
                SemanticLayer = rawLayerName ?? string.Empty,
                NormalizedLayer = (rawLayerName ?? string.Empty).Trim().ToUpperInvariant(),
                SourceType = CadCurveSourceType.NativeLine,
                P0 = p0,
                P1 = p1,
                MidPoint = p0 != null && p1 != null
                    ? new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, (p0.Z + p1.Z) * 0.5)
                    : null
            };
        }

        private static int AddSegments(CadDataset dataset, IEnumerable<CadSegment> segments)
        {
            if (dataset == null)
            {
                return 0;
            }

            int added = 0;
            foreach (CadSegment segment in segments ?? Enumerable.Empty<CadSegment>())
            {
                if (segment == null || segment.P0 == null || segment.P1 == null || segment.P0.DistanceTo(segment.P1) <= 1e-6)
                {
                    continue;
                }

                dataset.Segments.Add(segment);
                if (!dataset.SegmentsByRawLayer.TryGetValue(segment.RawLayerName ?? string.Empty, out List<CadSegment> bucket))
                {
                    bucket = new List<CadSegment>();
                    dataset.SegmentsByRawLayer[segment.RawLayerName ?? string.Empty] = bucket;
                }

                bucket.Add(segment);
                added++;
            }

            return added;
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

            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId wallLevelId = parameter != null ? parameter.AsElementId() : ElementId.InvalidElementId;
            return wallLevelId != null && wallLevelId.IntegerValue == levelId.IntegerValue;
        }

        private static bool TryClipLineToRect(
            XYZ p0,
            XYZ p1,
            double minX,
            double minY,
            double maxX,
            double maxY,
            out XYZ clippedP0,
            out XYZ clippedP1)
        {
            clippedP0 = null;
            clippedP1 = null;
            if (p0 == null || p1 == null)
            {
                return false;
            }

            double x0 = p0.X;
            double y0 = p0.Y;
            double x1 = p1.X;
            double y1 = p1.Y;
            int code0 = ComputeClipCode(x0, y0, minX, minY, maxX, maxY);
            int code1 = ComputeClipCode(x1, y1, minX, minY, maxX, maxY);

            while (true)
            {
                if ((code0 | code1) == 0)
                {
                    clippedP0 = new XYZ(x0, y0, p0.Z);
                    clippedP1 = new XYZ(x1, y1, p1.Z);
                    return true;
                }

                if ((code0 & code1) != 0)
                {
                    return false;
                }

                int codeOut = code0 != 0 ? code0 : code1;
                double x = 0.0;
                double y = 0.0;

                if ((codeOut & 8) != 0)
                {
                    x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0);
                    y = maxY;
                }
                else if ((codeOut & 4) != 0)
                {
                    x = x0 + (x1 - x0) * (minY - y0) / (y1 - y0);
                    y = minY;
                }
                else if ((codeOut & 2) != 0)
                {
                    y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0);
                    x = maxX;
                }
                else
                {
                    y = y0 + (y1 - y0) * (minX - x0) / (x1 - x0);
                    x = minX;
                }

                if (codeOut == code0)
                {
                    x0 = x;
                    y0 = y;
                    code0 = ComputeClipCode(x0, y0, minX, minY, maxX, maxY);
                }
                else
                {
                    x1 = x;
                    y1 = y;
                    code1 = ComputeClipCode(x1, y1, minX, minY, maxX, maxY);
                }
            }
        }

        private static int ComputeClipCode(double x, double y, double minX, double minY, double maxX, double maxY)
        {
            int code = 0;
            if (x < minX)
            {
                code |= 1;
            }
            else if (x > maxX)
            {
                code |= 2;
            }

            if (y < minY)
            {
                code |= 4;
            }
            else if (y > maxY)
            {
                code |= 8;
            }

            return code;
        }
    }
}
