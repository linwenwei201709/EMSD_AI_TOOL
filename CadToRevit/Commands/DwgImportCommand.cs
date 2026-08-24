using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models.Units;
using CadToRevit.Services.Common;
using CadToRevit.Services;
using CadToRevit.Services.CadRuntime;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Dwg;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Workflow;
using CadToRevit.UI.Dockable;
using CadToRevit.UI.Windows;
using System;
using System.Linq;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DwgImportCommand : IExternalCommand
    {
        private const int MaxShadedEnsureIdlingAttempts = 120;
        private static bool _pendingEnsureShadedAfter3DPost;
        private static int _ensureShadedIdlingAttempts;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeViewBeforeImport = uiDoc.ActiveView;
            bool was3DView = activeViewBeforeImport is View3D || (activeViewBeforeImport?.ViewType == ViewType.ThreeD);
            string filePath = PickDwgFilePath();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Result.Cancelled;
            }

            CadRuntimeInfo cadRuntime = CadRuntimeDetector.Detect(forceRefresh: true);
            bool cadRuntimeReady = cadRuntime != null && cadRuntime.IsReady;
            if (!cadRuntimeReady)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DwgImport] CAD runtime unavailable. Continue with manual DWG unit confirmation and reduced room/lift text recognition. " +
                    (cadRuntime != null ? cadRuntime.ToString() : string.Empty));
                CadRuntimeUserMessage.ShowWarningOnce(commandData.Application, cadRuntime);
            }

            DwgUnitDetectionResult detection = cadRuntimeReady
                ? DwgUnitDetector.Detect(filePath)
                : BuildManualUnitDetectionResult(cadRuntime);
            SourceUnit resolvedSourceUnit;
            string sourceUnitEvidence;
            bool askUserConfirm = ShouldAskUserToConfirm(detection);
            bool userConfirmed = false;
            if (askUserConfirm)
            {
                DwgSourceUnitSelectionWindow unitWindow = new DwgSourceUnitSelectionWindow(detection);
                TrySetOwner(unitWindow, commandData.Application);
                bool? unitResult = unitWindow.ShowDialog();
                if (unitResult != true)
                {
                    return Result.Cancelled;
                }

                resolvedSourceUnit = unitWindow.SelectedSourceUnit;
                userConfirmed = true;
            }
            else
            {
                resolvedSourceUnit = detection.SuggestedUnit;
            }

            if (!IsSupportedFinalSourceUnit(resolvedSourceUnit))
            {
                TaskDialog.Show("DWG Source Unit", "Please select a concrete DWG source unit before importing.");
                return Result.Cancelled;
            }

            sourceUnitEvidence = BuildSourceUnitEvidence(detection, resolvedSourceUnit, askUserConfirm, userConfirmed);

            DiagnosticRecorder.AppendDebug(
                "[DwgImport] DWG Source Unit Detection" +
                " | File=" + filePath +
                " | LUNITS=" + (detection.LunisText ?? "Unknown") +
                " | INSUNITS=" + (detection.InsunitsText ?? "Unknown") +
                " | DetectedUnit=" + (detection.IsResolved ? detection.DetectedUnit.ToString() : "Unknown") +
                " | SuggestedUnit=" + detection.SuggestedUnit +
                " | AskUserConfirm=" + askUserConfirm +
                " | UserConfirmed=" + userConfirmed +
                " | HasConflict=" + detection.HasConflict +
                " | Warning=" + (detection.WarningMessage ?? string.Empty) +
                " | RevitImportUnit=" + resolvedSourceUnit);

            bool hasExisting = DwgImportService.GetLinkedImportInstances(doc).Count > 0;
            bool replaceExisting = false;
            if (hasExisting)
            {
                bool replaceResult = LocalizedDialogService.Confirm(
                    commandData.Application,
                    Loc.T("Dialog.DwgImport.ReplaceExisting.Message", Environment.NewLine),
                    Loc.T("Dialog.DwgImport.ReplaceExisting.Title"));
                if (!replaceResult)
                {
                    return Result.Cancelled;
                }

                replaceExisting = true;
            }

            if (replaceExisting)
            {
                DwgContextResetService.ResetBeforeImport(doc, new DwgContextResetOptions
                {
                    DeleteGeneratedElements = true,
                    ClearMappingState = true,
                    ClearTrackingState = true
                });
            }

            DwgImportResult result = DwgImportService.ImportLink(
                doc,
                filePath,
                replaceExisting,
                resolvedSourceUnit,
                sourceUnitEvidence);
            if (!result.Success)
            {
                LocalizedDialogService.Error(
                    commandData.Application,
                    Loc.T("Dialog.DwgImport.Failed.MessageFormat", result.ErrorMessage ?? "Unknown"),
                    Loc.T("Dialog.DwgImport.Failed.Title"));
                return Result.Failed;
            }

            DwgContextResetService.ResetAfterImport(
                doc,
                result.LinkInstanceId,
                result.FilePath,
                result.Layers,
                result.SourceUnit,
                result.SourceUnitEvidence);
            RoutePlannerSessionCacheService.MarkDirty(doc, "DWG import changed model context.");
            if (!was3DView)
            {
                // Keep legacy behavior: force open Default 3D view after successful DWG import.
                TryPostDefault3DView(commandData.Application, uiDoc);
                ScheduleEnsureShadedWhen3DReady(commandData.Application);
            }
            else
            {
                // If already in 3D, apply shaded mode to current active 3D view.
                TryEnsureActive3DViewShaded(doc);
                ViewDisplayHelper.EnsureFineDetailLevel(doc);
            }

            string successText = Loc.T(
                "Dialog.DwgImport.Success.MessageFormat",
                Environment.NewLine,
                result.LinkInstanceId.IntegerValue,
                result.Layers == null ? 0 : result.Layers.Count);
            if (result.UnitSuspicious)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DwgImport] Imported bounding box looks abnormally large. Please check source DWG units. " +
                    "InstanceId=" + result.LinkInstanceId.IntegerValue.ToString());
            }

            LocalizedDialogService.Success(
                commandData.Application,
                successText,
                Loc.T("Dialog.DwgImport.Success.Title"));

            ProjectWorkflowModeStoreService.SetMode(doc, ProjectWorkflowMode.DwgImportMode);
            App.UpdateRibbonButtonAvailability(doc);

            // Show the CAD Import Wizard only after the user has started an EMSD workflow,
            // then refresh dockable pane status after a successful import.
            TryShowPreviewPane(commandData.Application);
            _ = PreviewPaneRuntime.ViewModel.RefreshAndAnalyzeAsync();
            return Result.Succeeded;
        }


        private static void TryShowPreviewPane(UIApplication uiApp)
        {
            if (uiApp == null)
            {
                return;
            }

            try
            {
                RoomRecognitionPaneRuntime.HidePanes(uiApp);
                DockablePane pane = uiApp.GetDockablePane(PreviewPaneRuntime.PaneId);
                pane?.Show();
            }
            catch
            {
                // Do not fail the DWG import if the dockable pane cannot be shown.
            }
        }

        private static DwgUnitDetectionResult BuildManualUnitDetectionResult(CadRuntimeInfo cadRuntime)
        {
            string reason = cadRuntime != null && !string.IsNullOrWhiteSpace(cadRuntime.Message)
                ? cadRuntime.Message
                : CadRuntimeTarget.ProductName + " runtime is unavailable.";

            return new DwgUnitDetectionResult
            {
                DetectedUnit = SourceUnit.Auto,
                SuggestedUnit = SourceUnit.Millimeter,
                IsResolved = false,
                HasConflict = false,
                Evidence = "Automatic DWG unit detection skipped because " + CadRuntimeTarget.ProductName + " runtime is unavailable. " + reason,
                WarningMessage = "Automatic DWG unit detection is unavailable. Please confirm the source unit manually.",
                LunisText = "Unknown",
                InsunitsText = "Unknown"
            };
        }

        private static bool ShouldAskUserToConfirm(DwgUnitDetectionResult detection)
        {
            if (detection == null)
            {
                return true;
            }

            if (detection.HasConflict || !detection.IsResolved)
            {
                return true;
            }

            return !IsSupportedFinalSourceUnit(detection.SuggestedUnit);
        }

        private static bool IsSupportedFinalSourceUnit(SourceUnit unit)
        {
            return unit == SourceUnit.Millimeter || unit == SourceUnit.Inch;
        }

        private static string BuildSourceUnitEvidence(
            DwgUnitDetectionResult detection,
            SourceUnit resolvedSourceUnit,
            bool askUserConfirm,
            bool userConfirmed)
        {
            string evidence = detection != null && !string.IsNullOrWhiteSpace(detection.Evidence)
                ? detection.Evidence
                : "Unknown";
            SourceUnit suggestedUnit = detection != null ? detection.SuggestedUnit : SourceUnit.Millimeter;
            evidence += "; Suggested=" + suggestedUnit;
            evidence += "; AskUserConfirm=" + askUserConfirm;
            evidence += "; UserConfirmed=" + userConfirmed;
            evidence += "; RevitImportUnit=" + resolvedSourceUnit;

            string warning = detection != null ? detection.WarningMessage : string.Empty;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                evidence += "; Warning=" + warning;
            }

            return evidence;
        }

        private static string PickDwgFilePath()
        {
            using (WinForms.OpenFileDialog dlg = new WinForms.OpenFileDialog())
            {
                dlg.Title = Loc.T("Dialog.DwgImport.PickFile.Title");
                dlg.Filter = Loc.T("Dialog.DwgImport.PickFile.Filter");
                dlg.Multiselect = false;
                dlg.CheckFileExists = true;
                dlg.CheckPathExists = true;
                if (dlg.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return null;
                }

                return dlg.FileName;
            }
        }

        private static void TrySetOwner(System.Windows.Window window, UIApplication uiApp)
        {
            if (window == null || uiApp == null)
            {
                return;
            }

            try
            {
                IntPtr handle = uiApp.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(window).Owner = handle;
                }
            }
            catch
            {
                // Best effort only; ShowDialog still works without an owner.
            }
        }

        /// <summary>
        /// Posts Revit's built-in command to open the default 3D view.
        /// </summary>
        private static bool TryPostDefault3DView(UIApplication uiApp, UIDocument uiDoc)
        {
            if (uiApp == null || uiDoc == null)
            {
                return false;
            }

            try
            {
                // English note: this matches the user menu item "View > 3D View > Default 3D View".
                RevitCommandId cmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.Default3DView);
                if (cmdId == null)
                {
                    return false;
                }

                uiApp.PostCommand(cmdId);

                // English note: Revit view switching may be asynchronous; best-effort check.
                return uiDoc.ActiveView is View3D || uiDoc.ActiveView?.ViewType == ViewType.ThreeD;
            }
            catch
            {
                return false;
            }
        }

        private static void ScheduleEnsureShadedWhen3DReady(UIApplication uiApp)
        {
            if (uiApp == null)
            {
                return;
            }

            // Register a one-shot idling watcher to handle asynchronous view switch.
            _pendingEnsureShadedAfter3DPost = true;
            _ensureShadedIdlingAttempts = 0;
            uiApp.Idling -= OnEnsureShadedAfter3DPostIdling;
            uiApp.Idling += OnEnsureShadedAfter3DPostIdling;
        }

        private static void OnEnsureShadedAfter3DPostIdling(object sender, IdlingEventArgs e)
        {
            UIApplication uiApp = sender as UIApplication;
            if (uiApp == null)
            {
                return;
            }

            if (!_pendingEnsureShadedAfter3DPost)
            {
                uiApp.Idling -= OnEnsureShadedAfter3DPostIdling;
                return;
            }

            _ensureShadedIdlingAttempts++;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            View3D view3D = doc?.ActiveView as View3D;

            if (view3D == null)
            {
                if (_ensureShadedIdlingAttempts >= MaxShadedEnsureIdlingAttempts)
                {
                    _pendingEnsureShadedAfter3DPost = false;
                    uiApp.Idling -= OnEnsureShadedAfter3DPostIdling;
                }

                return;
            }

            TryEnsureActive3DViewShaded(doc);
            ViewDisplayHelper.EnsureFineDetailLevel(doc);
            _pendingEnsureShadedAfter3DPost = false;
            uiApp.Idling -= OnEnsureShadedAfter3DPostIdling;
        }

        private static void TryEnsureActive3DViewShaded(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            View3D view3D = doc.ActiveView as View3D;
            if (view3D == null)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Set 3D View Shaded After DWG Import"))
                {
                    tx.Start();
                    // Apply shaded mode only to current active 3D view after successful DWG import.
                    ViewDisplayStyleHelper.Ensure3DViewShaded(view3D);
                    tx.Commit();
                }
            }
            catch
            {
                // Do not fail import flow if shaded switch is blocked by template or view state.
            }
        }
    }
}
