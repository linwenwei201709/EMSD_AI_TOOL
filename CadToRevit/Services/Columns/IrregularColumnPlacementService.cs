using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Columns
{
    public static class IrregularColumnPlacementService
    {
        private const double DefaultHeightFt = 4000.0 / 304.8;

        public static Element PlaceDirectShape(Document doc, ColumnCandidate candidate, Level level, double heightMm)
        {
            if (doc == null || candidate == null || level == null)
            {
                return null;
            }

            if (!string.Equals(candidate.ShapeType, "Irregular", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            List<XYZ> footprint = NormalizeFootprint(candidate.Footprint);
            if (footprint.Count < 4)
            {
                return null;
            }

            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < footprint.Count - 1; i++)
            {
                XYZ p0 = footprint[i];
                XYZ p1 = footprint[i + 1];
                if (p0.DistanceTo(p1) <= 1e-9)
                {
                    continue;
                }

                loop.Append(Line.CreateBound(p0, p1));
            }

            double heightFt = ResolveHeightFt(doc, level, heightMm);
            if (heightFt <= 1e-6)
            {
                heightFt = DefaultHeightFt;
            }

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                heightFt);
            if (solid == null || solid.Volume <= 1e-9)
            {
                return null;
            }

            ElementId categoryId = new ElementId(BuiltInCategory.OST_StructuralColumns);
            DirectShape ds = DirectShape.CreateElement(doc, categoryId);
            if (ds == null)
            {
                ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            }

            if (ds == null)
            {
                return null;
            }

            ds.ApplicationId = "CadToRevit.Column";
            ds.ApplicationDataId = "IrregularColumn_" + candidate.ClusterId;
            ds.SetShape(new GeometryObject[] { solid });
            // 中文注释：标识异形柱来源，便于后续筛查与售后定位。
            Parameter comments = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set("CAD Irregular Column");
            }

            return ds;
        }

        private static List<XYZ> NormalizeFootprint(List<XYZ> points)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in points ?? new List<XYZ>())
            {
                if (p == null)
                {
                    continue;
                }

                XYZ planar = new XYZ(p.X, p.Y, 0.0);
                if (result.Count == 0 || result[result.Count - 1].DistanceTo(planar) > 1e-9)
                {
                    result.Add(planar);
                }
            }

            if (result.Count >= 3 && result[0].DistanceTo(result[result.Count - 1]) > 1e-9)
            {
                result.Add(result[0]);
            }

            return result;
        }

        private static double ResolveHeightFt(Document doc, Level baseLevel, double heightMm)
        {
            if (heightMm > 0)
            {
                return UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
            }

            if (doc == null || baseLevel == null)
            {
                return DefaultHeightFt;
            }

            // 中文注释：优先用“下一标高 - 当前标高”作为异形柱拉伸高度，缺失时退回默认值。
            Level next = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x != null && x.Id != baseLevel.Id && x.Elevation > baseLevel.Elevation + 1e-6)
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            if (next != null)
            {
                double h = next.Elevation - baseLevel.Elevation;
                if (h > 1e-6)
                {
                    return h;
                }
            }

            return DefaultHeightFt;
        }
    }
}
