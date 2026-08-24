using Autodesk.Revit.DB;

namespace CadToRevit.Services.Diagnostics
{
    /// <summary>
    /// 墙批量生成失败预处理：自动删除警告，遇到错误时静默回滚，避免Revit失败弹窗阻塞。
    /// </summary>
    public sealed class WallBatchFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasError = false;
            foreach (FailureMessageAccessor f in failuresAccessor.GetFailureMessages())
            {
                if (f == null)
                {
                    continue;
                }

                FailureSeverity severity = f.GetSeverity();
                string id = string.Empty;
                string text = string.Empty;
                try
                {
                    id = f.GetFailureDefinitionId().Guid.ToString();
                }
                catch
                {
                }

                try
                {
                    text = f.GetDescriptionText() ?? string.Empty;
                }
                catch
                {
                }

                DiagnosticRecorder.AppendDebug("[FailureIntercept] Severity=" + severity + ", Id=" + id + ", Text=" + text);

                if (severity == FailureSeverity.Warning)
                {
                    // 自动删除所有Warning，避免批量生成时频繁弹窗。
                    failuresAccessor.DeleteWarning(f);
                    continue;
                }

                if (severity == FailureSeverity.Error || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                }
            }

            if (hasError)
            {
                // 错误统一回滚到调用层，由调用层决定二分重试或跳过。
                return FailureProcessingResult.ProceedWithRollBack;
            }

            return FailureProcessingResult.Continue;
        }
    }
}
