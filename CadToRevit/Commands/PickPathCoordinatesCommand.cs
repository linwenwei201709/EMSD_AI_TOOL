using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathObstacles;
using CadToRevit.Services.PathPreview;
using CadToRevit.UI.PathObstacles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Grid = System.Windows.Controls.Grid;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PickPathCoordinatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            if (uiApp == null)
            {
                return Result.Cancelled;
            }

            try
            {
                PathPreviewCoordinateCaptureService.Run(uiApp);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                DiagnosticRecorder.AppendDebug("[PathCapture] failed=" + ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Lets users draw a polygonal no-go zone for path recognition.
    /// The polygon is converted into a thin Generic Model DirectShape so it can be exported through IFC.
    ///
    /// NOTE:
    /// This command is intentionally kept in an existing compiled command file to avoid requiring a .csproj edit
    /// in legacy non-SDK style Revit add-in projects.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DrawPathObstacleCommand : IExternalCommand
    {
        private const string CommandTitle = "Path Obstacle";
        private const string ApplicationId = "CadToRevit";
        private const string ObstacleName = "CadToRevit_PathObstacle";
        private const string ObstacleComment = "CadToRevit_PathObstacle";
        private const string MaterialName = "CadToRevit Path Obstacle - Transparent Red";

        private const string PickMarkerName = "CadToRevit_PathObstacle_PickPoint";
        private const string PickMarkerComment = "CadToRevit_PathObstacle_TemporaryPickPoint";
        private const string PickMarkerMaterialName = "CadToRevit Path Obstacle Pick Point - Red";

        // Thin 3D body. Path programs usually use the plan footprint, but a 3D solid exports to IFC more reliably.
        private const double DefaultObstacleHeightMm = 200.0;
        private const double MinimumPointDistanceMm = 5.0;
        private const int MaterialTransparency = 65;

        // Temporary visual marker for each picked point. These markers are deleted automatically after finish/cancel.
        private const double PickMarkerRadiusMm = 45.0;
        private const double PickMarkerHeightMm = 25.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            UIDocument uiDoc = uiApp?.ActiveUIDocument;
            Document doc = uiDoc?.Document;

            if (uiDoc == null || doc == null)
            {
                return Result.Cancelled;
            }

            List<ElementId> pickMarkerIds = new List<ElementId>();

            try
            {
                double baseElevation = ResolveBaseElevation(doc);
                TryEnsureWorkPlane(doc, doc.ActiveView, baseElevation);

                PathObstacleMessageWindow.Show(
                    uiApp,
                    CommandTitle,
                    "Pick 3 or more points around the no-go zone.\n\n" +
                    "A small red marker will appear after each picked point.\n" +
                    "Press ESC once after the last point to create the obstacle.\n" +
                    "The temporary point markers will be removed automatically after the obstacle is created.");

                List<XYZ> pickedPoints = PickPolygonPoints(uiDoc, doc, baseElevation, pickMarkerIds);
                if (pickedPoints.Count == 0)
                {
                    DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                    return Result.Cancelled;
                }

                List<XYZ> polygonPoints = NormalizePolygonPoints(pickedPoints, baseElevation);
                if (polygonPoints.Count < 3)
                {
                    DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                    PathObstacleMessageWindow.Show(uiApp, CommandTitle, "Please pick at least 3 different points to create a path obstacle.");
                    return Result.Cancelled;
                }

                if (!HasValidPlanArea(polygonPoints))
                {
                    DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                    PathObstacleMessageWindow.Show(uiApp, CommandTitle, "The picked points are too close together or nearly collinear. Please draw a larger closed area.");
                    return Result.Cancelled;
                }

                string polygonValidationMessage;
                if (!IsSimplePlanPolygon(polygonPoints, out polygonValidationMessage))
                {
                    DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                    PathObstacleMessageWindow.Show(uiApp, CommandTitle, polygonValidationMessage);
                    return Result.Cancelled;
                }

                ElementId createdId;
                string mark;
                using (Transaction transaction = new Transaction(doc, "Create Path Obstacle"))
                {
                    transaction.Start();

                    ElementId materialId = EnsureObstacleMaterial(doc);
                    Solid obstacleSolid = CreateObstacleSolid(polygonPoints, materialId);
                    mark = BuildNextObstacleMark(doc);

                    DirectShape directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    directShape.ApplicationId = ApplicationId;
                    directShape.ApplicationDataId = mark;
                    directShape.Name = ObstacleName;
                    directShape.SetShape(new List<GeometryObject> { obstacleSolid });

                    SetStringParameter(directShape, BuiltInParameter.ALL_MODEL_MARK, mark);
                    SetStringParameter(directShape, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, ObstacleComment);
                    SetLookupParameter(directShape, "IfcExportAs", "IfcBuildingElementProxy");
                    SetLookupParameter(directShape, "IfcName", mark);

                    createdId = directShape.Id;
                    transaction.Commit();
                }

                DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                uiDoc.Selection.SetElementIds(new List<ElementId> { createdId });
                SaveObstacleName(uiApp, doc, createdId);
                RoutePlannerSessionCacheService.MarkDirty(doc, "Path obstacle was created.");
                DiagnosticRecorder.AppendDebug("[PathObstacle] Created Id=" + createdId.IntegerValue + ", Mark=" + mark + ", Points=" + polygonPoints.Count);
                // Do not show a success dialog here. Users create obstacles repeatedly during planning,
                // and the Revit selection/highlight plus debug log are enough confirmation.
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DeleteTemporaryPickMarkers(doc, pickMarkerIds);
                message = ex.Message;
                DiagnosticRecorder.AppendDebug("[PathObstacle] Create failed: " + ex);
                PathObstacleMessageWindow.Show(uiApp, CommandTitle, "Create path obstacle failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        public static double PrepareDrawing(Document doc)
        {
            double baseElevation = ResolveBaseElevation(doc);
            TryEnsureWorkPlane(doc, doc != null ? doc.ActiveView : null, baseElevation);
            return baseElevation;
        }

        public static XYZ PickSinglePoint(UIDocument uiDoc, double baseElevation, IList<ElementId> pickMarkerIds)
        {
            if (uiDoc == null || uiDoc.Document == null)
            {
                return null;
            }

            ObjectSnapTypes snapTypes = ObjectSnapTypes.Endpoints |
                                        ObjectSnapTypes.Intersections |
                                        ObjectSnapTypes.Midpoints |
                                        ObjectSnapTypes.Nearest;

            XYZ point = uiDoc.Selection.PickPoint(snapTypes, "Pick point of the restricted area.");
            ElementId markerId = CreateTemporaryPickMarker(uiDoc.Document, point, baseElevation);
            if (markerId != ElementId.InvalidElementId && pickMarkerIds != null)
            {
                pickMarkerIds.Add(markerId);
            }

            try
            {
                uiDoc.RefreshActiveView();
            }
            catch
            {
            }

            return point;
        }

        public static bool TryCreateRestrictedAreaFromPoints(
            UIApplication uiApp,
            IList<XYZ> pickedPoints,
            IList<ElementId> pickMarkerIds,
            out ElementId createdId,
            out string message)
        {
            createdId = ElementId.InvalidElementId;
            message = string.Empty;

            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                message = "No active document.";
                return false;
            }

            double baseElevation = ResolveBaseElevation(doc);
            List<XYZ> polygonPoints = NormalizePolygonPoints(pickedPoints, baseElevation);
            if (polygonPoints.Count < 3)
            {
                message = "Please select at least 3 points.";
                return false;
            }

            if (!HasValidPlanArea(polygonPoints))
            {
                message = "The picked points are too close together or nearly collinear. Please draw a larger closed area.";
                return false;
            }

            string polygonValidationMessage;
            if (!IsSimplePlanPolygon(polygonPoints, out polygonValidationMessage))
            {
                message = polygonValidationMessage;
                return false;
            }

            string mark = string.Empty;
            string defaultName = PathObstacleStoreService.BuildNextDefaultName(doc);

            try
            {
                using (Transaction transaction = new Transaction(doc, "Create Restricted Area"))
                {
                    transaction.Start();

                    ElementId materialId = EnsureObstacleMaterial(doc);
                    Solid obstacleSolid = CreateObstacleSolid(polygonPoints, materialId);
                    mark = BuildNextObstacleMark(doc);

                    DirectShape directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    directShape.ApplicationId = ApplicationId;
                    directShape.ApplicationDataId = mark;
                    directShape.Name = ObstacleName;
                    directShape.SetShape(new List<GeometryObject> { obstacleSolid });

                    SetStringParameter(directShape, BuiltInParameter.ALL_MODEL_MARK, mark);
                    SetStringParameter(directShape, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, ObstacleComment);
                    SetLookupParameter(directShape, "IfcExportAs", "IfcBuildingElementProxy");
                    SetLookupParameter(directShape, "IfcName", mark);

                    createdId = directShape.Id;
                    PathObstacleStoreService.Save(directShape, defaultName);
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                createdId = ElementId.InvalidElementId;
                message = "The selected points could not form a valid restricted area. Please redraw the polygon without crossing edges.";
                DiagnosticRecorder.AppendDebug("[PathObstacle] Restricted area geometry validation failed: " + ex);
                return false;
            }

            DeleteTemporaryPickMarkers(doc, pickMarkerIds);
            uiDoc.Selection.SetElementIds(new List<ElementId> { createdId });
            RoutePlannerSessionCacheService.MarkDirty(doc, "Restricted area was created.");
            DiagnosticRecorder.AppendDebug("[PathObstacle] Created restricted area Id=" + createdId.IntegerValue + ", Mark=" + mark + ", Points=" + polygonPoints.Count);
            return true;
        }

        public static void ClearTemporaryPickMarkers(Document doc, IList<ElementId> markerIds)
        {
            DeleteTemporaryPickMarkers(doc, markerIds);
        }

        private static List<XYZ> PickPolygonPoints(UIDocument uiDoc, Document doc, double baseElevation, IList<ElementId> pickMarkerIds)
        {
            List<XYZ> points = new List<XYZ>();
            ObjectSnapTypes snapTypes = ObjectSnapTypes.Endpoints |
                                        ObjectSnapTypes.Intersections |
                                        ObjectSnapTypes.Midpoints |
                                        ObjectSnapTypes.Nearest;

            while (true)
            {
                string prompt = points.Count == 0
                    ? "Pick first point of the path obstacle. Press ESC to cancel."
                    : "Pick next point of the path obstacle. Press ESC to finish.";

                try
                {
                    XYZ point = uiDoc.Selection.PickPoint(snapTypes, prompt);
                    points.Add(point);

                    ElementId markerId = CreateTemporaryPickMarker(doc, point, baseElevation);
                    if (markerId != ElementId.InvalidElementId)
                    {
                        pickMarkerIds.Add(markerId);
                    }

                    try
                    {
                        uiDoc.RefreshActiveView();
                    }
                    catch
                    {
                        // Refresh is optional. Revit will refresh the view after the transaction in most cases.
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
            }

            return points;
        }

        private static void SaveObstacleName(UIApplication uiApp, Document doc, ElementId createdId)
        {
            try
            {
                Element obstacle = doc.GetElement(createdId);
                if (obstacle == null)
                {
                    return;
                }

                string defaultName = PathObstacleStoreService.BuildNextDefaultName(doc);
                PathObstacleNameWindow nameWindow = new PathObstacleNameWindow(uiApp, defaultName);
                bool? result = nameWindow.ShowDialog();
                string obstacleName = result == true
                    ? nameWindow.ObstacleName
                    : defaultName;

                using (Transaction transaction = new Transaction(doc, "Save Path Obstacle Name"))
                {
                    transaction.Start();
                    PathObstacleStoreService.Save(obstacle, obstacleName);
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstacle] Save name skipped: " + ex);
            }
        }

        private static double ResolveBaseElevation(Document doc)
        {
            View activeView = doc.ActiveView;
            if (activeView != null && activeView.GenLevel != null)
            {
                return activeView.GenLevel.Elevation;
            }

            Level firstLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .FirstOrDefault();

            return firstLevel != null ? firstLevel.Elevation : 0.0;
        }

        private static void TryEnsureWorkPlane(Document doc, View view, double baseElevation)
        {
            if (doc == null || view == null || view.ViewType == ViewType.DrawingSheet)
            {
                return;
            }

            try
            {
                if (view.SketchPlane != null)
                {
                    return;
                }
            }
            catch
            {
                // Some view types may not expose SketchPlane reliably. Continue and try to assign one.
            }

            try
            {
                using (Transaction transaction = new Transaction(doc, "Set Path Obstacle Work Plane"))
                {
                    transaction.Start();
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, baseElevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    view.SketchPlane = sketchPlane;
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                // PickPoint may still work if the current view already has a usable work plane.
                DiagnosticRecorder.AppendDebug("[PathObstacle] Set work plane skipped: " + ex.Message);
            }
        }

        private static List<XYZ> NormalizePolygonPoints(IList<XYZ> rawPoints, double baseElevation)
        {
            double minDistance = UnitUtils.ConvertToInternalUnits(MinimumPointDistanceMm, UnitTypeId.Millimeters);
            List<XYZ> normalized = new List<XYZ>();

            foreach (XYZ rawPoint in rawPoints)
            {
                if (rawPoint == null)
                {
                    continue;
                }

                XYZ point = new XYZ(rawPoint.X, rawPoint.Y, baseElevation);
                if (normalized.Count == 0 || normalized[normalized.Count - 1].DistanceTo(point) > minDistance)
                {
                    normalized.Add(point);
                }
            }

            if (normalized.Count > 1 && normalized[0].DistanceTo(normalized[normalized.Count - 1]) <= minDistance)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            if (SignedPlanArea(normalized) < 0)
            {
                normalized.Reverse();
            }

            return normalized;
        }

        private static bool HasValidPlanArea(IList<XYZ> points)
        {
            if (points == null || points.Count < 3)
            {
                return false;
            }

            double area = Math.Abs(SignedPlanArea(points));
            double minArea = Math.Pow(UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters), 2);
            return area > minArea;
        }

        private static bool IsSimplePlanPolygon(IList<XYZ> points, out string message)
        {
            message = string.Empty;

            if (points == null || points.Count < 3)
            {
                message = "Please select at least 3 points.";
                return false;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(
                MinimumPointDistanceMm,
                UnitTypeId.Millimeters);

            // Reusing a non-adjacent vertex produces a self-touching CurveLoop
            // that Revit cannot extrude reliably.
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    bool adjacent =
                        j == i + 1 ||
                        (i == 0 && j == points.Count - 1);

                    if (!adjacent && PlanDistance(points[i], points[j]) <= tolerance)
                    {
                        message = "The selected points contain a repeated or touching vertex. Please redraw the restricted area.";
                        return false;
                    }
                }
            }

            // Adjacent polygon edges share a vertex by design. Any intersection
            // between non-adjacent edges means the polygon crosses itself.
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a1 = points[i];
                XYZ a2 = points[(i + 1) % points.Count];

                for (int j = i + 1; j < points.Count; j++)
                {
                    int iNext = (i + 1) % points.Count;
                    int jNext = (j + 1) % points.Count;

                    if (i == j ||
                        iNext == j ||
                        jNext == i)
                    {
                        continue;
                    }

                    XYZ b1 = points[j];
                    XYZ b2 = points[jNext];

                    if (SegmentsIntersect2D(a1, a2, b1, b2, tolerance))
                    {
                        message = "The selected points form a self-intersecting polygon. Please redraw the restricted area without crossing edges.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static double PlanDistance(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool SegmentsIntersect2D(
            XYZ a1,
            XYZ a2,
            XYZ b1,
            XYZ b2,
            double tolerance)
        {
            double o1 = Cross2D(a1, a2, b1);
            double o2 = Cross2D(a1, a2, b2);
            double o3 = Cross2D(b1, b2, a1);
            double o4 = Cross2D(b1, b2, a2);
            double crossTolerance = tolerance * tolerance;

            bool properIntersection =
                ((o1 > crossTolerance && o2 < -crossTolerance) ||
                 (o1 < -crossTolerance && o2 > crossTolerance)) &&
                ((o3 > crossTolerance && o4 < -crossTolerance) ||
                 (o3 < -crossTolerance && o4 > crossTolerance));

            if (properIntersection)
            {
                return true;
            }

            if (Math.Abs(o1) <= crossTolerance && IsPointOnSegment2D(b1, a1, a2, tolerance))
            {
                return true;
            }

            if (Math.Abs(o2) <= crossTolerance && IsPointOnSegment2D(b2, a1, a2, tolerance))
            {
                return true;
            }

            if (Math.Abs(o3) <= crossTolerance && IsPointOnSegment2D(a1, b1, b2, tolerance))
            {
                return true;
            }

            if (Math.Abs(o4) <= crossTolerance && IsPointOnSegment2D(a2, b1, b2, tolerance))
            {
                return true;
            }

            return false;
        }

        private static double Cross2D(XYZ a, XYZ b, XYZ c)
        {
            return ((b.X - a.X) * (c.Y - a.Y)) -
                   ((b.Y - a.Y) * (c.X - a.X));
        }

        private static bool IsPointOnSegment2D(
            XYZ point,
            XYZ start,
            XYZ end,
            double tolerance)
        {
            return point.X >= Math.Min(start.X, end.X) - tolerance &&
                   point.X <= Math.Max(start.X, end.X) + tolerance &&
                   point.Y >= Math.Min(start.Y, end.Y) - tolerance &&
                   point.Y <= Math.Max(start.Y, end.Y) + tolerance;
        }

        private static double SignedPlanArea(IList<XYZ> points)
        {
            if (points == null || points.Count < 3)
            {
                return 0.0;
            }

            double area2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                area2 += (a.X * b.Y) - (b.X * a.Y);
            }

            return area2 / 2.0;
        }

        private static Solid CreateObstacleSolid(IList<XYZ> points, ElementId materialId)
        {
            double height = UnitUtils.ConvertToInternalUnits(DefaultObstacleHeightMm, UnitTypeId.Millimeters);
            CurveLoop loop = new CurveLoop();

            for (int i = 0; i < points.Count; i++)
            {
                XYZ start = points[i];
                XYZ end = points[(i + 1) % points.Count];
                loop.Append(Line.CreateBound(start, end));
            }

            SolidOptions solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height,
                solidOptions);
        }

        private static ElementId CreateTemporaryPickMarker(Document doc, XYZ pickedPoint, double baseElevation)
        {
            if (doc == null || pickedPoint == null)
            {
                return ElementId.InvalidElementId;
            }

            try
            {
                using (Transaction transaction = new Transaction(doc, "Create Path Obstacle Pick Marker"))
                {
                    transaction.Start();

                    ElementId materialId = EnsurePickMarkerMaterial(doc);
                    Solid markerSolid = CreatePickMarkerSolid(new XYZ(pickedPoint.X, pickedPoint.Y, baseElevation), materialId);

                    DirectShape marker = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    marker.ApplicationId = ApplicationId;
                    marker.ApplicationDataId = PickMarkerName;
                    marker.Name = PickMarkerName;
                    marker.SetShape(new List<GeometryObject> { markerSolid });

                    SetStringParameter(marker, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, PickMarkerComment);
                    SetLookupParameter(marker, "IfcExportAs", "DontExport");

                    ElementId markerId = marker.Id;
                    transaction.Commit();
                    return markerId;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstacle] Create temporary pick marker failed: " + ex.Message);
                return ElementId.InvalidElementId;
            }
        }

        private static Solid CreatePickMarkerSolid(XYZ center, ElementId materialId)
        {
            double radius = UnitUtils.ConvertToInternalUnits(PickMarkerRadiusMm, UnitTypeId.Millimeters);
            double height = UnitUtils.ConvertToInternalUnits(PickMarkerHeightMm, UnitTypeId.Millimeters);
            double z = center.Z + UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);

            // Use a short red cylinder approximated by straight segments. It is easy to see while picking
            // and avoids leaving annotation-only elements that would not behave consistently in 3D views.
            CurveLoop loop = new CurveLoop();
            const int segmentCount = 20;
            XYZ previous = null;
            XYZ first = null;

            for (int i = 0; i < segmentCount; i++)
            {
                double angle = 2.0 * Math.PI * i / segmentCount;
                XYZ point = new XYZ(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle), z);

                if (first == null)
                {
                    first = point;
                }

                if (previous != null)
                {
                    loop.Append(Line.CreateBound(previous, point));
                }

                previous = point;
            }

            if (previous != null && first != null)
            {
                loop.Append(Line.CreateBound(previous, first));
            }

            SolidOptions solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                height,
                solidOptions);
        }

        private static void DeleteTemporaryPickMarkers(Document doc, IList<ElementId> markerIds)
        {
            if (doc == null || markerIds == null || markerIds.Count == 0)
            {
                return;
            }

            List<ElementId> validIds = markerIds
                .Where(id => id != null && id != ElementId.InvalidElementId && doc.GetElement(id) != null)
                .Distinct(new ElementIdEqualityComparer())
                .ToList();

            if (validIds.Count == 0)
            {
                markerIds.Clear();
                return;
            }

            try
            {
                using (Transaction transaction = new Transaction(doc, "Delete Path Obstacle Pick Markers"))
                {
                    transaction.Start();
                    doc.Delete(validIds);
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstacle] Delete temporary pick markers failed: " + ex.Message);
            }
            finally
            {
                markerIds.Clear();
            }
        }

        private static ElementId EnsureObstacleMaterial(Document doc)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, MaterialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, MaterialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            Autodesk.Revit.DB.Color obstacleColor = new Autodesk.Revit.DB.Color(255, 145, 145);
            material.Color = obstacleColor;
            material.Transparency = MaterialTransparency;

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill);

            if (solidFill != null)
            {
                material.SurfaceForegroundPatternId = solidFill.Id;
                material.SurfaceForegroundPatternColor = obstacleColor;
            }

            return material.Id;
        }

        private static ElementId EnsurePickMarkerMaterial(Document doc)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, PickMarkerMaterialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, PickMarkerMaterialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            Autodesk.Revit.DB.Color markerColor = new Autodesk.Revit.DB.Color(255, 0, 0);
            material.Color = markerColor;
            material.Transparency = 0;

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill);

            if (solidFill != null)
            {
                material.SurfaceForegroundPatternId = solidFill.Id;
                material.SurfaceForegroundPatternColor = markerColor;
            }

            return material.Id;
        }

        private static string BuildNextObstacleMark(Document doc)
        {
            int existingCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .Count(IsPathObstacleElement);

            return "PATH_OBSTACLE_" + (existingCount + 1).ToString("0000");
        }

        private static bool IsPathObstacleElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            if (string.Equals(element.Name, ObstacleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            return comments != null &&
                   comments.StorageType == StorageType.String &&
                   string.Equals(comments.AsString(), ObstacleComment, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetStringParameter(Element element, BuiltInParameter builtInParameter, string value)
        {
            try
            {
                Parameter parameter = element.get_Parameter(builtInParameter);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                }
            }
            catch
            {
                // Parameter is optional for this category/template.
            }
        }

        private static void SetLookupParameter(Element element, string parameterName, string value)
        {
            try
            {
                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                }
            }
            catch
            {
                // IFC custom parameters are optional and may not exist in every template.
            }
        }


        private sealed class PathObstacleMessageWindow : Window
        {
            private PathObstacleMessageWindow(UIApplication uiApp, string title, string message)
            {
                Title = "CadToRevit - " + (string.IsNullOrWhiteSpace(title) ? "Message" : title);
                Width = 520;
                Height = 260;
                MinWidth = 460;
                MinHeight = 220;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ResizeMode = ResizeMode.NoResize;
                ShowInTaskbar = false;
                Background = Brushes.White;

                AttachOwner(uiApp);
                Content = BuildContent(message);
            }

            public static void Show(UIApplication uiApp, string title, string message)
            {
                PathObstacleMessageWindow window = new PathObstacleMessageWindow(uiApp, title, message);
                window.ShowDialog();
            }

            private UIElement BuildContent(string message)
            {
                Grid root = new Grid
                {
                    Margin = new Thickness(24)
                };

                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock text = new TextBlock
                {
                    Text = message ?? string.Empty,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 52, 140))
                };
                Grid.SetRow(text, 0);
                root.Children.Add(text);

                Button closeButton = new Button
                {
                    Content = "OK",
                    Width = 110,
                    Height = 34,
                    IsDefault = true,
                    IsCancel = true,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 18, 0, 0)
                };
                closeButton.Click += delegate
                {
                    DialogResult = true;
                    Close();
                };
                Grid.SetRow(closeButton, 1);
                root.Children.Add(closeButton);

                return root;
            }

            private void AttachOwner(UIApplication uiApp)
            {
                IntPtr ownerHandle = uiApp != null ? uiApp.MainWindowHandle : IntPtr.Zero;
                if (ownerHandle == IntPtr.Zero)
                {
                    return;
                }

                WindowInteropHelper helper = new WindowInteropHelper(this);
                helper.Owner = ownerHandle;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
        }

        private sealed class ElementIdEqualityComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.IntegerValue == y.IntegerValue;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.IntegerValue.GetHashCode();
            }
        }
    }
}
