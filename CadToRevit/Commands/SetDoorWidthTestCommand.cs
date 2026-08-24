using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class SetDoorWidthTestCommand : IExternalCommand
    {
        private const double TargetWidthMm = 1500.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc == null ? null : uiDoc.Document;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            DoorWidthTestService.DoorWidthTestResult result = DoorWidthTestService.SetAllDoorsWidth(doc, TargetWidthMm);
            WinForms.MessageBox.Show(
                "门宽测试完成。\n" +
                "目标宽度(mm): " + TargetWidthMm.ToString("F0") + "\n" +
                "总门数: " + result.TotalDoors + "\n" +
                "成功: " + result.SuccessCount + "\n" +
                "失败: " + result.FailedCount + "\n" +
                "实例写入: " + result.InstanceWriteCount + "\n" +
                "类型写入: " + result.TypeWriteCount,
                "设置门宽度（测试）",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return Result.Succeeded;
        }
    }
}
