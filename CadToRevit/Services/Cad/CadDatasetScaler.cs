using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Units;
using System.Collections.Generic;

namespace CadToRevit.Services.Cad
{
    public static class CadDatasetScaler
    {
        public static CadDataset Scale(CadDataset source, UnitContext unitContext)
        {
            CadDataset scaled = new CadDataset();
            if (source == null || source.Segments == null)
            {
                return scaled;
            }

            double scale = unitContext == null ? 1.0 : unitContext.ScaleToFeet;
            Dictionary<int, CadSegment> copied = new Dictionary<int, CadSegment>();
            foreach (CadSegment s in source.Segments)
            {
                CadSegment c = CopySegment(s, scale);
                scaled.Segments.Add(c);
                copied[c.SegmentId] = c;
            }

            foreach (KeyValuePair<string, List<CadSegment>> kv in source.SegmentsByRawLayer)
            {
                List<CadSegment> list = new List<CadSegment>();
                foreach (CadSegment s in kv.Value)
                {
                    if (copied.ContainsKey(s.SegmentId))
                    {
                        list.Add(copied[s.SegmentId]);
                    }
                }

                scaled.SegmentsByRawLayer[kv.Key] = list;
            }

            Dictionary<CadText, CadText> copiedTexts = new Dictionary<CadText, CadText>();
            foreach (CadText t in source.Texts ?? new List<CadText>())
            {
                if (t == null)
                {
                    continue;
                }

                CadText c = new CadText
                {
                    RawLayerName = t.RawLayerName,
                    Text = t.Text,
                    Position = ScalePoint(t.Position, scale),
                    RotationRad = t.RotationRad,
                    RawCadX = t.RawCadX,
                    RawCadY = t.RawCadY,
                    RawCadZ = t.RawCadZ,
                    CadFeetX = t.CadFeetX * scale,
                    CadFeetY = t.CadFeetY * scale,
                    CadFeetZ = t.CadFeetZ * scale
                };
                copiedTexts[t] = c;
                scaled.Texts.Add(c);
            }

            foreach (KeyValuePair<string, List<CadText>> kv in source.TextsByRawLayer)
            {
                List<CadText> list = new List<CadText>();
                foreach (CadText t in kv.Value ?? new List<CadText>())
                {
                    if (t != null && copiedTexts.ContainsKey(t))
                    {
                        list.Add(copiedTexts[t]);
                    }
                }

                scaled.TextsByRawLayer[kv.Key] = list;
            }

            return scaled;
        }

        private static CadSegment CopySegment(CadSegment source, double scale)
        {
            return new CadSegment
            {
                SegmentId = source.SegmentId,
                NormalizedLayer = source.NormalizedLayer,
                SemanticLayer = source.SemanticLayer,
                LayerName = source.LayerName,
                RawLayerName = source.RawLayerName,
                SourceType = source.SourceType,
                P0 = ScalePoint(source.P0, scale),
                P1 = ScalePoint(source.P1, scale),
                IsArc = source.IsArc,
                Center = ScalePoint(source.Center, scale),
                RadiusFeet = source.RadiusFeet * scale,
                SweepAngleRad = source.SweepAngleRad,
                MidPoint = ScalePoint(source.MidPoint, scale)
            };
        }

        private static XYZ ScalePoint(XYZ p, double scale)
        {
            if (p == null)
            {
                return null;
            }

            return new XYZ(p.X * scale, p.Y * scale, p.Z * scale);
        }
    }
}
