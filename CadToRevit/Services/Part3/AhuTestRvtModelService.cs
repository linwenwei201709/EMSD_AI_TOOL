using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Part3
{
    internal static class AhuTestRvtModelService
    {
        internal const string ApplicationId = "CadToRevit.EMSD.AHU.TestModel";
        internal const string CoreDataId = "EMSD_AHU_CORE";

        private const string CoreComment = "EMSD_AHU_CORE";
        private const string VisualComment = "EMSD_AHU_VISUAL";

        internal static void CreateCleanAhuTestRvt(Application application, string savePath)
        {
            if (application == null)
            {
                throw new ArgumentNullException("application");
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("Save path is empty.", "savePath");
            }

            string folder = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Document testDoc = null;
            try
            {
                testDoc = application.NewProjectDocument(UnitSystem.Metric);
                using (Transaction tx = new Transaction(testDoc, "Create Clean AHU Test Model"))
                {
                    tx.Start();

                    ElementId grayMaterialId = GetOrCreateMaterial(testDoc, "EMSD AHU Body - Dark Grey", new Color(75, 75, 75), 0);
                    ElementId blueMaterialId = GetOrCreateMaterial(testDoc, "EMSD AHU FRONT - Deep Blue", new Color(0, 80, 190), 15);
                    ElementId darkMaterialId = GetOrCreateMaterial(testDoc, "EMSD AHU Intake/Outlet - Near Black", new Color(20, 20, 20), 0);
                    ElementId orangeMaterialId = GetOrCreateMaterial(testDoc, "EMSD AHU Service Clearance - Deep Orange", new Color(255, 85, 0), 45);

                    CreateAhuCore(testDoc, grayMaterialId);
                    CreateFrontFace(testDoc, blueMaterialId);
                    CreateAccessPanels(testDoc, darkMaterialId);
                    CreateServiceClearanceShell(testDoc, orangeMaterialId);
                    CreateDirectionAndFrameLines(testDoc);

                    tx.Commit();
                }

                SaveAsOptions options = new SaveAsOptions
                {
                    OverwriteExistingFile = true,
                    Compact = true
                };
                testDoc.SaveAs(savePath, options);
            }
            finally
            {
                if (testDoc != null && testDoc.IsValidObject)
                {
                    try
                    {
                        testDoc.Close(false);
                    }
                    catch
                    {
                        // Ignore close errors. The RVT has already been saved or the caller will receive the original exception.
                    }
                }
            }
        }

        private static void CreateAhuCore(Document doc, ElementId materialId)
        {
            // Core AHU size: 2200 x 1000 x 2200 mm. Bottom is Z=0 and the model is centered around the RVT origin.
            XYZ min = new XYZ(Mm(-1100), Mm(-500), Mm(0));
            XYZ max = new XYZ(Mm(1100), Mm(500), Mm(2200));
            DirectShape shape = CreateBoxDirectShape(doc, min, max, materialId, CoreDataId, CoreComment);
            TrySetComments(shape, CoreComment);
        }

        private static void CreateFrontFace(Document doc, ElementId materialId)
        {
            // Blue thin panel on +Y side marks AHU FRONT direction.
            XYZ min = new XYZ(Mm(-800), Mm(505), Mm(300));
            XYZ max = new XYZ(Mm(800), Mm(555), Mm(1900));
            DirectShape shape = CreateBoxDirectShape(doc, min, max, materialId, "EMSD_AHU_FRONT_FACE", VisualComment);
            TrySetComments(shape, "EMSD_AHU_FRONT_FACE");
        }

        private static void CreateAccessPanels(Document doc, ElementId materialId)
        {
            // Simple intake / outlet blocks, only for visual checking.
            CreateBoxDirectShape(
                doc,
                new XYZ(Mm(-1000), Mm(-320), Mm(550)),
                new XYZ(Mm(-780), Mm(320), Mm(1650)),
                materialId,
                "EMSD_AHU_INTAKE",
                VisualComment);

            CreateBoxDirectShape(
                doc,
                new XYZ(Mm(780), Mm(-320), Mm(550)),
                new XYZ(Mm(1000), Mm(320), Mm(1650)),
                materialId,
                "EMSD_AHU_OUTLET",
                VisualComment);
        }

        private static void CreateServiceClearanceShell(Document doc, ElementId materialId)
        {
            // Semi-transparent service area in front of the AHU. Link placement ignores it because the core DirectShape is explicitly tagged.
            CreateBoxDirectShape(
                doc,
                new XYZ(Mm(-1100), Mm(550), Mm(0)),
                new XYZ(Mm(1100), Mm(1250), Mm(2200)),
                materialId,
                "EMSD_AHU_SERVICE_CLEARANCE",
                VisualComment);
        }

        private static DirectShape CreateBoxDirectShape(Document doc, XYZ min, XYZ max, ElementId materialId, string dataId, string comment)
        {
            Solid solid = CreateBoxSolid(min, max, materialId);
            DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.ApplicationId = ApplicationId;
            shape.ApplicationDataId = dataId ?? Guid.NewGuid().ToString("N");
            shape.SetShape(new List<GeometryObject> { solid });
            TrySetMaterial(shape, materialId);
            TrySetComments(shape, comment);
            return shape;
        }

        private static Solid CreateBoxSolid(XYZ min, XYZ max, ElementId materialId)
        {
            CurveLoop loop = new CurveLoop();
            XYZ p1 = new XYZ(min.X, min.Y, min.Z);
            XYZ p2 = new XYZ(max.X, min.Y, min.Z);
            XYZ p3 = new XYZ(max.X, max.Y, min.Z);
            XYZ p4 = new XYZ(min.X, max.Y, min.Z);
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));
            SolidOptions options = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, max.Z - min.Z, options);
        }

        private static void CreateDirectionAndFrameLines(Document doc)
        {
            GraphicsStyle darkStyle = GetOrCreateLineStyle(doc, "EMSD AHU Frame", new Color(0, 0, 0));
            GraphicsStyle orangeStyle = GetOrCreateLineStyle(doc, "EMSD AHU Front Arrow", new Color(255, 55, 0));

            double x0 = Mm(-1100);
            double x1 = Mm(1100);
            double y0 = Mm(-500);
            double y1 = Mm(500);
            double z0 = Mm(0);
            double z1 = Mm(2200);

            AddModelLine(doc, new XYZ(x0, y0, z0), new XYZ(x1, y0, z0), darkStyle);
            AddModelLine(doc, new XYZ(x1, y0, z0), new XYZ(x1, y1, z0), darkStyle);
            AddModelLine(doc, new XYZ(x1, y1, z0), new XYZ(x0, y1, z0), darkStyle);
            AddModelLine(doc, new XYZ(x0, y1, z0), new XYZ(x0, y0, z0), darkStyle);

            AddModelLine(doc, new XYZ(x0, y0, z1), new XYZ(x1, y0, z1), darkStyle);
            AddModelLine(doc, new XYZ(x1, y0, z1), new XYZ(x1, y1, z1), darkStyle);
            AddModelLine(doc, new XYZ(x1, y1, z1), new XYZ(x0, y1, z1), darkStyle);
            AddModelLine(doc, new XYZ(x0, y1, z1), new XYZ(x0, y0, z1), darkStyle);

            AddModelLine(doc, new XYZ(x0, y0, z0), new XYZ(x0, y0, z1), darkStyle);
            AddModelLine(doc, new XYZ(x1, y0, z0), new XYZ(x1, y0, z1), darkStyle);
            AddModelLine(doc, new XYZ(x1, y1, z0), new XYZ(x1, y1, z1), darkStyle);
            AddModelLine(doc, new XYZ(x0, y1, z0), new XYZ(x0, y1, z1), darkStyle);

            // FRONT direction arrow on floor level, pointing toward +Y.
            XYZ start = new XYZ(Mm(0), Mm(650), Mm(80));
            XYZ end = new XYZ(Mm(0), Mm(1350), Mm(80));
            AddModelLine(doc, start, end, orangeStyle);
            AddModelLine(doc, end, new XYZ(Mm(-180), Mm(1120), Mm(80)), orangeStyle);
            AddModelLine(doc, end, new XYZ(Mm(180), Mm(1120), Mm(80)), orangeStyle);

            // Front-side maintenance rectangle on floor, easy to see after linking.
            AddModelLine(doc, new XYZ(Mm(-1100), Mm(550), Mm(40)), new XYZ(Mm(1100), Mm(550), Mm(40)), orangeStyle);
            AddModelLine(doc, new XYZ(Mm(1100), Mm(550), Mm(40)), new XYZ(Mm(1100), Mm(1250), Mm(40)), orangeStyle);
            AddModelLine(doc, new XYZ(Mm(1100), Mm(1250), Mm(40)), new XYZ(Mm(-1100), Mm(1250), Mm(40)), orangeStyle);
            AddModelLine(doc, new XYZ(Mm(-1100), Mm(1250), Mm(40)), new XYZ(Mm(-1100), Mm(550), Mm(40)), orangeStyle);
        }

        private static void AddModelLine(Document doc, XYZ start, XYZ end, GraphicsStyle lineStyle)
        {
            if (start == null || end == null || start.DistanceTo(end) < 0.000001)
            {
                return;
            }

            Line line = Line.CreateBound(start, end);
            XYZ direction = (end - start).Normalize();
            XYZ normal = direction.CrossProduct(XYZ.BasisZ);
            if (normal.GetLength() < 0.000001)
            {
                normal = XYZ.BasisX;
            }
            normal = normal.Normalize();

            Plane plane = Plane.CreateByNormalAndOrigin(normal, start);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
            ModelCurve curve = doc.Create.NewModelCurve(line, sketchPlane);
            if (curve != null && lineStyle != null)
            {
                try
                {
                    curve.LineStyle = lineStyle;
                }
                catch
                {
                }
            }
        }

        private static ElementId GetOrCreateMaterial(Document doc, string name, Color color, int transparency)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId id = Material.Create(doc, name);
                material = doc.GetElement(id) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            material.Color = color;
            material.Transparency = Math.Max(0, Math.Min(100, transparency));
            return material.Id;
        }

        private static GraphicsStyle GetOrCreateLineStyle(Document doc, string name, Color color)
        {
            try
            {
                Category lineCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lineCategory == null)
                {
                    return null;
                }

                Category subCategory = null;
                foreach (Category category in lineCategory.SubCategories)
                {
                    if (string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        subCategory = category;
                        break;
                    }
                }

                if (subCategory == null)
                {
                    subCategory = doc.Settings.Categories.NewSubcategory(lineCategory, name);
                }

                subCategory.LineColor = color;
                try
                {
                    subCategory.SetLineWeight(5, GraphicsStyleType.Projection);
                }
                catch
                {
                }

                return subCategory.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch
            {
                return null;
            }
        }

        private static void TrySetMaterial(Element element, ElementId materialId)
        {
            if (element == null || materialId == null || materialId == ElementId.InvalidElementId)
            {
                return;
            }

            try
            {
                Parameter parameter = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.ElementId)
                {
                    parameter.Set(materialId);
                }
            }
            catch
            {
            }
        }

        private static void TrySetComments(Element element, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value);
                }
            }
            catch
            {
            }
        }

        private static double Mm(double value)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
        }
    }
}
