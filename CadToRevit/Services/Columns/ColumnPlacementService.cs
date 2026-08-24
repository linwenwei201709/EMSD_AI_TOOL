using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadToRevit.Services;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services.Columns
{
    public static class ColumnPlacementService
    {
        public static List<Element> PlaceColumns(
            Document doc,
            IEnumerable<ColumnCandidate> candidates,
            FamilySymbol symbol,
            Level level,
            double heightMm,
            ColumnOrientationSettings orientationSettings)
        {
            List<Element> result = new List<Element>();
            if (doc == null || symbol == null || level == null)
            {
                return result;
            }


            IWallDirectionProvider wallProvider = new RevitWallDirectionProvider(doc);
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            foreach (ColumnCandidate candidate in candidates ?? new List<ColumnCandidate>())
            {
                if (candidate == null || candidate.Center == null)
                {
                    continue;
                }

                if (string.Equals(candidate.ShapeType, "Irregular", System.StringComparison.OrdinalIgnoreCase))
                {
                    Element ds = IrregularColumnPlacementService.PlaceDirectShape(doc, candidate, level, heightMm);
                    if (ds != null)
                    {
                        result.Add(ds);
                    }

                    continue;
                }

                int categoryId = symbol.Category == null ? 0 : symbol.Category.Id.IntegerValue;
                StructuralType structuralType = categoryId == (int)BuiltInCategory.OST_StructuralColumns
                    ? StructuralType.Column
                    : StructuralType.NonStructural;
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    candidate.Center,
                    symbol,
                    level,
                    structuralType);
                if (instance != null)
                {
                    ApplyColumnHeight(instance, symbol, level, heightMm);
                    result.Add(instance);
                    ColumnOrientationService.Align(doc, instance, candidate, wallProvider, orientationSettings);
                }
            }

            return result;
        }

        private static void ApplyColumnHeight(FamilyInstance instance, FamilySymbol symbol, Level baseLevel, double heightMm)
        {
            if (instance == null || baseLevel == null || heightMm <= 0)
            {
                return;
            }

            double heightFt = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
            TrySetElementId(instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM), baseLevel.Id);
            TrySetLength(instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM), 0.0);

            bool heightApplied =
                TrySetLength(instance.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM), heightFt) ||
                RevitParameterSetters.TrySetByNames(instance, heightMm, "Unconnected Height", "Height", "柱高度", "高度") ||
                RevitParameterSetters.TrySetByNames(symbol, heightMm, "Unconnected Height", "Height", "柱高度", "高度");

            Parameter topLevel = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            bool topConstraintApplied = false;
            if (TrySetElementId(topLevel, baseLevel.Id))
            {
                if (TrySetLength(instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM), heightFt))
                {
                    topConstraintApplied = true;
                }
            }

            if (!topConstraintApplied)
            {
                topConstraintApplied = TrySetLength(instance.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM), heightFt);
            }

            if (!heightApplied && !topConstraintApplied)
            {
                RevitParameterSetters.TrySetByNames(instance, heightMm, "Length", "长度");
                RevitParameterSetters.TrySetByNames(symbol, heightMm, "Length", "长度");
            }
        }

        private static bool TrySetLength(Parameter parameter, double value)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            {
                return false;
            }

            try
            {
                parameter.Set(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetElementId(Parameter parameter, ElementId value)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId || value == null)
            {
                return false;
            }

            try
            {
                parameter.Set(value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
