using Autodesk.Revit.UI.Events;
using CadToRevit.Services.Diagnostics;
using System;

namespace CadToRevit.Services.PathPreview
{
    internal sealed class PathPreviewDialogSuppressor
    {
        internal string CurrentStage { get; set; } = string.Empty;

        internal void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            string dialogId = string.Empty;
            try
            {
                dialogId = e.DialogId ?? string.Empty;
            }
            catch
            {
            }

            DiagnosticRecorder.AppendDebug(
                "[PathPreviewDialog] Stage=" + (CurrentStage ?? string.Empty) +
                ", DialogId=" + dialogId +
                ", EventType=" + e.GetType().Name);

            try
            {
                e.OverrideResult(1);
                DiagnosticRecorder.AppendDebug("[PathPreviewDialog] Stage=" + (CurrentStage ?? string.Empty) + ", Action=OverrideResult(1)");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreviewDialog] Stage=" + (CurrentStage ?? string.Empty) + ", OverrideFailed=" + ex.Message);
            }
        }
    }
}
