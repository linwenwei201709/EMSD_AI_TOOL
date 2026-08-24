using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ShowConfigDebugCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            WallRecognitionConfig cfg = WallRecognitionConfigProvider.Load();
            string output =
                "ConfigPath: " + (WallRecognitionConfigProvider.LastLoadedPath ?? "(unknown)") + "\n" +
                "DefaultSingleWallThicknessMm: " + cfg.DefaultSingleWallThicknessMm.ToString("F2") + "\n" +
                "WallThicknessTolMm: " + cfg.WallThicknessTolMm.ToString("F2") + "\n" +
                "CurrentDirectory: " + System.Environment.CurrentDirectory + "\n" +
                "Message: " + (WallRecognitionConfigProvider.LastLoadMessage ?? string.Empty);
            TaskDialog.Show("Config Debug", output);
            return Result.Succeeded;
        }
    }
}
