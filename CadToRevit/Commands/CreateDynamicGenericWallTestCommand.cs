using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateDynamicGenericWallTestCommand : IExternalCommand
    {
        private const string TargetWallTypeName = "EMSD_Generic_140mm";
        private const double WallThicknessMm = 140.0;
        private const double WallLengthMm = 10000.0;
        private const double WallHeightMm = 4000.0;

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
                TaskDialog.Show("测试动态墙", "未找到可用的 Level，无法创建测试墙。");
                return Result.Failed;
            }

            Wall createdWall = null;
            WallType targetWallType = null;

            try
            {
                using (Transaction tx = new Transaction(doc, "Create Dynamic Generic Wall Test"))
                {
                    tx.Start();

                    targetWallType = GetOrCreateTargetWallType(doc);
                    if (targetWallType == null)
                    {
                        throw new InvalidOperationException("No usable Basic Wall type was found.");
                    }

                    createdWall = CreateWallInstance(doc, level, targetWallType);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("测试动态墙", "创建测试动态墙失败：" + ex.Message);
                return Result.Failed;
            }

            if (createdWall != null)
            {
                ICollection<ElementId> createdIds = new List<ElementId> { createdWall.Id };
                uiDoc.Selection.SetElementIds(createdIds);
                uiDoc.ShowElements(createdIds);
            }

            string successMessage =
                "已创建测试动态墙。\n" +
                "类型：" + (targetWallType == null ? TargetWallTypeName : targetWallType.Name) + "\n" +
                "长度：10 m\n" +
                "厚度：140 mm\n" +
                "高度：3000 mm";
            TaskDialog.Show("测试动态墙", successMessage);
            return Result.Succeeded;
        }

        private static WallType GetOrCreateTargetWallType(Document doc)
        {
            WallType existingType = FindWallTypeByName(doc, TargetWallTypeName);
            if (existingType != null)
            {
                EnsureWallTypeThickness(existingType);
                return existingType;
            }

            WallType baseWallType = FindFirstBasicWallType(doc);
            if (baseWallType == null)
            {
                return null;
            }

            ElementType duplicatedElementType = baseWallType.Duplicate(TargetWallTypeName);
            WallType duplicatedType = duplicatedElementType as WallType;
            if (duplicatedType == null)
            {
                throw new InvalidOperationException("Failed to duplicate the base wall type.");
            }

            EnsureWallTypeThickness(duplicatedType);
            return duplicatedType;
        }

        private static WallType FindWallTypeByName(Document doc, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(
                    x => string.Equals(
                        x.Name,
                        typeName,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static WallType FindFirstBasicWallType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(
                    x => x != null &&
                         x.Kind == WallKind.Basic &&
                         SafeGetCompoundStructure(x) != null);
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

        private static void EnsureWallTypeThickness(WallType wallType)
        {
            double targetWidthFeet = UnitUtils.ConvertToInternalUnits(WallThicknessMm, UnitTypeId.Millimeters);
            CompoundStructure compoundStructure = SafeGetCompoundStructure(wallType);
            if (compoundStructure == null)
            {
                throw new InvalidOperationException("The selected wall type does not expose a compound structure.");
            }

            MaterialFunctionAssignment function = MaterialFunctionAssignment.Structure;
            IList<CompoundStructureLayer> existingLayers = compoundStructure.GetLayers();
            if (existingLayers != null && existingLayers.Count > 0)
            {
                function = existingLayers[0].Function;
            }

            // Rebuild the wall type as a single-layer basic wall with the target width.
            ElementId materialId = existingLayers != null &&
                compoundStructure.StructuralMaterialIndex >= 0 &&
                compoundStructure.StructuralMaterialIndex < existingLayers.Count
                ? existingLayers[compoundStructure.StructuralMaterialIndex].MaterialId
                : ElementId.InvalidElementId;

            IList<CompoundStructureLayer> layers = new List<CompoundStructureLayer>
            {
                new CompoundStructureLayer(targetWidthFeet, function, materialId)
            };

            CompoundStructure newStructure = CompoundStructure.CreateSimpleCompoundStructure(layers);
            wallType.SetCompoundStructure(newStructure);
        }

        private static Wall CreateWallInstance(Document doc, Level level, WallType wallType)
        {
            double wallLengthFeet = UnitUtils.ConvertToInternalUnits(WallLengthMm, UnitTypeId.Millimeters);
            double wallHeightFeet = UnitUtils.ConvertToInternalUnits(WallHeightMm, UnitTypeId.Millimeters);

            // Place the verification wall near the origin along the X axis.
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
