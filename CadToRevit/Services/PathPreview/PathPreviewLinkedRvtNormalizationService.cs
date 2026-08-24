using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewLinkedRvtNormalizationService
    {
        private static readonly Color SurfaceColor = new Color(210, 210, 210);
        private static readonly Color LineColor = new Color(80, 80, 80);

        internal static void NormalizeLinkedRvtDocument(Document linkedDoc)
        {
            if (linkedDoc == null)
            {
                throw new InvalidOperationException("Normalize linked RVT failed: document is null.");
            }

            try
            {
                using (Transaction tx = new Transaction(linkedDoc, "Normalize Linked RVT"))
                {
                    tx.Start();

                    ElementId grayMaterialId = GetOrCreateGrayMaterialId(linkedDoc);
                    NormalizeAllMaterials(linkedDoc);
                    NormalizeCategories(linkedDoc, grayMaterialId);
                    NormalizeElementMaterialParameters(linkedDoc, grayMaterialId);

                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug("[PathPreview] NormalizeLinkedRvt.Success");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] NormalizeLinkedRvt.Failed error=" + ex);
                throw new InvalidOperationException("Normalize linked RVT failed: " + ex.Message, ex);
            }
        }

        private static ElementId GetOrCreateGrayMaterialId(Document doc)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, PathPreviewConstants.GrayMaterialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, PathPreviewConstants.GrayMaterialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            material.Color = SurfaceColor;
            material.Transparency = 0;
            return material.Id;
        }

        private static void NormalizeAllMaterials(Document doc)
        {
            foreach (Material material in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
            {
                if (material == null)
                {
                    continue;
                }

                material.Color = SurfaceColor;
                material.Transparency = 0;
            }
        }

        private static void NormalizeCategories(Document doc, ElementId grayMaterialId)
        {
            foreach (BuiltInCategory bic in GetTargetCategories())
            {
                Category category = null;
                try
                {
                    category = doc.Settings.Categories.get_Item(bic);
                }
                catch
                {
                }

                if (category == null)
                {
                    continue;
                }

                try
                {
                    category.LineColor = LineColor;
                }
                catch
                {
                }

                if (grayMaterialId != ElementId.InvalidElementId)
                {
                    try
                    {
                        category.Material = doc.GetElement(grayMaterialId) as Material;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void NormalizeElementMaterialParameters(Document doc, ElementId grayMaterialId)
        {
            if (grayMaterialId == ElementId.InvalidElementId)
            {
                return;
            }

            foreach (BuiltInCategory bic in GetTargetCategories())
            {
                IEnumerable<Element> elements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .Where(x => x != null && !x.ViewSpecific);

                foreach (Element element in elements)
                {
                    TryAssignMaterial(element, BuiltInParameter.MATERIAL_ID_PARAM, grayMaterialId);
                    TryAssignMaterial(element, BuiltInParameter.STRUCTURAL_MATERIAL_PARAM, grayMaterialId);
                }
            }
        }

        private static void TryAssignMaterial(Element element, BuiltInParameter parameterId, ElementId materialId)
        {
            if (element == null || materialId == ElementId.InvalidElementId)
            {
                return;
            }

            try
            {
                Parameter parameter = element.get_Parameter(parameterId);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId)
                {
                    return;
                }

                parameter.Set(materialId);
            }
            catch
            {
            }
        }

        private static IEnumerable<BuiltInCategory> GetTargetCategories()
        {
            return new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Railings,
                BuiltInCategory.OST_GenericModel
            };
        }
    }
}
