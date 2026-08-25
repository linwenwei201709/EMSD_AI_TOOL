using Autodesk.Revit.DB;
using CadToRevit.Models.Path;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class Path3DVisualizationService
    {
        internal sealed class DrawResult
        {
            public int SegmentCount { get; set; }
            public int ArrowCount { get; set; }
            public int NodeCount { get; set; }
            public int PointMarkerCount { get; set; }
            public int RedZoneCount { get; set; }
            public List<ElementId> ElementIds { get; } = new List<ElementId>();
        }

        internal static void Clear(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => PathPreviewMetadataService.IsManagedName(x.Name))
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            doc.Delete(ids);
            DiagnosticRecorder.AppendDebug("[PathPreview] Clear deleted=" + ids.Count);
        }

        internal static DrawResult Draw(Document doc, View3D view3D, PathPolyline path)
        {
            return Draw(doc, view3D, path, true);
        }

        internal static DrawResult Draw(Document doc, View3D view3D, PathPolyline path, bool drawNodeLabels)
        {
            return Draw(doc, view3D, path, drawNodeLabels, PathPreviewConstants.PathColor);
        }

        private static DrawResult Draw(Document doc, View3D view3D, PathPolyline path, bool drawNodeLabels, Color pathColor)
        {
            DrawResult result = new DrawResult();
            if (doc == null || view3D == null || path == null || path.Points == null || path.Points.Count < 2)
            {
                return result;
            }

            MaterialContext materials = BuildMaterials(doc);

            if (HasOrientationBoxes(path))
            {
                List<Solid> boxSolids = BuildPointOrientationBoxes(path, materials.PathMaterialId);
                for (int i = 0; i < boxSolids.Count; i++)
                {
                    Solid boxSolid = boxSolids[i];
                    if (boxSolid == null || boxSolid.Faces == null || boxSolid.Faces.Size == 0)
                    {
                        continue;
                    }

                    DirectShape segmentShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    PathPreviewMetadataService.ApplyMetadata(
                        segmentShape,
                        BuildSegmentBoxName(path.PathId, i, 0),
                        BuildSegmentBoxDataId(path.PathId, i, 0));
                    segmentShape.SetShape(new List<GeometryObject> { boxSolid });
                    ApplyOverride(view3D, segmentShape.Id, pathColor, PathPreviewConstants.PathTransparency);
                    result.ElementIds.Add(segmentShape.Id);
                    result.SegmentCount += 1;
                }
            }
            else
            {
                for (int i = 0; i < path.Points.Count - 1; i++)
                {
                    List<Solid> boxSolids = BuildIndependentSegmentBoxes(path, i, materials.PathMaterialId);
                    if (boxSolids.Count == 0)
                    {
                        continue;
                    }

                    for (int boxIndex = 0; boxIndex < boxSolids.Count; boxIndex++)
                    {
                        Solid boxSolid = boxSolids[boxIndex];
                        if (boxSolid == null || boxSolid.Faces == null || boxSolid.Faces.Size == 0)
                        {
                            continue;
                        }

                        DirectShape segmentShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        PathPreviewMetadataService.ApplyMetadata(
                            segmentShape,
                            BuildSegmentBoxName(path.PathId, i, boxIndex),
                            BuildSegmentBoxDataId(path.PathId, i, boxIndex));
                        segmentShape.SetShape(new List<GeometryObject> { boxSolid });
                        ApplyOverride(view3D, segmentShape.Id, pathColor, PathPreviewConstants.PathTransparency);
                        result.ElementIds.Add(segmentShape.Id);
                        result.SegmentCount += 1;
                    }
                }
            }

            XYZ startDir = GetStartDirection(path);
            XYZ endDir = GetEndDirection(path);
            result.NodeCount += DrawNode(doc, view3D, path, path.PathId, path.Points.FirstOrDefault(), true, startDir, materials, drawNodeLabels, result);
            result.NodeCount += DrawNode(doc, view3D, path, path.PathId, path.Points.LastOrDefault(), false, endDir, materials, drawNodeLabels, result);
            result.PointMarkerCount += DrawPathCoordinateMarkers(doc, view3D, path, materials.LabelMaterialId, result);

            DiagnosticRecorder.AppendDebug(
                "[PathPreview] Draw complete, PathId=" + (path.PathId ?? string.Empty) +
                ", PointCount=" + path.Points.Count +
                ", BoxCount=" + result.SegmentCount +
                ", NodeCount=" + result.NodeCount +
                ", PointMarkerCount=" + result.PointMarkerCount);
            return result;
        }

        internal static DrawResult DrawRedZones(
            Document doc,
            View3D view3D,
            IList<RedZonePoint3D> redZones)
        {
            DrawResult result = new DrawResult();
            if (doc == null || view3D == null || redZones == null || redZones.Count == 0)
            {
                return result;
            }

            try
            {
                ElementId genericModelCategory = new ElementId(BuiltInCategory.OST_GenericModel);
                if (view3D.CanCategoryBeHidden(genericModelCategory))
                {
                    view3D.SetCategoryHidden(genericModelCategory, false);
                }
            }
            catch
            {
                // A view template can make category visibility read-only; the
                // DirectShape is still useful in the active view.
            }

            ElementId materialId = PathPreviewMaterialService.GetOrCreateRedZoneMaterialId(doc);
            List<GeometryObject> geometry = new List<GeometryObject>();
            int chunkIndex = 0;
            for (int i = 0; i < redZones.Count; i++)
            {
                Solid solid = BuildRedZoneSolid(redZones[i], materialId);
                if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
                {
                    continue;
                }

                geometry.Add(solid);
                result.RedZoneCount++;
                if (geometry.Count >= 200 || i == redZones.Count - 1)
                {
                    DirectShape shape = DirectShape.CreateElement(
                        doc,
                        new ElementId(BuiltInCategory.OST_GenericModel));
                    string name = "RED_ZONE_CHUNK_" + chunkIndex;
                    PathPreviewMetadataService.ApplyMetadata(
                        shape,
                        PathPreviewMetadataService.BuildNodeName("FAILED_PATH", name),
                        PathPreviewMetadataService.BuildNodeDataId("FAILED_PATH", name));
                    shape.SetShape(geometry);
                    ApplyOverride(view3D, shape.Id, PathPreviewConstants.RedZoneColor, PathPreviewConstants.RedZoneTransparency);
                    result.ElementIds.Add(shape.Id);
                    geometry = new List<GeometryObject>();
                    chunkIndex++;
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[PathPreview] Red zones drawn=" + result.RedZoneCount + ", shapes=" + chunkIndex);
            return result;
        }

        private static Solid BuildRedZoneSolid(RedZonePoint3D redZone, ElementId materialId)
        {
            if (redZone == null ||
                double.IsNaN(redZone.X) || double.IsNaN(redZone.Y) ||
                double.IsInfinity(redZone.X) || double.IsInfinity(redZone.Y))
            {
                return null;
            }

            const double mmToFeet = 1.0 / 304.8;
            double cellSizeMm = redZone.CellSizeMm > 0 ? redZone.CellSizeMm : 100.0;
            double half = cellSizeMm * 0.5 * mmToFeet;
            double height = 500.0 * mmToFeet;
            double baseZ = (redZone.Z + 900.0) * mmToFeet;
            double centerX = redZone.X * mmToFeet;
            double centerY = redZone.Y * mmToFeet;
            XYZ p0 = new XYZ(centerX - half, centerY - half, baseZ);
            XYZ p1 = new XYZ(centerX + half, centerY - half, baseZ);
            XYZ p2 = new XYZ(centerX + half, centerY + half, baseZ);
            XYZ p3 = new XYZ(centerX - half, centerY + half, baseZ);
            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height,
                new SolidOptions(materialId, ElementId.InvalidElementId));
        }

        internal static DrawResult DrawMany(Document doc, View3D view3D, IList<PathPolyline> paths, bool drawNodeLabels)
        {
            DrawResult result = new DrawResult();
            if (doc == null || view3D == null || paths == null || paths.Count == 0)
            {
                return result;
            }

            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                PathPolyline path = paths[pathIndex];
                Color pathColor = PathPreviewConstants.GetComparisonPathColor(pathIndex);
                DrawResult single = Draw(doc, view3D, path, drawNodeLabels, pathColor);
                result.SegmentCount += single.SegmentCount;
                result.ArrowCount += single.ArrowCount;
                result.NodeCount += single.NodeCount;
                result.PointMarkerCount += single.PointMarkerCount;
                result.ElementIds.AddRange(single.ElementIds);
            }

            return result;
        }


        private static int DrawPathCoordinateMarkers(Document doc, View3D view3D, PathPolyline path, ElementId materialId, DrawResult result)
        {
            if (doc == null || view3D == null || path == null || path.Points == null || path.Points.Count == 0)
            {
                return 0;
            }

            int count = 0;
            string pathId = string.IsNullOrWhiteSpace(path.PathId) ? "DELIVERY_ROUTE" : path.PathId;
            double distanceSinceMarkerMm = 0.0;
            for (int i = 0; i < path.Points.Count; i++)
            {
                if (i > 0)
                {
                    distanceSinceMarkerMm += DistanceMillimeters(path.Points[i - 1], path.Points[i]);
                }

                // Keep the endpoints visible and sample intermediate points
                // at approximately one-metre intervals. The route boxes still
                // use every backend point; only the diagnostic markers are
                // thinned for readability and Revit performance.
                bool isEndpoint = i == 0 || i == path.Points.Count - 1;
                bool isSpacingSample = distanceSinceMarkerMm >= PathPreviewConstants.PathPointMarkerSpacingMm;
                if (!isEndpoint && !isSpacingSample)
                {
                    continue;
                }

                count += DrawPathCoordinateMarker(doc, view3D, path, pathId, i, path.Points[i], materialId, result);
                distanceSinceMarkerMm = 0.0;
            }

            DiagnosticRecorder.AppendDebug(
                "[PathPreview] CoordinateMarkers PathId=" + pathId +
                ", Count=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", SourcePointCount=" + path.Points.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", SpacingMm=" + PathPreviewConstants.PathPointMarkerSpacingMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            return count;
        }

        private static double DistanceMillimeters(PathPoint3D first, PathPoint3D second)
        {
            if (first == null || second == null)
            {
                return 0.0;
            }

            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            double dz = second.Z - first.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static int DrawPathCoordinateMarker(Document doc, View3D view3D, PathPolyline path, string pathId, int pointIndex, PathPoint3D point, ElementId materialId, DrawResult result)
        {
            if (doc == null || view3D == null || point == null)
            {
                return 0;
            }

            Solid solid = BuildPathCoordinateMarkerDiskSolid(path, point, materialId);
            if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
            {
                return 0;
            }

            string nodeKind = "POINT_" + (pointIndex + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            PathPreviewMetadataService.ApplyMetadata(
                shape,
                PathPreviewMetadataService.BuildNodeName(pathId, nodeKind),
                PathPreviewMetadataService.BuildNodeDataId(pathId, nodeKind));
            shape.SetShape(new List<GeometryObject> { solid });
            ApplyOverride(view3D, shape.Id, PathPreviewConstants.LabelColor, PathPreviewConstants.LabelTransparency);
            if (result != null)
            {
                result.ElementIds.Add(shape.Id);
            }
            return 1;
        }

        private static Solid BuildPathCoordinateMarkerDiskSolid(PathPolyline path, PathPoint3D point, ElementId materialId)
        {
            if (point == null)
            {
                return null;
            }

            // Put the marker slightly above the route boxes so it remains visible in top/3D views.
            double radiusFt = 95.0 * PathPreviewConstants.MmToFeet;
            double thicknessFt = 35.0 * PathPreviewConstants.MmToFeet;
            double zOffsetFt = (GetBoxHeightMm(path) + 55.0) * PathPreviewConstants.MmToFeet;
            XYZ origin = new XYZ(
                point.X * PathPreviewConstants.MmToFeet,
                point.Y * PathPreviewConstants.MmToFeet,
                (point.Z * PathPreviewConstants.MmToFeet) + zOffsetFt);

            CurveLoop loop = new CurveLoop();
            loop.Append(Arc.Create(origin, radiusFt, 0.0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            loop.Append(Arc.Create(origin, radiusFt, Math.PI, Math.PI * 2.0, XYZ.BasisX, XYZ.BasisY));

            SolidOptions options = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                thicknessFt,
                options);
        }

        internal static int DrawRequestPointMarkers(Document doc, View3D view3D, XYZ startPoint, XYZ goalPoint)
        {
            if (doc == null || view3D == null)
            {
                return 0;
            }

            ElementId materialId = PathPreviewMaterialService.GetOrCreateStartMaterialId(doc);
            int count = 0;
            count += DrawRequestPointMarker(doc, view3D, startPoint, "REQUEST_START", materialId);
            count += DrawRequestPointMarker(doc, view3D, goalPoint, "REQUEST_GOAL", materialId);

            DiagnosticRecorder.AppendDebug(
                "[DeliveryRouteRequestPoint] MarkerCount=" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", StartFt=" + FormatPointFeet(startPoint) +
                ", GoalFt=" + FormatPointFeet(goalPoint));
            return count;
        }

        private static int DrawRequestPointMarker(Document doc, View3D view3D, XYZ point, string markerKind, ElementId materialId)
        {
            if (doc == null || view3D == null || point == null)
            {
                return 0;
            }

            Solid solid = BuildRequestPointDiskSolid(point, materialId);
            if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
            {
                return 0;
            }

            string pathId = "DELIVERY_ROUTE_REQUEST_POINTS";
            DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            PathPreviewMetadataService.ApplyMetadata(
                shape,
                PathPreviewMetadataService.BuildNodeName(pathId, markerKind),
                PathPreviewMetadataService.BuildNodeDataId(pathId, markerKind));
            shape.SetShape(new List<GeometryObject> { solid });
            ApplyOverride(view3D, shape.Id, PathPreviewConstants.StartColor, 0);
            return 1;
        }

        private static Solid BuildRequestPointDiskSolid(XYZ center, ElementId materialId)
        {
            if (center == null)
            {
                return null;
            }

            double radiusFt = 180.0 * PathPreviewConstants.MmToFeet;
            double thicknessFt = 70.0 * PathPreviewConstants.MmToFeet;
            double zOffsetFt = 90.0 * PathPreviewConstants.MmToFeet;
            XYZ origin = new XYZ(center.X, center.Y, center.Z + zOffsetFt);

            CurveLoop loop = new CurveLoop();
            loop.Append(Arc.Create(origin, radiusFt, 0.0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            loop.Append(Arc.Create(origin, radiusFt, Math.PI, Math.PI * 2.0, XYZ.BasisX, XYZ.BasisY));

            SolidOptions options = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                thicknessFt,
                options);
        }

        private static string FormatPointFeet(XYZ point)
        {
            if (point == null)
            {
                return "null";
            }

            return "[" +
                   point.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "]";
        }

        private static List<Solid> BuildIndependentSegmentBoxes(PathPolyline path, int segmentIndex, ElementId materialId)
        {
            List<Solid> solids = new List<Solid>();
            if (path == null || path.Points == null || segmentIndex < 0 || segmentIndex >= path.Points.Count - 1)
            {
                return solids;
            }

            PathPoint3D originalStart = path.Points[segmentIndex];
            PathPoint3D originalEnd = path.Points[segmentIndex + 1];
            if (originalStart == null || originalEnd == null)
            {
                return solids;
            }

            double boxLengthMm = GetBoxLengthMm(path);
            double boxWidthMm = GetBoxWidthMm(path);
            double boxHeightMm = GetBoxHeightMm(path);
            double trimStartMm = segmentIndex == 0 ? boxLengthMm * 0.5 : 0.0;
            double trimEndMm = segmentIndex == path.Points.Count - 2 ? boxLengthMm * 0.5 : 0.0;

            PathPoint3D effectiveStart = ShiftPointAlongSegment(originalStart, originalEnd, trimStartMm);
            PathPoint3D effectiveEnd = ShiftPointAlongSegment(originalEnd, originalStart, trimEndMm);
            if (effectiveStart == null || effectiveEnd == null)
            {
                return solids;
            }

            double effectiveLengthMm = GetDistanceMm(effectiveStart, effectiveEnd);
            if (effectiveLengthMm <= 1.0)
            {
                return solids;
            }

            List<double> centerOffsetsMm = BuildCenterOffsetsMm(effectiveLengthMm, boxLengthMm);
            foreach (double centerOffsetMm in centerOffsetsMm)
            {
                PathPoint3D boxStart = ShiftPointAlongSegment(effectiveStart, effectiveEnd, centerOffsetMm - boxLengthMm * 0.5);
                PathPoint3D boxEnd = ShiftPointAlongSegment(effectiveStart, effectiveEnd, centerOffsetMm + boxLengthMm * 0.5);
                if (boxStart == null || boxEnd == null)
                {
                    continue;
                }

                List<Solid> singleBox = PathPreviewGeometryBuilder.BuildSegmentBoxSolids(
                    boxStart,
                    boxEnd,
                    boxLengthMm,
                    boxWidthMm,
                    boxHeightMm,
                    materialId);
                Solid firstSolid = singleBox.FirstOrDefault(x => x != null && x.Faces != null && x.Faces.Size > 0);
                if (firstSolid != null)
                {
                    solids.Add(firstSolid);
                }
            }

            return solids;
        }

        private static bool HasOrientationBoxes(PathPolyline path)
        {
            return path != null &&
                path.Points != null &&
                path.Points.Any(x => x != null && x.OrientationRadians.HasValue);
        }

        private static List<Solid> BuildPointOrientationBoxes(PathPolyline path, ElementId materialId)
        {
            List<Solid> solids = new List<Solid>();
            if (path == null || path.Points == null || path.Points.Count == 0)
            {
                return solids;
            }

            double boxLengthMm = GetBoxLengthMm(path);
            double boxWidthMm = GetBoxWidthMm(path);
            double boxHeightMm = GetBoxHeightMm(path);

            foreach (PathPoint3D point in path.Points)
            {
                if (point == null || !point.OrientationRadians.HasValue)
                {
                    continue;
                }

                Solid solid = PathPreviewGeometryBuilder.BuildPointOrientedBoxSolid(
                    point,
                    point.OrientationRadians.Value,
                    boxLengthMm,
                    boxWidthMm,
                    boxHeightMm,
                    materialId);
                if (solid != null && solid.Faces != null && solid.Faces.Size > 0)
                {
                    solids.Add(solid);
                }
            }

            return solids;
        }

        private static List<double> BuildCenterOffsetsMm(double effectiveLengthMm, double boxLengthMm)
        {
            List<double> offsets = new List<double>();
            double spacingMm = Math.Max(1.0, boxLengthMm);
            double halfBoxLengthMm = boxLengthMm * 0.5;

            if (effectiveLengthMm <= boxLengthMm + 1e-6)
            {
                offsets.Add(effectiveLengthMm * 0.5);
                return offsets;
            }

            double maxCenterOffsetMm = effectiveLengthMm - halfBoxLengthMm;
            for (double offsetMm = halfBoxLengthMm; offsetMm <= maxCenterOffsetMm + 1e-6; offsetMm += spacingMm)
            {
                offsets.Add(offsetMm);
            }

            if (offsets.Count == 0)
            {
                offsets.Add(effectiveLengthMm * 0.5);
                return offsets;
            }

            double lastOffsetMm = offsets[offsets.Count - 1];
            if (maxCenterOffsetMm - lastOffsetMm > 1e-6)
            {
                offsets.Add(maxCenterOffsetMm);
            }

            return offsets;
        }

        private static PathPoint3D ShiftPointAlongSegment(PathPoint3D from, PathPoint3D to, double offsetMm)
        {
            if (from == null || to == null)
            {
                return null;
            }

            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double lengthMm = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (lengthMm <= 1e-9)
            {
                return null;
            }

            double clampedOffsetMm = Math.Max(0.0, Math.Min(offsetMm, lengthMm));
            double t = clampedOffsetMm / lengthMm;
            return new PathPoint3D(
                from.X + dx * t,
                from.Y + dy * t,
                from.Z + dz * t);
        }

        private static double GetDistanceMm(PathPoint3D start, PathPoint3D end)
        {
            if (start == null || end == null)
            {
                return 0.0;
            }

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double dz = end.Z - start.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static string BuildSegmentBoxName(string pathId, int segmentIndex, int boxIndex)
        {
            return PathPreviewMetadataService.BuildSegmentName(pathId, segmentIndex) + "__BOX_" + boxIndex;
        }

        private static string BuildSegmentBoxDataId(string pathId, int segmentIndex, int boxIndex)
        {
            return PathPreviewMetadataService.BuildSegmentDataId(pathId, segmentIndex) + "::BOX::" + boxIndex;
        }

        private static XYZ GetStartDirection(PathPolyline path)
        {
            if (path == null || path.Points == null || path.Points.Count < 2)
            {
                return XYZ.BasisX;
            }

            return GetDirection(path.Points[0], path.Points[1]);
        }

        private static XYZ GetEndDirection(PathPolyline path)
        {
            if (path == null || path.Points == null || path.Points.Count < 2)
            {
                return XYZ.BasisX;
            }

            return GetDirection(path.Points[path.Points.Count - 2], path.Points[path.Points.Count - 1]);
        }

        private static XYZ GetDirection(PathPoint3D start, PathPoint3D end)
        {
            if (start == null || end == null)
            {
                return XYZ.BasisX;
            }

            XYZ dir = new XYZ(
                (end.X - start.X) * PathPreviewConstants.MmToFeet,
                (end.Y - start.Y) * PathPreviewConstants.MmToFeet,
                0.0);
            if (dir.GetLength() <= 1e-9)
            {
                return XYZ.BasisX;
            }

            return dir.Normalize();
        }

        private static int DrawNode(Document doc, View3D view3D, PathPolyline path, string pathId, PathPoint3D point, bool isStart, XYZ dirHint, MaterialContext materials, bool drawNodeLabels, DrawResult result)
        {
            double boxLengthMm = GetBoxLengthMm(path);
            double boxWidthMm = GetBoxWidthMm(path);
            double boxHeightMm = GetBoxHeightMm(path);
            Solid nodeSolid = PathPreviewGeometryBuilder.BuildNodeSolid(
                point,
                boxLengthMm,
                boxWidthMm,
                boxHeightMm,
                isStart ? materials.StartMaterialId : materials.EndMaterialId);
            if (nodeSolid == null || nodeSolid.Faces == null || nodeSolid.Faces.Size == 0)
            {
                return 0;
            }

            string nodeKind = isStart ? "START" : "END";
            DirectShape nodeShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            PathPreviewMetadataService.ApplyMetadata(
                nodeShape,
                PathPreviewMetadataService.BuildNodeName(pathId, nodeKind),
                PathPreviewMetadataService.BuildNodeDataId(pathId, nodeKind));
            nodeShape.SetShape(new List<GeometryObject> { nodeSolid });
            ApplyOverride(view3D, nodeShape.Id, isStart ? PathPreviewConstants.StartColor : PathPreviewConstants.EndColor, PathPreviewConstants.NodeTransparency);
            if (result != null)
            {
                result.ElementIds.Add(nodeShape.Id);
            }

            if (drawNodeLabels)
            {
                List<Solid> labelSolids = PathPreviewGeometryBuilder.BuildNodeLabelSolids(
                    point,
                    dirHint,
                    nodeKind,
                    boxHeightMm,
                    materials.LabelMaterialId);
                if (labelSolids.Count > 0)
                {
                    DirectShape labelShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    PathPreviewMetadataService.ApplyMetadata(
                        labelShape,
                        PathPreviewMetadataService.BuildNodeName(pathId, nodeKind + "_LABEL"),
                        PathPreviewMetadataService.BuildNodeDataId(pathId, nodeKind + "_LABEL"));
                    labelShape.SetShape(labelSolids.Cast<GeometryObject>().ToList());
                    ApplyOverride(view3D, labelShape.Id, PathPreviewConstants.LabelColor, PathPreviewConstants.LabelTransparency);
                    if (result != null)
                    {
                        result.ElementIds.Add(labelShape.Id);
                    }
                }
            }

            return 1;
        }

        private static double GetBoxLengthMm(PathPolyline path)
        {
            return path != null && IsPositiveFinite(path.BoxLengthMm)
                ? path.BoxLengthMm
                : PathPreviewConstants.PathBoxLengthMm;
        }

        private static double GetBoxWidthMm(PathPolyline path)
        {
            return path != null && IsPositiveFinite(path.BoxWidthMm)
                ? path.BoxWidthMm
                : PathPreviewConstants.PathBoxWidthMm;
        }

        private static double GetBoxHeightMm(PathPolyline path)
        {
            return path != null && IsPositiveFinite(path.BoxHeightMm)
                ? path.BoxHeightMm
                : PathPreviewConstants.PathBoxHeightMm;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 1.0e-6 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static MaterialContext BuildMaterials(Document doc)
        {
            return new MaterialContext
            {
                PathMaterialId = PathPreviewMaterialService.GetOrCreatePathMaterialId(doc),
                ArrowMaterialId = PathPreviewMaterialService.GetOrCreateArrowMaterialId(doc),
                StartMaterialId = PathPreviewMaterialService.GetOrCreateStartMaterialId(doc),
                EndMaterialId = PathPreviewMaterialService.GetOrCreateEndMaterialId(doc),
                LabelMaterialId = PathPreviewMaterialService.GetOrCreateLabelMaterialId(doc)
            };
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);

            return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
        }

        private static void ApplyOverride(View3D view3D, ElementId elementId, Color color, int transparency)
        {
            if (view3D == null || elementId == ElementId.InvalidElementId)
            {
                return;
            }

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ElementId solidFillId = GetSolidFillPatternId(view3D.Document);
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternColor(color);
            }

            ogs.SetSurfaceTransparency(transparency);
            view3D.SetElementOverrides(elementId, ogs);
        }

        private sealed class MaterialContext
        {
            public ElementId PathMaterialId { get; set; } = ElementId.InvalidElementId;
            public ElementId ArrowMaterialId { get; set; } = ElementId.InvalidElementId;
            public ElementId StartMaterialId { get; set; } = ElementId.InvalidElementId;
            public ElementId EndMaterialId { get; set; } = ElementId.InvalidElementId;
            public ElementId LabelMaterialId { get; set; } = ElementId.InvalidElementId;
        }
    }
}
