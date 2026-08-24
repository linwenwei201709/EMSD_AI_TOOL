using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Part3;
using CadToRevit.UI.Part3;
using Microsoft.Win32;
using System;
using System.IO;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateAhuTestRvtCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            if (uiApp == null)
            {
                message = "No active Revit application.";
                return Result.Failed;
            }

            try
            {
                string savePath = PickSavePath();
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    return Result.Cancelled;
                }

                if (!string.Equals(Path.GetExtension(savePath), ".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath + ".rvt";
                }

                AhuTestRvtModelService.CreateCleanAhuTestRvt(uiApp.Application, savePath);

                Part3MessageWindow.ShowMessage(
                    uiApp,
                    "Clean AHU test RVT created successfully." + Environment.NewLine + savePath + Environment.NewLine + Environment.NewLine +
                    "You can now use Link RVT to Room and select this file for testing.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Part3MessageWindow.ShowMessage(uiApp, "Failed to create clean AHU test RVT." + Environment.NewLine + ex.Message);
                return Result.Failed;
            }
        }

        private static string PickSavePath()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save Clean AHU Test RVT",
                Filter = "Revit Project (*.rvt)|*.rvt",
                FileName = "Clean_AHU_TestModel.rvt",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true
            };

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
    }
}
