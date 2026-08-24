using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using CadToRevit.Services.Diagnostics;
using System;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal sealed class PathPreviewFailuresProcessingHandler
    {
        internal string CurrentStage { get; set; } = string.Empty;

        internal void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor accessor = e == null ? null : e.GetFailuresAccessor();
            if (accessor == null)
            {
                return;
            }

            bool hasError = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages().ToList())
            {
                if (failure == null)
                {
                    continue;
                }

                string id = string.Empty;
                string description = string.Empty;
                FailureSeverity severity = FailureSeverity.Warning;
                try
                {
                    severity = failure.GetSeverity();
                }
                catch
                {
                }

                try
                {
                    id = failure.GetFailureDefinitionId().Guid.ToString();
                }
                catch
                {
                }

                try
                {
                    description = failure.GetDescriptionText() ?? string.Empty;
                }
                catch
                {
                }

                DiagnosticRecorder.AppendDebug(
                    "[PathPreviewFailure] Stage=" + (CurrentStage ?? string.Empty) +
                    ", Severity=" + severity +
                    ", Id=" + id +
                    ", Text=" + description);

                if (severity == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                    continue;
                }

                if (severity == FailureSeverity.Error || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                }
            }

            if (!hasError)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            try
            {
                accessor.ResolveFailures(accessor.GetFailureMessages());
                e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
                DiagnosticRecorder.AppendDebug("[PathPreviewFailure] Stage=" + (CurrentStage ?? string.Empty) + ", Action=ResolveFailures");
                return;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreviewFailure] Stage=" + (CurrentStage ?? string.Empty) + ", ResolveFailuresFailed=" + ex.Message);
            }

            try
            {
                FailureHandlingOptions options = accessor.GetFailureHandlingOptions();
                options.SetClearAfterRollback(true);
                accessor.SetFailureHandlingOptions(options);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreviewFailure] Stage=" + (CurrentStage ?? string.Empty) + ", SetClearAfterRollbackFailed=" + ex.Message);
            }

            e.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
            DiagnosticRecorder.AppendDebug("[PathPreviewFailure] Stage=" + (CurrentStage ?? string.Empty) + ", Action=ProceedWithRollBack");
        }
    }
}
