using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Part3;
using CadToRevit.UI.Dockable;
using CadToRevit.UI.Part3;
using Microsoft.Win32;
using System;
using System.IO;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class LinkRvtToRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;

            if (uiApp == null || uiDoc == null || doc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            try
            {
                RoomSemanticRecord selectedRoom;
                if (!RoomRecognitionPaneRuntime.TryGetSelectedRoom(out selectedRoom) || selectedRoom == null)
                {
                    Part3MessageWindow.ShowMessage(uiApp, "Please select a room from Room Recognition Results first, then click Link RVT to Room.");
                    return Result.Cancelled;
                }

                string rvtPath = PickRvtFile();
                if (string.IsNullOrWhiteSpace(rvtPath))
                {
                    return Result.Cancelled;
                }

                if (!File.Exists(rvtPath))
                {
                    Part3MessageWindow.ShowMessage(uiApp, "Selected RVT file does not exist.");
                    return Result.Failed;
                }

                if (!string.Equals(Path.GetExtension(rvtPath), ".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    Part3MessageWindow.ShowMessage(uiApp, "Please select a valid .rvt file.");
                    return Result.Failed;
                }

                RoomLinkedRvtPlacementResult result = RoomLinkedRvtPlacementService.LinkRvtToRoomCenter(doc, uiDoc, selectedRoom, rvtPath);
                if (result == null || !result.Success)
                {
                    string resultMessage = result != null && !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "Failed to link RVT to selected room.";
                    Part3MessageWindow.ShowMessage(uiApp, resultMessage);
                    return Result.Failed;
                }

                Part3MessageWindow.ShowMessage(uiApp, result.Message);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Part3MessageWindow.ShowMessage(uiApp, "Failed to link RVT to selected room." + Environment.NewLine + ex.Message);
                return Result.Failed;
            }
        }

        private static string PickRvtFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select RVT Model to Link",
                Filter = "Revit Project (*.rvt)|*.rvt",
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true
            };

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
    }
}
