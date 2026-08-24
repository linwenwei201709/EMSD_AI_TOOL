using Autodesk.Revit.DB;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class Room3DVisualizationMaterialService
    {
        internal static ElementId GetOrCreateNormalMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(
                doc,
                Room3DVisualizationConstants.MaterialNormalName,
                Room3DVisualizationConstants.RegionNormalColor,
                Room3DVisualizationConstants.RegionNormalTransparency);
        }

        internal static ElementId GetOrCreateHighlightMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(
                doc,
                Room3DVisualizationConstants.MaterialHighlightName,
                Room3DVisualizationConstants.RegionHighlightColor,
                Room3DVisualizationConstants.RegionHighlightTransparency);
        }

        internal static ElementId GetOrCreateMarkerNormalMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(
                doc,
                Room3DVisualizationConstants.MaterialNormalName + "_MARKER",
                Room3DVisualizationConstants.MarkerNormalColor,
                Room3DVisualizationConstants.MarkerNormalTransparency);
        }

        internal static ElementId GetOrCreateMarkerHighlightMaterialId(Document doc)
        {
            return GetOrCreateMaterialId(
                doc,
                Room3DVisualizationConstants.MaterialHighlightName + "_MARKER",
                Room3DVisualizationConstants.MarkerHighlightColor,
                Room3DVisualizationConstants.MarkerHighlightTransparency);
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
                .FirstOrDefault(x => string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));

            Material mat = existing;
            if (mat == null)
            {
                ElementId id = Material.Create(doc, name);
                mat = doc.GetElement(id) as Material;
            }

            if (mat == null)
            {
                return ElementId.InvalidElementId;
            }

            mat.Color = color;
            mat.Transparency = transparency;
            return mat.Id;
        }
    }
}
