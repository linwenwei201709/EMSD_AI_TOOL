using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services;
using CadToRevit.Services.Rooms;
using CadToRevit.UI;
using System;
using System.Collections.Generic;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfThreading = System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ExportIfcForPathCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData == null ? null : commandData.Application;
            UIDocument uiDoc = uiApp == null ? null : uiApp.ActiveUIDocument;

            if (uiApp == null || uiDoc == null || uiDoc.Document == null)
            {
                message = "Please open a Revit project before exporting IFC.";
                TaskDialog.Show("Export IFC", message);
                return Result.Cancelled;
            }

            try
            {
                RevitCommandId exportIfcCommandId =
                    RevitCommandId.LookupPostableCommandId(PostableCommand.ExportIFC);

                if (exportIfcCommandId == null)
                {
                    message = "Revit native IFC export command was not found.";
                    TaskDialog.Show("Export IFC", message);
                    return Result.Failed;
                }

                if (!uiApp.CanPostCommand(exportIfcCommandId))
                {
                    message = "Revit native IFC export command cannot be started in the current context. " +
                              "Please finish the current Revit operation and try again.";
                    TaskDialog.Show("Export IFC", message);
                    return Result.Cancelled;
                }

                if (!ConfirmInternalOriginExport())
                {
                    return Result.Cancelled;
                }

                Room3DVisualizationIfcCleanupService.DeleteVisualizationElementsForIfcExport(uiDoc.Document);
                // Preserve Door family instances during the default native IFC export.
                // Lift/door elements are no longer deleted automatically before export.

                // PostCommand runs after this external command returns control to Revit.
                // This opens the same UI as File > Export > IFC.
                uiApp.PostCommand(exportIfcCommandId);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "Failed to open Revit native IFC export dialog." + Environment.NewLine + ex.Message;
                TaskDialog.Show("Export IFC", message);
                return Result.Failed;
            }
        }


        private static bool ConfirmInternalOriginExport()
        {
            TaskDialog dialog = new TaskDialog("Export IFC");
            dialog.MainInstruction = "Use Internal Origin for path planning IFC export.";
            dialog.MainContent =
                "Path planning requires the IFC to be exported with Internal Origin.\n\n" +
                "In the Revit Export IFC window, please check:\n" +
                "Modify setup... > Geographic Reference > Coordinate Base = Internal Origin\n\n" +
                "Do not use Shared Coordinates. Shared Coordinates may generate very large coordinates and cause path planning failure.";
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Continue Export");
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            dialog.DefaultButton = TaskDialogResult.Cancel;

            TaskDialogResult result = dialog.Show();
            return result == TaskDialogResult.CommandLink1;
        }

        #region Legacy custom IFC export workflow - kept for rollback, not called by default

        private Result ExecuteLegacyCustomIfcExport(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc == null ? null : uiDoc.Document;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            IFCVersion? selectedIfcVersion = ShowIfcVersionDialog();
            if (!selectedIfcVersion.HasValue)
            {
                return Result.Cancelled;
            }

            string defaultName = string.IsNullOrWhiteSpace(doc.Title) ? "AI_Path_Export.ifc" : (doc.Title + "_AI_Path.ifc");
            using (WinForms.SaveFileDialog dialog = new WinForms.SaveFileDialog())
            {
                dialog.Title = "导出 IFC（路径识别）";
                dialog.Filter = "IFC files (*.ifc)|*.ifc";
                dialog.FileName = defaultName;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                IfcPathExportService.IfcPathExportResult result;
                using (IfcExportProgressWindow progressWindow = new IfcExportProgressWindow())
                {
                    IfcExportProgressReporter reporter = new IfcExportProgressReporter(progressWindow);
                    progressWindow.Show();
                    reporter.UpdateProgress("Stage: Export IFC", 0, 5, "Preparing IFC export...");
                    result = IfcPathExportService.Export(doc, dialog.FileName, selectedIfcVersion.Value, reporter);
                    reporter.UpdateProgress(
                        result.Success ? "Stage: IFC Export Completed" : "Stage: IFC Export Failed",
                        5,
                        5,
                        result.Success ? "IFC export completed." : (result.Error ?? "IFC export failed."));
                }

                if (!result.Success)
                {
                    LocalizedDialogService.Error(
                        uiApp,
                        BuildIfcExportFailureMessage(result));
                    return Result.Failed;
                }

                LocalizedDialogService.Info(
                    uiApp,
                    BuildIfcExportSuccessMessage(result));
            }

            return Result.Succeeded;
        }

        #endregion


        private static IFCVersion? ShowIfcVersionDialog()
        {
            List<IfcVersionListItem> items = BuildIfcVersionItems();
            if (items.Count == 0)
            {
                return null;
            }

            using (WinForms.Form form = new WinForms.Form())
            using (WinForms.Label label = new WinForms.Label())
            using (WinForms.ComboBox combo = new WinForms.ComboBox())
            using (WinForms.Button okButton = new WinForms.Button())
            using (WinForms.Button cancelButton = new WinForms.Button())
            {
                form.Text = "Select IFC Version";
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new System.Drawing.Size(720, 300);
                form.ShowInTaskbar = false;
                form.Font = new System.Drawing.Font(form.Font.FontFamily, 13.0f);

                label.Text = "IFC Version:";
                label.AutoSize = false;
                label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                label.Left = 46;
                label.Top = 72;
                label.Width = 180;
                label.Height = 42;

                combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
                combo.Left = 240;
                combo.Top = 72;
                combo.Width = 420;
                combo.Height = 42;
                combo.DropDownWidth = 420;
                combo.IntegralHeight = true;
                combo.Font = new System.Drawing.Font(form.Font.FontFamily, 13.0f);
                foreach (IfcVersionListItem item in items)
                {
                    combo.Items.Add(item);
                }
                combo.SelectedIndex = ResolveDefaultSelectedIndex(items);

                okButton.Text = "Export";
                okButton.Left = 320;
                okButton.Top = 198;
                okButton.Width = 170;
                okButton.Height = 52;
                okButton.Font = new System.Drawing.Font(form.Font.FontFamily, 12.0f);
                okButton.DialogResult = WinForms.DialogResult.OK;

                cancelButton.Text = "Cancel";
                cancelButton.Left = 510;
                cancelButton.Top = 198;
                cancelButton.Width = 170;
                cancelButton.Height = 52;
                cancelButton.Font = new System.Drawing.Font(form.Font.FontFamily, 12.0f);
                cancelButton.DialogResult = WinForms.DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(combo);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return null;
                }

                IfcVersionListItem selected = combo.SelectedItem as IfcVersionListItem;
                return selected == null ? items[0].Version : selected.Version;
            }
        }

        private static int ResolveDefaultSelectedIndex(List<IfcVersionListItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && string.Equals(items[i].VersionName, "IFC2x3", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && string.Equals(items[i].VersionName, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private static List<IfcVersionListItem> BuildIfcVersionItems()
        {
            List<IfcVersionListItem> items = new List<IfcVersionListItem>();
            AddIfcVersionIfAvailable(items, "IFC2x2", "IFC 2x2");
            AddIfcVersionIfAvailable(items, "IFC2x3", "IFC 2x3");
            AddIfcVersionIfAvailable(items, "IFC4", "IFC 4");
            AddIfcVersionIfAvailable(items, "IFC4x3", "IFC 4.3");
            AddIfcVersionIfAvailable(items, "IFC4X3", "IFC 4.3");
            return items;
        }

        private static void AddIfcVersionIfAvailable(List<IfcVersionListItem> items, string enumName, string displayName)
        {
            if (items == null || string.IsNullOrWhiteSpace(enumName))
            {
                return;
            }

            IFCVersion parsed;
            if (!Enum.TryParse(enumName, true, out parsed))
            {
                return;
            }

            foreach (IfcVersionListItem existing in items)
            {
                if (existing != null && EqualityComparer<IFCVersion>.Default.Equals(existing.Version, parsed))
                {
                    return;
                }
            }

            items.Add(new IfcVersionListItem(parsed, parsed.ToString(), displayName));
        }

        private static string ToIfcVersionDisplayName(string versionName)
        {
            if (string.Equals(versionName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return "Default (Revit exporter default)";
            }
            if (string.Equals(versionName, "IFC2x2", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x2";
            }
            if (string.Equals(versionName, "IFC2x3", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3";
            }
            if (string.Equals(versionName, "IFC2x3CV2", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3 Coordination View 2.0";
            }
            if (string.Equals(versionName, "IFC2x3FM", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 2x3 FM Handover View";
            }
            if (string.Equals(versionName, "IFCBCA", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC BCA";
            }
            if (string.Equals(versionName, "IFCCOBIE", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC COBie";
            }
            if (string.Equals(versionName, "IFC4", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4";
            }
            if (string.Equals(versionName, "IFC4RV", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4 Reference View";
            }
            if (string.Equals(versionName, "IFC4DTV", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4 Design Transfer View";
            }
            if (string.Equals(versionName, "IFC4x3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(versionName, "IFC4X3", StringComparison.OrdinalIgnoreCase))
            {
                return "IFC 4.3";
            }

            return versionName;
        }

        private static string BuildIfcExportSuccessMessage(IfcPathExportService.IfcPathExportResult result)
        {
            return "IFC export completed." + System.Environment.NewLine +
                "IFC Version: " + (result == null ? string.Empty : result.IfcVersion ?? string.Empty) + System.Environment.NewLine +
                "View: " + (result == null ? string.Empty : result.ExportViewName ?? string.Empty) + System.Environment.NewLine +
                "DoorMode: " + (result == null ? string.Empty : result.DoorMode ?? string.Empty) + System.Environment.NewLine +
                "Path: " + (result == null ? string.Empty : result.ExportPath ?? string.Empty);
        }

        private static string BuildIfcExportFailureMessage(IfcPathExportService.IfcPathExportResult result)
        {
            return "IFC export failed." + System.Environment.NewLine +
                "IFC Version: " + (result == null ? string.Empty : result.IfcVersion ?? string.Empty) + System.Environment.NewLine +
                "View: " + (result == null ? string.Empty : result.ExportViewName ?? string.Empty) + System.Environment.NewLine +
                "DoorMode: " + (result == null ? string.Empty : result.DoorMode ?? string.Empty) + System.Environment.NewLine +
                "Path: " + (result == null ? string.Empty : result.ExportPath ?? string.Empty) + System.Environment.NewLine +
                "Error: " + (result == null ? string.Empty : result.Error ?? string.Empty);
        }


        private sealed class IfcExportProgressReporter : IfcPathExportService.IIfcExportProgressReporter
        {
            private readonly IfcExportProgressWindow _window;

            public IfcExportProgressReporter(IfcExportProgressWindow window)
            {
                _window = window;
            }

            public bool IsCancellationRequested
            {
                get { return _window != null && _window.IsCancellationRequested; }
            }

            public void UpdateProgress(string stage, int current, int total, string detail)
            {
                if (_window == null || !_window.IsLoaded)
                {
                    return;
                }

                _window.UpdateProgress(stage, current, total, detail);
            }
        }

        private sealed class IfcExportProgressWindow : Wpf.Window, IDisposable
        {
            private readonly WpfControls.TextBlock _stageText = new WpfControls.TextBlock();
            private readonly WpfControls.TextBlock _detailText = new WpfControls.TextBlock();
            private readonly WpfControls.ProgressBar _progressBar = new WpfControls.ProgressBar();
            private readonly WpfControls.Button _cancelButton = new WpfControls.Button();

            public bool IsCancellationRequested { get; private set; }

            public IfcExportProgressWindow()
            {
                Title = "Progress";
                Width = 760;
                Height = 320;
                ResizeMode = Wpf.ResizeMode.NoResize;
                WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
                WindowStyle = Wpf.WindowStyle.SingleBorderWindow;
                Topmost = true;

                WpfControls.Grid root = new WpfControls.Grid
                {
                    Margin = new Wpf.Thickness(28, 24, 28, 24)
                };
                root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
                root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
                root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
                root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
                root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

                _stageText.Text = "Stage: Export IFC";
                _stageText.FontSize = 24;
                _stageText.FontWeight = Wpf.FontWeights.SemiBold;
                _stageText.Margin = new Wpf.Thickness(0, 0, 0, 16);
                WpfControls.Grid.SetRow(_stageText, 0);
                root.Children.Add(_stageText);

                _progressBar.Minimum = 0;
                _progressBar.Maximum = 100;
                _progressBar.Height = 30;
                _progressBar.Margin = new Wpf.Thickness(0, 0, 0, 18);
                _progressBar.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0, 128, 0));
                WpfControls.Grid.SetRow(_progressBar, 1);
                root.Children.Add(_progressBar);

                _detailText.Text = "Preparing IFC export...";
                _detailText.FontSize = 18;
                _detailText.TextWrapping = Wpf.TextWrapping.Wrap;
                _detailText.Margin = new Wpf.Thickness(0, 0, 0, 16);
                WpfControls.Grid.SetRow(_detailText, 2);
                root.Children.Add(_detailText);

                WpfControls.StackPanel buttonPanel = new WpfControls.StackPanel
                {
                    Orientation = WpfControls.Orientation.Horizontal,
                    HorizontalAlignment = Wpf.HorizontalAlignment.Right
                };
                _cancelButton.Content = "Cancel";
                _cancelButton.Width = 170;
                _cancelButton.Height = 52;
                _cancelButton.FontSize = 16;
                _cancelButton.Click += (s, e) =>
                {
                    IsCancellationRequested = true;
                    _cancelButton.IsEnabled = false;
                    _cancelButton.Content = "Cancelling...";
                };
                buttonPanel.Children.Add(_cancelButton);
                WpfControls.Grid.SetRow(buttonPanel, 4);
                root.Children.Add(buttonPanel);

                Content = root;
            }

            public void UpdateProgress(string stage, int current, int total, string detail)
            {
                _stageText.Text = stage ?? string.Empty;
                _detailText.Text = detail ?? string.Empty;
                int safeTotal = total <= 0 ? 1 : total;
                int safeCurrent = current < 0 ? 0 : Math.Min(current, safeTotal);
                _progressBar.Value = Math.Round((safeCurrent * 100.0) / safeTotal);
                PumpUi();
            }

            public void Dispose()
            {
                try
                {
                    Close();
                }
                catch
                {
                    // Ignore close errors during Revit shutdown or command cancellation.
                }
            }

            private static void PumpUi()
            {
                WpfThreading.DispatcherFrame frame = new WpfThreading.DispatcherFrame();
                WpfThreading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    WpfThreading.DispatcherPriority.Background,
                    new WpfThreading.DispatcherOperationCallback(o =>
                    {
                        ((WpfThreading.DispatcherFrame)o).Continue = false;
                        return null;
                    }),
                    frame);
                WpfThreading.Dispatcher.PushFrame(frame);
            }
        }

        private sealed class IfcVersionListItem
        {
            public IfcVersionListItem(IFCVersion version, string versionName, string displayName)
            {
                Version = version;
                VersionName = versionName ?? string.Empty;
                DisplayName = displayName ?? VersionName;
            }

            public IFCVersion Version { get; private set; }
            public string VersionName { get; private set; }
            public string DisplayName { get; private set; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
