using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewMaterialService
    {
        internal static ElementId GetOrCreatePathMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(doc, PathPreviewConstants.PathMaterialName, PathPreviewConstants.PathColor, PathPreviewConstants.PathTransparency);
        }

        internal static ElementId GetOrCreateArrowMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(doc, PathPreviewConstants.ArrowMaterialName, PathPreviewConstants.ArrowColor, PathPreviewConstants.ArrowTransparency);
        }

        internal static ElementId GetOrCreateStartMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(doc, PathPreviewConstants.StartMaterialName, PathPreviewConstants.StartColor, PathPreviewConstants.NodeTransparency);
        }

        internal static ElementId GetOrCreateEndMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(doc, PathPreviewConstants.EndMaterialName, PathPreviewConstants.EndColor, PathPreviewConstants.NodeTransparency);
        }

        internal static ElementId GetOrCreateLabelMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(doc, PathPreviewConstants.LabelMaterialName, PathPreviewConstants.LabelColor, PathPreviewConstants.LabelTransparency);
        }

        internal static ElementId GetOrCreateRedZoneMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(
                doc,
                "EMSD_PATHVIS_MAT_RED_ZONE",
                PathPreviewConstants.RedZoneColor,
                PathPreviewConstants.RedZoneTransparency);
        }

        private static ElementId GetOrCreateMaterialId(Document doc, string name, Color color, int transparency)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name))
            {
                return ElementId.InvalidElementId;
            }

            Material existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            Material material = existing;
            if (material == null)
            {
                ElementId materialId = Material.Create(doc, name);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            material.Color = color;
            material.Transparency = transparency;
            return material.Id;
        }
    }
}
