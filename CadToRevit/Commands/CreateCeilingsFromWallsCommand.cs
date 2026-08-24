using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services.Ceiling;
using CadToRevit.UI;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateCeilingsFromWallsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();
            List<CeilingType> ceilingTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(x => x.Name)
                .ToList();

            if (levels.Count == 0 || ceilingTypes.Count == 0)
            {
                UiMessageService.Warning("Command.CreateCeilings.Title", "Dialog.NoLevelOrCeilingType.Message");
                return Result.Cancelled;
            }

            ElementId levelId = ResolveDefaultLevelId(doc, levels);
            ElementId ceilingTypeId = ceilingTypes.First().Id;
            CeilingGenerationMode generationMode = CeilingGenerationMode.RoomCircuits;
            double ceilingHeightMm = 2800.0;
            double gapTolMm = 50.0;
            double minAreaM2 = 1.0;
            bool enableCluster = true;
            bool enableExtend = true;
            bool enableBridge = true;
            bool autoCleanupTempLines = true;
            List<ElementId> previewTempLineIds = new List<ElementId>();

            while (true)
            {
                using (CreateCeilingsForm form = new CreateCeilingsForm(
                    levels,
                    ceilingTypes,
                    generationMode,
                    levelId,
                    ceilingTypeId,
                    ceilingHeightMm,
                    gapTolMm,
                    minAreaM2,
                    enableCluster,
                    enableExtend,
                    enableBridge,
                    autoCleanupTempLines))
                {
                    var dr = form.ShowDialog();
                    if (dr != System.Windows.Forms.DialogResult.OK || form.Action == CreateCeilingsFormAction.Cancel)
                    {
                        // 关闭时按当前选项决定是否清理临时补线。
                        if (autoCleanupTempLines && previewTempLineIds.Count > 0)
                        {
                            CeilingBoundaryRepairService.CleanupTemporaryLines(doc, previewTempLineIds);
                            previewTempLineIds.Clear();
                        }
                        return Result.Cancelled;
                    }

                    levelId = form.SelectedLevelId;
                    ceilingTypeId = form.SelectedCeilingTypeId;
                    generationMode = form.GenerationMode;
                    ceilingHeightMm = form.CeilingHeightMm;
                    gapTolMm = form.GapToleranceMm;
                    minAreaM2 = form.MinAreaM2;
                    enableCluster = form.EnableCluster;
                    enableExtend = form.EnableExtend;
                    enableBridge = form.EnableBridge;
                    autoCleanupTempLines = form.AutoCleanupTempLines;

                    if (form.Action == CreateCeilingsFormAction.Detect)
                    {
                        CeilingGapPreviewResult gap = CeilingBoundaryRepairService.DetectGaps(doc, levelId, gapTolMm);
                        CeilingDetectionResult detect = CeilingDetectionService.Detect(doc, levelId, minAreaM2);
                        UiMessageService.ShowTaskDialogText(
                            "Command.CreateCeilings.Title",
                            "Gap candidates: " + gap.GapCandidateCount + "\n" +
                            "Max gap (mm): " + gap.MaxGapMm.ToString("F1") + "\n" +
                            "Gap log: " + gap.LogPath + "\n\n" +
                            "Closed regions: " + detect.ClosedCircuitCount + "\n" +
                            "Total circuits: " + detect.TotalCircuitCount + "\n" +
                            "Skipped by min area: " + detect.SkippedByMinArea + "\n" +
                            "Log: " + detect.LogPath);
                        continue;
                    }

                    if (form.Action == CreateCeilingsFormAction.PreviewRepair)
                    {
                        if (previewTempLineIds.Count > 0)
                        {
                            CeilingBoundaryRepairService.CleanupTemporaryLines(doc, previewTempLineIds);
                            previewTempLineIds.Clear();
                        }

                        CeilingGapPreviewResult preview = CeilingBoundaryRepairService.PreviewRepair(
                            doc,
                            levelId,
                            gapTolMm,
                            new CeilingGapPreviewOptions
                            {
                                EnableEndpointClustering = enableCluster,
                                EnableExtendToIntersection = enableExtend,
                                EnableGapBridging = enableBridge
                            });
                        previewTempLineIds.AddRange(preview.TemporaryLineIds);

                        UiMessageService.ShowTaskDialogText(
                            "Command.CreateCeilings.Title",
                            "Cluster: " + preview.ClusterCount + "\n" +
                            "Extend: " + preview.ExtendCount + "\n" +
                            "Bridge: " + preview.BridgeCount + "\n" +
                            "Remaining open(est): " + preview.RemainingOpenEstimate + "\n" +
                            "Temp lines: " + preview.TemporaryLineIds.Count + "\n" +
                            "Log: " + preview.LogPath);
                        continue;
                    }

                    CeilingCreateResult result = CeilingAutoCreateService.Create(
                        doc,
                        levelId,
                        ceilingTypeId,
                        generationMode,
                        ceilingHeightMm,
                        minAreaM2,
                        previewTempLineIds,
                        autoCleanupTempLines);
                    UiMessageService.ShowTaskDialogText(
                        "Command.CreateCeilings.Title",
                        "Mode: " + generationMode + "\n" +
                        "Ceilings created: " + result.CreatedCount + "\n" +
                        "Closed regions: " + result.ClosedCircuitCount + "\n" +
                        "Skipped by min area: " + result.SkippedByMinArea + "\n" +
                        "Temp lines cleaned: " + result.CleanupDeletedCount + "\n" +
                        "Failures: " + result.Failures.Count + "\n" +
                        "Log: " + result.LogPath);
                    previewTempLineIds.Clear();
                    return Result.Succeeded;
                }
            }
        }

        private static ElementId ResolveDefaultLevelId(Document doc, List<Level> levels)
        {
            View activeView = doc.ActiveView;
            if (activeView != null && activeView.GenLevel != null)
            {
                return activeView.GenLevel.Id;
            }

            Level level = levels.FirstOrDefault();
            return level != null ? level.Id : ElementId.InvalidElementId;
        }
    }
}
