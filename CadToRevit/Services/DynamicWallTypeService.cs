using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    internal static class DynamicWallTypeService
    {
        private const string SeedWallTypeName = "EMSD_Generic_Seed";
        private const string DynamicWallTypeNamePrefix = "EMSD_Generic_";
        private static readonly Dictionary<string, ElementId> CachedTypeIdsByKey = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            CachedTypeIdsByKey.Clear();
        }

        public static int NormalizeThicknessMm(double thicknessMm)
        {
            int normalized = (int)Math.Round(thicknessMm, MidpointRounding.AwayFromZero);

            // Temporary compatibility workaround: the current door families fail to
            // regenerate when hosted on a wall whose thickness is exactly 100 mm.
            // Keep all other wall thicknesses unchanged.
            if (normalized == 100)
            {
                normalized = 99;
            }

            return Math.Max(1, normalized);
        }

        public static WallType ResolveSeedWallType(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException("doc");
            }

            WallType namedSeed = FindWallTypeByName(doc, SeedWallTypeName);
            if (namedSeed != null && namedSeed.Kind == WallKind.Basic && SafeGetCompoundStructure(namedSeed) != null)
            {
                return namedSeed;
            }

            WallType basicWallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(x => x != null && x.Kind == WallKind.Basic && SafeGetCompoundStructure(x) != null);
            if (basicWallType != null)
            {
                return basicWallType;
            }

            throw new InvalidOperationException("No usable Basic Wall type with CompoundStructure was found.");
        }

        public static WallType GetOrCreateDynamicGenericWallType(
            Document doc,
            double thicknessMm,
            out string normalizedTypeName)
        {
            if (doc == null)
            {
                throw new ArgumentNullException("doc");
            }

            int normalizedThicknessMm = NormalizeThicknessMm(thicknessMm);
            normalizedTypeName = BuildDynamicWallTypeName(normalizedThicknessMm);
            string cacheKey = BuildCacheKey(normalizedTypeName);

            return GetOrCreateWallTypeByTemplate(doc, ResolveSeedWallType(doc), normalizedThicknessMm, normalizedTypeName, cacheKey);
        }

        public static WallType GetOrCreateTemplateThicknessWallType(
            Document doc,
            WallType templateWallType,
            double thicknessMm,
            out string normalizedTypeName)
        {
            if (doc == null)
            {
                throw new ArgumentNullException("doc");
            }

            if (templateWallType == null)
            {
                throw new ArgumentNullException("templateWallType");
            }

            int normalizedThicknessMm = NormalizeThicknessMm(thicknessMm);
            normalizedTypeName = BuildTemplateWallTypeName(templateWallType, normalizedThicknessMm);
            string cacheKey = BuildCacheKey(normalizedTypeName);

            return GetOrCreateWallTypeByTemplate(doc, templateWallType, normalizedThicknessMm, normalizedTypeName, cacheKey);
        }

        private static WallType GetOrCreateWallTypeByTemplate(
            Document doc,
            WallType templateWallType,
            int normalizedThicknessMm,
            string normalizedTypeName,
            string cacheKey)
        {
            ElementId cachedTypeId;
            if (CachedTypeIdsByKey.TryGetValue(cacheKey, out cachedTypeId))
            {
                WallType cachedType = doc.GetElement(cachedTypeId) as WallType;
                if (cachedType != null && cachedType.IsValidObject)
                {
                    return cachedType;
                }

                CachedTypeIdsByKey.Remove(cacheKey);
            }

            WallType existingType = FindWallTypeByName(doc, normalizedTypeName);
            if (existingType != null)
            {
                ApplyThickness(existingType, normalizedThicknessMm);
                CachedTypeIdsByKey[cacheKey] = existingType.Id;
                return existingType;
            }

            ElementType duplicatedElementType = templateWallType.Duplicate(normalizedTypeName);
            WallType dynamicWallType = duplicatedElementType as WallType;
            if (dynamicWallType == null)
            {
                throw new InvalidOperationException("Failed to duplicate the template Basic Wall type.");
            }

            ApplyThickness(dynamicWallType, normalizedThicknessMm);
            CachedTypeIdsByKey[cacheKey] = dynamicWallType.Id;
            return dynamicWallType;
        }

        private static string BuildCacheKey(string wallTypeName)
        {
            return wallTypeName ?? string.Empty;
        }

        private static string BuildDynamicWallTypeName(int normalizedThicknessMm)
        {
            return DynamicWallTypeNamePrefix + normalizedThicknessMm + "mm";
        }

        private static string BuildTemplateWallTypeName(WallType templateWallType, int normalizedThicknessMm)
        {
            string baseName = templateWallType == null || string.IsNullOrWhiteSpace(templateWallType.Name)
                ? "BasicWall"
                : templateWallType.Name.Trim();
            return baseName + "_Custom_" + normalizedThicknessMm + "mm";
        }

        private static WallType FindWallTypeByName(Document doc, string wallTypeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(
                    x => x != null &&
                         string.Equals(x.Name, wallTypeName, StringComparison.OrdinalIgnoreCase));
        }

        private static CompoundStructure SafeGetCompoundStructure(WallType wallType)
        {
            try
            {
                return wallType.GetCompoundStructure();
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyThickness(WallType wallType, int normalizedThicknessMm)
        {
            CompoundStructure compoundStructure = SafeGetCompoundStructure(wallType);
            if (compoundStructure == null)
            {
                throw new InvalidOperationException("The dynamic wall seed type does not expose CompoundStructure.");
            }

            double targetWidthFeet = UnitUtils.ConvertToInternalUnits(normalizedThicknessMm, UnitTypeId.Millimeters);
            IList<CompoundStructureLayer> layers = compoundStructure.GetLayers();

            // Always rebuild a one-layer structure so the final total width exactly matches the target thickness.
            ElementId materialId = ResolveMaterialId(compoundStructure, layers);
            MaterialFunctionAssignment function = ResolveLayerFunction(layers);
            IList<CompoundStructureLayer> singleLayers = new List<CompoundStructureLayer>
            {
                new CompoundStructureLayer(targetWidthFeet, function, materialId)
            };

            CompoundStructure rebuilt = CompoundStructure.CreateSimpleCompoundStructure(singleLayers);
            wallType.SetCompoundStructure(rebuilt);
        }

        private static ElementId ResolveMaterialId(
            CompoundStructure compoundStructure,
            IList<CompoundStructureLayer> layers)
        {
            if (compoundStructure != null && layers != null)
            {
                int structuralMaterialIndex = compoundStructure.StructuralMaterialIndex;
                if (structuralMaterialIndex >= 0 && structuralMaterialIndex < layers.Count)
                {
                    return layers[structuralMaterialIndex].MaterialId;
                }

                if (layers.Count > 0)
                {
                    return layers[0].MaterialId;
                }
            }

            return ElementId.InvalidElementId;
        }

        private static MaterialFunctionAssignment ResolveLayerFunction(IList<CompoundStructureLayer> layers)
        {
            if (layers != null && layers.Count > 0)
            {
                return layers[0].Function;
            }

            return MaterialFunctionAssignment.Structure;
        }
    }
}
