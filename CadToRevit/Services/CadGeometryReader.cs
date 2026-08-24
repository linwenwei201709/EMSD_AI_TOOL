using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Services
{
    public static class CadGeometryReader
    {
        public static IEnumerable<CadGeometryData> ReadGeometryItems(Document doc, ImportInstance importInstance)
        {
            List<CadGeometryData> result = new List<CadGeometryData>();
            if (doc == null || importInstance == null)
            {
                return result;
            }

            Options options = new Options
            {
                IncludeNonVisibleObjects = true,
                ComputeReferences = false
            };

            GeometryElement geometryElement = importInstance.get_Geometry(options);
            if (geometryElement == null)
            {
                return result;
            }

            foreach (GeometryObject obj in geometryElement)
            {
                ReadGeometryObject(doc, obj, result);
            }

            return result;
        }

        private static void ReadGeometryObject(Document doc, GeometryObject obj, ICollection<CadGeometryData> output)
        {
            if (obj == null)
            {
                return;
            }

            GeometryInstance geometryInstance = obj as GeometryInstance;
            if (geometryInstance != null)
            {
                GeometryElement nestedGeometry = geometryInstance.GetInstanceGeometry();
                if (nestedGeometry == null)
                {
                    return;
                }

                foreach (GeometryObject nestedObj in nestedGeometry)
                {
                    ReadGeometryObject(doc, nestedObj, output);
                }

                return;
            }

            string layerName = LayerNameResolver.ResolveLayerName(doc, obj);
            string rawLayerName = LayerNameResolver.ResolveRawLayerName(doc, obj);

            Line line = obj as Line;
            if (line != null)
            {
                output.Add(new CadGeometryData(layerName, rawLayerName, "Line"));
                return;
            }

            PolyLine polyLine = obj as PolyLine;
            if (polyLine != null)
            {
                output.Add(new CadGeometryData(layerName, rawLayerName, "PolyLine"));
                return;
            }

            Arc arc = obj as Arc;
            if (arc != null)
            {
                output.Add(new CadGeometryData(layerName, rawLayerName, "Arc"));
                return;
            }

            Curve curve = obj as Curve;
            if (curve != null)
            {
                output.Add(new CadGeometryData(layerName, rawLayerName, "OtherCurve"));
            }
        }
    }

    public class CadGeometryData
    {
        public CadGeometryData(string layerName, string rawLayerName, string geometryType)
        {
            LayerName = layerName;
            RawLayerName = rawLayerName;
            GeometryType = geometryType;
        }

        public string LayerName { get; private set; }

        public string RawLayerName { get; private set; }

        public string GeometryType { get; private set; }
    }
}
