using Autodesk.Revit.DB;

namespace CadToRevit.Services.Diagnostics
{
    /// <summary>
    /// For auxiliary transactions, auto-dismiss warnings so batch flows are not blocked by modal dialogs.
    /// Errors are still surfaced by rolling back the transaction.
    /// </summary>
    public sealed class NonCriticalWarningsPreprocessor : IFailuresPreprocessor
    {
        private readonly string _scope;

        public NonCriticalWarningsPreprocessor(string scope)
        {
            _scope = string.IsNullOrWhiteSpace(scope) ? "NonCritical" : scope;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            if (failuresAccessor == null)
            {
                return FailureProcessingResult.Continue;
            }

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

                DiagnosticRecorder.AppendDebug(
                    "[" + _scope + "FailureIntercept] Severity=" + severity +
                    ", Id=" + id +
                    ", Text=" + text);

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                    continue;
                }

                if (severity == FailureSeverity.Error || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                }
            }

            return hasError
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }
}
