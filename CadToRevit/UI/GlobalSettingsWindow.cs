using Autodesk.Revit.DB;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WpfGrid = System.Windows.Controls.Grid;

namespace CadToRevit.UI
{
    /// <summary>
    /// Provides a pure C# WPF window for editing shared project-level settings.
    /// </summary>
    internal sealed class GlobalSettingsWindow : Window
    {
        private const double StandardEditorWidth = 300.0;
        private const double StandardEditorHeight = 27.0;

        private const string LabelHeadroomClearance = "Headroom clearance (mm)";
        private const string LabelOverrideWallHeight = "Override wall height for all wall layers";
        private const string LabelWallHeight = "Wall height (mm)";
        private const string LabelCreateDoorOpeningOnly = "Create wall openings instead of door families";
        private const string LabelOverrideDoorHeight = "Override door height for all door layers";
        private const string LabelDoorHeight = "Door height (mm)";
        private const string LabelOverrideDoorSillHeight = "Override door sill height for all door layers";
        private const string LabelDoorSillHeight = "Door sill height (mm)";
        private const string LabelRecognitionWindow = "Recognition window (m)";
        private const string LabelTargetKeywords = "Target keywords";
        private const string LabelLiftGeometryLayerNames = "Lift Geometry Layer Names";
        private const string LabelMaximumDoorGap = "Maximum door gap (mm)";
        private const string LabelSmallGapPatch = "Small gap patch (mm)";

        private readonly Document _document;
        private readonly GlobalSettingsViewModel _viewModel;
        private CheckBox _safeModeCheckBox;
        private CheckBox _autoJoinCheckBox;
        private TextBox _headRoomTextBox;
        private CheckBox _globalWallHeightCheckBox;
        private TextBox _globalWallHeightTextBox;
        private CheckBox _globalDoorHeightCheckBox;
        private TextBox _globalDoorHeightTextBox;
        private CheckBox _globalDoorSillHeightCheckBox;
        private TextBox _globalDoorSillHeightTextBox;
        private CheckBox _createDoorOpeningOnlyCheckBox;
        private ComboBox _recognitionWindowComboBox;
        private TextBox _targetKeywordsTextBox;
        private TextBox _liftGeometryLayerNamesTextBox;
        private TextBox _doorGapTextBox;
        private TextBox _smallGapPatchTextBox;

        public GlobalSettingsWindow(Document document)
        {
            _document = document;
            LayerOverrideStoreData store = LoadDocScopedStore(document);
            _viewModel = GlobalSettingsViewModel.FromSettings(
                store != null ? store.GlobalGenerationSettings : null,
                store != null ? store.RoomRecognitionSettings : null);

            Title = Loc.T(LocalizedKeys.GlobalSettings.Title);
            Width = 920;
            Height = 680;
            MinWidth = 820;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildContent();
        }

