using Autodesk.Revit.DB;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models.Settings;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

        private LayerRuleProfileStoreData _layerRuleStore;
        private StackPanel _layerRuleRowsPanel;

        public GlobalSettingsWindow(Document document)
        {
            _document = document;
            LayerOverrideStoreData store = LoadDocScopedStore(document);
            _viewModel = GlobalSettingsViewModel.FromSettings(
                store != null ? store.GlobalGenerationSettings : null,
                store != null ? store.RoomRecognitionSettings : null);
            _layerRuleStore = LayerRuleProfileStoreService.Load();

            Title = Loc.T(LocalizedKeys.GlobalSettings.Title);
            Width = 1100;
            Height = 760;
            MinWidth = 960;
            MinHeight = 650;
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

            // Make the Global Settings tabs easier to read and click while preserving
            // the existing Revit/WPF theme and selected-tab appearance.
            Style tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 14.0));
            tabItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(14, 6, 14, 6)));
            tabItemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34.0));
            tabs.ItemContainerStyle = tabItemStyle;

            tabs.Items.Add(BuildGeneralTab());
            tabs.Items.Add(BuildWallTab());
            tabs.Items.Add(BuildDoorTab());
            tabs.Items.Add(BuildRoomTab());
            tabs.Items.Add(BuildLayerRulesTab());
            tabs.Items.Add(BuildLogTab());
            WpfGrid.SetRow(tabs, 0);
            root.Children.Add(tabs);

            DockPanel footer = new DockPanel
            {
                Margin = new Thickness(16, 4, 16, 16),
                LastChildFill = false
            };

            Button saveButton = CreatePrimaryActionButton(Loc.T(LocalizedKeys.Common.Save), 100);
            saveButton.IsDefault = true;
            saveButton.Margin = new Thickness(0, 0, 10, 0);
            saveButton.Click += OnSaveClick;
            DockPanel.SetDock(saveButton, Dock.Right);

            Button cancelButton = CreateSecondaryActionButton(Loc.T(LocalizedKeys.Common.Cancel), 100);
            cancelButton.IsCancel = true;
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


        private TabItem BuildLayerRulesTab()
        {
            WpfGrid root = new WpfGrid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Button addButton = CreateSecondaryActionButton("Add Rule", 96);
            addButton.HorizontalAlignment = HorizontalAlignment.Left;
            addButton.Margin = new Thickness(0, 0, 10, 12);
            addButton.Click += OnAddLayerRuleClick;
            WpfGrid.SetRow(addButton, 0);
            root.Children.Add(addButton);

            // Keep the rules list visually consistent with Family Library Manager:
            // light header, compact rows, alternating row background and complete cell borders.
            Border listBorder = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(225, 230, 236)),
                BorderThickness = new Thickness(1),
                Background = System.Windows.Media.Brushes.White
            };

            WpfGrid listRoot = new WpfGrid();
            listRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            WpfGrid header = BuildLayerRuleHeader();
            WpfGrid.SetRow(header, 0);
            listRoot.Children.Add(header);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = System.Windows.Media.Brushes.White
            };
            _layerRuleRowsPanel = new StackPanel
            {
                Background = System.Windows.Media.Brushes.White
            };
            scroll.Content = _layerRuleRowsPanel;
            WpfGrid.SetRow(scroll, 1);
            listRoot.Children.Add(scroll);

            listBorder.Child = listRoot;
            WpfGrid.SetRow(listBorder, 1);
            root.Children.Add(listBorder);

            RefreshLayerRuleRows();
            return new TabItem { Header = "Layer Rules", Content = root };
        }

        private static WpfGrid BuildLayerRuleHeader()
        {
            WpfGrid grid = CreateLayerRuleGrid(true, false);
            grid.Children.Add(CreateLayerRuleCell("Rule Name", 0, true));
            grid.Children.Add(CreateLayerRuleCell("Actions", 1, true));
            grid.Children.Add(CreateLayerRuleCell("Active", 2, true));
            return grid;
        }

        private void RefreshLayerRuleRows()
        {
            if (_layerRuleRowsPanel == null)
            {
                return;
            }

            _layerRuleRowsPanel.Children.Clear();
            int rowIndex = 0;
            foreach (LayerRuleProfile rule in LayerRuleProfileStoreService.GetProfilesIncludingBuiltIn(_layerRuleStore))
            {
                WpfGrid row = CreateLayerRuleGrid(false, rowIndex % 2 == 1);
                row.Children.Add(CreateLayerRuleCell(rule.Name, 0, false));

                Border actionsBorder = CreateLayerRuleBorder(1, false);
                WpfGrid actionsGrid = new WpfGrid
                {
                    Margin = new Thickness(4, 0, 4, 0)
                };
                actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Button editButton = CreateLayerRuleTextActionButton("Edit", !rule.IsBuiltIn, rule.Key);
                editButton.Click += OnEditLayerRuleClick;
                WpfGrid.SetColumn(editButton, 0);
                actionsGrid.Children.Add(editButton);

                if (!rule.IsBuiltIn)
                {
                    Button deleteButton = CreateLayerRuleTextActionButton("Delete", true, rule.Key);
                    deleteButton.Click += OnDeleteLayerRuleClick;
                    WpfGrid.SetColumn(deleteButton, 1);
                    actionsGrid.Children.Add(deleteButton);
                }

                actionsBorder.Child = actionsGrid;
                row.Children.Add(actionsBorder);

                Border activeBorder = CreateLayerRuleBorder(2, false);
                RadioButton activeButton = new RadioButton
                {
                    Content = "Active",
                    GroupName = "LayerRuleProfileActive",
                    IsChecked = string.Equals(_layerRuleStore.ActiveRuleKey, rule.Key, StringComparison.OrdinalIgnoreCase),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(10, 0, 0, 0),
                    Tag = rule.Key
                };
                activeButton.Checked += (sender, args) =>
                {
                    RadioButton radio = sender as RadioButton;
                    if (radio != null && radio.Tag != null)
                    {
                        _layerRuleStore.ActiveRuleKey = radio.Tag.ToString();
                    }
                };
                activeBorder.Child = activeButton;
                row.Children.Add(activeBorder);

                _layerRuleRowsPanel.Children.Add(row);
                rowIndex++;
            }
        }

        private static WpfGrid CreateLayerRuleGrid(bool isHeader, bool isAlternate)
        {
            WpfGrid grid = new WpfGrid
            {
                Height = isHeader ? 38 : 36,
                Background = isHeader
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 232, 247))
                    : (isAlternate
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252))
                        : System.Windows.Media.Brushes.White)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
            return grid;
        }

        private static Border CreateLayerRuleBorder(int column, bool isHeader)
        {
            Border border = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(218, 224, 231)),
                // Use half-pixel borders on every cell. Adjacent cells combine into a crisp
                // one-pixel grid line, so every title/data cell has a complete visible border
                // without the heavy doubled black lines of the previous editor table.
                BorderThickness = new Thickness(0.5),
                Background = System.Windows.Media.Brushes.Transparent,
                SnapsToDevicePixels = true
            };
            WpfGrid.SetColumn(border, column);
            return border;
        }

        private static Border CreateLayerRuleCell(string text, int column, bool bold)
        {
            Border border = CreateLayerRuleBorder(column, bold);
            border.Padding = new Thickness(8, 0, 8, 0);
            border.Child = new TextBlock
            {
                Text = text ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            return border;
        }

        private static Button CreatePrimaryActionButton(string text, double width)
        {
            System.Windows.Media.SolidColorBrush primary = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(24, 112, 211));
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Foreground = System.Windows.Media.Brushes.White,
                Background = primary,
                BorderBrush = primary,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 1, 10, 1),
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateSecondaryActionButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(214, 214, 214)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 1, 10, 1),
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateLayerRuleTextActionButton(string text, bool enabled, string key)
        {
            return new Button
            {
                Content = text,
                IsEnabled = enabled,
                Tag = key,
                MinWidth = 70,
                Height = 28,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 2, 4, 2),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private void OnAddLayerRuleClick(object sender, RoutedEventArgs e)
        {
            LayerRuleEditorWindow editor = new LayerRuleEditorWindow(null, _layerRuleStore)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || editor.ResultProfile == null)
            {
                return;
            }

            _layerRuleStore.Rules.Add(editor.ResultProfile);
            RefreshLayerRuleRows();
        }

        private void OnEditLayerRuleClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string key = button != null && button.Tag != null ? button.Tag.ToString() : string.Empty;
            LayerRuleProfile existing = (_layerRuleStore.Rules ?? new List<LayerRuleProfile>())
                .FirstOrDefault(x => x != null && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return;
            }

            LayerRuleEditorWindow editor = new LayerRuleEditorWindow(existing, _layerRuleStore)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || editor.ResultProfile == null)
            {
                return;
            }

            int index = _layerRuleStore.Rules.FindIndex(x => x != null && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _layerRuleStore.Rules[index] = editor.ResultProfile;
            }
            RefreshLayerRuleRows();
        }

        private void OnDeleteLayerRuleClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string key = button != null && button.Tag != null ? button.Tag.ToString() : string.Empty;
            LayerRuleProfile existing = (_layerRuleStore.Rules ?? new List<LayerRuleProfile>())
                .FirstOrDefault(x => x != null && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return;
            }

            MessageBoxResult confirm = MessageBox.Show(
                "Delete layer rule '" + existing.Name + "'?",
                "Layer Rules",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            _layerRuleStore.Rules.Remove(existing);
            if (string.Equals(_layerRuleStore.ActiveRuleKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _layerRuleStore.ActiveRuleKey = LayerRuleProfileStoreService.BuiltInRuleKey;
            }
            RefreshLayerRuleRows();
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
            LayerRuleProfileStoreService.Save(_layerRuleStore);
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

    internal sealed class LayerRuleEditorWindow : Window
    {
        private const int InitialLayerColumnCount = 5;
        private const double CategoryColumnWidth = 175.0;
        private const double LayerColumnWidth = 175.0;
        private const double EditorRowHeight = 40.0;

        private readonly LayerRuleProfile _sourceProfile;
        private readonly LayerRuleProfileStoreData _store;
        private readonly Dictionary<string, List<TextBox>> _categoryEditors =
            new Dictionary<string, List<TextBox>>(StringComparer.OrdinalIgnoreCase);

        private TextBox _nameTextBox;
        private WpfGrid _mappingTableGrid;
        private int _layerColumnCount;

        public LayerRuleProfile ResultProfile { get; private set; }

        public LayerRuleEditorWindow(LayerRuleProfile sourceProfile, LayerRuleProfileStoreData store)
        {
            _sourceProfile = LayerRuleProfileStoreService.CloneProfile(sourceProfile);
            _store = LayerRuleProfileStoreService.CloneStore(store);
            _layerColumnCount = ResolveInitialLayerColumnCount(_sourceProfile);

            Title = sourceProfile == null ? "Add Layer Rule" : "Edit Layer Rule";
            Width = 1180;
            Height = 760;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = BuildContent();
        }

        private FrameworkElement BuildContent()
        {
            WpfGrid root = new WpfGrid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            WpfGrid top = new WpfGrid { Margin = new Thickness(0, 0, 0, 14) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel namePanel = new StackPanel
            {
                Width = 500,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            namePanel.Children.Add(new TextBlock
            {
                Text = "Rule Name",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _nameTextBox = new TextBox
            {
                Text = _sourceProfile != null ? (_sourceProfile.Name ?? string.Empty) : string.Empty,
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 2, 8, 2)
            };
            namePanel.Children.Add(_nameTextBox);
            WpfGrid.SetColumn(namePanel, 0);
            top.Children.Add(namePanel);

            Button addColumnButton = CreateSecondaryActionButton("Add Column", 104);
            addColumnButton.HorizontalAlignment = HorizontalAlignment.Right;
            addColumnButton.VerticalAlignment = VerticalAlignment.Bottom;
            addColumnButton.Margin = new Thickness(18, 0, 0, 0);
            addColumnButton.Click += OnAddColumnClick;
            WpfGrid.SetColumn(addColumnButton, 1);
            top.Children.Add(addColumnButton);

            WpfGrid.SetRow(top, 0);
            root.Children.Add(top);

            ScrollViewer tableScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false
            };
            _mappingTableGrid = new WpfGrid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            tableScroll.Content = _mappingTableGrid;
            WpfGrid.SetRow(tableScroll, 1);
            root.Children.Add(tableScroll);

            RebuildMappingTable(false);

            DockPanel footer = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Button saveButton = CreatePrimaryActionButton("Save", 104);
            saveButton.IsDefault = true;
            saveButton.Margin = new Thickness(10, 0, 0, 0);
            saveButton.Click += OnSaveClick;
            DockPanel.SetDock(saveButton, Dock.Right);

            Button cancelButton = CreateSecondaryActionButton("Cancel", 104);
            cancelButton.IsCancel = true;
            DockPanel.SetDock(cancelButton, Dock.Right);
            footer.Children.Add(saveButton);
            footer.Children.Add(cancelButton);
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        private static Button CreatePrimaryActionButton(string text, double width)
        {
            System.Windows.Media.SolidColorBrush primary = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(24, 112, 211));
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Foreground = System.Windows.Media.Brushes.White,
                Background = primary,
                BorderBrush = primary,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 1, 10, 1),
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateSecondaryActionButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(214, 214, 214)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 1, 10, 1),
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private void OnAddColumnClick(object sender, RoutedEventArgs e)
        {
            CaptureEditorValues();
            _layerColumnCount++;
            RebuildMappingTable(true);
        }

        private void RebuildMappingTable(bool useCapturedEditors)
        {
            Dictionary<string, List<string>> values = useCapturedEditors
                ? ReadCurrentEditorValues()
                : ReadSourceValues();

            _mappingTableGrid.Children.Clear();
            _mappingTableGrid.RowDefinitions.Clear();
            _mappingTableGrid.ColumnDefinitions.Clear();
            _categoryEditors.Clear();

            _mappingTableGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(CategoryColumnWidth)
            });
            for (int columnIndex = 0; columnIndex < _layerColumnCount; columnIndex++)
            {
                _mappingTableGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(LayerColumnWidth)
                });
            }

            _mappingTableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EditorRowHeight) });
            AddTableHeaderCell("Related Category", 0);
            for (int columnIndex = 0; columnIndex < _layerColumnCount; columnIndex++)
            {
                AddTableHeaderCell("Layer Name " + (columnIndex + 1), columnIndex + 1);
            }

            int rowIndex = 1;
            foreach (LayerRuleEditableCategoryDefinition definition in LayerRuleProfileStoreService.EditableCategories)
            {
                _mappingTableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EditorRowHeight) });
                AddCategoryCell(definition.DisplayName, rowIndex);

                List<string> rowValues;
                if (!values.TryGetValue(definition.Key, out rowValues))
                {
                    rowValues = new List<string>();
                }

                List<TextBox> editors = new List<TextBox>();
                for (int columnIndex = 0; columnIndex < _layerColumnCount; columnIndex++)
                {
                    string value = columnIndex < rowValues.Count ? rowValues[columnIndex] : string.Empty;
                    TextBox editor = CreateLayerNameCellEditor(value, rowIndex, columnIndex + 1);
                    editors.Add(editor);
                }
                _categoryEditors[definition.Key] = editors;
                rowIndex++;
            }
        }

        private void AddTableHeaderCell(string text, int column)
        {
            Border border = CreateEditorTableBorder(0, column, true);
            border.Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _mappingTableGrid.Children.Add(border);
        }

        private void AddCategoryCell(string text, int row)
        {
            Border border = CreateEditorTableBorder(row, 0, false);
            border.Child = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _mappingTableGrid.Children.Add(border);
        }

        private TextBox CreateLayerNameCellEditor(string value, int row, int column)
        {
            Border border = CreateEditorTableBorder(row, column, false);
            TextBox editor = new TextBox
            {
                Text = value ?? string.Empty,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0),
                TextWrapping = TextWrapping.NoWrap
            };
            border.Child = editor;
            _mappingTableGrid.Children.Add(border);
            return editor;
        }

        private static Border CreateEditorTableBorder(int row, int column, bool header)
        {
            System.Windows.Media.Brush background;
            if (header)
            {
                background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(214, 232, 247));
            }
            else
            {
                background = row % 2 == 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252))
                    : System.Windows.Media.Brushes.White;
            }

            Border border = new Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(218, 224, 231)),
                // Every header/data cell has a complete border. Half-pixel cell borders keep
                // shared grid lines visually at roughly one pixel, matching the Family Library
                // table style while preserving the customer's fully boxed table requirement.
                BorderThickness = new Thickness(0.5),
                Background = background,
                SnapsToDevicePixels = true
            };
            WpfGrid.SetRow(border, row);
            WpfGrid.SetColumn(border, column);
            return border;
        }

        private Dictionary<string, List<string>> ReadSourceValues()
        {
            Dictionary<string, List<string>> values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (LayerRuleEditableCategoryDefinition definition in LayerRuleProfileStoreService.EditableCategories)
            {
                LayerRuleCategoryMapping existing = _sourceProfile != null
                    ? (_sourceProfile.Mappings ?? new List<LayerRuleCategoryMapping>())
                        .FirstOrDefault(x => x != null && string.Equals(x.Category, definition.Key, StringComparison.OrdinalIgnoreCase))
                    : null;
                values[definition.Key] = existing != null
                    ? new List<string>(existing.LayerNames ?? new List<string>())
                    : new List<string>();
            }
            return values;
        }

        private Dictionary<string, List<string>> ReadCurrentEditorValues()
        {
            Dictionary<string, List<string>> values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (LayerRuleEditableCategoryDefinition definition in LayerRuleProfileStoreService.EditableCategories)
            {
                List<TextBox> editors;
                if (!_categoryEditors.TryGetValue(definition.Key, out editors))
                {
                    values[definition.Key] = new List<string>();
                    continue;
                }

                values[definition.Key] = editors
                    .Select(x => x != null ? (x.Text ?? string.Empty) : string.Empty)
                    .ToList();
            }
            return values;
        }

        private void CaptureEditorValues()
        {
            // Values are read directly from the current cell editors immediately before the
            // grid is rebuilt. This method intentionally exists as a named interaction step so
            // Add Column never discards unsaved user input.
        }

        private static int ResolveInitialLayerColumnCount(LayerRuleProfile profile)
        {
            int configuredCount = 0;
            if (profile != null)
            {
                configuredCount = (profile.Mappings ?? new List<LayerRuleCategoryMapping>())
                    .Where(x => x != null)
                    .Select(x => (x.LayerNames ?? new List<string>()).Count)
                    .DefaultIfEmpty(0)
                    .Max();
            }
            return Math.Max(InitialLayerColumnCount, configuredCount);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string ruleName = (_nameTextBox.Text ?? string.Empty).Trim();
            if (ruleName.Length == 0)
            {
                MessageBox.Show("Rule Name is required.", "Layer Rules", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool duplicateName = (_store.Rules ?? new List<LayerRuleProfile>())
                .Any(x => x != null &&
                    !string.Equals(x.Key, _sourceProfile != null ? _sourceProfile.Key : string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((x.Name ?? string.Empty).Trim(), ruleName, StringComparison.OrdinalIgnoreCase));
            if (duplicateName)
            {
                MessageBox.Show("A layer rule with the same name already exists.", "Layer Rules", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<LayerRuleCategoryMapping> mappings = new List<LayerRuleCategoryMapping>();
            Dictionary<string, string> ownerByLayer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LayerRuleEditableCategoryDefinition definition in LayerRuleProfileStoreService.EditableCategories)
            {
                List<TextBox> editors;
                if (!_categoryEditors.TryGetValue(definition.Key, out editors))
                {
                    continue;
                }

                List<string> names = LayerRuleProfileStoreService.NormalizeLayerNames(
                    (editors ?? new List<TextBox>())
                        .Select(x => x != null ? (x.Text ?? string.Empty).Trim() : string.Empty));

                foreach (string name in names)
                {
                    string existingCategory;
                    if (ownerByLayer.TryGetValue(name, out existingCategory))
                    {
                        MessageBox.Show(
                            "Layer '" + name + "' is already assigned to " + existingCategory + ". Each exact layer name can belong to only one category in a rule.",
                            "Layer Rules",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    ownerByLayer[name] = definition.DisplayName;
                }

                if (names.Count > 0)
                {
                    mappings.Add(new LayerRuleCategoryMapping
                    {
                        Category = definition.Key,
                        LayerNames = names
                    });
                }
            }

            ResultProfile = new LayerRuleProfile
            {
                Key = _sourceProfile != null && !string.IsNullOrWhiteSpace(_sourceProfile.Key)
                    ? _sourceProfile.Key
                    : "rule_" + Guid.NewGuid().ToString("N"),
                Name = ruleName,
                IsBuiltIn = false,
                Mappings = mappings
            };
            DialogResult = true;
            Close();
        }
    }

}
