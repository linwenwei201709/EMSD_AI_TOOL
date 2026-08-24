using Autodesk.Revit.UI;
using FontAwesome.Sharp;
using CadToRevit.Infrastructure.Localization;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace CadToRevit.UI.Dockable
{
    public sealed class PreviewPaneProvider : IDockablePaneProvider
    {
        private static readonly BooleanToVisibilityConverter BoolToVisibility = new BooleanToVisibilityConverter();
        private static readonly MapCategoryDisplayConverter CategoryDisplayConverter = new MapCategoryDisplayConverter();
        private static readonly LayerVisibilityIconVisibilityConverter LayerVisibilityIconVisibility = new LayerVisibilityIconVisibilityConverter();
        private static readonly Style TransparentIconButtonStyle = BuildTransparentIconButtonStyle();
        private static readonly Style DeleteIconButtonStyle = BuildDeleteIconButtonStyle();
        private static readonly Style DarkToolTipStyle = BuildDarkToolTipStyle();
        private const int ActionToolTipInitialShowDelayMs = 350;
        private const int ActionToolTipBetweenShowDelayMs = 0;
        private const int ActionToolTipShowDurationMs = 8000;
        private static DataGrid _layerMappingsGrid;
        private static readonly Brush LayerSelectionBackground = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        private static readonly Brush LayerSelectionBorder = new SolidColorBrush(Color.FromRgb(0, 84, 153));
        private static readonly Brush LayerSelectionForeground = Brushes.White;

        // Revit does not expose a separate initial width for a docked pane.
        // For docked panes, MinimumWidth also affects the first visible width in a fresh UI profile.
        // Keep it wide enough for first launch, but do NOT make the root element a fixed width;
        // otherwise Revit cannot resize the pane by dragging the splitter.
        private const double MinimumDockedPaneWidth = 580;
        private const double InitialDockedPaneMinHeight = 320.0;

        public FrameworkElement FrameworkElement { get; private set; }

        public PreviewPaneProvider()
        {
            FrameworkElement = BuildPaneView();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = FrameworkElement;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                MinimumWidth = (int)MinimumDockedPaneWidth,
                MinimumHeight = (int)InitialDockedPaneMinHeight
            };
        }

        private static FrameworkElement BuildPaneView()
        {
            SolidColorBrush bgPane = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            SolidColorBrush bgCard = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            SolidColorBrush border = new SolidColorBrush(Color.FromRgb(214, 214, 214));
            SolidColorBrush fgText = new SolidColorBrush(Color.FromRgb(38, 38, 38));

            Grid root = new Grid
            {
                Margin = new Thickness(0),
                Background = bgPane,
                MinWidth = MinimumDockedPaneWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            // Keep tooltip styling local to this preview pane so other plug-in UI is not affected.
            root.Resources.Add(typeof(ToolTip), DarkToolTipStyle);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border feedbackCard = BuildFeedbackPanel();
            // Temporarily hide the operation feedback panel to save vertical space.
            feedbackCard.Visibility = Visibility.Collapsed;
            Grid.SetRow(feedbackCard, 0);

            Border statusCard = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Background = bgCard,
                Margin = new Thickness(12, 12, 12, 8),
                Padding = new Thickness(10)
            };
            Grid.SetRow(statusCard, 1);
            statusCard.Child = BuildStatusPanel(fgText);

            Grid contentHost = new Grid { Margin = new Thickness(12, 0, 12, 8) };
            Grid.SetRow(contentHost, 2);

            ScrollViewer mainScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = BuildMainContent(bgCard, border, fgText)
            };
            contentHost.Children.Add(mainScroll);

            Border bottomCard = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Background = bgCard,
                Margin = new Thickness(12, 0, 12, 12),
                Padding = new Thickness(10)
            };
            Grid.SetRow(bottomCard, 3);
            bottomCard.Child = BuildBottomActions(fgText, bgCard, border);

            root.Children.Add(feedbackCard);
            root.Children.Add(statusCard);
            root.Children.Add(contentHost);
            root.Children.Add(bottomCard);
            root.DataContext = PreviewPaneRuntime.ViewModel;
            return root;
        }

        private static Border BuildFeedbackPanel()
        {
            SolidColorBrush feedbackBg = new SolidColorBrush(Color.FromRgb(234, 245, 255));
            SolidColorBrush feedbackBorder = new SolidColorBrush(Color.FromRgb(76, 141, 206));
            SolidColorBrush feedbackTitle = new SolidColorBrush(Color.FromRgb(32, 86, 140));

            Border card = new Border
            {
                BorderBrush = feedbackBorder,
                BorderThickness = new Thickness(1),
                Background = feedbackBg,
                Margin = new Thickness(12, 12, 12, 8),
                Padding = new Thickness(10)
            };

            Grid panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

            TextBlock title = new TextBlock
            {
                Text = Loc.T("DockablePane.Feedback.Title"),
                FontWeight = FontWeights.SemiBold,
                Foreground = feedbackTitle,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(title, 0);
            panel.Children.Add(title);

            ListBox feedbackList = new ListBox
            {
                BorderBrush = feedbackBorder,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Padding = new Thickness(4),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            ScrollViewer.SetVerticalScrollBarVisibility(feedbackList, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(feedbackList, ScrollBarVisibility.Disabled);
            feedbackList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("ErrorList"));
            Grid.SetRow(feedbackList, 1);
            panel.Children.Add(feedbackList);

            card.Child = panel;
            return card;
        }

        private static FrameworkElement BuildStatusPanel(Brush fg)
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel info = new StackPanel();
            TextBlock summary = BindText("{0}", "HeaderSummaryText", new Thickness(0), fg);
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            info.Children.Add(summary);
            Grid.SetRow(info, 0);
            grid.Children.Add(info);

            Grid quickActions = new Grid();
            quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            Button analyze = BindButton(Loc.T("DockablePane.Button.LayerAnalyze"), "AnalyzeCommand", 110, fg, Brushes.White, new SolidColorBrush(Color.FromRgb(214, 214, 214)));
            analyze.Margin = new Thickness(0, 8, 8, 0);
            leftActions.Children.Add(analyze);

            Button cadToggle = BindButton(Loc.T("DockablePane.Button.CadHide"), "ToggleCadVisibilityCommand", 110, fg, Brushes.White, new SolidColorBrush(Color.FromRgb(214, 214, 214)));
            cadToggle.Margin = new Thickness(0, 8, 8, 0);
            cadToggle.SetBinding(ContentControl.ContentProperty, new Binding("CadVisibilityButtonText"));
            leftActions.Children.Add(cadToggle);

            Button buildingToggle = BindButton(Loc.T("DockablePane.Button.BuildingHide"), "ToggleBuildingElementsVisibilityCommand", 130, fg, Brushes.White, new SolidColorBrush(Color.FromRgb(214, 214, 214)));
            buildingToggle.Margin = new Thickness(0, 8, 0, 0);
            buildingToggle.SetBinding(ContentControl.ContentProperty, new Binding("BuildingElementsVisibilityButtonText"));
            leftActions.Children.Add(buildingToggle);

            Grid.SetColumn(leftActions, 0);
            quickActions.Children.Add(leftActions);

            SolidColorBrush generateFloorBlue = new SolidColorBrush(Color.FromRgb(24, 112, 211));
            Button generateFloor = BindButton(
                "Generate Floor",
                "CreateGroundFloorCommand",
                130,
                Brushes.White,
                generateFloorBlue,
                generateFloorBlue);
            generateFloor.Margin = new Thickness(18, 8, 0, 0);
            generateFloor.HorizontalAlignment = HorizontalAlignment.Right;
            generateFloor.FontSize = 12;
            SetActionToolTip(generateFloor, "Create the project ground floor using the current model/import extents.");
            Grid.SetColumn(generateFloor, 1);
            quickActions.Children.Add(generateFloor);

            Grid.SetRow(quickActions, 1);
            grid.Children.Add(quickActions);
            return grid;
        }

        private static FrameworkElement BuildMainContent(Brush bgCard, Brush border, Brush fg)
        {
            StackPanel host = new StackPanel();
            SolidColorBrush layerListBorder = new SolidColorBrush(Color.FromRgb(249, 249, 249));

            Border layersCard = new Border
            {
                BorderBrush = layerListBorder,
                BorderThickness = new Thickness(1),
                Background = bgCard,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid layersGrid = new Grid();
            layersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layersGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(420) });
            layersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            FrameworkElement statsHeader = BuildLayerStatsHeader();
            Grid.SetRow(statsHeader, 0);
            layersGrid.Children.Add(statsHeader);

            _layerMappingsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                EnableRowVirtualization = false,
                EnableColumnVirtualization = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                Background = bgCard,
                Foreground = fg,
                BorderBrush = border,
                HorizontalGridLinesBrush = border,
                VerticalGridLinesBrush = border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 8)
            };
            _layerMappingsGrid.SetValue(ScrollViewer.CanContentScrollProperty, false);
            _layerMappingsGrid.RowStyle = BuildLayerRowStyle();
            _layerMappingsGrid.CellStyle = BuildLayerCellStyle();
            _layerMappingsGrid.PreviewMouseLeftButtonDown += OnLayerMappingsGridPreviewMouseLeftButtonDown;
            _layerMappingsGrid.Columns.Add(BuildSelectedColumn());
            _layerMappingsGrid.Columns.Add(BuildLayerVisibilityColumn());
            _layerMappingsGrid.Columns.Add(BuildLayerNameColumn());
            _layerMappingsGrid.Columns.Add(BuildCategoryColumn());
            _layerMappingsGrid.Columns.Add(BuildFamilyTypeColumn());
            _layerMappingsGrid.Columns.Add(BuildLayerGenerationColumn());
            _layerMappingsGrid.Loaded += (s, e) => ApplyLayerColumnWidths(_layerMappingsGrid);
            _layerMappingsGrid.SizeChanged += (s, e) => ApplyLayerColumnWidths(_layerMappingsGrid);
            _layerMappingsGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("LayerMappings"));
            _layerMappingsGrid.SetBinding(DataGrid.SelectedItemProperty, new Binding("SelectedLayerMapping") { Mode = BindingMode.TwoWay });
            Grid.SetRow(_layerMappingsGrid, 1);
            layersGrid.Children.Add(_layerMappingsGrid);

            Border settingsBox = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Padding = new Thickness(8)
            };
            settingsBox.SetBinding(UIElement.VisibilityProperty, new Binding("HasSelectedLayer") { Converter = BoolToVisibility });
            settingsBox.Child = BuildSettingsContent(bgCard, border, fg);
            Grid.SetRow(settingsBox, 2);
            layersGrid.Children.Add(settingsBox);
            layersCard.Child = layersGrid;
            host.Children.Add(layersCard);

            return host;
        }

        private static FrameworkElement BuildSettingsContent(Brush bgCard, Brush border, Brush fg)
        {
            Grid host = new Grid();

            FrameworkElement wallContent = BuildWallSettingsContent(bgCard, border, fg);
            wallContent.SetBinding(
                UIElement.VisibilityProperty,
                new Binding("SelectedIsWall")
                {
                    Converter = BoolToVisibility
                });
            host.Children.Add(wallContent);

            FrameworkElement legacyContent = BuildLegacySettingsContent(bgCard, border, fg);
            Style legacyStyle = new Style(typeof(Grid));
            legacyStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            legacyStyle.Triggers.Add(new DataTrigger
            {
                Binding = new Binding("SelectedIsWall"),
                Value = true,
                Setters =
                {
                    new Setter(UIElement.VisibilityProperty, Visibility.Collapsed)
                }
            });
            legacyContent.Style = legacyStyle;
            host.Children.Add(legacyContent);

            return host;
        }

        private static FrameworkElement BuildWallSettingsContent(Brush bgCard, Brush border, Brush fg)
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            WrapPanel header = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Orientation = Orientation.Horizontal };
            TextBlock layerLine = new TextBlock { FontWeight = FontWeights.SemiBold, Foreground = fg, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            layerLine.SetBinding(TextBlock.TextProperty, new Binding("SelectedLayerMapping.RawLayerName") { StringFormat = Loc.T("DockablePane.Settings.LayerFormat") });
            header.Children.Add(layerLine);
            TextBlock categoryLine = new TextBlock { Foreground = fg, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            categoryLine.SetBinding(TextBlock.TextProperty, new Binding("SelectedLayerMapping.Category") { StringFormat = Loc.T("DockablePane.Settings.CategoryFormat") });
            header.Children.Add(categoryLine);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(BuildSettingsGroup("Wall Settings",
                BindField("Wall height (mm)", "SelectedLayerMapping.WallHeightMm"),
                BindField("Wall base offset (mm)", "SelectedLayerMapping.WallBaseOffsetMm"),
                BindField("Default wall thickness (mm)", "SelectedLayerMapping.DefaultSingleWallThicknessMm"),
                BindField("Minimum wall length (mm)", "SelectedLayerMapping.MinWallLengthMm"),
                BindField("Maximum wall thickness (mm)", "SelectedLayerMapping.WallMaxWallThicknessMm")));

            Border card = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Background = bgCard,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            card.Child = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            Grid.SetRow(card, 1);
            root.Children.Add(card);
            return root;
        }

        private static FrameworkElement BuildLegacySettingsContent(Brush bgCard, Brush border, Brush fg)
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            WrapPanel header = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Orientation = Orientation.Horizontal };
            TextBlock layerLine = new TextBlock { FontWeight = FontWeights.SemiBold, Foreground = fg, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            layerLine.SetBinding(TextBlock.TextProperty, new Binding("SelectedLayerMapping.RawLayerName") { StringFormat = Loc.T("DockablePane.Settings.LayerFormat") });
            header.Children.Add(layerLine);
            TextBlock categoryLine = new TextBlock { Foreground = fg, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            categoryLine.SetBinding(TextBlock.TextProperty, new Binding("SelectedLayerMapping.Category") { StringFormat = Loc.T("DockablePane.Settings.CategoryFormat") });
            header.Children.Add(categoryLine);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            TabControl tabs = new TabControl
            {
                Background = bgCard,
                BorderBrush = border,
                BorderThickness = new Thickness(1)
            };
            tabs.Items.Add(BuildNormalTab(fg));
            TabItem expertTab = BuildExpertTab(fg);
            expertTab.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsWall") { Converter = BoolToVisibility });
            tabs.Items.Add(expertTab);
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);
            return root;
        }

        private static FrameworkElement BuildWallFixedPanel()
        {
            // Hide advanced override/default controls from the main pane.
            // The underlying settings still use their existing defaults.
            return new StackPanel();
        }

        private static TabItem BuildWallRecognitionTab()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(BindField("Minimum wall length (mm)", "SelectedLayerMapping.MinWallLengthMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallThicknessTolMm"), "SelectedLayerMapping.WallThicknessTolMm"));
            panel.Children.Add(BindField("Maximum wall thickness (mm)", "SelectedLayerMapping.WallMaxWallThicknessMm"));
            panel.Children.Add(BindField("Default wall thickness (mm)", "SelectedLayerMapping.DefaultSingleWallThicknessMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallParallelAngleTolDeg"), "SelectedLayerMapping.WallParallelAngleTolDeg"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallEndpointMergeTolMm"), "SelectedLayerMapping.WallEndpointMergeTolMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallArcThicknessTolMm"), "SelectedLayerMapping.WallArcThicknessTolMm"));
            return new TabItem { Header = Loc.T("DockablePane.Tab.WallRecognition"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildWallModelingTab()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(BindField("Wall height (mm)", "SelectedLayerMapping.WallHeightMm"));
            panel.Children.Add(BindField("Wall base offset (mm)", "SelectedLayerMapping.WallBaseOffsetMm"));
            return new TabItem { Header = Loc.T("DockablePane.Tab.WallModeling"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildWallDoubleLineTab()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallForceSingleLineMode"), "SelectedLayerMapping.WallForceSingleLineMode"));
            panel.Children.Add(BindCombo(Loc.T("DockablePane.Field.WallDoubleLineSingleWallPlaceMode"), "SelectedLayerMapping.WallDoubleLineSingleWallPlaceMode", "WallDoubleLineSingleWallPlaceModeOptions"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableAutoDoubleLineThickness"), "SelectedLayerMapping.WallEnableAutoDoubleLineThickness"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAutoThicknessTopK"), "SelectedLayerMapping.WallAutoThicknessTopK"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAutoThicknessBinMm"), "SelectedLayerMapping.WallAutoThicknessBinMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallMinDoubleLineThicknessMm"), "SelectedLayerMapping.WallMinDoubleLineThicknessMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallMinDoubleLineOverlapLenMm"), "SelectedLayerMapping.WallMinDoubleLineOverlapLenMm"));
            panel.Children.Add(BindCombo(Loc.T("DockablePane.Field.WallDoubleLineLengthPolicy"), "SelectedLayerMapping.WallDoubleLineLengthPolicy", "WallDoubleLineLengthPolicyOptions"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDoubleLineAdaptiveContainTolMm"), "SelectedLayerMapping.WallDoubleLineAdaptiveContainTolMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDoubleLineAdaptiveExtendMaxMm"), "SelectedLayerMapping.WallDoubleLineAdaptiveExtendMaxMm"));
            return new TabItem { Header = Loc.T("DockablePane.Tab.WallDoubleLine"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildWallTopologyTab()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableOrthogonalSnap"), "SelectedLayerMapping.WallEnableOrthogonalSnap"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAngleSnapDeg"), "SelectedLayerMapping.WallAngleSnapDeg"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableExtendToIntersection"), "SelectedLayerMapping.WallEnableExtendToIntersection"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendSearchTolMm"), "SelectedLayerMapping.WallExtendSearchTolMm"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableEndpointClustering"), "SelectedLayerMapping.WallEnableEndpointClustering"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallEndpointClusterTolMm"), "SelectedLayerMapping.WallEndpointClusterTolMm"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableDuplicateRemoval"), "SelectedLayerMapping.WallEnableDuplicateRemoval"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDuplicateTolMm"), "SelectedLayerMapping.WallDuplicateTolMm"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableExtendCollinear"), "SelectedLayerMapping.WallEnableExtendCollinear"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendCollinearTolMm"), "SelectedLayerMapping.WallExtendCollinearTolMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallCollinearOffsetTolMm"), "SelectedLayerMapping.WallCollinearOffsetTolMm"));
            panel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendProjectionTolMm"), "SelectedLayerMapping.WallExtendProjectionTolMm"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableMergeCollinear"), "SelectedLayerMapping.WallEnableMergeCollinear"));
            panel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallUseDirectionalClustering"), "SelectedLayerMapping.WallUseDirectionalClustering"));
            return new TabItem { Header = Loc.T("DockablePane.Tab.WallTopology"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildNormalTab(Brush fg)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            // Keep the UI focused on generation parameters; hide override/default toggles.
            StackPanel wallPanel = new StackPanel();
            wallPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsWall") { Converter = BoolToVisibility });
            wallPanel.Children.Add(BindField("Wall height (mm)", "SelectedLayerMapping.WallHeightMm"));
            wallPanel.Children.Add(BindField("Wall base offset (mm)", "SelectedLayerMapping.WallBaseOffsetMm"));
            wallPanel.Children.Add(BindField("Minimum wall length (mm)", "SelectedLayerMapping.MinWallLengthMm"));
            wallPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallThicknessTolMm"), "SelectedLayerMapping.WallThicknessTolMm"));
            wallPanel.Children.Add(BindField("Maximum wall thickness (mm)", "SelectedLayerMapping.WallMaxWallThicknessMm"));
            wallPanel.Children.Add(BindField("Default wall thickness (mm)", "SelectedLayerMapping.DefaultSingleWallThicknessMm"));
            wallPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallParallelAngleTolDeg"), "SelectedLayerMapping.WallParallelAngleTolDeg"));
            wallPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallEndpointMergeTolMm"), "SelectedLayerMapping.WallEndpointMergeTolMm"));
            wallPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallArcThicknessTolMm"), "SelectedLayerMapping.WallArcThicknessTolMm"));
            panel.Children.Add(wallPanel);

            StackPanel doorPanel = new StackPanel();
            doorPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsDoor") { Converter = BoolToVisibility });
            doorPanel.Children.Add(BindField("Door height (mm)", "SelectedLayerMapping.DoorHeightMm"));
            doorPanel.Children.Add(BindField("Door sill height (mm)", "SelectedLayerMapping.DoorSillHeightMm"));
            // Min/max door width are hidden from the customer UI.
            // Existing defaults remain in SelectedLayerMapping and are still used by generation.
            // Customer-facing door settings are intentionally simplified.
            // DoorWallMatchTolMm and fixed-width options remain in the data model and keep their defaults,
            // but are hidden from the dockable pane to avoid exposing algorithm tuning parameters.
            panel.Children.Add(doorPanel);

            StackPanel windowPanel = new StackPanel();
            windowPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsWindow") { Converter = BoolToVisibility });
            windowPanel.Children.Add(BindField("窗高度 (WindowHeightMm)", "SelectedLayerMapping.WindowHeightMm"));
            windowPanel.Children.Add(BindField("窗台高度 (WindowSillHeightMm)", "SelectedLayerMapping.WindowSillHeightMm"));
            windowPanel.Children.Add(BindCheck("使用窗台高+窗高 (WindowUseSillPlusHeight)", "SelectedLayerMapping.WindowUseSillPlusHeight"));
            panel.Children.Add(windowPanel);

            StackPanel beamPanel = new StackPanel();
            beamPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsBeam") { Converter = BoolToVisibility });
            beamPanel.Children.Add(BindField("梁最小长度 (BeamMinLengthMm)", "SelectedLayerMapping.BeamMinLengthMm"));
            beamPanel.Children.Add(BindField("梁标高偏移 (BeamElevationOffsetMm)", "SelectedLayerMapping.BeamElevationOffsetMm"));
            panel.Children.Add(beamPanel);

            StackPanel columnPanel = new StackPanel();
            columnPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsColumn") { Converter = BoolToVisibility });
            columnPanel.Children.Add(BuildSettingsGroup("Column Settings",
                BindField("Column height (mm)", "SelectedLayerMapping.ColumnHeightMm"),
                BindField("Minimum column size (mm)", "SelectedLayerMapping.ColumnMinSizeMm"),
                BindField("Maximum column size (mm)", "SelectedLayerMapping.ColumnMaxSizeMm")));
            panel.Children.Add(columnPanel);
            return new TabItem { Header = Loc.T("DockablePane.Tab.Normal"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildExpertTab(Brush fg)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(10) };

            StackPanel wallExpertPanel = new StackPanel();
            wallExpertPanel.SetBinding(UIElement.VisibilityProperty, new Binding("SelectedIsWall") { Converter = BoolToVisibility });
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallEndpointClusterTolMm"), "SelectedLayerMapping.WallEndpointClusterTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendSearchTolMm"), "SelectedLayerMapping.WallExtendSearchTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDuplicateTolMm"), "SelectedLayerMapping.WallDuplicateTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAngleSnapDeg"), "SelectedLayerMapping.WallAngleSnapDeg"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendCollinearTolMm"), "SelectedLayerMapping.WallExtendCollinearTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallCollinearOffsetTolMm"), "SelectedLayerMapping.WallCollinearOffsetTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallExtendProjectionTolMm"), "SelectedLayerMapping.WallExtendProjectionTolMm"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableOrthogonalSnap"), "SelectedLayerMapping.WallEnableOrthogonalSnap"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableExtendToIntersection"), "SelectedLayerMapping.WallEnableExtendToIntersection"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableEndpointClustering"), "SelectedLayerMapping.WallEnableEndpointClustering"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableDuplicateRemoval"), "SelectedLayerMapping.WallEnableDuplicateRemoval"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableExtendCollinear"), "SelectedLayerMapping.WallEnableExtendCollinear"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableMergeCollinear"), "SelectedLayerMapping.WallEnableMergeCollinear"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallUseDirectionalClustering"), "SelectedLayerMapping.WallUseDirectionalClustering"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallForceSingleLineMode"), "SelectedLayerMapping.WallForceSingleLineMode"));
            wallExpertPanel.Children.Add(BindCombo(Loc.T("DockablePane.Field.WallDoubleLineSingleWallPlaceMode"), "SelectedLayerMapping.WallDoubleLineSingleWallPlaceMode", "WallDoubleLineSingleWallPlaceModeOptions"));
            wallExpertPanel.Children.Add(BindCheck(Loc.T("DockablePane.Field.WallEnableAutoDoubleLineThickness"), "SelectedLayerMapping.WallEnableAutoDoubleLineThickness"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAutoThicknessTopK"), "SelectedLayerMapping.WallAutoThicknessTopK"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallAutoThicknessBinMm"), "SelectedLayerMapping.WallAutoThicknessBinMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallMinDoubleLineThicknessMm"), "SelectedLayerMapping.WallMinDoubleLineThicknessMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallMinDoubleLineOverlapLenMm"), "SelectedLayerMapping.WallMinDoubleLineOverlapLenMm"));
            wallExpertPanel.Children.Add(BindCombo(Loc.T("DockablePane.Field.WallDoubleLineLengthPolicy"), "SelectedLayerMapping.WallDoubleLineLengthPolicy", "WallDoubleLineLengthPolicyOptions"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDoubleLineAdaptiveContainTolMm"), "SelectedLayerMapping.WallDoubleLineAdaptiveContainTolMm"));
            wallExpertPanel.Children.Add(BindField(Loc.T("DockablePane.Field.WallDoubleLineAdaptiveExtendMaxMm"), "SelectedLayerMapping.WallDoubleLineAdaptiveExtendMaxMm"));
            panel.Children.Add(wallExpertPanel);

            // Column expert algorithm settings are intentionally hidden from the customer UI.
            // Existing default values are still preserved in AdvancedSettingsRow and ColumnRecognitionConfig.json.

            // Door expert settings are intentionally hidden from the customer UI.
            // Keep existing defaults:
            // - DoorWallMatchTolMm controls internal door-to-wall matching tolerance.
            // - UseFixedDoorWidth / DoorExpectedWidthMm / PreferGeometryOpeningWidth control internal width detection.
            // These values remain stored on SelectedLayerMapping and are still used by generation logic.

            return new TabItem { Header = Loc.T("DockablePane.Tab.Expert"), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        }

        private static TabItem BuildReadonlyTab(string header, string bindPath, Brush fg)
        {
            WpfTextBox box = new WpfTextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            };
            box.SetBinding(WpfTextBox.TextProperty, new Binding(bindPath));
            return new TabItem { Header = header, Content = box };
        }

        private static Style BuildDarkToolTipStyle()
        {
            SolidColorBrush darkBackground = new SolidColorBrush(Color.FromRgb(31, 35, 41));
            SolidColorBrush darkBorder = new SolidColorBrush(Color.FromRgb(31, 35, 41));

            DataTemplate textTemplate = new DataTemplate();
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
            text.SetValue(TextBlock.FontSizeProperty, 13.0);
            text.SetValue(TextBlock.LineHeightProperty, 19.0);
            text.SetValue(FrameworkElement.MaxWidthProperty, 520.0);
            textTemplate.VisualTree = text;

            ControlTemplate template = new ControlTemplate(typeof(ToolTip));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(presenter);
            template.VisualTree = border;

            Style style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.BackgroundProperty, darkBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, darkBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
            style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, textTemplate));
            style.Setters.Add(new Setter(ToolTip.HasDropShadowProperty, false));
            style.Setters.Add(new Setter(ToolTip.PlacementProperty, PlacementMode.MousePoint));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static void SetActionToolTip(FrameworkElement element, string text)
        {
            element.ToolTip = text;
            SetActionToolTipTiming(element);
        }

        private static void SetActionToolTipTiming(DependencyObject element)
        {
            element.SetValue(ToolTipService.InitialShowDelayProperty, ActionToolTipInitialShowDelayMs);
            element.SetValue(ToolTipService.BetweenShowDelayProperty, ActionToolTipBetweenShowDelayMs);
            element.SetValue(ToolTipService.ShowDurationProperty, ActionToolTipShowDurationMs);
        }

        private static void SetActionToolTipTiming(FrameworkElementFactory factory)
        {
            factory.SetValue(ToolTipService.InitialShowDelayProperty, ActionToolTipInitialShowDelayMs);
            factory.SetValue(ToolTipService.BetweenShowDelayProperty, ActionToolTipBetweenShowDelayMs);
            factory.SetValue(ToolTipService.ShowDurationProperty, ActionToolTipShowDurationMs);
        }

        private static FrameworkElement BuildBottomActions(Brush fg, Brush bg, Brush border)
        {
            SolidColorBrush generateBg = new SolidColorBrush(Color.FromRgb(0, 120, 215));

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button gen = BindButton("Batch Generate", "CreateElementsCommand", double.NaN, Brushes.White, generateBg, border);
            Button regenerate = BindButton("Batch Rebuild", "RegenerateCommand", double.NaN, fg, bg, border);
            Button delete = BindButton("Batch Delete Models", "DeleteSelectedLayersCommand", double.NaN, new SolidColorBrush(Color.FromRgb(180, 35, 24)), bg, border);

            SetActionToolTip(gen, "Generate models only for newly selected layers. Existing models will not be affected.");
            SetActionToolTip(regenerate, "Rebuild models for all selected layers. Warning: Manual modifications in the model will be overwritten.");
            SetActionToolTip(delete, "Delete the generated models for the selected layers from the current project.");

            gen.HorizontalAlignment = HorizontalAlignment.Stretch;
            regenerate.HorizontalAlignment = HorizontalAlignment.Stretch;
            delete.HorizontalAlignment = HorizontalAlignment.Stretch;

            gen.Margin = new Thickness(0, 0, 8, 0);
            regenerate.Margin = new Thickness(0, 0, 8, 0);
            delete.Margin = new Thickness(0);

            Grid.SetColumn(gen, 0);
            Grid.SetColumn(regenerate, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(gen);
            row.Children.Add(regenerate);
            row.Children.Add(delete);
            return row;
        }

        private static FrameworkElement BuildLayerStatsHeader()
        {
            SolidColorBrush cardBorder = new SolidColorBrush(Color.FromRgb(214, 214, 214));
            SolidColorBrush cardBg = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            StackPanel host = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            Grid statsGrid = new Grid();
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.Children.Add(BuildLayerStatCard(Loc.T("DockablePane.Label.Layers"), "LayerCount", new SolidColorBrush(Color.FromRgb(108, 117, 125)), new SolidColorBrush(Color.FromRgb(33, 37, 41)), cardBg, cardBorder, 0));
            statsGrid.Children.Add(BuildLayerStatCard(Loc.T("DockablePane.Label.Selected"), "SelectedLayerCount", new SolidColorBrush(Color.FromRgb(40, 167, 69)), new SolidColorBrush(Color.FromRgb(40, 167, 69)), cardBg, cardBorder, 1));
            statsGrid.Children.Add(BuildLayerStatCard(Loc.T("DockablePane.Label.Ignore"), "IgnoreLayerCount", new SolidColorBrush(Color.FromRgb(220, 53, 69)), new SolidColorBrush(Color.FromRgb(220, 53, 69)), cardBg, cardBorder, 2));
            host.Children.Add(statsGrid);

            Grid buttonRow = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel selectionButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            Button undoDetachButton = BindButton(string.Empty, "UndoDetachCommand", 125, Brushes.Black, Brushes.White, cardBorder);
            undoDetachButton.SetBinding(ContentControl.ContentProperty, new Binding("UndoDetachButtonText"));
            undoDetachButton.SetBinding(UIElement.IsEnabledProperty, new Binding("CanUndoDetach"));
            Button restoreButton = BindButton(string.Empty, "RestoreBindingCommand", 135, Brushes.Black, Brushes.White, cardBorder);
            restoreButton.SetBinding(ContentControl.ContentProperty, new Binding("RestoreBindingButtonText"));
            restoreButton.SetBinding(UIElement.IsEnabledProperty, new Binding("CanRestoreBinding"));
            Button detachButton = BindButton(string.Empty, "DetachSelectedElementsCommand", 135, Brushes.Black, Brushes.White, cardBorder);
            detachButton.SetBinding(ContentControl.ContentProperty, new Binding("DetachElementsButtonText"));
            detachButton.SetBinding(UIElement.IsEnabledProperty, new Binding("CanDetachSelectedElements"));
            undoDetachButton.Padding = new Thickness(3, 1, 3, 1);
            restoreButton.Padding = new Thickness(3, 1, 3, 1);
            detachButton.Padding = new Thickness(1, 1, 1, 1);
            TextBlock detachedStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(73, 80, 87)),
                Margin = new Thickness(0, 0, 8, 0),
                Visibility = Visibility.Collapsed
            };
            detachedStatus.SetBinding(TextBlock.TextProperty, new Binding("DetachedElementsSessionStatusText"));
            undoDetachButton.Command = PreviewPaneRuntime.ViewModel.UndoDetachCommand;
            restoreButton.Command = PreviewPaneRuntime.ViewModel.RestoreBindingCommand;
            detachButton.Command = PreviewPaneRuntime.ViewModel.DetachSelectedElementsCommand;
            undoDetachButton.Margin = new Thickness(0);
            detachButton.Margin = new Thickness(4, 0, 0, 0);
            restoreButton.Margin = new Thickness(4, 0, 0, 0);
            SetActionToolTip(undoDetachButton, "Restore the most recent Detach Elements batch.");
            SetActionToolTip(restoreButton, "Restore selected detached elements to their original CAD layer binding.");
            SetActionToolTip(detachButton, "Detach selected generated elements from their source CAD layers. Detached elements will not be deleted by layer Rebuild or Delete.");
            selectionButtons.Children.Add(undoDetachButton);
            selectionButtons.Children.Add(detachButton);
            //selectionButtons.Children.Add(restoreButton);
            selectionButtons.Children.Add(detachedStatus);
            Grid.SetColumn(selectionButtons, 0);
            buttonRow.Children.Add(selectionButtons);
            host.Children.Add(buttonRow);

            return host;
        }

        private static Border BuildLayerStatCard(string title, string valuePath, Brush titleBrush, Brush valueBrush, Brush bg, Brush border, int column)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(3) };
            TextBlock titleText = new TextBlock
            {
                Text = title,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = titleBrush
            };
            TextBlock valueText = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = valueBrush
            };
            valueText.SetBinding(TextBlock.TextProperty, new Binding(valuePath));
            panel.Children.Add(titleText);
            panel.Children.Add(valueText);

            Border card = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Background = bg,
                Padding = new Thickness(4),
                Height = 56,
                Margin = new Thickness(0, 0, 6, 0),
                Child = panel
            };
            if (column == 2)
            {
                card.Margin = new Thickness(0);
            }

            Grid.SetColumn(card, column);
            return card;
        }

        internal static void RefreshLayerMappingsGrid()
        {
            if (_layerMappingsGrid == null)
            {
                return;
            }

            try
            {
                _layerMappingsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                _layerMappingsGrid.CommitEdit(DataGridEditingUnit.Row, true);
                _layerMappingsGrid.CancelEdit(DataGridEditingUnit.Cell);
                _layerMappingsGrid.CancelEdit(DataGridEditingUnit.Row);
            }
            catch
            {
                // Fall back to refresh/layout below when the grid is not in an editable state.
            }

            _layerMappingsGrid.Items.Refresh();
            _layerMappingsGrid.UpdateLayout();
        }

        internal static void ClearLayerMappingsGridSelection()
        {
            if (_layerMappingsGrid == null)
            {
                return;
            }

            _layerMappingsGrid.UnselectAll();
            _layerMappingsGrid.SelectedItem = null;
            _layerMappingsGrid.CurrentCell = new DataGridCellInfo();
            _layerMappingsGrid.Items.Refresh();
            _layerMappingsGrid.UpdateLayout();
        }

        private static DataGridTemplateColumn BuildLayerVisibilityColumn()
        {
            FrameworkElementFactory visibilityIconHost = new FrameworkElementFactory(typeof(Grid));
            visibilityIconHost.SetValue(FrameworkElement.WidthProperty, 20.0);
            visibilityIconHost.SetValue(FrameworkElement.HeightProperty, 22.0);
            visibilityIconHost.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            visibilityIconHost.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            bool hasVisibilityIcon = false;
            //hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "hide-black", "HideBlack");
            //hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "hide-white", "HideWhite");
            //hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "show-black", "ShowBlack");
            //hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "show-white", "ShowWhite");
            hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "hide-black", "ShowBlack");
            hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "hide-white", "ShowWhite");
            hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "show-black", "HideBlack");
            hasVisibilityIcon |= AppendLayerVisibilityIcon(visibilityIconHost, "show-white", "HideWhite");

            if (!hasVisibilityIcon)
            {
                FrameworkElementFactory fallback = new FrameworkElementFactory(typeof(TextBlock));
                fallback.SetValue(TextBlock.TextProperty, "👁");
                fallback.SetValue(TextBlock.FontSizeProperty, 13.0);
                fallback.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
                fallback.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                fallback.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                fallback.SetBinding(UIElement.VisibilityProperty, new Binding("IsLayerVisibilityToggleVisible")
                {
                    Converter = BoolToVisibility
                });
                visibilityIconHost.AppendChild(fallback);
            }

            FrameworkElementFactory visibilityButton = new FrameworkElementFactory(typeof(Button));
            visibilityButton.SetValue(Button.WidthProperty, 22.0);
            visibilityButton.SetValue(Button.HeightProperty, 22.0);
            visibilityButton.SetValue(Button.PaddingProperty, new Thickness(0));
            visibilityButton.SetValue(Button.MarginProperty, new Thickness(0));
            visibilityButton.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            visibilityButton.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            visibilityButton.SetValue(Button.StyleProperty, TransparentIconButtonStyle);
            visibilityButton.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            visibilityButton.SetValue(Control.BorderBrushProperty, Brushes.Transparent);
            visibilityButton.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            visibilityButton.SetValue(Control.FocusVisualStyleProperty, null);
            visibilityButton.SetValue(Control.CursorProperty, Cursors.Hand);
            visibilityButton.AppendChild(visibilityIconHost);
            visibilityButton.SetBinding(Button.ToolTipProperty, new Binding("LayerVisibilityToggleToolTip"));
            SetActionToolTipTiming(visibilityButton);
            visibilityButton.SetBinding(Button.VisibilityProperty, new Binding("IsLayerVisibilityToggleVisible")
            {
                Converter = BoolToVisibility
            });
            visibilityButton.SetBinding(Button.CommandProperty, new Binding("DataContext.ToggleLayerVisibilityCommand")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            visibilityButton.SetBinding(Button.CommandParameterProperty, new Binding());

            DataTemplate template = new DataTemplate { VisualTree = visibilityButton };
            return new DataGridTemplateColumn
            {
                Header = string.Empty,
                Width = new DataGridLength(24, DataGridLengthUnitType.Pixel),
                MinWidth = 20,
                MaxWidth = 28,
                IsReadOnly = true,
                CellTemplate = template
            };
        }

        private static bool AppendLayerVisibilityIcon(FrameworkElementFactory parent, string resourceName, string iconState)
        {
            ImageSource source = TryLoadResourceIcon(resourceName);
            if (source == null)
            {
                return false;
            }

            FrameworkElementFactory icon = new FrameworkElementFactory(typeof(Image));
            icon.SetValue(Image.SourceProperty, source);
            icon.SetValue(FrameworkElement.WidthProperty, 16.0);
            icon.SetValue(FrameworkElement.HeightProperty, 16.0);
            icon.SetValue(Image.StretchProperty, Stretch.Uniform);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetBinding(UIElement.VisibilityProperty, BuildLayerVisibilityIconBinding(iconState));
            parent.AppendChild(icon);
            return true;
        }

        private static MultiBinding BuildLayerVisibilityIconBinding(string iconState)
        {
            MultiBinding binding = new MultiBinding
            {
                Converter = LayerVisibilityIconVisibility,
                ConverterParameter = iconState,
                FallbackValue = System.Windows.Visibility.Collapsed
            };
            binding.Bindings.Add(new Binding("IsGeneratedElementsHidden"));
            // Prefer the explicit ViewModel-maintained flag, and also listen to the actual
            // DataGridRow.IsSelected state. In this grid, selection can be driven by the row,
            // the current cell, or a button click; using both sources keeps the icon colour
            // in sync with the blue selected-row visual.
            binding.Bindings.Add(new Binding("IsUiRowSelected"));
            binding.Bindings.Add(new Binding("IsLayerVisibilityToggleVisible"));
            binding.Bindings.Add(new Binding("IsSelected")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
            });
            return binding;
        }

        private static DataGridTemplateColumn BuildLayerNameColumn()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            ImageSource warningSource = TryLoadResourceIcon("Warning16");
            ImageSource unknownSource = TryLoadResourceIcon("Unknown") ?? TryLoadResourceIcon("Unknown ") ?? warningSource;
            ImageSource notForBuildSource = TryLoadResourceIcon("Not for Build") ?? warningSource;
            if (unknownSource != null)
            {
                FrameworkElementFactory icon = new FrameworkElementFactory(typeof(Image));
                icon.SetValue(Image.SourceProperty, unknownSource);
                icon.SetValue(FrameworkElement.WidthProperty, 16.0);
                icon.SetValue(FrameworkElement.HeightProperty, 16.0);
                icon.SetValue(Image.StretchProperty, Stretch.Uniform);
                icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
                icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                icon.SetBinding(UIElement.VisibilityProperty, new Binding("ShowUnknownLayerIcon")
                {
                    Converter = BoolToVisibility
                });
                panel.AppendChild(icon);
            }

            if (notForBuildSource != null)
            {
                FrameworkElementFactory icon = new FrameworkElementFactory(typeof(Image));
                icon.SetValue(Image.SourceProperty, notForBuildSource);
                icon.SetValue(FrameworkElement.WidthProperty, 16.0);
                icon.SetValue(FrameworkElement.HeightProperty, 16.0);
                icon.SetValue(Image.StretchProperty, Stretch.Uniform);
                icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
                icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                icon.SetBinding(UIElement.VisibilityProperty, new Binding("ShowNotForBuildLayerIcon")
                {
                    Converter = BoolToVisibility
                });
                panel.AppendChild(icon);
            }

            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("RawLayerName"));
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            panel.AppendChild(text);

            DataTemplate template = new DataTemplate { VisualTree = panel };
            return new DataGridTemplateColumn
            {
                Header = Loc.T("DockablePane.Column.LayerName"),
                Width = new DataGridLength(35, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                CellTemplate = template
            };
        }

        private static DataGridTemplateColumn BuildSelectedColumn()
        {
            CheckBox headerCheckBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsThreeState = true,
                Padding = new Thickness(0),
                ToolTip = "Select or deselect all visible generatable layers."
            };
            headerCheckBox.SetBinding(WpfToggleButton.IsCheckedProperty, new Binding("AllVisibleLayerSelectionState")
            {
                Source = PreviewPaneRuntime.ViewModel,
                Mode = BindingMode.OneWay
            });
            headerCheckBox.SetBinding(UIElement.IsEnabledProperty, new Binding("CanToggleAllLayerSelection")
            {
                Source = PreviewPaneRuntime.ViewModel,
                Mode = BindingMode.OneWay
            });
            headerCheckBox.Command = PreviewPaneRuntime.ViewModel.ToggleAllLayerSelectionCommand;

            FrameworkElementFactory checkFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkFactory.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkFactory.SetBinding(WpfToggleButton.IsCheckedProperty, new Binding("IsSelected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            DataTemplate template = new DataTemplate
            {
                VisualTree = checkFactory
            };

            return new DataGridTemplateColumn
            {
                Header = headerCheckBox,
                Width = new DataGridLength(24, DataGridLengthUnitType.Pixel),
                MinWidth = 20,
                MaxWidth = 25,
                CellTemplate = template,
                CellEditingTemplate = template
            };
        }

        private static Style BuildTransparentIconButtonStyle()
        {
            Style style = new Style(typeof(Button));

            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            border.AppendChild(presenter);
            template.VisualTree = border;

            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
        }

        private static Style BuildDeleteIconButtonStyle()
        {
            Style style = new Style(typeof(Button));

            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(250, 250, 250))));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(128, 128, 128))));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "DeleteButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            border.AppendChild(presenter);
            template.VisualTree = border;

            Trigger mouseOverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 255, 255)), "DeleteButtonBorder"));
            mouseOverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(96, 96, 96)), "DeleteButtonBorder"));
            template.Triggers.Add(mouseOverTrigger);

            Trigger pressedTrigger = new Trigger
            {
                Property = ButtonBase.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 232, 232)), "DeleteButtonBorder"));
            pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(80, 80, 80)), "DeleteButtonBorder"));
            template.Triggers.Add(pressedTrigger);

            Trigger disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "DeleteButtonBorder"));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style BuildLayerRowStyle()
        {
            Style style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));

            Trigger selected = new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, LayerSelectionBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, LayerSelectionForeground));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, LayerSelectionBorder));
            style.Triggers.Add(selected);
            return style;
        }

        private static Style BuildLayerCellStyle()
        {
            Style style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            Trigger selected = new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, LayerSelectionBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, LayerSelectionForeground));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, LayerSelectionBorder));
            style.Triggers.Add(selected);
            return style;
        }

        private static async void OnLayerMappingsGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataGridRow row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
            {
                return;
            }

            if (FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) != null)
            {
                PreviewPaneRuntime.ViewModel.SuppressNextSelectedLayerHighlight();
                return;
            }

            if (IsSelectionToggleIgnored(e.OriginalSource as DependencyObject))
            {
                return;
            }

            PreviewPaneLayerItem clickedItem = row.Item as PreviewPaneLayerItem;
            PreviewPaneLayerItem selectedItem = PreviewPaneRuntime.ViewModel.SelectedLayerMapping;
            if (clickedItem == null || !ReferenceEquals(clickedItem, selectedItem))
            {
                return;
            }

            e.Handled = true;
            PreviewPaneRuntime.ViewModel.ResetSelectedLayerHighlightState();
            ClearLayerMappingsGridSelection();
            await PreviewPaneRuntime.ViewModel.ClearGeneratedElementHighlightAsync();
        }

        private static bool IsSelectionToggleIgnored(DependencyObject source)
        {
            return FindAncestor<CheckBox>(source) != null ||
                FindAncestor<WpfComboBox>(source) != null ||
                FindAncestor<WpfTextBox>(source) != null ||
                FindAncestor<ButtonBase>(source) != null ||
                FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(source) != null;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static DataGridTemplateColumn BuildButtonColumn(string text, string command)
        {
            FrameworkElementFactory btn = new FrameworkElementFactory(typeof(Button));
            btn.SetValue(Button.ContentProperty, text);
            btn.SetValue(Button.PaddingProperty, new Thickness(4, 1, 4, 1));
            btn.SetBinding(Button.CommandProperty, new Binding("DataContext." + command)
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            btn.SetBinding(Button.CommandParameterProperty, new Binding());
            DataTemplate template = new DataTemplate { VisualTree = btn };
            return new DataGridTemplateColumn { Header = text, CellTemplate = template };
        }

        private static DataGridTemplateColumn BuildLayerGenerationColumn()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory actionButton = new FrameworkElementFactory(typeof(Button));
            actionButton.SetBinding(Button.ContentProperty, new Binding("GenerationActionText"));
            actionButton.SetBinding(Button.ToolTipProperty, new Binding("GenerationActionToolTip"));
            SetActionToolTipTiming(actionButton);
            actionButton.SetValue(Button.MinWidthProperty, 68.0);
            actionButton.SetValue(Button.HeightProperty, 22.0);
            actionButton.SetValue(Button.PaddingProperty, new Thickness(6, 0, 6, 0));
            actionButton.SetValue(Button.MarginProperty, new Thickness(2, 0, 4, 0));
            actionButton.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            actionButton.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            actionButton.SetBinding(Button.VisibilityProperty, new Binding("IsSingleLayerActionVisible")
            {
                Converter = BoolToVisibility
            });
            actionButton.SetBinding(Button.CommandProperty, new Binding("DataContext.GenerateLayerCommand")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            actionButton.SetBinding(Button.CommandParameterProperty, new Binding());
            panel.AppendChild(actionButton);

            // Use the same vector FontAwesome trash icon as the Room & Lift list.
            // This avoids the blurry raster PNG previously loaded from ResourceIcons.del16.
            IconChar deleteIconChar = ResolveFontAwesomeIcon(
                IconChar.Pencil,
                "TrashCan",
                "TrashAlt",
                "Trash");

            FrameworkElementFactory deleteContent = new FrameworkElementFactory(typeof(IconBlock));
            deleteContent.SetValue(IconBlock.IconProperty, deleteIconChar);
            deleteContent.SetValue(TextBlock.FontSizeProperty, 15.0);
            deleteContent.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(229, 57, 53)));
            deleteContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            deleteContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory deleteButton = new FrameworkElementFactory(typeof(Button));
            deleteButton.SetValue(Button.WidthProperty, 22.0);
            deleteButton.SetValue(Button.HeightProperty, 22.0);
            deleteButton.SetValue(Button.PaddingProperty, new Thickness(0));
            deleteButton.SetValue(Button.MarginProperty, new Thickness(0));
            deleteButton.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            deleteButton.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            deleteButton.SetValue(Button.StyleProperty, DeleteIconButtonStyle);
            deleteButton.SetBinding(Button.ToolTipProperty, new Binding("DeleteSingleLayerToolTip"));
            SetActionToolTipTiming(deleteButton);
            deleteButton.SetBinding(Button.VisibilityProperty, new Binding("IsSingleDeleteActionVisible")
            {
                Converter = BoolToVisibility
            });
            deleteButton.SetBinding(Button.CommandProperty, new Binding("DataContext.DeleteLayerCommand")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            deleteButton.SetBinding(Button.CommandParameterProperty, new Binding());
            deleteButton.AppendChild(deleteContent);
            panel.AppendChild(deleteButton);

            DataTemplate template = new DataTemplate { VisualTree = panel };
            return new DataGridTemplateColumn
            {
                Header = "Actions",
                Width = new DataGridLength(118, DataGridLengthUnitType.Pixel),
                CellTemplate = template
            };
        }

        private static IconChar ResolveFontAwesomeIcon(IconChar fallback, params string[] names)
        {
            if (names != null)
            {
                foreach (string name in names)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (Enum.TryParse(name, true, out IconChar icon))
                    {
                        return icon;
                    }
                }
            }

            return fallback;
        }

        private static DataGridTemplateColumn BuildFamilyTypeColumn()
        {
            FrameworkElementFactory combo = new FrameworkElementFactory(typeof(WpfComboBox));
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FamilyTypeOptions"));
            combo.SetBinding(Selector.SelectedItemProperty, new Binding("FamilyTypeName") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            DataTemplate template = new DataTemplate { VisualTree = combo };
            return new DataGridTemplateColumn
            {
                Header = Loc.T("Common.FamilyType"),
                Width = new DataGridLength(35, DataGridLengthUnitType.Star),
                CellTemplate = template,
                CellEditingTemplate = template
            };
        }

        private static DataGridTemplateColumn BuildCategoryColumn()
        {
            FrameworkElementFactory combo = new FrameworkElementFactory(typeof(WpfComboBox));
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("DataContext.CategoryOptions")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            combo.SetBinding(Selector.SelectedItemProperty, new Binding("Category")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            FrameworkElementFactory display = new FrameworkElementFactory(typeof(TextBlock));
            display.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = CategoryDisplayConverter });
            combo.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = display });

            DataTemplate template = new DataTemplate { VisualTree = combo };
            return new DataGridTemplateColumn
            {
                Header = BuildCategoryHeader(),
                Width = new DataGridLength(20, DataGridLengthUnitType.Star),
                CellTemplate = template,
                CellEditingTemplate = template,
                CanUserSort = false
            };
        }

        private static FrameworkElement BuildCategoryHeader()
        {
            DockPanel panel = new DockPanel
            {
                LastChildFill = false,
                MinWidth = 92
            };

            TextBlock label = new TextBlock
            {
                Text = Loc.T("DockablePane.Column.Category"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(label, Dock.Left);
            panel.Children.Add(label);

            WpfToggleButton filterButton = new WpfToggleButton
            {
                Content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0 0 L 7 0 L 3.5 4.5 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(94, 94, 94)),
                    Width = 7,
                    Height = 5,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = BuildCategoryFilterButtonStyle(),
                ToolTip = "Filter Category"
            };
            DockPanel.SetDock(filterButton, Dock.Right);
            panel.Children.Add(filterButton);

            Popup popup = new Popup
            {
                PlacementTarget = filterButton,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsChecked")
            {
                Source = filterButton,
                Mode = BindingMode.TwoWay
            });

            Border popupBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 10, 6)
            };

            StackPanel options = new StackPanel
            {
                Orientation = Orientation.Vertical,
                MinWidth = 118
            };
            options.Children.Add(BuildCategoryFilterCheckBox("Valid", "ShowValidCategoryFilter"));
            options.Children.Add(BuildCategoryFilterCheckBox("Invalid", "ShowInvalidCategoryFilter"));
            options.Children.Add(BuildCategoryFilterCheckBox("Not for Build", "ShowNotForBuildCategoryFilter"));
            popupBorder.Child = options;
            popup.Child = popupBorder;
            panel.Children.Add(popup);

            return panel;
        }

        private static Style BuildCategoryFilterButtonStyle()
        {
            Style style = new Style(typeof(WpfToggleButton));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

            ControlTemplate template = new ControlTemplate(typeof(WpfToggleButton));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "FilterButtonBorder";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            Trigger hoverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(238, 238, 238)), "FilterButtonBorder"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(205, 205, 205)), "FilterButtonBorder"));
            template.Triggers.Add(hoverTrigger);

            Trigger checkedTrigger = new Trigger
            {
                Property = WpfToggleButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(224, 236, 248)), "FilterButtonBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(159, 190, 224)), "FilterButtonBorder"));
            template.Triggers.Add(checkedTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static CheckBox BuildCategoryFilterCheckBox(string text, string bindingPath)
        {
            CheckBox checkBox = new CheckBox
            {
                Content = text,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.SetBinding(WpfToggleButton.IsCheckedProperty, new Binding(bindingPath)
            {
                Source = PreviewPaneRuntime.ViewModel,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return checkBox;
        }

        private static void ApplyLayerColumnWidths(DataGrid mappings)
        {
            if (mappings == null || mappings.Columns == null || mappings.Columns.Count < 6)
            {
                return;
            }

            double available = mappings.ActualWidth - mappings.BorderThickness.Left - mappings.BorderThickness.Right - SystemParameters.VerticalScrollBarWidth - 6.0;
            if (available <= 0)
            {
                return;
            }

            double selectedWidth = 24.0;
            double visibilityWidth = 24.0;
            double actionWidth = 124.0;
            double contentWidth = Math.Max(0.0, available - selectedWidth - visibilityWidth - actionWidth);
            mappings.Columns[0].Width = new DataGridLength(selectedWidth, DataGridLengthUnitType.Pixel);
            mappings.Columns[1].Width = new DataGridLength(visibilityWidth, DataGridLengthUnitType.Pixel);
            mappings.Columns[2].Width = new DataGridLength(Math.Max(120.0, contentWidth * 0.35), DataGridLengthUnitType.Pixel);
            mappings.Columns[3].Width = new DataGridLength(Math.Max(108.0, contentWidth * 0.20 + 12.0), DataGridLengthUnitType.Pixel);
            mappings.Columns[4].Width = new DataGridLength(Math.Max(140.0, contentWidth * 0.35), DataGridLengthUnitType.Pixel);
            mappings.Columns[5].Width = new DataGridLength(actionWidth, DataGridLengthUnitType.Pixel);
        }

        private static FrameworkElement BuildSettingsGroup(string header, params UIElement[] children)
        {
            StackPanel content = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
            foreach (UIElement child in children)
            {
                content.Children.Add(child);
            }

            return new GroupBox
            {
                Header = header,
                Content = content,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(4)
            };
        }

        private static FrameworkElement BuildSettingsExpander(string header, bool isExpanded, params UIElement[] children)
        {
            StackPanel content = new StackPanel { Margin = new Thickness(8, 6, 8, 2) };
            foreach (UIElement child in children)
            {
                content.Children.Add(child);
            }

            return new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = isExpanded,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(0, 0, 0, 4)
            };
        }

        private static FrameworkElement BindField(string label, string path)
        {
            return BindField(label, path, 100);
        }

        private static FrameworkElement BindField(string label, string path, double textBoxWidth)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(120.0, textBoxWidth)) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 0);
            row.Children.Add(tb);
            WpfTextBox box = new WpfTextBox { Height = 24, Width = textBoxWidth, HorizontalAlignment = HorizontalAlignment.Left };
            box.SetBinding(WpfTextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            // Show a compact unit tag on the right side to improve readability.
            TextBlock unit = new TextBlock
            {
                Text = ResolveUnitLabel(label),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(unit, 2);
            row.Children.Add(unit);
            return row;
        }

        private static string ResolveUnitLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            string text = label.ToLowerInvariant();
            if (text.Contains("(mm)") || text.Contains("mm)"))
            {
                return "mm";
            }

            if (text.Contains("(deg)") || text.Contains("deg)"))
            {
                return "deg";
            }

            if (text.Contains("(m2)") || text.Contains("m2)"))
            {
                return "m2";
            }

            if (text.Contains("(ratio)") || text.Contains("ratio)"))
            {
                return "ratio";
            }

            if (text.Contains("(count)") || text.Contains("count)"))
            {
                return "count";
            }

            return string.Empty;
        }

        private static FrameworkElement BindCombo(string label, string selectedPath, string optionsPath)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            TextBlock tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 0);
            row.Children.Add(tb);

            WpfComboBox combo = new WpfComboBox { Height = 24, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(optionsPath));
            combo.SetBinding(Selector.SelectedItemProperty, new Binding(selectedPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            return row;
        }

        private static FrameworkElement BindCheck(string label, string path)
        {
            CheckBox cb = new CheckBox { Content = label, Margin = new Thickness(0, 0, 0, 6) };
            cb.IsThreeState = false;
            cb.SetBinding(
                WpfToggleButton.IsCheckedProperty,
                new Binding(path)
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    TargetNullValue = false,
                    FallbackValue = false
                });
            return cb;
        }

        private static TextBlock BindText(string format, string path, Thickness? margin = null, Brush fg = null)
        {
            TextBlock tb = new TextBlock
            {
                Margin = margin ?? new Thickness(0, 0, 0, 3),
                Foreground = fg
            };
            tb.SetBinding(TextBlock.TextProperty, new Binding(path) { StringFormat = format });
            return tb;
        }

        private static TextBlock BindText(string format, string path1, string path2, Brush fg)
        {
            TextBlock tb = new TextBlock { Foreground = fg };
            MultiBinding mb = new MultiBinding { StringFormat = format };
            mb.Bindings.Add(new Binding(path1));
            mb.Bindings.Add(new Binding(path2));
            tb.SetBinding(TextBlock.TextProperty, mb);
            return tb;
        }

        private static TextBlock BindText(string format, string path1, string path2, string path3, Brush fg)
        {
            TextBlock tb = new TextBlock { Foreground = fg };
            MultiBinding mb = new MultiBinding { StringFormat = format };
            mb.Bindings.Add(new Binding(path1));
            mb.Bindings.Add(new Binding(path2));
            mb.Bindings.Add(new Binding(path3));
            tb.SetBinding(TextBlock.TextProperty, mb);
            return tb;
        }

        private static Button BindButton(string text, string commandPath, double width, Brush fg, Brush bg, Brush border)
        {
            Button btn = new Button
            {
                Content = text,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = fg,
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1)
            };
            if (!double.IsNaN(width) && width > 0)
            {
                btn.Width = width;
            }
            btn.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        private static Button BindIconButton(string glyph, string text, string commandPath, double width, Brush fg, Brush bg, Brush border)
        {
            return BindResourceIconButton(null, glyph, text, commandPath, width, fg, bg, border);
        }

        private static Button BindResourceIconButton(string resourceName, string fallbackGlyph, string text, string commandPath, double width, Brush fg, Brush bg, Brush border)
        {
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            FrameworkElement iconElement = TryCreateResourceIcon(resourceName);
            if (iconElement == null)
            {
                iconElement = new TextBlock
                {
                    Text = fallbackGlyph,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 15,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            TextBlock label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(iconElement);
            content.Children.Add(label);

            Button btn = BindButton(string.Empty, commandPath, width, fg, bg, border);
            btn.Content = content;
            return btn;
        }

        private static FrameworkElement TryCreateResourceIcon(string resourceName)
        {
            ImageSource source = TryLoadResourceIcon(resourceName);
            if (source == null)
            {
                return null;
            }

            return new Image
            {
                Source = source,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static ImageSource TryLoadResourceIcon(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                return null;
            }

            Type resourceType = ResolveResourceIconsType();
            if (resourceType != null)
            {
                // Resource names such as "show-black" / "hide-white" are valid .resx keys,
                // but they cannot be exposed as normal C# properties because of the hyphen.
                // The old LayerName-column icons used property-safe keys such as show16, so
                // reflection worked before. The new independent column must load hyphen keys
                // through ResourceManager first.
                ImageSource fromResourceManager = TryLoadResourceIconFromResourceManager(resourceType, resourceName)
                    ?? TryLoadResourceIconFromResourceManager(resourceType, resourceName.Replace('-', '_'));
                if (fromResourceManager != null)
                {
                    return fromResourceManager;
                }

                ImageSource fromProperty = TryLoadResourceIconFromProperty(resourceType, resourceName)
                    ?? TryLoadResourceIconFromProperty(resourceType, resourceName.Replace('-', '_'));
                if (fromProperty != null)
                {
                    return fromProperty;
                }
            }

            return TryLoadResourceIconFromFile(resourceName)
                ?? TryLoadResourceIconFromFile(resourceName.Replace('-', '_'));
        }

        private static ImageSource TryLoadResourceIconFromResourceManager(Type resourceType, string resourceName)
        {
            try
            {
                System.Reflection.PropertyInfo managerProperty = resourceType.GetProperty(
                    "ResourceManager",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                System.Resources.ResourceManager manager = managerProperty != null
                    ? managerProperty.GetValue(null, null) as System.Resources.ResourceManager
                    : null;

                if (manager == null)
                {
                    return null;
                }

                object value = manager.GetObject(resourceName, CultureInfo.CurrentUICulture)
                    ?? manager.GetObject(resourceName, CultureInfo.InvariantCulture);
                return ConvertResourceIconValueToImageSource(value);
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource TryLoadResourceIconFromProperty(Type resourceType, string resourceName)
        {
            try
            {
                System.Reflection.PropertyInfo property = resourceType.GetProperty(
                    resourceName,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (property == null)
                {
                    return null;
                }

                return ConvertResourceIconValueToImageSource(property.GetValue(null, null));
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource ConvertResourceIconValueToImageSource(object value)
        {
            BitmapSource bitmapSource = value as BitmapSource;
            if (bitmapSource != null)
            {
                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }
                return bitmapSource;
            }

            System.Drawing.Image drawingImage = value as System.Drawing.Image;
            if (drawingImage != null)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    drawingImage.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;

                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }

            return null;
        }

        private static ImageSource TryLoadResourceIconFromFile(string resourceName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(baseDir, "Resources", resourceName + ".png"),
                    Path.Combine(baseDir, resourceName + ".png")
                };

                foreach (string path in candidates)
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                // Icon is optional; keep the UI functional if the resource cannot be resolved.
            }

            return null;
        }

        private static Type ResolveResourceIconsType()
        {
            string[] typeNames =
            {
                "CadToRevit.ResourceIcons",
                "CadToRevit.Properties.ResourceIcons",
                "CadToRevit.Resources.ResourceIcons",
                "ResourceIcons"
            };

            foreach (string typeName in typeNames)
            {
                Type type = Type.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string typeName in typeNames)
                {
                    Type type = assembly.GetType(typeName);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private sealed class LayerVisibilityIconVisibilityConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                bool hidden = values != null && values.Length > 0 && values[0] is bool && (bool)values[0];
                bool rowSelected = (values != null && values.Length > 1 && values[1] is bool && (bool)values[1])
                    || (values != null && values.Length > 3 && values[3] is bool && (bool)values[3]);
                bool toggleVisible = values != null && values.Length > 2 && values[2] is bool && (bool)values[2];
                if (!toggleVisible)
                {
                    return System.Windows.Visibility.Collapsed;
                }

                string state = parameter as string ?? string.Empty;
                bool match;
                switch (state)
                {
                    case "HideWhite":
                        match = !hidden && rowSelected;
                        break;
                    case "HideBlack":
                        match = !hidden && !rowSelected;
                        break;
                    case "ShowWhite":
                        match = hidden && rowSelected;
                        break;
                    case "ShowBlack":
                        match = hidden && !rowSelected;
                        break;
                    default:
                        match = false;
                        break;
                }

                return match ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class MapCategoryDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is CadToRevit.Models.Mapping.MapCategory &&
                    (CadToRevit.Models.Mapping.MapCategory)value == CadToRevit.Models.Mapping.MapCategory.NotForBuild)
                {
                    return "Not for Build";
                }

                return value != null ? value.ToString() : string.Empty;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