        private FrameworkElement BuildContent()
        {
            WpfGrid root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TabControl tabs = new TabControl
            {
                Margin = new Thickness(16)
            };
            tabs.Items.Add(BuildGeneralTab());
            tabs.Items.Add(BuildWallTab());
            tabs.Items.Add(BuildDoorTab());
            tabs.Items.Add(BuildRoomTab());
            tabs.Items.Add(BuildLogTab());
            WpfGrid.SetRow(tabs, 0);
            root.Children.Add(tabs);

            DockPanel footer = new DockPanel
            {
                Margin = new Thickness(16, 4, 16, 16),
                LastChildFill = false
            };

            Button saveButton = new Button
            {
                Content = Loc.T(LocalizedKeys.Common.Save),
                MinWidth = 100,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            saveButton.Click += OnSaveClick;
            DockPanel.SetDock(saveButton, Dock.Right);

            Button cancelButton = new Button
            {
                Content = Loc.T(LocalizedKeys.Common.Cancel),
                MinWidth = 100,
                MinHeight = 32,
                IsCancel = true
            };
            cancelButton.Click += (sender, args) => Close();
            DockPanel.SetDock(cancelButton, Dock.Right);

            footer.Children.Add(cancelButton);
            footer.Children.Add(saveButton);
            WpfGrid.SetRow(footer, 1);
            root.Children.Add(footer);

            return root;
        }

        private TabItem BuildGeneralTab()
        {
            StackPanel panel = CreatePanel();

            // Keep these two settings alive internally, but hide them from the UI.
            // They are legacy safety defaults and should remain enabled unless changed by code.
            _safeModeCheckBox = CreateCheckBox(Loc.T(LocalizedKeys.GlobalSettings.SafeMode), _viewModel.SafeModeEnabled);
            _safeModeCheckBox.Visibility = System.Windows.Visibility.Collapsed;
            _autoJoinCheckBox = CreateCheckBox(Loc.T(LocalizedKeys.GlobalSettings.AutoJoinWalls), _viewModel.AutoJoinWallsAfterCreate);
            _autoJoinCheckBox.Visibility = System.Windows.Visibility.Collapsed;

            _headRoomTextBox = CreateTextBox(_viewModel.HeadRoomMm);

            panel.Children.Add(_safeModeCheckBox);
            panel.Children.Add(_autoJoinCheckBox);
            panel.Children.Add(CreateField(LabelHeadroomClearance, _headRoomTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Sets the reserved vertical clearance used by path recognition and delivery-route checks."));
            return CreateTab(Loc.T(LocalizedKeys.GlobalSettings.TabGeneral), panel);
        }

        private TabItem BuildWallTab()
        {
            StackPanel panel = CreatePanel();
            _globalWallHeightCheckBox = CreateCheckBox(LabelOverrideWallHeight, _viewModel.UseGlobalWallHeightOverride);
            _globalWallHeightTextBox = CreateTextBox(_viewModel.GlobalWallHeightMm);
            _globalWallHeightCheckBox.Checked += (sender, args) => UpdateEnabledStates();
            _globalWallHeightCheckBox.Unchecked += (sender, args) => UpdateEnabledStates();

            panel.Children.Add(_globalWallHeightCheckBox);
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "When enabled, applies one common wall height to every generated wall layer instead of using each layer's own height."));
            panel.Children.Add(CreateField(LabelWallHeight, _globalWallHeightTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Defines the common generated wall height used when the wall-height override is enabled."));
            return CreateTab(Loc.T(LocalizedKeys.GlobalSettings.TabWalls), panel);
        }

        private TabItem BuildDoorTab()
        {
            StackPanel panel = CreatePanel();
            _globalDoorHeightCheckBox = CreateCheckBox(LabelOverrideDoorHeight, _viewModel.UseGlobalDoorHeightOverride);
            _globalDoorHeightTextBox = CreateTextBox(_viewModel.GlobalDoorHeightMm);
            _globalDoorSillHeightCheckBox = CreateCheckBox(LabelOverrideDoorSillHeight, _viewModel.UseGlobalDoorSillHeightOverride);
            _globalDoorSillHeightTextBox = CreateTextBox(_viewModel.GlobalDoorSillHeightMm);
            _createDoorOpeningOnlyCheckBox = CreateCheckBox(LabelCreateDoorOpeningOnly, _viewModel.CreateDoorOpeningOnly);
            _globalDoorHeightCheckBox.Checked += (sender, args) => UpdateEnabledStates();
            _globalDoorHeightCheckBox.Unchecked += (sender, args) => UpdateEnabledStates();
            _globalDoorSillHeightCheckBox.Checked += (sender, args) => UpdateEnabledStates();
            _globalDoorSillHeightCheckBox.Unchecked += (sender, args) => UpdateEnabledStates();

            panel.Children.Add(_createDoorOpeningOnlyCheckBox);
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Checked: door layers create rectangular wall openings for path recognition. Unchecked: door layers create Revit door family instances."));
            panel.Children.Add(_globalDoorHeightCheckBox);
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "When enabled, applies one common door height to every generated door layer."));
            panel.Children.Add(CreateField(LabelDoorHeight, _globalDoorHeightTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Defines the common generated door height used when the door-height override is enabled."));
            panel.Children.Add(_globalDoorSillHeightCheckBox);
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "When enabled, applies one common sill height to every generated door layer."));
            panel.Children.Add(CreateField(LabelDoorSillHeight, _globalDoorSillHeightTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Defines the vertical distance from the host level to the bottom of generated doors when the sill-height override is enabled."));
            return CreateTab(Loc.T(LocalizedKeys.GlobalSettings.TabDoors), panel);
        }

        private TabItem BuildRoomTab()
        {
            StackPanel panel = CreatePanel();
            _recognitionWindowComboBox = new ComboBox
            {
                Width = StandardEditorWidth,
                Height = StandardEditorHeight,
                MinHeight = StandardEditorHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 1, 4, 1),
                ItemsSource = new[] { "12", "18" },
                SelectedValue = ((int)RoomRecognitionSettings.NormalizeModelRecognitionWindowSizeM(_viewModel.RoomRecognitionWindowSizeM)).ToString(CultureInfo.InvariantCulture)
            };
            _targetKeywordsTextBox = new TextBox
            {
                Text = _viewModel.TargetKeywordsText ?? string.Empty,
                Width = StandardEditorWidth,
                Height = StandardEditorHeight,
                MinHeight = StandardEditorHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 1, 4, 1)
            };
            _liftGeometryLayerNamesTextBox = new TextBox
            {
                Text = _viewModel.LiftGeometryLayerNames ?? string.Empty,
                Width = StandardEditorWidth,
                Height = StandardEditorHeight,
                MinHeight = StandardEditorHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 1, 4, 1)
            };
            _doorGapTextBox = CreateTextBox(_viewModel.DoorGapMaxMm);
            _smallGapPatchTextBox = CreateTextBox(_viewModel.SmallGapPatchMaxMm);

            panel.Children.Add(CreateField(LabelRecognitionWindow, _recognitionWindowComboBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Defines the search-window size used when scanning the imported model for room and lift candidates. A larger value covers a wider area but may increase processing time."));
            panel.Children.Add(CreateField(LabelTargetKeywords, _targetKeywordsTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Provides comma-separated keywords used to identify target equipment rooms, such as A/C, AHU, or PAU."));
            panel.Children.Add(CreateField(LabelLiftGeometryLayerNames, _liftGeometryLayerNamesTextBox));
            panel.Children.Add(CreateSettingDescription(
                "Purpose",
                "Lists CAD/DWG layer names used to detect lift geometry. Separate multiple names with commas or semicolons, for example DT001, LIFT, ELEVATOR."));
            // Keep these two recognition parameters active and persisted, but hide them from
            // the Settings UI for now. Their current stored/default values are still loaded into
            // the controls above and are written back unchanged when the user clicks Save.
            return CreateTab(Loc.T(LocalizedKeys.GlobalSettings.TabRooms), panel);
        }


        private TabItem BuildLogTab()
        {
            StackPanel panel = CreatePanel();

            TextBlock description = new TextBlock
            {
                Text = "Export today's EMSD AI Tool logs as a ZIP archive, or open the current log folder.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 18)
            };
            panel.Children.Add(description);

            Border pathBorder = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 22),
                Child = new TextBlock
                {
                    Text = DiagnosticRecorder.GetLogDirectory(),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DimGray
                }
            };
            panel.Children.Add(pathBorder);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            Button exportButton = new Button
            {
                Content = "Export Logs",
                MinWidth = 130,
                MinHeight = 38,
                Margin = new Thickness(0, 0, 12, 0)
            };
            exportButton.Click += OnExportLogsClick;

            Button openButton = new Button
            {
                Content = "Open Logs",
                MinWidth = 130,
                MinHeight = 38
            };
            openButton.Click += OnOpenLogsClick;

            buttonPanel.Children.Add(exportButton);
            buttonPanel.Children.Add(openButton);
            panel.Children.Add(buttonPanel);

            return CreateTab("Log", panel);
        }

        private void OnExportLogsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string outputDirectory = ResolveDownloadsDirectory();
                Directory.CreateDirectory(outputDirectory);

                string outputPath = Path.Combine(
                    outputDirectory,
                    "EMSD_AI_Tool_Logs_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");

                int exportedFileCount;
                if (!DiagnosticRecorder.TryExportTodayLogs(outputPath, out exportedFileCount))
                {
                    MessageBox.Show(
                        "No log files generated today were found.",
                        "Export Logs",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show(
                    "Today's logs were exported successfully." + Environment.NewLine +
                    "Files: " + exportedFileCount + Environment.NewLine +
                    "ZIP: " + outputPath,
                    "Export Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ShowFileInExplorer(outputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to export today's logs." + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Export Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnOpenLogsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string logDirectory = DiagnosticRecorder.GetLogDirectory();
                Process.Start(new ProcessStartInfo
                {
                    FileName = logDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open the log folder." + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Open Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string ResolveDownloadsDirectory()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads))
                {
                    return downloads;
                }
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                return desktop;
            }

            return DiagnosticRecorder.GetLogDirectory();
        }

        private static void ShowFileInExplorer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + filePath + "\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Export has already succeeded; Explorer activation is optional.
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadViewModel())
            {
                return;
            }

            LayerOverrideStoreService.SaveGlobalSettings(
                _document,
                _viewModel.BuildRoomRecognitionSettings(),
                _viewModel.BuildGlobalSettings());
            DialogResult = true;
            Close();
        }

        private static LayerOverrideStoreData LoadDocScopedStore(Document document)
        {
            // Use the same merged load path as generation. The previous version only accepted
            // LoadSource == "RVT", so a setting saved to AppData as a fallback could be ignored
            // and the UI would reopen with the default checked state.
            return LayerOverrideStoreService.Load(document) ?? new LayerOverrideStoreData();
        }

        private bool TryReadViewModel()
        {
            double headRoomMm;
            double wallHeightMm;
            double doorHeightMm;
            double doorSillHeightMm;
            double roomDoorGapMm;
            double smallGapPatchMm;
            double recognitionWindowM;

            if (!TryParseNonNegative(_headRoomTextBox.Text, LabelHeadroomClearance, out headRoomMm) ||
                !TryParsePositive(_globalWallHeightTextBox.Text, LabelWallHeight, out wallHeightMm) ||
                !TryParsePositive(_globalDoorHeightTextBox.Text, LabelDoorHeight, out doorHeightMm) ||
                !TryParseNonNegative(_globalDoorSillHeightTextBox.Text, LabelDoorSillHeight, out doorSillHeightMm) ||
                !TryParsePositive(_doorGapTextBox.Text, LabelMaximumDoorGap, out roomDoorGapMm) ||
                !TryParsePositive(_smallGapPatchTextBox.Text, LabelSmallGapPatch, out smallGapPatchMm) ||
                !TryParsePositive(_recognitionWindowComboBox.SelectedValue as string, LabelRecognitionWindow, out recognitionWindowM))
            {
                return false;
            }

            _viewModel.SafeModeEnabled = _safeModeCheckBox == null ? _viewModel.SafeModeEnabled : _safeModeCheckBox.IsChecked == true;
            _viewModel.AutoJoinWallsAfterCreate = _autoJoinCheckBox == null ? _viewModel.AutoJoinWallsAfterCreate : _autoJoinCheckBox.IsChecked == true;
            _viewModel.HeadRoomMm = headRoomMm;
            _viewModel.UseGlobalWallHeightOverride = _globalWallHeightCheckBox.IsChecked == true;
            _viewModel.GlobalWallHeightMm = wallHeightMm;
            _viewModel.UseGlobalDoorHeightOverride = _globalDoorHeightCheckBox.IsChecked == true;
            _viewModel.GlobalDoorHeightMm = doorHeightMm;
            _viewModel.UseGlobalDoorSillHeightOverride = _globalDoorSillHeightCheckBox.IsChecked == true;
            _viewModel.GlobalDoorSillHeightMm = doorSillHeightMm;
            _viewModel.CreateDoorOpeningOnly = _createDoorOpeningOnlyCheckBox != null && _createDoorOpeningOnlyCheckBox.IsChecked == true;
            _viewModel.RoomRecognitionWindowSizeM = recognitionWindowM;
            _viewModel.TargetKeywordsText = _targetKeywordsTextBox.Text ?? string.Empty;
            _viewModel.LiftGeometryLayerNames = _liftGeometryLayerNamesTextBox.Text ?? string.Empty;
            _viewModel.DoorGapMaxMm = roomDoorGapMm;
            _viewModel.SmallGapPatchMaxMm = smallGapPatchMm;
            return true;
        }

        private void UpdateEnabledStates()
        {
            if (_globalWallHeightTextBox != null)
            {
                _globalWallHeightTextBox.IsEnabled = _globalWallHeightCheckBox.IsChecked == true;
            }

            if (_globalDoorHeightTextBox != null)
            {
                _globalDoorHeightTextBox.IsEnabled = _globalDoorHeightCheckBox.IsChecked == true;
            }

            if (_globalDoorSillHeightTextBox != null)
            {
                _globalDoorSillHeightTextBox.IsEnabled = _globalDoorSillHeightCheckBox.IsChecked == true;
            }
        }

        protected override void OnContentRendered(System.EventArgs e)
        {
            base.OnContentRendered(e);
            UpdateEnabledStates();
        }

        private static StackPanel CreatePanel()
        {
            return new StackPanel
            {
                Margin = new Thickness(16)
            };
        }

        private static TabItem CreateTab(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = content
                }
            };
        }

        private static CheckBox CreateCheckBox(string label, bool value)
        {
            return new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static TextBox CreateTextBox(double value)
        {
            return new TextBox
            {
                Text = value.ToString("0.##", CultureInfo.InvariantCulture),
                Width = StandardEditorWidth,
                Height = StandardEditorHeight,
                MinHeight = StandardEditorHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 1, 4, 1)
            };
        }

        private static TextBlock CreateSettingDescription(string title, string description)
        {
            TextBlock textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                FontSize = 12.5,
                LineHeight = 18,
                Margin = new Thickness(26, -7, 18, 16)
            };

            textBlock.Inlines.Add(new Run((title ?? string.Empty) + ": ")
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.DimGray
            });
            textBlock.Inlines.Add(new Run(description ?? string.Empty));
            return textBlock;
        }

        private static FrameworkElement CreateField(string label, System.Windows.Controls.Control editor)
        {
            WpfGrid row = new WpfGrid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(text, 0);
            WpfGrid.SetColumn(editor, 1);
            editor.HorizontalAlignment = HorizontalAlignment.Left;
            editor.MinWidth = Math.Max(editor.MinWidth, StandardEditorWidth);
            editor.Height = StandardEditorHeight;
            editor.MinHeight = StandardEditorHeight;
            row.Children.Add(text);
            row.Children.Add(editor);
            return row;
        }

        private static bool TryParsePositive(string text, string fieldName, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return true;
            }

            MessageBox.Show(
                Loc.T(LocalizedKeys.GlobalSettings.ValidationPositive, fieldName),
                Loc.T(LocalizedKeys.GlobalSettings.Title),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private static bool TryParseNonNegative(string text, string fieldName, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0)
            {
                return true;
            }

            MessageBox.Show(
                Loc.T(LocalizedKeys.GlobalSettings.ValidationNonNegative, fieldName),
                Loc.T(LocalizedKeys.GlobalSettings.Title),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }
}
