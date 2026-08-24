using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.Lifts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services
{
    public static class IfcPathExportService
    {
        public interface IIfcExportProgressReporter
        {
            bool IsCancellationRequested { get; }

            void UpdateProgress(string stage, int current, int total, string detail);
        }

        public sealed class IfcPathExportResult
        {
            public string ExportViewName { get; set; } = "AI_Path_IFC_Export_View";
            public string DoorMode { get; set; } = "None";
            public string ExportPath { get; set; } = string.Empty;
            public string IfcVersion { get; set; } = "Revit exporter default";
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
            public int IfcDoorCount { get; set; }
            public int IfcOpeningCount { get; set; }
            public int IfcRelVoidsCount { get; set; }
            public int IfcRelFillsCount { get; set; }
        }

        public static IfcPathExportResult Export(Document doc, string exportPath)
        {
            return Export(doc, exportPath, null, null);
        }

        public static IfcPathExportResult Export(Document doc, string exportPath, IFCVersion? ifcVersion)
        {
            return Export(doc, exportPath, ifcVersion, null);
        }

        public static IfcPathExportResult Export(
            Document doc,
            string exportPath,
            IFCVersion? ifcVersion,
            IIfcExportProgressReporter progress)
        {
            IfcPathExportResult result = new IfcPathExportResult
            {
                ExportPath = exportPath ?? string.Empty,
                IfcVersion = FormatIfcVersion(ifcVersion)
            };
            if (doc == null || string.IsNullOrWhiteSpace(exportPath))
            {
                result.Error = "Document or export path is null.";
                LogResult(result);
                return result;
            }

            ElementId tempExportViewId = ElementId.InvalidElementId;
            try
            {
                ReportProgress(progress, "Stage: Prepare IFC Export", 1, 5, "Preparing temporary 3D export view...");
                if (IsCancellationRequested(progress))
                {
                    result.Error = "IFC export cancelled.";
                    result.Success = false;
                    LogResult(result);
                    return result;
                }

                View3D exportView;
                using (Transaction tx = new Transaction(doc, "Prepare AI IFC Export View"))
                {
                    tx.Start();
                    exportView = CreateTemporaryExportView(doc);
                    tempExportViewId = exportView.Id;
                    result.ExportViewName = exportView.Name;
                    // Preserve Door family instances in the temporary export view.
                    HideRoom3DVisualizationHelpers(exportView);
                    HideAhuEquipmentForPathExport(exportView);
                    doc.Regenerate();
                    tx.Commit();
                }

                ReportProgress(progress, "Stage: Prepare IFC Export", 2, 5, "Temporary export view is ready.");

                ReportProgress(
                    progress,
                    "Stage: Prepare IFC Export",
                    3,
                    5,
                    "Preserving door families; no temporary wall openings will be created.");

                if (IsCancellationRequested(progress))
                {
                    result.Error = "IFC export cancelled.";
                    result.Success = false;
                    DiagnosticRecorder.AppendDebug("[IfcExport] CancelledBeforeExport=True");
                    return result;
                }

                DiagnosticRecorder.AppendDebug(
                    "[IfcExportDoorMode] DoorFamiliesPreserved=True, TemporaryOpenings=False");
                LogDoorExportContext(doc, exportView);
                ReportProgress(progress, "Stage: Export IFC", 4, 5, "Exporting IFC file. This may take several minutes...");
                bool exportOk = ExportIfc(doc, exportPath, exportView, ifcVersion);

                result.DoorMode = "DoorFamiliesPreserved";
                result.Success = exportOk;
                ReportProgress(progress, "Stage: Finalize IFC Export", 5, 5, "Checking exported IFC file...");
                PopulateIfcEntityStats(exportPath, result);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Success = false;
            }
            finally
            {
                TryDeleteTemporaryExportView(doc, tempExportViewId);
            }

            LogResult(result);
            return result;
        }



        private static void ReportProgress(IIfcExportProgressReporter progress, string stage, int current, int total, string detail)
        {
            try
            {
                if (progress != null)
                {
                    progress.UpdateProgress(stage, current, total, detail);
                }
            }
            catch
            {
                // Ignore progress UI errors so IFC export can continue.
            }
        }

        private static bool IsCancellationRequested(IIfcExportProgressReporter progress)
        {
            try
            {
                return progress != null && progress.IsCancellationRequested;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatIfcVersion(IFCVersion? ifcVersion)
        {
            if (!ifcVersion.HasValue)
            {
                return "Revit exporter default";
            }

            string name = ifcVersion.Value.ToString();
            if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return "Default (Revit exporter default)";
            }
            if (string.Equals(name, "IFC2x2", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x2";
            }
            if (string.Equals(name, "IFC2x3", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3";
            }
            if (string.Equals(name, "IFC2x3CV2", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3 Coordination View 2.0";
            }
            if (string.Equals(name, "IFC2x3FM", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3 FM Handover View";
            }
            if (string.Equals(name, "IFCBCA", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC BCA";
            }
            if (string.Equals(name, "IFCCOBIE", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC COBie";
            }
            if (string.Equals(name, "IFC4", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4";
            }
            if (string.Equals(name, "IFC4RV", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4 Reference View";
            }
            if (string.Equals(name, "IFC4DTV", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4 Design Transfer View";
            }
            if (string.Equals(name, "IFC4x3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "IFC4X3", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4.3";
            }

            return name;
        }

        private static View3D CreateTemporaryExportView(Document doc)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x != null && x.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                throw new InvalidOperationException("No 3D view family type found.");
            }

            View3D created = View3D.CreateIsometric(doc, type.Id);
            created.Name = BuildTemporaryExportViewName(doc);
            return created;
        }

        private static string BuildTemporaryExportViewName(Document doc)
        {
            string baseName = "AI_Path_IFC_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            HashSet<string> usedNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(x => x != null && !x.IsTemplate && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!usedNames.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 1; i < 10000; i++)
            {
                string candidate = baseName + "_" + i;
                if (!usedNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static void TryDeleteTemporaryExportView(Document doc, ElementId viewId)
        {
            if (doc == null || viewId == null || viewId == ElementId.InvalidElementId)
            {
                return;
            }

            try
            {
                if (doc.GetElement(viewId) == null)
                {
                    return;
                }

                using (Transaction tx = new Transaction(doc, "Cleanup AI IFC Export View"))
                {
                    tx.Start();
                    doc.Delete(viewId);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportViewCleanup] Failed=" + ex.Message);
            }
        }

        private static void HideDoorCategory(View view)
        {
            if (view == null)
            {
                return;
            }

            Category cat = view.Document.Settings.Categories.get_Item(BuiltInCategory.OST_Doors);
            if (cat == null || !view.CanCategoryBeHidden(cat.Id))
            {
                return;
            }

            view.SetCategoryHidden(cat.Id, true);
        }

        private static void HideRoom3DVisualizationHelpers(View view)
        {
            if (view == null || view.Document == null)
            {
                return;
            }

            List<ElementId> helperIds = new FilteredElementCollector(view.Document, view.Id)
                .WhereElementIsNotElementType()
                .Where(IsRoom3DVisualizationHelperElement)
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();
            if (helperIds.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportViewPrep] HiddenRoom3DHelpers=0");
                return;
            }

            // Hide helper elements per-element so real generic models stay exportable.
            view.HideElements(helperIds);
            DiagnosticRecorder.AppendDebug("[IfcExportViewPrep] HiddenRoom3DHelpers=" + helperIds.Count);
        }

        private static void HideAhuEquipmentForPathExport(View view)
        {
            if (view == null || view.Document == null)
            {
                return;
            }

            List<ElementId> ahuIds = new FilteredElementCollector(view.Document, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(IsAhuEquipmentForPathExport)
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .Where(x => CanHideElementInView(view, x))
                .ToList();

            if (ahuIds.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportViewPrep] HiddenAhuEquipment=0");
                return;
            }

            view.HideElements(ahuIds);
            DiagnosticRecorder.AppendDebug("[IfcExportViewPrep] HiddenAhuEquipment=" + ahuIds.Count);
        }

        private static bool IsAhuEquipmentForPathExport(FamilyInstance instance)
        {
            if (instance == null)
            {
                return false;
            }

            string familyName = instance.Symbol != null ? instance.Symbol.FamilyName ?? string.Empty : string.Empty;
            string symbolName = instance.Symbol != null ? instance.Symbol.Name ?? string.Empty : string.Empty;
            string typeName = instance.Name ?? string.Empty;
            string comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty;
            string mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
            string combined = string.Join(" ", new[] { familyName, symbolName, typeName, comments, mark });

            bool hasAhuName = ContainsAny(combined, "AHU", "PAU");
            bool hasPluginMetadata = comments.StartsWith("ROOM_CUSTOM_FAMILY__", StringComparison.OrdinalIgnoreCase) ||
                                     mark.StartsWith("ROOM_CUSTOM_FAMILY__", StringComparison.OrdinalIgnoreCase) ||
                                     ContainsAny(comments, "EMSD", "CadToRevit", "AHU", "PAU") ||
                                     ContainsAny(mark, "EMSD", "CadToRevit", "AHU", "PAU");

            if (hasPluginMetadata && hasAhuName)
            {
                return true;
            }

            Category category = instance.Category;
            int categoryId = category != null && category.Id != null ? category.Id.IntegerValue : 0;
            bool isEquipmentCategory =
                categoryId == (int)BuiltInCategory.OST_MechanicalEquipment ||
                categoryId == (int)BuiltInCategory.OST_GenericModel;

            if (isEquipmentCategory && hasPluginMetadata && ContainsAny(combined, "Equipment", "AHU", "PAU"))
            {
                return true;
            }

            return isEquipmentCategory && hasAhuName;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens == null)
            {
                return false;
            }

            foreach (string token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) &&
                    text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanHideElementInView(View view, ElementId id)
        {
            if (view == null || id == null || id == ElementId.InvalidElementId)
            {
                return false;
            }

            try
            {
                Element element = view.Document.GetElement(id);
                return element != null && element.CanBeHidden(view);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRoom3DVisualizationHelperElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            DirectShape shape = element as DirectShape;
            if (shape != null)
            {
                string applicationId = shape.ApplicationId ?? string.Empty;
                string name = shape.Name ?? string.Empty;
                if (string.Equals(applicationId, Room3DVisualizationConstants.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(applicationId, Lift3DVisualizationService.ApplicationId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return Room3DVisualizationMetadataService.IsManagedName(name) ||
                       Lift3DVisualizationService.IsManagedLiftElement(shape);
            }

            FamilyInstance instance = element as FamilyInstance;
            if (instance != null)
            {
                string familyName = instance.Symbol != null ? (instance.Symbol.FamilyName ?? string.Empty) : string.Empty;
                if (familyName.IndexOf(Room3DVisualizationConstants.TextFamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                return Room3DVisualizationMetadataService.IsManagedTextElement(instance);
            }

            return false;
        }

        private static void ConfigureIfcTempFailureHandling(Transaction tx, string scope)
        {
            if (tx == null)
            {
                return;
            }

            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new NonCriticalWarningsPreprocessor(scope));
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
        }

        private static bool ExportIfc(Document doc, string exportPath, View view, IFCVersion? ifcVersion)
        {
            string folder = Path.GetDirectoryName(exportPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string fileName = Path.GetFileNameWithoutExtension(exportPath);
            IFCExportOptions options = new IFCExportOptions();
            if (ifcVersion.HasValue)
            {
                options.FileVersion = ifcVersion.Value;
            }
            if (view != null)
            {
                options.FilterViewId = view.Id;
            }
            // Lock IFC export coordinates to Internal Origin instead of relying on exporter defaults.
            options.AddOption("SitePlacement", "0");
            options.AddOption("SelectedSite", "Internal");
            options.AddOption("VisibleElementsOfCurrentView", "true");

            // Keep a write context open for IFC exporters that create temporary elements internally.
            using (Transaction tx = new Transaction(doc, "IFC Export Transaction"))
            {
                tx.Start();
                bool ok = doc.Export(folder, fileName, options);
                if (ok)
                {
                    tx.Commit();
                }
                else
                {
                    tx.RollBack();
                }
                return ok;
            }
        }
        private static void CreateTemporarySingleDoorOpenings(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            // Capture single-door opening seeds before deleting door instances in export transaction.
            List<TempOpeningSeed> seeds = new List<TempOpeningSeed>();
            List<FamilyInstance> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(x => x != null)
                .ToList();

            foreach (FamilyInstance door in doors)
            {
                try
                {
                    Wall hostWall = door.Host as Wall;
                    if (hostWall == null)
                    {
                        continue;
                    }

                    XYZ doorPoint = ResolveDoorLocationPoint(door);
                    if (doorPoint == null)
                    {
                        continue;
                    }

                    double widthMm = ResolveDoorWidthMm(door);
                    double heightMm = ResolveDoorHeightMm(door);
                    double sillMm = ResolveDoorSillMm(door);
                    bool isDoubleDoor = IsDoubleDoor(door, widthMm);

                    if (isDoubleDoor)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[IfcExportTempOpening] DoorId=" + door.Id.IntegerValue +
                            ", IsDoubleDoor=True" +
                            ", HostWallId=" + hostWall.Id.IntegerValue +
                            ", SkipTemporaryOpeningForDoubleDoor=True");
                        continue;
                    }
                    seeds.Add(new TempOpeningSeed
                    {
                        DoorId = door.Id,
                        HostWallId = hostWall.Id,
                        DoorPoint = doorPoint,
                        WidthMm = widthMm,
                        HeightMm = heightMm,
                        SillMm = sillMm
                    });
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[IfcExportTempOpening] DoorId=" + door.Id.IntegerValue +
                        ", TemporaryOpeningCreated=False" +
                    ", Error=" + ex.Message);
                }
            }

            if (seeds.Count == 0)
            {
                return;
            }

            // Remove single-door family instances so IFC export no longer depends on family cut behavior.
            ICollection<ElementId> deleteIds = seeds.Select(x => x.DoorId).ToList();
            doc.Delete(deleteIds);
            DiagnosticRecorder.AppendDebug("[IfcExportTempOpening] DeletedSingleDoorInstances=" + deleteIds.Count);

            foreach (TempOpeningSeed seed in seeds)
            {
                try
                {
                    Wall hostWall = doc.GetElement(seed.HostWallId) as Wall;
                    bool created = TryCreateWallOpening(doc, hostWall, seed.DoorPoint, seed.WidthMm, seed.HeightMm, seed.SillMm);
                    DiagnosticRecorder.AppendDebug(
                        "[IfcExportTempOpening] DoorId=" + seed.DoorId.IntegerValue +
                        ", IsDoubleDoor=False" +
                        ", HostWallId=" + seed.HostWallId.IntegerValue +
                        ", OpeningWidthMm=" + seed.WidthMm.ToString("F1") +
                        ", OpeningHeightMm=" + seed.HeightMm.ToString("F1") +
                        ", OpeningSillMm=" + seed.SillMm.ToString("F1") +
                        ", TemporaryOpeningCreated=" + created);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[IfcExportTempOpening] DoorId=" + seed.DoorId.IntegerValue +
                        ", TemporaryOpeningCreated=False" +
                        ", Error=" + ex.Message);
                }
            }
        }

        private static void CreateTemporaryLiftDoorOpenings(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            List<LiftRecognitionRecord> lifts = LiftRecognitionStorageService.Load(doc)
                .Where(x => x != null && x.VirtualDoorStart != null && x.VirtualDoorEnd != null)
                .ToList();
            if (lifts.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportVirtualLiftOpening] LiftDoorCount=0");
                return;
            }

            int createdCount = 0;
            foreach (LiftRecognitionRecord lift in lifts)
            {
                try
                {
                    XYZ start = lift.VirtualDoorStart;
                    XYZ end = lift.VirtualDoorEnd;
                    XYZ mid = new XYZ((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5, (start.Z + end.Z) * 0.5);
                    Wall hostWall = ResolveLiftDoorHostWall(doc, lift, mid);
                    double widthMm = lift.VirtualDoorWidthMm > 1.0
                        ? lift.VirtualDoorWidthMm
                        : UnitUtils.ConvertFromInternalUnits(start.DistanceTo(end), UnitTypeId.Millimeters);
                    if (widthMm <= 1.0)
                    {
                        widthMm = 900.0;
                    }

                    double heightMm = lift.VirtualDoorHeightMm > 1.0 ? lift.VirtualDoorHeightMm : 2100.0;
                    double sillMm = Math.Max(0.0, lift.VirtualDoorSillMm);
                    bool created = TryCreateWallOpening(doc, hostWall, mid, widthMm, heightMm, sillMm);
                    if (created)
                    {
                        createdCount++;
                    }

                    DiagnosticRecorder.AppendDebug(
                        "[IfcExportVirtualLiftOpening] LiftId=" + (lift.LiftId ?? string.Empty) +
                        ", LiftName=" + (lift.LiftName ?? string.Empty) +
                        ", HostWallId=" + (hostWall != null ? hostWall.Id.IntegerValue.ToString() : "-") +
                        ", OpeningWidthMm=" + widthMm.ToString("F1") +
                        ", OpeningHeightMm=" + heightMm.ToString("F1") +
                        ", OpeningSillMm=" + sillMm.ToString("F1") +
                        ", TemporaryOpeningCreated=" + created);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[IfcExportVirtualLiftOpening] LiftId=" + (lift != null ? (lift.LiftId ?? string.Empty) : string.Empty) +
                        ", TemporaryOpeningCreated=False" +
                        ", Error=" + ex.Message);
                }
            }

            DiagnosticRecorder.AppendDebug("[IfcExportVirtualLiftOpening] Created=" + createdCount + ", Candidates=" + lifts.Count);
        }

        private static Wall ResolveLiftDoorHostWall(Document doc, LiftRecognitionRecord lift, XYZ doorMidPoint)
        {
            if (doc == null || lift == null || doorMidPoint == null)
            {
                return null;
            }

            if (lift.VirtualDoorHostWallId != null && lift.VirtualDoorHostWallId != ElementId.InvalidElementId)
            {
                Wall storedWall = doc.GetElement(lift.VirtualDoorHostWallId) as Wall;
                if (storedWall != null)
                {
                    return storedWall;
                }
            }

            double maxDistanceFt = UnitUtils.ConvertToInternalUnits(1200.0, UnitTypeId.Millimeters);
            Wall bestWall = null;
            double bestDistance = double.MaxValue;
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall == null)
                {
                    continue;
                }

                LocationCurve location = wall.Location as LocationCurve;
                Line line = location != null ? location.Curve as Line : null;
                if (line == null)
                {
                    continue;
                }

                IntersectionResult projection = line.Project(doorMidPoint);
                XYZ projected = projection != null ? projection.XYZPoint : null;
                if (projected == null)
                {
                    continue;
                }

                double distance = Distance2D(projected, doorMidPoint);
                if (distance < bestDistance && distance <= maxDistanceFt)
                {
                    bestDistance = distance;
                    bestWall = wall;
                }
            }

            return bestWall;
        }

        private static double Distance2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class TempOpeningSeed
        {
            public ElementId DoorId { get; set; }
            public ElementId HostWallId { get; set; }
            public XYZ DoorPoint { get; set; }
            public double WidthMm { get; set; }
            public double HeightMm { get; set; }
            public double SillMm { get; set; }
        }


        private static bool TryCreateWallOpening(
            Document doc,
            Wall wall,
            XYZ doorPoint,
            double widthMm,
            double heightMm,
            double sillMm)
        {
            if (doc == null || wall == null || doorPoint == null || widthMm <= 1.0 || heightMm <= 1.0)
            {
                return false;
            }

            LocationCurve wallLocation = wall.Location as LocationCurve;
            Line wallLine = wallLocation != null ? wallLocation.Curve as Line : null;
            if (wallLine == null)
            {
                return false;
            }

            XYZ projected = wallLine.Project(doorPoint)?.XYZPoint;
            if (projected == null)
            {
                return false;
            }

            XYZ dir = wallLine.Direction.Normalize();
            double halfWidthFt = UnitUtils.ConvertToInternalUnits(widthMm * 0.5, UnitTypeId.Millimeters);
            double sillFt = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, sillMm), UnitTypeId.Millimeters);
            double heightFt = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);

            XYZ p1 = projected - dir.Multiply(halfWidthFt);
            XYZ p2 = projected + dir.Multiply(halfWidthFt);
            p1 = new XYZ(p1.X, p1.Y, doorPoint.Z + sillFt);
            p2 = new XYZ(p2.X, p2.Y, doorPoint.Z + sillFt + heightFt);

            Opening opening = doc.Create.NewOpening(wall, p1, p2);
            return opening != null;
        }

        private static XYZ ResolveDoorLocationPoint(FamilyInstance door)
        {
            LocationPoint location = door.Location as LocationPoint;
            if (location != null && location.Point != null)
            {
                return location.Point;
            }

            BoundingBoxXYZ box = door.get_BoundingBox(null);
            if (box != null)
            {
                return (box.Min + box.Max) * 0.5;
            }

            return null;
        }

        private static bool IsDoubleDoor(FamilyInstance door, double widthMm)
        {
            if (door == null)
            {
                return false;
            }

            FamilySymbol symbol = door.Symbol;
            string name = ((symbol == null ? string.Empty : symbol.Name) + " " + (symbol == null ? string.Empty : symbol.FamilyName)).ToLowerInvariant();
            if (name.Contains("double") || name.Contains("双") || name.Contains("2leaf"))
            {
                return true;
            }

            // Conservative fallback for phase-1: wide doors are treated as possible double doors and skipped.
            return widthMm > 1300.0;
        }

        private static double ResolveDoorWidthMm(FamilyInstance door)
        {
            return ResolveLengthMm(
                door,
                new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                900.0);
        }

        private static double ResolveDoorHeightMm(FamilyInstance door)
        {
            return ResolveLengthMm(
                door,
                new[] { "Height", "Rough Height", "Door Height", "高度" },
                2100.0);
        }

        private static double ResolveDoorSillMm(FamilyInstance door)
        {
            if (door == null)
            {
                return 0.0;
            }

            Parameter p = door.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
            if (p != null && p.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            }

            return ResolveLengthMm(door, new[] { "Sill Height", "Door Sill Height", "窗台高度", "门槛高度" }, 0.0);
        }

        private static double ResolveLengthMm(FamilyInstance door, IEnumerable<string> names, double fallbackMm)
        {
            if (door == null)
            {
                return fallbackMm;
            }

            Parameter inst = FindLengthParameter(door, names);
            if (inst != null)
            {
                return UnitUtils.ConvertFromInternalUnits(inst.AsDouble(), UnitTypeId.Millimeters);
            }

            FamilySymbol symbol = door.Symbol;
            if (symbol != null)
            {
                Parameter type = FindLengthParameter(symbol, names);
                if (type != null)
                {
                    return UnitUtils.ConvertFromInternalUnits(type.AsDouble(), UnitTypeId.Millimeters);
                }
            }

            return fallbackMm;
        }

        private static Parameter FindLengthParameter(Element element, IEnumerable<string> names)
        {
            if (element == null || names == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Parameter p = element.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    return p;
                }
            }

            return null;
        }

        private static void LogResult(IfcPathExportResult result)
        {
            DiagnosticRecorder.AppendDebug(
                "[IfcExportAI] View=" + (result?.ExportViewName ?? string.Empty) +
                ", DoorMode=" + (result?.DoorMode ?? string.Empty) +
                ", IfcVersion=" + (result?.IfcVersion ?? string.Empty) +
                ", ExportPath=" + (result?.ExportPath ?? string.Empty) +
                ", Success=" + (result != null && result.Success) +
                ", IfcDoorCount=" + (result == null ? 0 : result.IfcDoorCount) +
                ", IfcOpeningCount=" + (result == null ? 0 : result.IfcOpeningCount) +
                ", IfcRelVoidsCount=" + (result == null ? 0 : result.IfcRelVoidsCount) +
                ", IfcRelFillsCount=" + (result == null ? 0 : result.IfcRelFillsCount));
        }

        private static void LogDoorExportContext(Document doc, View view)
        {
            try
            {
                int doorCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                List<FamilyInstance> doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(x => x != null)
                    .ToList();

                int hostedOnWall = doors.Count(x => x.Host is Wall);
                int hostedOnOther = doors.Count - hostedOnWall;
                bool hiddenInView = false;
                if (view != null)
                {
                    Category cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Doors);
                    if (cat != null && view.CanCategoryBeHidden(cat.Id))
                    {
                        hiddenInView = view.GetCategoryHidden(cat.Id);
                    }
                }

                DiagnosticRecorder.AppendDebug(
                    "[IfcExportDoorContext] DoorCount=" + doorCount +
                    ", HostedOnWall=" + hostedOnWall +
                    ", HostedOnOther=" + hostedOnOther +
                    ", DoorCategoryHiddenInExportView=" + hiddenInView +
                    ", ExportView=" + (view == null ? string.Empty : view.Name));
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportDoorContext] LogFailed=" + ex.Message);
            }
        }

        private static void PopulateIfcEntityStats(string exportPath, IfcPathExportResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(exportPath) || !File.Exists(exportPath))
            {
                return;
            }

            try
            {
                int door = 0;
                int opening = 0;
                int relVoids = 0;
                int relFills = 0;
                foreach (string line in File.ReadLines(exportPath))
                {
                    // IFC entity names are uppercase in default Revit export output.
                    if (line.IndexOf("IFCDOOR(", StringComparison.Ordinal) >= 0) door++;
                    if (line.IndexOf("IFCOPENINGELEMENT(", StringComparison.Ordinal) >= 0) opening++;
                    if (line.IndexOf("IFCRELVOIDSELEMENT(", StringComparison.Ordinal) >= 0) relVoids++;
                    if (line.IndexOf("IFCRELFILLSELEMENT(", StringComparison.Ordinal) >= 0) relFills++;
                }

                result.IfcDoorCount = door;
                result.IfcOpeningCount = opening;
                result.IfcRelVoidsCount = relVoids;
                result.IfcRelFillsCount = relFills;
                DiagnosticRecorder.AppendDebug(
                    "[IfcExportEntityStats] Door=" + door +
                    ", Opening=" + opening +
                    ", RelVoids=" + relVoids +
                    ", RelFills=" + relFills +
                    ", Path=" + exportPath);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[IfcExportEntityStats] Failed=" + ex.Message);
            }
        }
    }
}
