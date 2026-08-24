using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models;
using CadToRevit.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateWallsFromCenterlinesCommand : IExternalCommand
    {
        private const double DefaultWallHeightMm = 4000.0;
        private const double MinCenterlineLengthFeet = 1e-6;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            ImportInstance importInstance = GetSelectedImportInstance(uiDoc)
                ?? GetFirstImportInstance(doc);
            if (importInstance == null)
            {
                UiMessageService.Warning("Command.CreateWalls.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            Level level = FindDefaultLevel(doc);
            if (level == null)
            {
                UiMessageService.Warning("Command.CreateWalls.Title", "Dialog.NoLevelFound.Message");
                return Result.Failed;
            }

            WallType wallType = FindDefaultWallType(doc);
            if (wallType == null)
            {
                UiMessageService.Warning("Command.CreateWalls.Title", "Dialog.NoWallTypeFound.Message");
                return Result.Failed;
            }

            List<WallCenterlineCandidate> centerlines = DetectCenterlines(doc, importInstance);
            if (centerlines.Count == 0)
            {
                UiMessageService.Info("Command.CreateWalls.Title", "Dialog.NoWallCenterlines.Message");
                return Result.Succeeded;
            }

            double wallHeightFeet = UnitUtils.ConvertToInternalUnits(DefaultWallHeightMm, UnitTypeId.Millimeters);
            int createdCount = 0;
            int failedCount = 0;
            List<string> failureMessages = new List<string>();

            using (Transaction tx = new Transaction(doc, "Create Walls From Centerlines"))
            {
                tx.Start();
                foreach (WallCenterlineCandidate candidate in centerlines)
                {
                    if (candidate == null || candidate.CenterLine == null)
                    {
                        failedCount++;
                        continue;
                    }

                    if (candidate.CenterLine.Length <= MinCenterlineLengthFeet)
                    {
                        failedCount++;
                        continue;
                    }

                    try
                    {
                        Wall.Create(
                            doc,
                            candidate.CenterLine,
                            wallType.Id,
                            level.Id,
                            wallHeightFeet,
                            0.0,
                            false,
                            false);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        if (failureMessages.Count < 5)
                        {
                            failureMessages.Add(ex.Message);
                        }
                    }
                }

                tx.Commit();
            }

            string output = BuildOutput(
                importInstance,
                level,
                wallType,
                DefaultWallHeightMm,
                centerlines.Count,
                createdCount,
                failedCount,
                centerlines,
                failureMessages);
            UiMessageService.ShowTaskDialogText("Command.CreateWalls.Title", output);
            return Result.Succeeded;
        }

        private static List<WallCenterlineCandidate> DetectCenterlines(Document doc, ImportInstance importInstance)
        {
            CadSegmentBuildResult segmentResult = CadSegmentBuilder.BuildSegments(doc, importInstance, null);
            WallRecognitionResult detectResult = WallRecognitionEngine.RecognizeWalls(segmentResult.Segments);
            return detectResult.Centerlines;
        }

        private static ImportInstance GetSelectedImportInstance(UIDocument uiDoc)
        {
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                return null;
            }

            foreach (ElementId id in selectedIds)
            {
                ImportInstance instance = uiDoc.Document.GetElement(id) as ImportInstance;
                if (instance != null)
                {
                    return instance;
                }
            }

            return null;
        }

        private static ImportInstance GetFirstImportInstance(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .FirstOrDefault();
        }

        private static Level FindDefaultLevel(Document doc)
        {
            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();

            Level namedLevel = levels.FirstOrDefault(
                x => string.Equals(x.Name, "Level 1", StringComparison.OrdinalIgnoreCase));
            return namedLevel ?? levels.FirstOrDefault();
        }

        private static WallType FindDefaultWallType(Document doc)
        {
            List<WallType> wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .ToList();

            WallType basic = wallTypes.FirstOrDefault(x => x.Kind == WallKind.Basic);
            return basic ?? wallTypes.FirstOrDefault();
        }

        private static string BuildOutput(
            ImportInstance importInstance,
            Level level,
            WallType wallType,
            double wallHeightMm,
            int detectedCenterlineCount,
            int createdCount,
            int failedCount,
            List<WallCenterlineCandidate> centerlines,
            List<string> failureMessages)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CAD Link: " + (importInstance.Name ?? "(Unnamed)"));
            sb.AppendLine("Detected Centerlines: " + detectedCenterlineCount);
            sb.AppendLine("Created Walls: " + createdCount);
            sb.AppendLine("Failed Walls: " + failedCount);
            sb.AppendLine("Level: " + level.Name);
            sb.AppendLine("WallType: " + wallType.Name);
            sb.AppendLine("Height: " + wallHeightMm.ToString("F0") + " mm");

            if (centerlines.Count > 0)
            {
                double avgThickness = centerlines.Average(x => x.ThicknessMm);
                sb.AppendLine("Centerline Thickness Avg: " + avgThickness.ToString("F1") + " mm");
            }

            if (failureMessages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failure Samples:");
                foreach (string error in failureMessages)
                {
                    sb.AppendLine("- " + error);
                }
            }

            return sb.ToString();
        }
    }
}
