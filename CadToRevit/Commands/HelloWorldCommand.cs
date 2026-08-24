using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CadToRevit.Commands
{
    /// <summary>
    /// 最小命令：用于验证插件已成功加载并可正常响应点击。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class HelloWorldCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            // 弹出提示，确认命令执行通路正常。
            TaskDialog.Show("CadToRevit", "Hello World from Revit 2023 add-in.");
            return Result.Succeeded;
        }
    }
}
