using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using System;

namespace CadToRevit.Services.PathPreview
{
    internal sealed class PathPreviewExecutionScope : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly PathPreviewFailuresProcessingHandler _failuresHandler;
        private readonly PathPreviewDialogSuppressor _dialogSuppressor;
        private bool _disposed;

        internal PathPreviewExecutionScope(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new InvalidOperationException("PathPreviewExecutionScope requires UIApplication.");
            _failuresHandler = new PathPreviewFailuresProcessingHandler();
            _dialogSuppressor = new PathPreviewDialogSuppressor();

            _uiApp.Application.FailuresProcessing += _failuresHandler.OnFailuresProcessing;
            _uiApp.DialogBoxShowing += _dialogSuppressor.OnDialogBoxShowing;
            DiagnosticRecorder.AppendDebug("[PathPreview] ExecutionScope.Registered");
        }

        internal void UpdateStage(string stage)
        {
            _failuresHandler.CurrentStage = stage ?? string.Empty;
            _dialogSuppressor.CurrentStage = stage ?? string.Empty;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _uiApp.Application.FailuresProcessing -= _failuresHandler.OnFailuresProcessing;
            _uiApp.DialogBoxShowing -= _dialogSuppressor.OnDialogBoxShowing;
            DiagnosticRecorder.AppendDebug("[PathPreview] ExecutionScope.Disposed");
        }
    }
}
