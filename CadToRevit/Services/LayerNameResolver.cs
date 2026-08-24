using Autodesk.Revit.DB;

namespace CadToRevit.Services
{
    public static class LayerNameResolver
    {
        public static string ResolveLayerName(Document doc, GeometryObject geometryObject)
        {
            if (doc == null || geometryObject == null)
            {
                return "UNKNOWN";
            }

            string rawName = ResolveRawLayerName(doc, geometryObject);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "UNKNOWN";
            }

            string normalized = ExtractLayerSuffix(rawName).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "UNKNOWN";
            }

            return normalized.ToUpperInvariant();
        }

        public static string ResolveRawLayerName(Document doc, GeometryObject geometryObject)
        {
            if (doc == null || geometryObject == null)
            {
                return "UNKNOWN";
            }

            string rawName = TryResolveRawLayerName(doc, geometryObject);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "UNKNOWN";
            }

            string normalizedRaw = NormalizeRawLayerName(rawName);
            return string.IsNullOrWhiteSpace(normalizedRaw) ? "UNKNOWN" : normalizedRaw;
        }

        private static string ExtractLayerSuffix(string rawName)
        {
            int pipeIndex = rawName.LastIndexOf('|');
            if (pipeIndex >= 0 && pipeIndex < rawName.Length - 1)
            {
                return rawName.Substring(pipeIndex + 1);
            }

            int colonIndex = rawName.LastIndexOf(':');
            if (colonIndex >= 0 && colonIndex < rawName.Length - 1)
            {
                return rawName.Substring(colonIndex + 1);
            }

            return rawName;
        }

        private static string TryResolveRawLayerName(Document doc, GeometryObject geometryObject)
        {
            string direct = TryGetGraphicsStyleLayerName(doc, geometryObject);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            GeometryInstance geometryInstance = geometryObject as GeometryInstance;
            if (geometryInstance == null)
            {
                return null;
            }

            GeometryElement nestedGeometry = geometryInstance.GetInstanceGeometry();
            if (nestedGeometry == null)
            {
                return null;
            }

            foreach (GeometryObject nestedObject in nestedGeometry)
            {
                string nestedName = TryResolveRawLayerName(doc, nestedObject);
                if (!string.IsNullOrWhiteSpace(nestedName))
                {
                    return nestedName;
                }
            }

            return null;
        }

        private static string TryGetGraphicsStyleLayerName(Document doc, GeometryObject geometryObject)
        {
            if (geometryObject == null || geometryObject.GraphicsStyleId == ElementId.InvalidElementId)
            {
                return null;
            }

            GraphicsStyle graphicsStyle = doc.GetElement(geometryObject.GraphicsStyleId) as GraphicsStyle;
            if (graphicsStyle == null || graphicsStyle.GraphicsStyleCategory == null)
            {
                return null;
            }

            return graphicsStyle.GraphicsStyleCategory.Name;
        }

        private static string NormalizeRawLayerName(string rawName)
        {
            string value = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] splitters = new[] { '|', '/', '@' };
            foreach (char splitter in splitters)
            {
                int index = value.LastIndexOf(splitter);
                if (index >= 0 && index < value.Length - 1)
                {
                    value = value.Substring(index + 1).Trim();
                }
            }

            int colonIndex = value.LastIndexOf(':');
            if (colonIndex >= 0 && colonIndex < value.Length - 1)
            {
                value = value.Substring(colonIndex + 1).Trim();
            }

            return value;
        }
    }
}
