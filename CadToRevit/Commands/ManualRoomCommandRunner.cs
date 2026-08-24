using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.Manual;
using CadToRevit.UI.Dockable;
using CadToRevit.UI.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

namespace CadToRevit.Commands
{
    internal static class ManualRoomCommandRunner
    {
        public static Result Run(UIApplication uiApp, out string message)
        {
            message = string.Empty;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || uiDoc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                List<Element> boundaryElements = uiDoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(ManualRoomBoundaryBuilder.IsSupportedBoundaryElement)
                    .ToList();

                if (boundaryElements.Count == 0)
                {
                    LocalizedDialogService.Warning(uiApp, "Please select boundary walls or columns before creating a manual room.", "EMSD AI Tool");
                    return Result.Cancelled;
                }

                ManualRoomBoundaryBuildResult buildResult = ManualRoomBoundaryBuilder.Build(doc, doc.ActiveView, boundaryElements);
                if (buildResult == null || !buildResult.Success || buildResult.Record == null)
                {
                    LocalizedDialogService.Warning(
                        uiApp,
                        buildResult != null && !string.IsNullOrWhiteSpace(buildResult.Message)
                            ? buildResult.Message
                            : "The selected elements do not form a closed room boundary. Please select more boundary walls.",
                        "EMSD AI Tool");
                    return Result.Cancelled;
                }

                ManualRoomDuplicateValidationResult duplicateResult = ManualRoomDuplicateValidator.Validate(
                    doc,
                    buildResult.Record,
                    RoomRecognitionPaneRuntime.GetRoomValidationSnapshot());
                if (duplicateResult != null && duplicateResult.IsDuplicate)
                {
                    LocalizedDialogService.Warning(
                        uiApp,
                        duplicateResult.Message ?? "A room already exists in this area. Please delete the existing room first if you need to recreate it.",
                        "EMSD AI Tool");
                    return Result.Cancelled;
                }

                RoomPointProbeService.RecreatePreviewFromLoopPoints(doc, doc.ActiveView, buildResult.Record.LoopPoints);

                string defaultName = BuildDefaultRoomName(doc);
                SaveManualRoomWindow window = new SaveManualRoomWindow(buildResult.Record, defaultName);
                if (uiApp.MainWindowHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
                }

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                {
                    RoomPointProbeService.ClearProbePreview(doc);
                    return Result.Cancelled;
                }

                ManualRoomRecord record = buildResult.Record;
                record.Key = "manual_room_" + Guid.NewGuid().ToString("N");
                record.RoomName = window.RoomName;
                record.RoomNumber = window.RoomNumber;
                record.RoomType = window.RoomType;
                record.SourceType = "Manual";
                record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                using (Transaction tx = new Transaction(doc, "Save Manual Room"))
                {
                    tx.Start();
                    ManualRoomStorageService.Upsert(doc, record);
                    tx.Commit();
                }

                RoomPointProbeService.ClearProbePreview(doc);
                RoomRecognitionPaneRuntime.AddManualRoomAndRefresh(doc, uiDoc, record);
                RoomRecognitionPaneRuntime.TryHidePreviewPane(uiApp);
                RoomRecognitionPaneRuntime.ShowRoomAndLiftPane(uiApp);
                LocalizedDialogService.Success(uiApp, "Manual room saved successfully.", "EMSD AI Tool");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                RoomPointProbeService.ClearProbePreview(doc);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                RoomPointProbeService.ClearProbePreview(doc);
                message = ex.Message;
                LocalizedDialogService.Error(uiApp, "Manual room failed: " + ex.Message, "EMSD AI Tool");
                return Result.Failed;
            }
        }

        private static string BuildDefaultRoomName(Document doc)
        {
            int count = ManualRoomStorageService.Load(doc).Count + 1;
            return "Manual Room " + count.ToString("000");
        }
    }
}
