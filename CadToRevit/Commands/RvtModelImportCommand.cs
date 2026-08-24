using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Common;
using CadToRevit.UI.Part3;
using CadToRevit.Services.Workflow;
using Microsoft.Win32;
using System;
using System.IO;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RvtModelImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData != null ? commandData.Application : null;
                if (uiApp == null)
                {
                    return Result.Failed;
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

                UIDocument importedDoc = uiApp.OpenAndActivateDocument(rvtPath);
                Document importedRevitDoc = importedDoc?.Document ?? uiApp.ActiveUIDocument?.Document;
                ProjectWorkflowModeStoreService.SetMode(importedRevitDoc, ProjectWorkflowMode.RvtModelImportMode);
                App.UpdateRibbonButtonAvailability(importedRevitDoc);
                try
                {
                    AnalyzeRoomsCommandRunner.RunAnalyzeRoomsForActiveModel(
                        uiApp,
                        "RVT model imported successfully, but no room candidates were found.",
                        true);
                    // Preserve imported RVT Door family instances by default.
                    // Door-to-opening conversion is no longer performed automatically.
                    ViewDisplayHelper.EnsureFineDetailLevel(importedRevitDoc);
                }
                catch (Exception analyzeEx)
                {
                    Part3MessageWindow.ShowMessage(
                        uiApp,
                        "RVT model imported successfully, but Analyze Rooms failed." + Environment.NewLine + analyzeEx.Message);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                if (commandData != null && commandData.Application != null)
                {
                    Part3MessageWindow.ShowMessage(
                        commandData.Application,
                        "Failed to open RVT model." + Environment.NewLine + ex.Message);
                }

                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string PickRvtFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select RVT Model",
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
