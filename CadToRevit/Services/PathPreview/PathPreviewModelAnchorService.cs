using Autodesk.Revit.DB;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewModelAnchorService
    {
        internal sealed class ModelAnchorInfo
        {
            public XYZ ModelMin { get; set; }
            public XYZ ModelMax { get; set; }
            public XYZ ModelCenter { get; set; }
            public XYZ ModelSize { get; set; }
            public XYZ SuggestedPathBasePoint { get; set; }
        }

        internal static ModelAnchorInfo Resolve(View3D previewView, RevitLinkInstance linkInstance)
        {
            BoundingBoxXYZ box = linkInstance == null ? null : (linkInstance.get_BoundingBox(previewView) ?? linkInstance.get_BoundingBox(null));
            if (box == null || box.Min == null || box.Max == null)
            {
                XYZ origin = XYZ.Zero;
                return new ModelAnchorInfo
                {
                    ModelMin = origin,
                    ModelMax = origin,
                    ModelCenter = origin,
                    ModelSize = origin,
                    SuggestedPathBasePoint = origin
                };
            }

            XYZ min = box.Min;
            XYZ max = box.Max;
            XYZ size = max - min;
            XYZ center = new XYZ((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);

            XYZ basePoint = new XYZ(
                min.X + ((max.X - min.X) * 0.15),
                min.Y + ((max.Y - min.Y) * 0.15),
                min.Z);

            if (size.GetLength() <= 1e-9)
            {
                basePoint = center;
            }

            return new ModelAnchorInfo
            {
                ModelMin = min,
                ModelMax = max,
                ModelCenter = center,
                ModelSize = size,
                SuggestedPathBasePoint = basePoint
            };
        }

        internal static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return "(null)";
            }

            return "(" + point.X.ToString("F3") + "," + point.Y.ToString("F3") + "," + point.Z.ToString("F3") + ")";
        }
    }
}
