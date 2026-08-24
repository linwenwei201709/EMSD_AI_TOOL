using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathPreview;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateGroundFloorCommand : IExternalCommand
    {
        private const string GroundMarker = "CadToRevit_GroundFloor";
        private const string GroundFloorTypeName = "CadToRevit Ground - Solid Gray 20mm";
        private const string LegacyGroundFloorTypeName = "CadToRevit Ground - Light Gray";
        private const string GroundMaterialName = "CadToRevit Ground Solid Gray";
        private const double GroundFloorThicknessMm = 10.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExecuteForUiApplication(commandData != null ? commandData.Application : null);
        }

        public static Result ExecuteForUiApplication(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            if (uiDoc == null)
            {
                return Result.Cancelled;
            }

            Document doc = uiDoc.Document;

            try
            {
                Level level = ResolveLevel(doc);
                if (level == null)
                {
                    LocalizedDialogService.Warning(uiApp, "No level found.", "Ground Floor");
                    return Result.Cancelled;
                }

                FloorType baseFloorType = ResolveBaseFloorType(doc);
                if (baseFloorType == null)
                {
                    LocalizedDialogService.Warning(uiApp, "No floor type found.", "Ground Floor");
                    return Result.Cancelled;
                }

                BoundingBoxXYZ box = CollectModelBoundingBox(doc) ?? CollectImportBoundingBox(doc);
                if (box == null)
                {
                    LocalizedDialogService.Warning(uiApp, "No model range found.", "Ground Floor");
                    return Result.Cancelled;
                }

                double padding = UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);
                double minX = box.Min.X - padding;
                double minY = box.Min.Y - padding;
                double maxX = box.Max.X + padding;
                double maxY = box.Max.Y + padding;
                double z = level.Elevation;

                Floor existingGround = FindExistingGroundFloor(doc, level.Id, minX, minY, maxX, maxY);
                if (existingGround != null)
                {
                    TryClearSelection(uiDoc);
                    DiagnosticRecorder.AppendDebug("[Ground] Existing ground floor detected. Id=" + existingGround.Id.IntegerValue);
                    LocalizedDialogService.Warning(
                        uiApp,
                        "Ground floor already exists.\n\nExisting floor Id: " + existingGround.Id.IntegerValue + "\n\nPlease delete the existing ground floor first if you need to rebuild it.",
                        "Ground Floor");
                    return Result.Succeeded;
                }

                XYZ p1 = new XYZ(minX, minY, z);
                XYZ p2 = new XYZ(maxX, minY, z);
                XYZ p3 = new XYZ(maxX, maxY, z);
                XYZ p4 = new XYZ(minX, maxY, z);

                CurveLoop loop = new CurveLoop();
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p4));
                loop.Append(Line.CreateBound(p4, p1));

                ElementId createdId = ElementId.InvalidElementId;
                FloorType groundFloorType = baseFloorType;
                using (Transaction t = new Transaction(doc, "Create Ground Floor"))
                {
                    t.Start();

                    groundFloorType = EnsureGroundFloorType(doc, baseFloorType);
                    Floor floor = Floor.Create(doc, new List<CurveLoop> { loop }, groundFloorType.Id, level.Id);
                    if (floor == null)
                    {
                        t.RollBack();
                        LocalizedDialogService.Error(uiApp, "Failed to create floor.", "Ground Floor");
                        return Result.Failed;
                    }

                    Parameter pStructural = floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                    if (pStructural != null && !pStructural.IsReadOnly)
                    {
                        pStructural.Set(0);
                    }

                    SetFloorOffsetToLevel(floor, 0.0);
                    SetTextParameter(floor, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, GroundMarker);
                    SetTextParameter(floor, BuiltInParameter.ALL_MODEL_MARK, GroundMarker);

                    createdId = floor.Id;
                    t.Commit();
                }

                DiagnosticRecorder.AppendDebug("[Ground] Level: " + level.Name);
                DiagnosticRecorder.AppendDebug("[Ground] Base FloorType: " + baseFloorType.Name);
                DiagnosticRecorder.AppendDebug("[Ground] Ground FloorType: " + groundFloorType.Name);
                DiagnosticRecorder.AppendDebug(string.Format(
                    "[Ground] BoundingBox: {0:F3}, {1:F3}, {2:F3}, {3:F3}",
                    minX, minY, maxX, maxY));
                DiagnosticRecorder.AppendDebug("[Ground] Floor Created: Id=" + createdId.IntegerValue);

                TryClearSelection(uiDoc);
                RoutePlannerSessionCacheService.MarkDirty(doc, "Ground floor was created.");
                LocalizedDialogService.Success(
                    uiApp,
                    "Ground floor created successfully.\n\nFloor Id: " + createdId.IntegerValue + "\nFloor type: " + groundFloorType.Name + "\nThickness: " + GroundFloorThicknessMm.ToString("0") + " mm",
                    "Ground Floor");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Ground] Create failed: " + ex.Message);
                LocalizedDialogService.Error(uiApp, "Create ground floor failed: " + ex.Message, "Ground Floor");
                return Result.Failed;
            }
        }

        private static Level ResolveLevel(Document doc)
        {
            Level level = null;
            View activeView = doc.ActiveView;
            if (activeView != null)
            {
                level = activeView.GenLevel;
            }

            if (level != null)
            {
                return level;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static FloorType ResolveBaseFloorType(Document doc)
        {
            List<FloorType> floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Where(x => x != null)
                .OrderBy(x => x.Name)
                .ToList();

            if (floorTypes.Count == 0)
            {
                return null;
            }

            FloorType preferred = floorTypes.FirstOrDefault(x => (NameContains(x.Name, "Generic") || NameContains(x.Name, "常规")) && !NameContains(x.Name, "Wood") && !NameContains(x.Name, "Joist"));
            if (preferred == null)
            {
                preferred = floorTypes.FirstOrDefault(x => NameContains(x.Name, "Concrete") || NameContains(x.Name, "混凝土"));
            }
            if (preferred != null)
            {
                return preferred;
            }

            return floorTypes.FirstOrDefault();
        }

        private static FloorType EnsureGroundFloorType(Document doc, FloorType baseFloorType)
        {
            FloorType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(x => string.Equals(x.Name, GroundFloorTypeName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Name, LegacyGroundFloorTypeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                ApplySolidGrayThinFloorStructure(doc, existing);
                return existing;
            }

            FloorType duplicated = null;
            try
            {
                duplicated = baseFloorType.Duplicate(GroundFloorTypeName) as FloorType;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Ground] Duplicate floor type failed: " + ex.Message);
            }

            if (duplicated == null)
            {
                duplicated = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(x => string.Equals(x.Name, GroundFloorTypeName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Name, LegacyGroundFloorTypeName, StringComparison.OrdinalIgnoreCase));
            }

            if (duplicated == null)
            {
                ApplySolidGrayThinFloorStructure(doc, baseFloorType);
                return baseFloorType;
            }

            ApplySolidGrayThinFloorStructure(doc, duplicated);
            return duplicated;
        }

        private static void ApplySolidGrayThinFloorStructure(Document doc, FloorType floorType)
        {
            if (doc == null || floorType == null)
            {
                return;
            }

            try
            {
                Material material = GetOrCreateGroundMaterial(doc);
                if (material == null)
                {
                    return;
                }

                double thickness = UnitUtils.ConvertToInternalUnits(GroundFloorThicknessMm, UnitTypeId.Millimeters);
                CompoundStructure structure = CompoundStructure.CreateSingleLayerCompoundStructure(
                    MaterialFunctionAssignment.Structure,
                    thickness,
                    material.Id);

                floorType.SetCompoundStructure(structure);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Ground] Apply solid gray thin floor structure failed: " + ex.Message);
            }
        }

        private static Material GetOrCreateGroundMaterial(Document doc)
        {
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, GroundMaterialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, GroundMaterialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material != null)
            {
                material.Color = new Autodesk.Revit.DB.Color(170, 170, 170);
                material.Transparency = 0;
                TrySetSolidSurfacePattern(doc, material, new Autodesk.Revit.DB.Color(170, 170, 170));
            }

            return material;
        }

        private static void TrySetSolidSurfacePattern(Document doc, Material material, Autodesk.Revit.DB.Color color)
        {
            try
            {
                FillPatternElement solid = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(x =>
                    {
                        FillPattern pattern = x.GetFillPattern();
                        return pattern != null && pattern.IsSolidFill;
                    });

                if (solid == null)
                {
                    return;
                }

                material.SurfaceForegroundPatternId = solid.Id;
                material.SurfaceForegroundPatternColor = color;
                material.CutForegroundPatternId = solid.Id;
                material.CutForegroundPatternColor = color;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Ground] Set solid surface pattern failed: " + ex.Message);
            }
        }

        private static void SetFloorOffsetToLevel(Floor floor, double offsetInternal)
        {
            try
            {
                Parameter offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (offset != null && !offset.IsReadOnly)
                {
                    offset.Set(offsetInternal);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Ground] Set floor offset failed: " + ex.Message);
            }
        }

        private static BoundingBoxXYZ CollectModelBoundingBox(Document doc)
        {
            HashSet<int> categories = new HashSet<int>
            {
                (int)BuiltInCategory.OST_Walls,
                (int)BuiltInCategory.OST_Doors,
                (int)BuiltInCategory.OST_Windows,
                (int)BuiltInCategory.OST_Columns,
                (int)BuiltInCategory.OST_StructuralColumns,
                (int)BuiltInCategory.OST_StructuralFraming,
                (int)BuiltInCategory.OST_Ceilings,
                (int)BuiltInCategory.OST_GenericModel
            };

            BoundingBoxXYZ total = null;
            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(x => x.Category != null && categories.Contains(x.Category.Id.IntegerValue));
            foreach (Element e in elements)
            {
                BoundingBoxXYZ box = e.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }

                total = UnionBoundingBox(total, box);
            }

            return total;
        }

        private static BoundingBoxXYZ CollectImportBoundingBox(Document doc)
        {
            BoundingBoxXYZ total = null;
            foreach (ImportInstance instance in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            {
                BoundingBoxXYZ box = instance.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }

                total = UnionBoundingBox(total, box);
            }

            return total;
        }

        private static Floor FindExistingGroundFloor(Document doc, ElementId levelId, double minX, double minY, double maxX, double maxY)
        {
            double tolerance = UnitUtils.ConvertToInternalUnits(200.0, UnitTypeId.Millimeters);

            List<Floor> floors = new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .Where(x => x != null)
                .ToList();

            foreach (Floor floor in floors)
            {
                if (floor.LevelId != ElementId.InvalidElementId && levelId != ElementId.InvalidElementId && floor.LevelId != levelId)
                {
                    continue;
                }

                if (HasGroundMarker(floor))
                {
                    return floor;
                }

                FloorType type = doc.GetElement(floor.GetTypeId()) as FloorType;
                if (type != null && (string.Equals(type.Name, GroundFloorTypeName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.Name, LegacyGroundFloorTypeName, StringComparison.OrdinalIgnoreCase)))
                {
                    return floor;
                }

                if (HasApproximatelySameFootprint(floor, minX, minY, maxX, maxY, tolerance))
                {
                    return floor;
                }
            }

            return null;
        }

        private static bool HasGroundMarker(Element element)
        {
            string comments = GetTextParameter(element, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string mark = GetTextParameter(element, BuiltInParameter.ALL_MODEL_MARK);
            return TextEquals(comments, GroundMarker) || TextEquals(mark, GroundMarker);
        }

        private static bool HasApproximatelySameFootprint(Element element, double minX, double minY, double maxX, double maxY, double tolerance)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                return false;
            }

            return Math.Abs(box.Min.X - minX) <= tolerance
                && Math.Abs(box.Min.Y - minY) <= tolerance
                && Math.Abs(box.Max.X - maxX) <= tolerance
                && Math.Abs(box.Max.Y - maxY) <= tolerance;
        }

        private static void SetTextParameter(Element element, BuiltInParameter parameterId, string value)
        {
            try
            {
                Parameter parameter = element.get_Parameter(parameterId);
                if (parameter != null && !parameter.IsReadOnly)
                {
                    parameter.Set(value);
                }
            }
            catch
            {
            }
        }

        private static string GetTextParameter(Element element, BuiltInParameter parameterId)
        {
            try
            {
                Parameter parameter = element.get_Parameter(parameterId);
                if (parameter != null && parameter.StorageType == StorageType.String)
                {
                    return parameter.AsString();
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TextEquals(string value, string expected)
        {
            return string.Equals(value ?? string.Empty, expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool NameContains(string value, string keyword)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            return value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryClearSelection(UIDocument uiDoc)
        {
            try
            {
                if (uiDoc != null)
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId>());
                }
            }
            catch
            {
            }
        }

        private static void TrySelectElement(UIDocument uiDoc, ElementId elementId)
        {
            try
            {
                if (uiDoc != null && elementId != null && elementId != ElementId.InvalidElementId)
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId> { elementId });
                }
            }
            catch
            {
            }
        }

        private static BoundingBoxXYZ UnionBoundingBox(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
            }

            BoundingBoxXYZ c = new BoundingBoxXYZ();
            c.Min = new XYZ(
                Math.Min(a.Min.X, b.Min.X),
                Math.Min(a.Min.Y, b.Min.Y),
                Math.Min(a.Min.Z, b.Min.Z));
            c.Max = new XYZ(
                Math.Max(a.Max.X, b.Max.X),
                Math.Max(a.Max.Y, b.Max.Y),
                Math.Max(a.Max.Z, b.Max.Z));
            return c;
        }
    }
}
