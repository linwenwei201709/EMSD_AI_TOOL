using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.PathPreview;
using System;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CalculatePathApiTestCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData == null ? null : commandData.Application.ActiveUIDocument;
            Document doc = uiDoc == null ? null : uiDoc.Document;
            if (uiDoc == null || doc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                string sessionId;
                if (!ProjectInitializationCommand.TryGetSavedSessionId(doc, out sessionId))
                {
                    TaskDialog.Show(
                        "Calculate Path",
                        "No saved session_id was found in the current Revit document. Please run Project Init first.");
                    return Result.Cancelled;
                }

                Reference startReference = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    "Pick start point on floor surface.");
                Reference goalReference = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    "Pick goal point on floor surface.");
                if (startReference == null || goalReference == null)
                {
                    return Result.Cancelled;
                }

                XYZ startPoint = startReference.GlobalPoint;
                XYZ goalPoint = goalReference.GlobalPoint;
                if (startPoint == null || goalPoint == null)
                {
                    TaskDialog.Show(
                        "Calculate Path",
                        "The selected point could not be resolved. Please click a floor, wall, or other model surface.");
                    return Result.Failed;
                }

                CalculatePathExecutionResult result = CalculatePathApiService.CalculateAndDraw(
                    doc,
                    uiDoc,
                    sessionId,
                    startPoint,
                    goalPoint);
                if (!result.Success || !result.Drawn)
                {
                    TaskDialog.Show("Calculate Path", result.Message ?? "Calculate path failed.");
                    return Result.Failed;
                }

                ShowResponseDialog(result.ResponseBody ?? result.Message ?? string.Empty);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Calculate Path", ex.Message);
                return Result.Failed;
            }
        }

        private static void ShowResponseDialog(string responseText)
        {
            TaskDialog dialog = new TaskDialog("Calculate Path Result");
            dialog.MainInstruction = "POST /api/calculate_path completed.";
            dialog.MainContent = responseText;
            dialog.ExpandedContent = responseText;
            dialog.CommonButtons = TaskDialogCommonButtons.Close;
            dialog.Show();
        }

    }
}
