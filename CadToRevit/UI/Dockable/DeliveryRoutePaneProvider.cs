using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace CadToRevit.UI.Dockable
{
    public sealed class DeliveryRoutePaneProvider : IDockablePaneProvider
    {
        public FrameworkElement FrameworkElement { get; }

        public DeliveryRoutePaneProvider()
        {
            FrameworkElement = BuildView();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = FrameworkElement;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }

        private static FrameworkElement BuildView()
        {
            Grid layout = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                MinWidth = 360
            };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            StackPanel root = new StackPanel
            {
                Margin = new Thickness(14, 12, 14, 14)
            };
            root.Children.Add(BuildOverview());
            root.Children.Add(BuildEditor());
            scroll.Content = root;
            Grid.SetRow(scroll, 0);
            layout.Children.Add(scroll);

            FrameworkElement footer = BuildFooter();
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);

            layout.DataContext = DeliveryRoutePaneRuntime.ViewModel;
            return layout;
        }

        private static FrameworkElement BuildOverview()
        {
            StackPanel panel = new StackPanel();
            panel.SetBinding(UIElement.VisibilityProperty, new Binding("IsOverviewVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Border outerCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel content = new StackPanel();

            Grid header = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14)
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Delivery Routes",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 32, 41)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            Grid actionHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right
            };

            StackPanel normalActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            normalActions.SetBinding(UIElement.VisibilityProperty, new Binding("NormalOverviewActionsVisibility"));
            normalActions.Children.Add(BuildOverviewActionButton("+ New Route", "NewRouteCommand"));
            normalActions.Children.Add(BuildOverviewActionButton("Compare Mode", "CompareModeCommand", true));
            actionHost.Children.Add(normalActions);

            StackPanel compareActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            compareActions.SetBinding(UIElement.VisibilityProperty, new Binding("CompareOverviewActionsVisibility"));
            compareActions.Children.Add(BuildOverviewActionButton("Cancel", "CancelCompareModeCommand"));

            Button doneButton = BuildOverviewActionButton("Done", "FinishCompareModeCommand", true);
            doneButton.SetBinding(ContentControl.ContentProperty, new Binding("DoneCompareButtonText"));
            compareActions.Children.Add(doneButton);
            actionHost.Children.Add(compareActions);

            Grid.SetColumn(actionHost, 1);
            header.Children.Add(actionHost);

            content.Children.Add(header);
            content.Children.Add(BuildCompareRoutesDisplayedPanel());

            ItemsControl routes = new ItemsControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            routes.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SavedRoutes"));
            routes.ItemTemplate = BuildSavedRouteCardTemplate();
            content.Children.Add(routes);

            TextBlock empty = new TextBlock
            {
                Text = "No saved delivery routes.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 132, 146)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 20)
            };
            empty.SetBinding(UIElement.VisibilityProperty, new Binding("IsEmptyOverviewVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            content.Children.Add(empty);

            TextBlock hint = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            hint.SetBinding(TextBlock.TextProperty, new Binding("HintText"));
            content.Children.Add(hint);

            outerCard.Child = content;
            panel.Children.Add(outerCard);
            return panel;
        }


        private static Button BuildOverviewActionButton(string text, string commandPath, bool addLeftMargin = false)
        {
            Button button = new Button
            {
                Content = text,
                Height = 34,
                MinWidth = text.IndexOf("Compare", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 118 : 108,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = addLeftMargin ? new Thickness(8, 0, 0, 0) : new Thickness(0),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 71, 92)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(199, 211, 224)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static FrameworkElement BuildCompareRoutesDisplayedPanel()
        {
            Border panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 249, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 211, 236)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.SetBinding(UIElement.VisibilityProperty, new Binding("CompareRoutesDisplayedVisibility"));

            DockPanel row = new DockPanel
            {
                LastChildFill = true
            };

            Button clear = BuildOverviewActionButton("Clear Compare Routes", "ClearCompareRoutesCommand");
            clear.MinWidth = 132;
            clear.Height = 30;
            clear.SetValue(DockPanel.DockProperty, Dock.Right);
            row.Children.Add(clear);

            TextBlock text = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 71, 92)),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            text.SetBinding(TextBlock.TextProperty, new Binding("CompareRoutesDisplayedText"));
            row.Children.Add(text);

            panel.Child = row;
            return panel;
        }

        private static DataTemplate BuildSavedRouteCardTemplate()
        {
            DataTemplate template = new DataTemplate();

            FrameworkElementFactory card = new FrameworkElementFactory(typeof(Border));
            card.SetBinding(Border.BackgroundProperty, new Binding("CompareCardBackground"));
            card.SetBinding(Border.BorderBrushProperty, new Binding("CompareBorderBrush"));
            card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            card.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            card.SetValue(Border.PaddingProperty, new Thickness(14, 12, 14, 12));
            card.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 14));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));

            FrameworkElementFactory compareCheck = new FrameworkElementFactory(typeof(CheckBox));
            compareCheck.SetValue(FrameworkElement.WidthProperty, 20.0);
            compareCheck.SetValue(FrameworkElement.HeightProperty, 20.0);
            compareCheck.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            compareCheck.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            compareCheck.SetBinding(UIElement.VisibilityProperty, new Binding("CompareCheckBoxVisibility"));
            compareCheck.SetBinding(UIElement.IsEnabledProperty, new Binding("IsCompareEligible"));
            compareCheck.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsCompareSelected") { Mode = BindingMode.OneWay });
            compareCheck.SetBinding(ButtonBase.CommandProperty, new Binding("CompareCommand"));
            compareCheck.SetBinding(ButtonBase.CommandParameterProperty, new Binding("."));

            FrameworkElementFactory title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("DisplayTitle"));
            title.SetValue(TextBlock.FontSizeProperty, 16.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            title.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(28, 35, 43)));
            title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory leftHeader = new FrameworkElementFactory(typeof(StackPanel));
            leftHeader.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            leftHeader.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leftHeader.AppendChild(compareCheck);
            leftHeader.AppendChild(title);

            FrameworkElementFactory date = new FrameworkElementFactory(typeof(TextBlock));
            date.SetBinding(TextBlock.TextProperty, new Binding("CreatedAtText"));
            date.SetValue(TextBlock.FontSizeProperty, 12.0);
            date.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(142, 153, 166)));
            date.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            date.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory headerUniform = new FrameworkElementFactory(typeof(UniformGrid));
            headerUniform.SetValue(UniformGrid.ColumnsProperty, 2);
            headerUniform.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            headerUniform.AppendChild(leftHeader);
            headerUniform.AppendChild(date);
            stack.AppendChild(headerUniform);

            FrameworkElementFactory separator = new FrameworkElementFactory(typeof(Border));
            separator.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(224, 230, 236)));
            separator.SetValue(FrameworkElement.HeightProperty, 1.0);
            separator.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            stack.AppendChild(separator);

            stack.AppendChild(BuildSavedRouteTwoColumnRowFactory(
                "Start Location:", "StartLiftName",
                "Target Room:", "TargetRoomName",
                Visibility.Visible));

            stack.AppendChild(BuildSavedRouteSingleRowFactory("Equipment:", "EquipmentDisplayName"));

            FrameworkElementFactory modulesRow = BuildSavedRouteTwoColumnRowFactory(
                "Modules:", "ModulesText",
                "Max Dims:", "MaxDimsText",
                Visibility.Visible);
            modulesRow.SetBinding(UIElement.VisibilityProperty, new Binding("ModuleSummaryVisibility"));
            stack.AppendChild(modulesRow);

            FrameworkElementFactory statusRow = new FrameworkElementFactory(typeof(Grid));
            statusRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 10));

            FrameworkElementFactory statusLeft = BuildSavedRouteLabelValueFactory("Route Length(m):", "RouteLengthValue");
            statusLeft.SetValue(Grid.ColumnProperty, 0);
            statusLeft.SetBinding(UIElement.VisibilityProperty, new Binding("RouteLengthVisibility"));

            FrameworkElementFactory statusRight = new FrameworkElementFactory(typeof(StackPanel));
            statusRight.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            statusRight.SetValue(Grid.ColumnProperty, 1);
            statusRight.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            FrameworkElementFactory statusLabel = new FrameworkElementFactory(typeof(TextBlock));
            statusLabel.SetValue(TextBlock.TextProperty, "Status:");
            statusLabel.SetValue(TextBlock.FontSizeProperty, 13.0);
            statusLabel.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            statusLabel.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(35, 42, 49)));
            statusLabel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));

            FrameworkElementFactory statusValue = new FrameworkElementFactory(typeof(TextBlock));
            statusValue.SetBinding(TextBlock.TextProperty, new Binding("StatusText"));
            statusValue.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusForeground"));
            statusValue.SetValue(TextBlock.FontSizeProperty, 13.0);

            statusRight.AppendChild(statusLabel);
            statusRight.AppendChild(statusValue);

            FrameworkElementFactory statusUniform = new FrameworkElementFactory(typeof(UniformGrid));
            statusUniform.SetValue(UniformGrid.ColumnsProperty, 2);
            statusUniform.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 10));
            statusUniform.AppendChild(statusLeft);
            statusUniform.AppendChild(statusRight);
            stack.AppendChild(statusUniform);

            FrameworkElementFactory buttonBar = new FrameworkElementFactory(typeof(DockPanel));
            buttonBar.SetValue(DockPanel.LastChildFillProperty, true);
            buttonBar.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            buttonBar.SetBinding(UIElement.VisibilityProperty, new Binding("ActionButtonsVisibility"));

            FrameworkElementFactory deleteButton =
                BuildSavedRouteActionButtonFactory("Delete", "DeleteCommand", false, new Thickness(8, 0, 0, 0));
            deleteButton.SetValue(FrameworkElement.WidthProperty, 100.0);
            deleteButton.SetValue(DockPanel.DockProperty, Dock.Right);
            buttonBar.AppendChild(deleteButton);

            FrameworkElementFactory exportButton =
                BuildSavedRouteActionButtonFactory("Export Report", "ExportReportCommand", true, new Thickness(8, 0, 0, 0));
            exportButton.SetValue(FrameworkElement.WidthProperty, 150.0);
            exportButton.SetValue(DockPanel.DockProperty, Dock.Right);
            buttonBar.AppendChild(exportButton);

            FrameworkElementFactory detailButton =
                BuildSavedRouteActionButtonFactory("Detail", "DetailCommand", true, new Thickness(0));
            buttonBar.AppendChild(detailButton);

            stack.AppendChild(buttonBar);

            card.AppendChild(stack);
            template.VisualTree = card;
            return template;
        }

        private static FrameworkElementFactory BuildSavedRouteTwoColumnRowFactory(
            string leftLabel,
            string leftPath,
            string rightLabel,
            string rightPath,
            Visibility visibility)
        {
            FrameworkElementFactory row = new FrameworkElementFactory(typeof(UniformGrid));
            row.SetValue(UniformGrid.ColumnsProperty, 2);
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            row.SetValue(UIElement.VisibilityProperty, visibility);
            row.AppendChild(BuildSavedRouteLabelValueFactory(leftLabel, leftPath));
            row.AppendChild(BuildSavedRouteLabelValueFactory(rightLabel, rightPath));
            return row;
        }

        private static FrameworkElementFactory BuildSavedRouteSingleRowFactory(string label, string valuePath)
        {
            FrameworkElementFactory row = BuildSavedRouteLabelValueFactory(label, valuePath);
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            return row;
        }

        private static FrameworkElementFactory BuildSavedRouteLabelValueFactory(string label, string valuePath)
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory labelBlock = new FrameworkElementFactory(typeof(TextBlock));
            labelBlock.SetValue(TextBlock.TextProperty, label);
            labelBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
            labelBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            labelBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(35, 42, 49)));
            labelBlock.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));

            FrameworkElementFactory valueBlock = new FrameworkElementFactory(typeof(TextBlock));
            valueBlock.SetBinding(TextBlock.TextProperty, new Binding(valuePath));
            valueBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
            valueBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(35, 42, 49)));
            valueBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

            panel.AppendChild(labelBlock);
            panel.AppendChild(valueBlock);
            return panel;
        }

        private static FrameworkElementFactory BuildSavedRouteActionButtonFactory(
            string text,
            string commandPath,
            bool primary,
            Thickness margin)
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.ContentProperty, text);
            button.SetValue(FrameworkElement.HeightProperty, 36.0);
            button.SetValue(FrameworkElement.MarginProperty, margin);
            button.SetValue(Button.FontSizeProperty, 13.0);
            button.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
            button.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);

            if (primary)
            {
                button.SetValue(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(33, 109, 190)));
                button.SetValue(Control.ForegroundProperty, Brushes.White);
                button.SetValue(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(33, 109, 190)));
            }
            else
            {
                button.SetValue(Control.BackgroundProperty, Brushes.White);
                button.SetValue(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(224, 45, 45)));
                button.SetValue(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(214, 222, 232)));
            }

            button.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            // The ItemsControl item (DeliveryRouteCardViewModel) is the DataContext here.
            // Pass that card to the shared ViewModel command; otherwise the command
            // receives null and Detail / Export Report / Delete all become no-ops.
            button.SetBinding(Button.CommandParameterProperty, new Binding("."));
            return button;
        }

        private static FrameworkElement BuildEditor()
        {
            StackPanel panel = new StackPanel();
            panel.SetBinding(UIElement.VisibilityProperty, new Binding("IsEditorVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            panel.Children.Add(BuildTitle("Delivery Route"));
            panel.Children.Add(BuildLabel("Route Name"));
            System.Windows.Controls.TextBox routeName = new System.Windows.Controls.TextBox
            {
                Height = 34,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 6, 0, 10)
            };
            routeName.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding("RouteName")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.Children.Add(routeName);

            panel.Children.Add(BuildStartLocationEditor());

            panel.Children.Add(BuildSelectionLabel("Target Room", "IsTargetRoomDefined"));
            panel.Children.Add(BuildCombo("TargetRoomOptions", "SelectedTargetRoom", "RoomName"));
            panel.Children.Add(BuildWarningCard(
                "No rooms detected in the project",
                "Please go to \"Room & Lift\" on the top toolbar and use \"Auto-Detect icon\" or click \"+ Create Room\" in the Room List to define a destination.",
                "IsNoRoomsWarningVisible"));
            panel.Children.Add(BuildWarningCard(
                "No committed equipment found in this room",
                "Unapplied draft layouts cannot be routed. Please go to \"Layout Plan\" on the top toolbar, select this room to configure equipment, and click \"Save & Submit\" at the bottom to generate the 3D model instance.",
                "IsNoEquipmentWarningVisible"));
            panel.Children.Add(BuildButton("Define Target Room", "DefineTargetRoomCommand", false));
            panel.Children.Add(BuildHint());
            Button generateButton = BuildButton("Generate Delivery Route", "GenerateDeliveryRouteCommand", true);
            generateButton.SetBinding(UIElement.IsEnabledProperty, new Binding("CanGenerateDeliveryRoute"));
            ApplyGenerateRouteButtonVisual(generateButton);
            panel.Children.Add(generateButton);
            panel.Children.Add(BuildResult());
            panel.Children.Add(BuildSubModuleTable());
            return panel;
        }

        private static FrameworkElement BuildStartLocationEditor()
        {
            StackPanel host = new StackPanel();
            host.Children.Add(BuildSelectionLabel("Start Location", "IsStartLocationDefined"));

            Grid selectorRow = new Grid
            {
                Margin = new Thickness(0, 6, 0, 8)
            };
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });

            System.Windows.Controls.ComboBox modeCombo = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            modeCombo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("StartLocationModeOptions"));
            modeCombo.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedStartLocationMode")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(modeCombo, 0);
            selectorRow.Children.Add(modeCombo);

            Grid rightHost = new Grid();
            Grid.SetColumn(rightHost, 2);

            System.Windows.Controls.ComboBox liftCombo = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                DisplayMemberPath = "DisplayName",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            liftCombo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("StartLiftOptions"));
            liftCombo.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedStartLift")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            liftCombo.SetBinding(UIElement.VisibilityProperty, new Binding("LiftStartControlsVisibility"));
            rightHost.Children.Add(liftCombo);

            Button setLocation = BuildButton("Set Location", "SetStartLocationCommand", true);
            setLocation.Margin = new Thickness(0);
            setLocation.HorizontalAlignment = HorizontalAlignment.Stretch;
            setLocation.SetBinding(UIElement.VisibilityProperty, new Binding("PointSetButtonVisibility"));
            rightHost.Children.Add(setLocation);

            Grid selectedPoint = new Grid
            {
                Height = 36
            };
            selectedPoint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selectedPoint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            selectedPoint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selectedPoint.SetBinding(UIElement.VisibilityProperty, new Binding("PointSummaryVisibility"));

            Border pointNameBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 216, 229)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0)
            };
            StackPanel pointNameRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            pointNameRow.Children.Add(new TextBlock
            {
                Text = "✓",
                Foreground = new SolidColorBrush(Color.FromRgb(32, 185, 100)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock pointName = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 82, 98))
            };
            pointName.SetBinding(TextBlock.TextProperty, new Binding("StartPointName"));
            pointNameRow.Children.Add(pointName);
            pointNameBorder.Child = pointNameRow;
            Grid.SetColumn(pointNameBorder, 0);
            selectedPoint.Children.Add(pointNameBorder);

            Button remove = BuildButton("Remove", "RemoveStartLocationCommand", false);
            remove.Height = 36;
            remove.Margin = new Thickness(0);
            Grid.SetColumn(remove, 2);
            selectedPoint.Children.Add(remove);
            rightHost.Children.Add(selectedPoint);

            selectorRow.Children.Add(rightHost);
            host.Children.Add(selectorRow);

            FrameworkElement liftWarning = BuildWarningCard(
                "No lifts detected in the project",
                "Please go to \"Room & Lift\" on the top toolbar and use \"Auto-Detect icon\" or click \"+ Create Lift\" in the Lift List to define a starting point.",
                "IsNoLiftsWarningVisible");
            host.Children.Add(liftWarning);

            Button defineLift = BuildButton("Define Start Lift", "DefineStartLiftCommand", false);
            defineLift.SetBinding(UIElement.VisibilityProperty, new Binding("LiftStartControlsVisibility"));
            host.Children.Add(defineLift);

            Button definePoint = BuildButton("Define Start Location", "DefineStartLocationCommand", false);
            definePoint.SetBinding(UIElement.VisibilityProperty, new Binding("PointSummaryVisibility"));
            host.Children.Add(definePoint);

            return host;
        }

        private static FrameworkElement BuildFooter()
        {
            Border footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 233)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 12, 14, 12)
            };
            footer.SetBinding(UIElement.VisibilityProperty, new Binding("IsEditorVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button cancel = BuildFooterActionButton("Cancel", "CancelCommand", false);
            Grid.SetColumn(cancel, 0);
            row.Children.Add(cancel);

            Button save = BuildFooterActionButton("Save", "SaveCommand", true);
            save.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSaveRoute"));
            Grid.SetColumn(save, 2);
            row.Children.Add(save);

            footer.Child = row;
            return footer;
        }

        private static FrameworkElement BuildTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static FrameworkElement BuildLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private static FrameworkElement BuildSelectionLabel(string text, string completedPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            Border checkCircle = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(53, 105, 184)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            checkCircle.Child = new TextBlock
            {
                Text = "✓",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };
            checkCircle.SetBinding(UIElement.VisibilityProperty, new Binding(completedPath)
            {
                Converter = new BooleanToVisibilityConverter()
            });
            Grid.SetColumn(checkCircle, 1);
            row.Children.Add(checkCircle);

            return row;
        }

        private static FrameworkElement BuildCombo(string itemsPath, string selectedPath, string displayPath)
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                DisplayMemberPath = displayPath,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 8)
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding(selectedPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return comboBox;
        }

        private static FrameworkElement BuildHint()
        {
            TextBlock hint = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 10)
            };
            hint.SetBinding(TextBlock.TextProperty, new Binding("HintText"));
            return hint;
        }

        private static FrameworkElement BuildWarningCard(string titleText, string bodyText, string visibilityPath)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 251, 234)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 226, 138)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 10)
            };
            card.SetBinding(UIElement.VisibilityProperty, new Binding(visibilityPath)
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Grid layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Use a real warning icon instead of prefixing the title with a plain "!".
            // The outlined circle matches the storyboard and avoids adding another icon dependency.
            Grid icon = new Grid
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 1, 9, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            icon.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                StrokeThickness = 1.6,
                Fill = Brushes.Transparent
            });
            icon.Children.Add(new TextBlock
            {
                Text = "!",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(221, 139, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            });
            Grid.SetColumn(icon, 0);
            layout.Children.Add(icon);

            StackPanel content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = titleText,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(92, 65, 0)),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = bodyText,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(content, 1);
            layout.Children.Add(content);

            card.Child = layout;
            return card;
        }

        private static FrameworkElement BuildResult()
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 249, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 210, 245)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 14, 0, 0)
            };
            card.SetBinding(UIElement.VisibilityProperty, new Binding("IsResultVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            StackPanel stack = new StackPanel();

            Grid titleRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid iconHost = new Grid
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            FrameworkElement passedIcon = BuildResultStatusIcon("✓", Color.FromRgb(32, 185, 100));
            passedIcon.SetBinding(UIElement.VisibilityProperty, new Binding("IsPassedResultVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            iconHost.Children.Add(passedIcon);

            FrameworkElement failedIcon = BuildResultStatusIcon("×", Color.FromRgb(239, 68, 68));
            failedIcon.SetBinding(UIElement.VisibilityProperty, new Binding("IsFailedResultVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            iconHost.Children.Add(failedIcon);

            Grid.SetColumn(iconHost, 0);
            titleRow.Children.Add(iconHost);

            TextBlock title = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            title.SetBinding(TextBlock.TextProperty, new Binding("ResultTitle"));
            Grid.SetColumn(title, 1);
            titleRow.Children.Add(title);
            stack.Children.Add(titleRow);

            TextBlock message = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 62, 80)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("ResultMessage"));
            stack.Children.Add(message);

            TextBlock failureReason = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 62, 80)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            failureReason.SetBinding(TextBlock.TextProperty, new Binding("FailureReasonText"));
            failureReason.SetBinding(UIElement.VisibilityProperty, new Binding("IsFailedResultVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            stack.Children.Add(failureReason);

            stack.Children.Add(BuildResultRow("Route Length:", "RouteLengthText"));
            stack.Children.Add(BuildResultRow("Disassembly:", "DisassemblyText"));
            stack.Children.Add(BuildResultRow("Max Dims:", "MaxDimsText"));
            card.Child = stack;
            return card;
        }

        private static FrameworkElement BuildResultStatusIcon(string glyph, Color backgroundColor)
        {
            Border circle = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(backgroundColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            circle.Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };
            return circle;
        }

        private static FrameworkElement BuildSubModuleTable()
        {
            Border table = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(211, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin = new Thickness(0, 12, 0, 0)
            };
            table.SetBinding(UIElement.VisibilityProperty, new Binding("HasSubModuleRows")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            StackPanel rows = new StackPanel();
            rows.Children.Add(BuildSubModuleHeaderRow());

            ItemsControl items = new ItemsControl();
            items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SubModuleRows"));
            items.ItemTemplate = BuildSubModuleRowTemplate();
            rows.Children.Add(items);

            table.Child = rows;
            return table;
        }

        private static FrameworkElement BuildSubModuleHeaderRow()
        {
            UniformGrid header = new UniformGrid
            {
                Columns = 3,
                Background = new SolidColorBrush(Color.FromRgb(237, 243, 249))
            };
            header.Children.Add(BuildSubModuleHeaderCell("Sub-module", true));
            header.Children.Add(BuildSubModuleHeaderCell("Type", true));
            header.Children.Add(BuildSubModuleHeaderCell("Dimensions(mm)", false));
            return header;
        }

        private static FrameworkElement BuildSubModuleHeaderCell(string text, bool showRightBorder)
        {
            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(211, 220, 230)),
                BorderThickness = showRightBorder ? new Thickness(0, 0, 1, 1) : new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 9, 12, 9)
            };
            cell.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 82, 98)),
                TextWrapping = TextWrapping.Wrap
            };
            return cell;
        }

        private static DataTemplate BuildSubModuleRowTemplate()
        {
            DataTemplate template = new DataTemplate();

            FrameworkElementFactory rowBorder = new FrameworkElementFactory(typeof(Border));
            rowBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(221, 228, 235)));
            rowBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));

            FrameworkElementFactory row = new FrameworkElementFactory(typeof(UniformGrid));
            row.SetValue(UniformGrid.ColumnsProperty, 3);

            row.AppendChild(BuildSubModuleCellFactory("SubModule", true));
            row.AppendChild(BuildSubModuleCellFactory("Type", true));
            row.AppendChild(BuildSubModuleCellFactory("DimensionsMm", false));
            rowBorder.AppendChild(row);

            template.VisualTree = rowBorder;
            return template;
        }

        private static FrameworkElementFactory BuildSubModuleCellFactory(string bindingPath, bool showRightBorder)
        {
            FrameworkElementFactory cell = new FrameworkElementFactory(typeof(Border));
            cell.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(221, 228, 235)));
            cell.SetValue(Border.BorderThicknessProperty, showRightBorder ? new Thickness(0, 0, 1, 0) : new Thickness(0));
            cell.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));

            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            text.SetValue(TextBlock.FontSizeProperty, 12.0);
            text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(32, 40, 48)));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            cell.AppendChild(text);
            return cell;
        }

        private static FrameworkElement BuildResultRow(string label, string bindingPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 82, 98))
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            TextBlock valueBlock = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                TextWrapping = TextWrapping.Wrap
            };
            valueBlock.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            return row;
        }

        private static Button BuildFooterActionButton(string text, string commandPath, bool primary)
        {
            Color blue = Color.FromRgb(53, 105, 184);

            Color normalBackground = primary
                ? blue
                : Colors.White;

            Color hoverBackground = primary
                ? Color.FromRgb(43, 91, 166)
                : Color.FromRgb(242, 247, 252);

            Color pressedBackground = primary
                ? Color.FromRgb(33, 77, 145)
                : Color.FromRgb(229, 238, 247);

            Color normalBorder = primary
                ? blue
                : Color.FromRgb(205, 216, 229);

            Color hoverBorder = primary
                ? Color.FromRgb(43, 91, 166)
                : Color.FromRgb(173, 194, 218);

            Button button = new Button
            {
                Content = text,
                Height = 40,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Background = new SolidColorBrush(normalBackground),
                Foreground = primary ? Brushes.White : new SolidColorBrush(blue),
                BorderBrush = new SolidColorBrush(normalBorder),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Keep the simple template used previously so a disabled Save button
            // remains blue/white. Do NOT use named Template Trigger targets here:
            // that can throw while the DockablePane is being constructed during
            // Revit startup and can abort the add-in Ribbon initialization.
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
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
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            presenter.SetBinding(System.Windows.Documents.TextElement.ForegroundProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;

            // Restore hover/pressed feedback with direct Button events instead
            // of Template.TargetName triggers. This is much safer for a
            // programmatically-created DockablePane during Revit startup.
            button.MouseEnter += delegate
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                button.Background = new SolidColorBrush(hoverBackground);
                button.BorderBrush = new SolidColorBrush(hoverBorder);
            };

            button.MouseLeave += delegate
            {
                button.Background = new SolidColorBrush(normalBackground);
                button.BorderBrush = new SolidColorBrush(normalBorder);
            };

            button.PreviewMouseLeftButtonDown += delegate
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                button.Background = new SolidColorBrush(pressedBackground);
            };

            button.PreviewMouseLeftButtonUp += delegate
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                button.Background = new SolidColorBrush(
                    button.IsMouseOver ? hoverBackground : normalBackground);
                button.BorderBrush = new SolidColorBrush(
                    button.IsMouseOver ? hoverBorder : normalBorder);
            };

            button.LostMouseCapture += delegate
            {
                button.Background = new SolidColorBrush(
                    button.IsMouseOver && button.IsEnabled
                        ? hoverBackground
                        : normalBackground);

                button.BorderBrush = new SolidColorBrush(
                    button.IsMouseOver && button.IsEnabled
                        ? hoverBorder
                        : normalBorder);
            };

            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static void ApplyGenerateRouteButtonVisual(Button button)
        {
            Color enabledBackground = Color.FromRgb(24, 112, 211);
            Color enabledHoverBackground = Color.FromRgb(18, 95, 181);
            Color enabledPressedBackground = Color.FromRgb(15, 78, 150);
            Color enabledBorder = Color.FromRgb(24, 112, 211);

            Color disabledBackground = Color.FromRgb(241, 243, 246);
            Color disabledForeground = Color.FromRgb(82, 94, 108);
            Color disabledBorder = Color.FromRgb(190, 199, 210);

            button.Height = 38;
            button.FontSize = 14;
            button.FontWeight = FontWeights.SemiBold;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.BorderThickness = new Thickness(1);
            button.Opacity = 1.0;

            // Use a small, trigger-free template so the Revit/WPF theme does
            // not fade the white foreground until the disabled caption becomes
            // almost invisible. Avoid named TargetName triggers here: a similar
            // programmatic template previously caused DockablePane construction
            // to fail during Revit startup.
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
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
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            presenter.SetBinding(System.Windows.Documents.TextElement.ForegroundProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;

            button.IsEnabledChanged += delegate
            {
                RefreshGenerateRouteButtonVisual(
                    button,
                    enabledBackground,
                    enabledBorder,
                    disabledBackground,
                    disabledForeground,
                    disabledBorder);
            };

            button.Loaded += delegate
            {
                RefreshGenerateRouteButtonVisual(
                    button,
                    enabledBackground,
                    enabledBorder,
                    disabledBackground,
                    disabledForeground,
                    disabledBorder);
            };

            button.MouseEnter += delegate
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                button.Background = new SolidColorBrush(enabledHoverBackground);
            };

            button.MouseLeave += delegate
            {
                RefreshGenerateRouteButtonVisual(
                    button,
                    enabledBackground,
                    enabledBorder,
                    disabledBackground,
                    disabledForeground,
                    disabledBorder);
            };

            button.PreviewMouseLeftButtonDown += delegate
            {
                if (button.IsEnabled)
                {
                    button.Background = new SolidColorBrush(enabledPressedBackground);
                }
            };

            button.PreviewMouseLeftButtonUp += delegate
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                button.Background = new SolidColorBrush(
                    button.IsMouseOver ? enabledHoverBackground : enabledBackground);
            };

            RefreshGenerateRouteButtonVisual(
                button,
                enabledBackground,
                enabledBorder,
                disabledBackground,
                disabledForeground,
                disabledBorder);
        }

        private static void RefreshGenerateRouteButtonVisual(
            Button button,
            Color enabledBackground,
            Color enabledBorder,
            Color disabledBackground,
            Color disabledForeground,
            Color disabledBorder)
        {
            button.Opacity = 1.0;
            button.BorderThickness = new Thickness(1);

            if (button.IsEnabled)
            {
                button.Background = new SolidColorBrush(enabledBackground);
                button.Foreground = Brushes.White;
                button.BorderBrush = new SolidColorBrush(enabledBorder);
                button.Cursor = System.Windows.Input.Cursors.Hand;
                return;
            }

            button.Background = new SolidColorBrush(disabledBackground);
            button.Foreground = new SolidColorBrush(disabledForeground);
            button.BorderBrush = new SolidColorBrush(disabledBorder);
            button.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        private static Button BuildButton(string text, string commandPath, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Height = 34,
                MinWidth = primary ? 118 : 112,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = primary ? new Thickness(0, 0, 8, 0) : new Thickness(0),
                Background = primary
                    ? new SolidColorBrush(Color.FromRgb(24, 112, 211))
                    : Brushes.White,
                Foreground = primary
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                BorderBrush = primary
                    ? new SolidColorBrush(Color.FromRgb(24, 112, 211))
                    : new SolidColorBrush(Color.FromRgb(185, 190, 198)),
                BorderThickness = new Thickness(1)
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }
    }
}