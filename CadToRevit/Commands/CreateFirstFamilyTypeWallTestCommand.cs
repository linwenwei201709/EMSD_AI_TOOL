using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateFirstFamilyTypeWallTestCommand : IExternalCommand
    {
        private const double TestWallLengthMm = 20000.0;
        private const double TestWallHeightMm = 8000.0;
        private const double TestWallThicknessMm = 1000.0;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Level level = FindTargetLevel(doc, doc.ActiveView);
            if (level == null)
            {
                message = "No usable level was found in the current document.";
                TaskDialog.Show("测试FamilyType建墙", "没有可用 Level，无法创建测试墙。");
                return Result.Failed;
            }

            WallType wallType = FindFirstBasicWallType(doc);
            if (wallType == null)
            {
                message = "No usable Basic WallType was found in the current document.";
                TaskDialog.Show("测试FamilyType建墙", "没有可用的 Basic WallType，无法创建测试墙。");
                return Result.Failed;
            }

            Wall createdWall = null;
            WallType customWallType = null;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create First FamilyType Wall Test"))
                {
                    tx.Start();
                    customWallType = GetOrCreateCustomWallType(doc, wallType, TestWallThicknessMm);
                    createdWall = CreateWallInstance(doc, level, customWallType);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("测试FamilyType建墙", "Wall.Create 失败：" + ex.Message);
                return Result.Failed;
            }

            if (createdWall != null)
            {
                ICollection<ElementId> createdIds = new List<ElementId> { createdWall.Id };
                uiDoc.Selection.SetElementIds(createdIds);
                uiDoc.ShowElements(createdIds);
            }

            string successMessage =
                "测试墙创建成功。\n" +
                "模板类型：" + wallType.Name + "\n" +
                "新类型：" + (customWallType == null ? string.Empty : customWallType.Name) + "\n" +
                "Length：20 m\n" +
                "Height：8 m\n" +
                "Level：" + level.Name + "\n" +
                "目标厚度：1000 mm";
            TaskDialog.Show("测试FamilyType建墙", successMessage);
            return Result.Succeeded;
        }

        private static Wall CreateWallInstance(Document doc, Level level, WallType wallType)
        {
            double wallLengthFeet = UnitUtils.ConvertToInternalUnits(TestWallLengthMm, UnitTypeId.Millimeters);
            double wallHeightFeet = UnitUtils.ConvertToInternalUnits(TestWallHeightMm, UnitTypeId.Millimeters);

            // Place a simple horizontal wall on the resolved level for FamilyType-based creation verification.
            XYZ start = new XYZ(0.0, 0.0, level.Elevation);
            XYZ end = new XYZ(wallLengthFeet, 0.0, level.Elevation);
            Line line = Line.CreateBound(start, end);

            Wall wall = Wall.Create(
                doc,
                line,
                wallType.Id,
                level.Id,
                wallHeightFeet,
                0.0,
                false,
                false);

            Parameter heightParameter = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParameter != null && !heightParameter.IsReadOnly)
            {
                heightParameter.Set(wallHeightFeet);
            }

            return wall;
        }

        private static WallType GetOrCreateCustomWallType(Document doc, WallType templateWallType, double targetThicknessMm)
        {
            int normalizedThicknessMm = (int)Math.Round(targetThicknessMm, MidpointRounding.AwayFromZero);
            string newTypeName = BuildCustomWallTypeName(templateWallType, normalizedThicknessMm);
            WallType existingType = FindWallTypeByName(doc, newTypeName);
            if (existingType != null)
            {
                double existingWidthMm = GetWallTypeWidthMm(existingType);
                if (Math.Abs(existingWidthMm - normalizedThicknessMm) <= 0.5)
                {
                    return existingType;
                }

                ApplyWallTypeThickness(existingType, normalizedThicknessMm);
                return existingType;
            }

            ElementType duplicatedElementType = templateWallType.Duplicate(newTypeName);
            WallType duplicatedWallType = duplicatedElementType as WallType;
            if (duplicatedWallType == null)
            {
                throw new InvalidOperationException("Failed to duplicate the template WallType.");
            }

            ApplyWallTypeThickness(duplicatedWallType, normalizedThicknessMm);
            return duplicatedWallType;
        }

        private static string BuildCustomWallTypeName(WallType templateWallType, int normalizedThicknessMm)
        {
            string baseName = templateWallType == null || string.IsNullOrWhiteSpace(templateWallType.Name)
                ? "BasicWall"
                : templateWallType.Name.Trim();
            return baseName + "_Custom_" + normalizedThicknessMm + "mm";
        }

        private static WallType FindFirstBasicWallType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(x => x != null && x.Kind == WallKind.Basic && SafeGetCompoundStructure(x) != null);
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

        private static WallType FindWallTypeByName(Document doc, string wallTypeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(
                    x => x != null &&
                         string.Equals(x.Name, wallTypeName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyWallTypeThickness(WallType wallType, int normalizedThicknessMm)
        {
            CompoundStructure compoundStructure = SafeGetCompoundStructure(wallType);
            if (compoundStructure == null)
            {
                throw new InvalidOperationException("The selected template WallType does not expose CompoundStructure.");
            }

            double targetWidthFeet = UnitUtils.ConvertToInternalUnits(normalizedThicknessMm, UnitTypeId.Millimeters);
            IList<CompoundStructureLayer> existingLayers = compoundStructure.GetLayers();
            ElementId materialId = ResolveMaterialId(compoundStructure, existingLayers);
            MaterialFunctionAssignment function = ResolveLayerFunction(existingLayers);

            // Always rebuild a single-layer structure for multi-layer templates so the final total width matches the target.
            IList<CompoundStructureLayer> rebuiltLayers = new List<CompoundStructureLayer>
            {
                new CompoundStructureLayer(targetWidthFeet, function, materialId)
            };

            CompoundStructure rebuilt = CompoundStructure.CreateSimpleCompoundStructure(rebuiltLayers);
            wallType.SetCompoundStructure(rebuilt);
        }

        private static double GetWallTypeWidthMm(WallType wallType)
        {
            if (wallType == null)
            {
                return 0.0;
            }

            try
            {
                if (wallType.Width > 1e-9)
                {
                    return UnitUtils.ConvertFromInternalUnits(wallType.Width, UnitTypeId.Millimeters);
                }
            }
            catch
            {
            }

            CompoundStructure compoundStructure = SafeGetCompoundStructure(wallType);
            if (compoundStructure != null)
            {
                return UnitUtils.ConvertFromInternalUnits(compoundStructure.GetWidth(), UnitTypeId.Millimeters);
            }

            return 0.0;
        }

        private static ElementId ResolveMaterialId(CompoundStructure compoundStructure, IList<CompoundStructureLayer> layers)
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

        private static Level FindTargetLevel(Document doc, View activeView)
        {
            Level viewLevel = TryGetViewLevel(doc, activeView);
            if (viewLevel != null)
            {
                return viewLevel;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static Level TryGetViewLevel(Document doc, View view)
        {
            if (view == null)
            {
                return null;
            }

            if (view.GenLevel != null)
            {
                return view.GenLevel;
            }

            Parameter associatedLevel = view.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
            if (associatedLevel != null && associatedLevel.StorageType == StorageType.ElementId)
            {
                ElementId levelId = associatedLevel.AsElementId();
                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    return doc.GetElement(levelId) as Level;
                }
            }

            return null;
        }
    }
}
