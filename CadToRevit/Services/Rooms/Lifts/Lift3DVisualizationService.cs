using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms.Lifts
{
    public static class Lift3DVisualizationService
    {
        public const string ApplicationId = "CadToRevit.LiftHighlight";
        private const double FallbackHaloRadiusMm = 1300.0;
        private const double CenterPointRadiusMm = 95.0;
        private const double HaloThicknessMm = 30.0;
        private const double CenterPointThicknessMm = 90.0;
        private const double LiftRegionThicknessMm = 80.0;
        private const double VirtualDoorMarkerWidthMm = 90.0;
        private const double TopViewEyeHeightMm = 30000.0;
        private const double FocusHalfSpanMm = 8000.0;
        private static readonly Color HaloColor = new Color(156, 216, 255);
        private static readonly Color RegionColor = new Color(125, 182, 236);
        private static readonly Color CenterPointColor = new Color(0, 0, 0);
        private static readonly Color DoorMarkerColor = new Color(0, 180, 220);

        public static bool Highlight(Document doc, LiftRecognitionRecord lift)
        {
            if (doc == null || lift == null || lift.Position == null || !(doc.ActiveView is View3D))
            {
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Highlight Lift 3D Visualization"))
            {
                tx.Start();
                ClearInternal(doc);

                List<DirectShape> shapes = CreateHighlightShapes(doc, lift);
                if (shapes.Count == 0)
                {
                    tx.RollBack();
                    return false;
                }

                ApplyOverrides(doc.ActiveView as View3D, shapes);
                tx.Commit();
                return true;
            }
        }

        public static bool Focus(UIDocument uiDoc, LiftRecognitionRecord lift)
        {
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || lift == null || lift.Position == null)
            {
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Highlight Lift 3D Visualization"))
            {
                tx.Start();
                ClearInternal(doc);

                List<DirectShape> shapes = CreateHighlightShapes(doc, lift);
                if (shapes.Count == 0)
                {
                    tx.RollBack();
                    return false;
                }

                if (doc.ActiveView is View3D view3D)
                {
                    ApplyTopViewOrientation(view3D, lift.Position);
                    ApplyOverrides(view3D, shapes);
                }

                tx.Commit();
            }
            UIView uiView = uiDoc.GetOpenUIViews()
                .FirstOrDefault(x => x != null && x.ViewId == uiDoc.ActiveView.Id);
            if (uiView != null)
            {
                BoundingBoxXYZ box = BuildFocusBox(lift.Position);
                uiView.ZoomAndCenterRectangle(box.Min, box.Max);
            }

            return true;
        }

        public static bool FocusPreserveView(UIDocument uiDoc, LiftRecognitionRecord lift)
        {
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || lift == null || lift.Position == null)
            {
                return false;
            }

            using (Transaction tx = new Transaction(doc, "Highlight Lift 3D Visualization"))
            {
                tx.Start();
                ClearInternal(doc);

                List<DirectShape> shapes = CreateHighlightShapes(doc, lift);
                if (shapes.Count == 0)
                {
                    tx.RollBack();
                    return false;
                }

                if (doc.ActiveView is View3D view3D)
                {
                    ApplyOverrides(view3D, shapes);
                }

                tx.Commit();
            }

            UIView uiView = uiDoc.GetOpenUIViews()
                .FirstOrDefault(x => x != null && x.ViewId == uiDoc.ActiveView.Id);
            if (uiView != null)
            {
                BoundingBoxXYZ box = BuildFocusBox(lift.Position);
                uiView.ZoomAndCenterRectangle(box.Min, box.Max);
            }

            return true;
        }

        public static int Clear(Document doc)
        {
            if (doc == null)
            {
                return 0;
            }

            using (Transaction tx = new Transaction(doc, "Clear Lift 3D Visualization"))
            {
                tx.Start();
                int deleted = ClearInternal(doc);
                tx.Commit();
                return deleted;
            }
        }

        internal static bool IsManagedLiftElement(Element element)
        {
            DirectShape shape = element as DirectShape;
            if (shape == null)
            {
                return false;
            }

            return string.Equals(shape.ApplicationId, ApplicationId, StringComparison.OrdinalIgnoreCase) ||
                   ((shape.Name ?? string.Empty).StartsWith("EMSD_LIFTVIS_", StringComparison.OrdinalIgnoreCase));
        }

        private static int ClearInternal(Document doc)
        {
            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(IsManagedLiftElement)
                .Select(x => x.Id)
                .ToList();

            if (ids.Count > 0)
            {
                doc.Delete(ids);
            }

            return ids.Count;
        }

        private static List<DirectShape> CreateHighlightShapes(Document doc, LiftRecognitionRecord lift)
        {
            List<DirectShape> shapes = new List<DirectShape>();
            if (doc == null || lift == null || lift.Position == null)
            {
                return shapes;
            }

            Solid region = CreateLiftRegionSolid(lift.BoundaryPoints);
            if (region != null)
            {
                shapes.Add(CreateShape(doc, region, "REGION"));
            }
            else
            {
                Solid fallbackHalo = CreateDiskSolid(lift.Position, FallbackHaloRadiusMm, HaloThicknessMm, 15.0);
                if (fallbackHalo != null)
                {
                    shapes.Add(CreateShape(doc, fallbackHalo, "INNER"));
                }
            }

            Solid centerPoint = CreateDiskSolid(lift.Position, CenterPointRadiusMm, CenterPointThicknessMm, 22.0);
            if (centerPoint != null)
            {
                shapes.Add(CreateShape(doc, centerPoint, "CENTER"));
            }

            return shapes;
        }

        private static DirectShape CreateShape(Document doc, Solid solid, string suffix)
        {
            DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.Name = "EMSD_LIFTVIS_" + (suffix ?? "SHAPE");
            shape.ApplicationId = ApplicationId;
            shape.ApplicationDataId = Guid.NewGuid().ToString("N") + "_" + suffix;
            shape.SetShape(new List<GeometryObject> { solid });
            return shape;
        }

        private static Solid CreateLiftRegionSolid(IList<XYZ> boundaryPoints)
        {
            List<XYZ> points = RemoveConsecutiveDuplicates(boundaryPoints);
            if (points.Count < 3)
            {
                return null;
            }

            if (points[0].DistanceTo(points[points.Count - 1]) > 1e-6)
            {
                points.Add(points[0]);
            }

            if (points.Count < 4)
            {
                return null;
            }

            try
            {
                List<Curve> curves = new List<Curve>();
                for (int i = 0; i < points.Count - 1; i++)
                {
                    if (points[i] == null || points[i + 1] == null || points[i].DistanceTo(points[i + 1]) <= 1e-6)
                    {
                        continue;
                    }

                    curves.Add(Line.CreateBound(points[i], points[i + 1]));
                }

                if (curves.Count < 3)
                {
                    return null;
                }

                CurveLoop loop = CurveLoop.Create(curves);
                double thicknessFt = UnitUtils.ConvertToInternalUnits(LiftRegionThicknessMm, UnitTypeId.Millimeters);
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, thicknessFt);
            }
            catch
            {
                return null;
            }
        }

        private static Solid CreateVirtualDoorMarkerSolid(XYZ start, XYZ end)
        {
            if (start == null || end == null || start.DistanceTo(end) <= 1e-6)
            {
                return null;
            }

            try
            {
                XYZ dir = (end - start).Normalize();
                XYZ perp = new XYZ(-dir.Y, dir.X, 0.0);
                double halfWidth = UnitUtils.ConvertToInternalUnits(VirtualDoorMarkerWidthMm * 0.5, UnitTypeId.Millimeters);
                double zOffset = UnitUtils.ConvertToInternalUnits(105.0, UnitTypeId.Millimeters);
                XYZ s = new XYZ(start.X, start.Y, start.Z + zOffset);
                XYZ e = new XYZ(end.X, end.Y, end.Z + zOffset);
                List<XYZ> rect = new List<XYZ>
                {
                    s - perp.Multiply(halfWidth),
                    e - perp.Multiply(halfWidth),
                    e + perp.Multiply(halfWidth),
                    s + perp.Multiply(halfWidth),
                    s - perp.Multiply(halfWidth)
                };

                CurveLoop loop = CurveLoop.Create(new List<Curve>
                {
                    Line.CreateBound(rect[0], rect[1]),
                    Line.CreateBound(rect[1], rect[2]),
                    Line.CreateBound(rect[2], rect[3]),
                    Line.CreateBound(rect[3], rect[4])
                });
                double thicknessFt = UnitUtils.ConvertToInternalUnits(55.0, UnitTypeId.Millimeters);
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, thicknessFt);
            }
            catch
            {
                return null;
            }
        }

        private static Solid CreateDiskSolid(XYZ center, double radiusMm, double thicknessMm, double offsetMm)
        {
            double radiusFt = UnitUtils.ConvertToInternalUnits(radiusMm, UnitTypeId.Millimeters);
            double thicknessFt = UnitUtils.ConvertToInternalUnits(thicknessMm, UnitTypeId.Millimeters);
            double z = center.Z + UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
            XYZ origin = new XYZ(center.X, center.Y, z);

            CurveLoop loop = new CurveLoop();
            loop.Append(Arc.Create(origin, radiusFt, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            loop.Append(Arc.Create(origin, radiusFt, Math.PI, Math.PI * 2.0, XYZ.BasisX, XYZ.BasisY));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                thicknessFt);
        }

        private static void ApplyOverrides(View3D view3D, IEnumerable<DirectShape> shapes)
        {
            if (view3D == null || shapes == null)
            {
                return;
            }

            foreach (DirectShape shape in shapes)
            {
                if (shape == null || shape.Id == ElementId.InvalidElementId)
                {
                    continue;
                }

                string dataId = shape.ApplicationDataId ?? string.Empty;
                if (dataId.EndsWith("_CENTER", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverride(view3D, shape.Id, CenterPointColor, 0, 4);
                }
                else if (dataId.EndsWith("_REGION", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverride(view3D, shape.Id, RegionColor, 55, 2);
                }
                else if (dataId.EndsWith("_VIRTUAL_DOOR", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverride(view3D, shape.Id, DoorMarkerColor, 0, 3);
                }
                else if (dataId.EndsWith("_INNER", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverride(view3D, shape.Id, HaloColor, 52, 2);
                }
            }
        }

        private static void ApplyOverride(View3D view3D, ElementId elementId, Color color, int transparency, int lineWeight)
        {
            if (view3D == null || elementId == null || elementId == ElementId.InvalidElementId)
            {
                return;
            }

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetCutLineColor(color);
            ogs.SetProjectionLineWeight(lineWeight);
            ogs.SetSurfaceTransparency(transparency);

            FillPatternElement solidFill = new FilteredElementCollector(view3D.Document)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);
            if (solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceBackgroundPatternVisible(true);
                ogs.SetSurfaceBackgroundPatternId(solidFill.Id);
                ogs.SetSurfaceBackgroundPatternColor(color);
            }

            view3D.SetElementOverrides(elementId, ogs);
        }

        private static void ApplyTopViewOrientation(View3D view3D, XYZ center)
        {
            if (view3D == null || center == null)
            {
                return;
            }

            double eyeHeightFt = UnitUtils.ConvertToInternalUnits(TopViewEyeHeightMm, UnitTypeId.Millimeters);
            XYZ eye = new XYZ(center.X, center.Y, center.Z + eyeHeightFt);
            ViewOrientation3D orientation = new ViewOrientation3D(eye, XYZ.BasisY, -XYZ.BasisZ);
            view3D.SetOrientation(orientation);
        }

        private static BoundingBoxXYZ BuildFocusBox(XYZ center)
        {
            double halfSpanFt = UnitUtils.ConvertToInternalUnits(FocusHalfSpanMm, UnitTypeId.Millimeters);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(center.X - halfSpanFt, center.Y - halfSpanFt, center.Z - 1.0),
                Max = new XYZ(center.X + halfSpanFt, center.Y + halfSpanFt, center.Z + 1.0)
            };
        }

        private static List<XYZ> RemoveConsecutiveDuplicates(IList<XYZ> points)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p == null)
                {
                    continue;
                }

                XYZ q = new XYZ(p.X, p.Y, p.Z);
                if (result.Count == 0 || result[result.Count - 1].DistanceTo(q) > 1e-6)
                {
                    result.Add(q);
                }
            }

            if (result.Count >= 2 && result[0].DistanceTo(result[result.Count - 1]) <= 1e-6)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }
    }
}
