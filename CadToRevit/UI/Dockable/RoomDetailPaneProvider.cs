using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using FontAwesome.Sharp;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomDetailPaneProvider : IDockablePaneProvider
    {
        public FrameworkElement FrameworkElement { get; }

        public RoomDetailPaneProvider()
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
            Grid root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 386
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border detailCard = new Border
            {
                Margin = new Thickness(12, 10, 12, 12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.White,
                Padding = new Thickness(0)
            };
            Grid.SetRow(detailCard, 0);
            detailCard.Child = BuildPaneContent();
            root.Children.Add(detailCard);

            root.DataContext = RoomRecognitionPaneRuntime.DetailViewModel;
            return root;
        }

#if false
        private static FrameworkElement BuildDetailContent()
        {
            Grid grid = new Grid();
            for (int i = 0; i < 9; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(BuildPair(0, "名称", "RoomName"));
            grid.Children.Add(BuildPair(1, "类型", "TargetRoomType"));
            grid.Children.Add(BuildPair(2, "面积", "AreaText"));
            grid.Children.Add(BuildPair(3, "楼层", "LevelText"));
            grid.Children.Add(BuildPair(4, "状态", "StatusText"));
            grid.Children.Add(BuildPair(5, "边界来源", "BoundaryLayersText"));
            grid.Children.Add(BuildPair(6, "Key", "RoomKeyText"));
            grid.Children.Add(BuildPair(7, "CloseGap(mm)", "CloseGapText"));

            StackPanel buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Button focus = new Button
            {
                Content = Loc.T("DockablePane.RoomDetail.Button.Focus"),
                Width = 120,
                Height = 30
            };
            focus.SetBinding(Button.CommandProperty, new Binding("FocusRoomCommand"));
            Button copy = new Button
            {
                Content = Loc.T("DockablePane.RoomDetail.Button.Copy"),
                Width = 120,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0)
            };
            copy.SetBinding(Button.CommandProperty, new Binding("CopyInfoCommand"));
            buttonRow.Children.Add(focus);
            buttonRow.Children.Add(copy);
            Grid.SetRow(buttonRow, 8);
            grid.Children.Add(buttonRow);

            TextBlock hint = new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap
            };
            hint.SetBinding(TextBlock.TextProperty, new Binding("RoomName"));
            Grid.SetRow(hint, 9);
            grid.Children.Add(hint);

            return grid;
        }
#endif

        private static FrameworkElement BuildPaneContent()
        {
            Grid contentGrid = new Grid
            {
                Margin = new Thickness(14)
            };

            ScrollViewer overview = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Content = BuildOverviewContent()
            };
            overview.SetBinding(UIElement.VisibilityProperty, new Binding("CurrentPageMode")
            {
                Converter = new PageModeToVisibilityConverter(),
                ConverterParameter = RoomDetailPageMode.Overview
            });
            contentGrid.Children.Add(overview);

            FrameworkElement editor = BuildEditorContent();
            editor.SetBinding(UIElement.VisibilityProperty, new Binding("CurrentPageMode")
            {
                Converter = new PageModeToVisibilityConverter(),
                ConverterParameter = RoomDetailPageMode.SolutionEditor
            });
            contentGrid.Children.Add(editor);

            return contentGrid;
        }

        private static FrameworkElement BuildOverviewContent()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildLayoutPlansSection());
            return panel;
        }

        private static FrameworkElement BuildEditorContent()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildEditorPlannerHeader());
            panel.Children.Add(BuildEditorPlanningHint());
            panel.Children.Add(BuildSolutionAndRoomInformationCard());
            panel.Children.Add(BuildAhuSubModuleWorkflowCard());

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Content = panel
            };
            Grid.SetRow(scrollViewer, 0);
            root.Children.Add(scrollViewer);

            FrameworkElement footer = BuildEditorFooter();
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            return root;
        }

        private static FrameworkElement BuildEditorPlannerHeader()
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            Border badge = BuildEditorBadge("CurrentEditor.PlanningContextBadgeText", "New Building Design");
            badge.SetValue(DockPanel.DockProperty, Dock.Right);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(badge);

            row.Children.Add(new TextBlock
            {
                Text = "Equipment Planner",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return row;
        }

        private static FrameworkElement BuildEditorPlanningHint()
        {
            TextBlock hint = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 110, 130)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            hint.SetBinding(TextBlock.TextProperty, new Binding("CurrentEditor.PlanningContextHint"));
            return hint;
        }

        private static FrameworkElement BuildSolutionAndRoomInformationCard()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildEditorLabel("Solution Name"));

            System.Windows.Controls.TextBox solutionName = BuildEditorTextBox("CurrentEditor.SolutionName");
            solutionName.Height = 32;
            solutionName.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(solutionName);

            DockPanel roomHeader = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            Border statusBadge = BuildEditorBadge(null, "PENDING");
            statusBadge.SetValue(DockPanel.DockProperty, Dock.Right);
            roomHeader.Children.Add(statusBadge);

            roomHeader.Children.Add(new TextBlock
            {
                Text = "Room Information",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(roomHeader);
            panel.Children.Add(BuildRoomInformationEditorBlock());

            return BuildSectionCard(null, panel, false);
        }

        private static FrameworkElement BuildRoomInformationEditorBlock()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildEditorLabel("Target Room"));
            panel.Children.Add(BuildEditorRoomComboRow());
            panel.Children.Add(BuildRoomSummaryGrid());
            return panel;
        }

        private static FrameworkElement BuildEditorRoomComboRow()
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 32,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                DisplayMemberPath = "DisplayName",
                IsEditable = false
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("EditorRoomOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("CurrentEditor.SelectedRoomOption")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return comboBox;
        }

        private static FrameworkElement BuildRoomSummaryGrid()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddRoomSummaryCell(grid, "Room Length", "CurrentEditor.EditorRoomLengthText", 0, 0);
            AddRoomSummaryCell(grid, "Room Width", "CurrentEditor.EditorRoomWidthText", 2, 0);
            AddRoomSummaryCell(grid, "Room Height", "CurrentEditor.EditorRoomHeightText", 4, 0);
            AddRoomSummaryCell(grid, "Door Width", "CurrentEditor.EditorDoorWidthText", 0, 1);
            AddRoomSummaryCell(grid, "Door Height", "CurrentEditor.EditorDoorHeightText", 2, 1);
            AddRoomSummaryCell(grid, "Available / Usable Area", "CurrentEditor.EditorAvailableUsableAreaText", 4, 1);

            return grid;
        }

        private static void AddRoomSummaryCell(Grid grid, string labelText, string bindingPath, int column, int row)
        {
            FrameworkElement cell = BuildRoomSummaryCell(labelText, bindingPath);
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
        }

        private static FrameworkElement BuildRoomSummaryCell(string labelText, string bindingPath)
        {
            StackPanel cell = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            cell.Children.Add(new TextBlock
            {
                Text = labelText,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(99, 115, 129)),
                Margin = new Thickness(0, 0, 0, 2)
            });

            TextBlock value = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                TextWrapping = TextWrapping.Wrap
            };
            value.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            cell.Children.Add(value);
            return cell;
        }

        private static FrameworkElement BuildAhuSubModuleWorkflowCard()
        {
            StackPanel content = new StackPanel();

            content.Children.Add(new TextBlock
            {
                Text = "AHU Sub-module Configuration",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                VerticalAlignment = VerticalAlignment.Center
            });

            content.Children.Add(new TextBlock
            {
                Text = "The system determined the AHU composition based on the selected equipment.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 100, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 14)
            });

            // Keep all AHU configuration steps inside one parent workflow card.
            // Their existing Visibility bindings continue to control the step-by-step flow.
            content.Children.Add(BuildFlowRateEvaluationCard());
            content.Children.Add(BuildEquipmentSelectionCard());
            content.Children.Add(BuildAhuSubModuleConfigurationCard());
            content.Children.Add(BuildConnectivityLayoutCard());

            // Delivery Route is intentionally not added here. Its implementation remains
            // available for the future standalone Delivery Route Ribbon workflow.
            return BuildSectionCard(null, content, false);
        }

        private static FrameworkElement BuildFlowRateEvaluationCard()
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 32,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable = false
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FlowRateOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("CurrentEditor.SelectedFlowRate")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            row.Children.Add(comboBox);

            Button button = BuildSecondaryButton("Size Evaluation", "SizeEvaluationCommand");
            button.Width = 120;
            button.Height = 32;
            button.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);

            FrameworkElement card = BuildSectionCard("Flow Rate", row);
            card.SetBinding(UIElement.VisibilityProperty, new Binding("IsFlowRateCardVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            return card;
        }

        private static Border BuildEditorBadge(string bindingPath, string fallbackText)
        {
            Border badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 214, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 4, 12, 4)
            };

            TextBlock badgeText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 68, 112)),
                VerticalAlignment = VerticalAlignment.Center,
                Text = fallbackText ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(bindingPath))
            {
                badgeText.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            }

            badge.Child = badgeText;
            return badge;
        }

        private static FrameworkElement BuildRoomInformationBlock()
        {
            Grid grid = new Grid();
            for (int i = 0; i < 5; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.Children.Add(BuildPair(0, "Name", "RoomName"));
            grid.Children.Add(BuildPair(1, "Type", "TargetRoomType"));
            grid.Children.Add(BuildPair(2, "Area", "AreaText"));
            grid.Children.Add(BuildPair(3, "Level", "LevelText"));
            grid.Children.Add(BuildPair(4, "Status", "StatusText"));
            return grid;
        }

        private static FrameworkElement BuildLayoutPlansSection()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildLayoutPlansHeaderRow());
            panel.Children.Add(BuildCompareRoutesDisplayedRow());

            FrameworkElement emptyPlaceholder = BuildEmptyLayoutPlansPlaceholder();
            emptyPlaceholder.SetBinding(UIElement.VisibilityProperty, new Binding("LayoutPlans.Count")
            {
                Converter = new ZeroCountToVisibilityConverter()
            });
            panel.Children.Add(emptyPlaceholder);

            ItemsControl plans = new ItemsControl();
            plans.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("LayoutPlans"));
            plans.ItemTemplate = BuildLayoutPlanTemplate();
            panel.Children.Add(plans);
            return panel;
        }

        private static FrameworkElement BuildCompareRoutesDisplayedRow()
        {
            Border border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 250, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(199, 224, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            border.SetBinding(UIElement.VisibilityProperty, new Binding("CompareRoutesDisplayedVisibility"));

            DockPanel row = new DockPanel();

            Button clearButton = BuildSecondaryButton("Clear Compare Routes", "ClearCompareRoutesCommand");
            clearButton.Height = 30;
            clearButton.MinWidth = 150;
            clearButton.SetValue(DockPanel.DockProperty, Dock.Right);
            row.Children.Add(clearButton);

            TextBlock status = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(43, 94, 145)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            status.SetBinding(TextBlock.TextProperty, new Binding("CompareRoutesDisplayedText"));
            row.Children.Add(status);

            border.Child = row;
            return border;
        }

        private static FrameworkElement BuildEmptyLayoutPlansPlaceholder()
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 229, 239)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 12),
                MinHeight = 96,
                Child = new TextBlock
                {
                    Text = "No data available",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(125, 135, 148)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        private static FrameworkElement BuildEditorFlowRateRow()
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = "Flow Rate",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                FontWeight = FontWeights.SemiBold
            };
            row.Children.Add(label);

            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 30,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FlowRateOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("CurrentEditor.SelectedFlowRate")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(comboBox, 1);
            row.Children.Add(comboBox);
            return row;
        }

        private static FrameworkElement BuildEquipmentSelectionSection()
        {
            StackPanel panel = new StackPanel();

            DockPanel header = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            Button toggleButton = BuildSecondaryButton(string.Empty, "ToggleEquipmentOptionsCommand");
            toggleButton.SetValue(DockPanel.DockProperty, Dock.Right);
            toggleButton.Padding = new Thickness(10, 0, 10, 0);
            toggleButton.SetBinding(ContentControl.ContentProperty, new Binding("EquipmentOptionsToggleText"));
            header.Children.Add(toggleButton);

            header.Children.Add(new TextBlock
            {
                Text = "Equipment Selection",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(header);
            panel.Children.Add(BuildEquipmentInsertStatusBanner());

            StackPanel contentPanel = new StackPanel();
            contentPanel.SetBinding(UIElement.VisibilityProperty, new Binding("IsEquipmentSelectionExpanded")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            StackPanel defaultMessagePanel = new StackPanel();
            defaultMessagePanel.SetBinding(UIElement.VisibilityProperty, new Binding("ShowEquipmentDefaultMessage")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            defaultMessagePanel.Children.Add(BuildInfoBanner("Complete Size Evaluation to unlock equipment options."));
            Border placeholderBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 251, 254)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 219, 228)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 16, 12, 16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            placeholderBanner.Child = new TextBlock
            {
                Text = "Run Size Evaluation to view recommended equipment.",
                Foreground = new SolidColorBrush(Color.FromRgb(90, 98, 106)),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            defaultMessagePanel.Children.Add(placeholderBanner);
            contentPanel.Children.Add(defaultMessagePanel);

            StackPanel optionsPanel = new StackPanel();
            optionsPanel.SetBinding(UIElement.VisibilityProperty, new Binding("ShowEquipmentOptions")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            ItemsControl recommendedList = new ItemsControl();
            recommendedList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("RecommendedEquipmentOptions"));
            recommendedList.ItemTemplate = BuildEquipmentSelectionTemplate();
            optionsPanel.Children.Add(recommendedList);

            TextBlock optionalHeader = new TextBlock
            {
                Text = "OPTIONAL EQUIPMENT",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(99, 115, 129)),
                Margin = new Thickness(0, 4, 0, 8)
            };
            optionalHeader.SetBinding(UIElement.VisibilityProperty, new Binding("HasOptionalEquipment")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            optionsPanel.Children.Add(optionalHeader);

            ItemsControl optionalList = new ItemsControl();
            optionalList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("OptionalEquipmentOptions"));
            optionalList.ItemTemplate = BuildEquipmentSelectionTemplate();
            optionalList.SetBinding(UIElement.VisibilityProperty, new Binding("HasOptionalEquipment")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            optionsPanel.Children.Add(optionalList);
            contentPanel.Children.Add(optionsPanel);

            Button confirmButton = BuildPrimaryButton("Confirm Equipment", "ConfirmEquipmentCommand");
            confirmButton.Height = 38;
            confirmButton.Margin = new Thickness(0, 4, 0, 0);
            confirmButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            confirmButton.SetBinding(ContentControl.ContentProperty, new Binding("ConfirmEquipmentButtonText"));
            confirmButton.SetBinding(UIElement.IsEnabledProperty, new Binding("CanConfirmEquipment"));
            confirmButton.Style = BuildConfirmEquipmentButtonStyle();
            contentPanel.Children.Add(confirmButton);

            panel.Children.Add(contentPanel);
            return panel;
        }

        private static FrameworkElement BuildEquipmentInsertStatusBanner()
        {
            Border banner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 241, 242)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 241, 242)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };

            TextBlock text = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 90, 150)),
                TextWrapping = TextWrapping.Wrap
            };
            text.SetBinding(TextBlock.TextProperty, new Binding("EquipmentInsertStatusText"));
            banner.Child = text;

            banner.SetBinding(UIElement.VisibilityProperty, new Binding("IsEquipmentInsertStatusVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            return banner;
        }

        private static FrameworkElement BuildEquipmentSelectionCard()
        {
            FrameworkElement card = BuildSectionCard(null, BuildEquipmentSelectionSection(), false);
            card.SetBinding(UIElement.VisibilityProperty, new Binding("IsEquipmentSelectionVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            return card;
        }

        private static FrameworkElement BuildAhuSubModuleConfigurationCard()
        {
            Border selectedCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(217, 226, 236)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel selectedContent = new StackPanel();

            DockPanel selectedHeader = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 12)
            };

            Border placedTag = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(226, 246, 235)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(156, 215, 183)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            placedTag.SetValue(DockPanel.DockProperty, Dock.Right);
            placedTag.Child = new TextBlock
            {
                Text = "Placed",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(27, 124, 73))
            };
            selectedHeader.Children.Add(placedTag);

            Border validationTag = new Border
            {
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            validationTag.SetValue(DockPanel.DockProperty, Dock.Right);
            validationTag.SetBinding(Border.BackgroundProperty, new Binding("ConfirmedEquipmentValidationBadgeBackground"));
            validationTag.SetBinding(UIElement.VisibilityProperty, new Binding("HasConfirmedEquipmentValidationResult")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            validationTag.Child = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            ((TextBlock)validationTag.Child).SetBinding(TextBlock.TextProperty, new Binding("ConfirmedEquipmentValidationStatusText"));
            selectedHeader.Children.Add(validationTag);

            TextBlock equipmentName = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(18, 32, 46)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            };
            equipmentName.SetBinding(TextBlock.TextProperty, new Binding("ConfirmedEquipmentName"));
            selectedHeader.Children.Add(equipmentName);
            selectedContent.Children.Add(selectedHeader);

            selectedContent.Children.Add(BuildAhuCardBoundRow("Dimensions(mm):", "ConfirmedEquipmentDimensionsValueText"));
            selectedContent.Children.Add(BuildAhuCardAirflowWeightRow());
            selectedContent.Children.Add(BuildAhuCardBoundRow("Required Maintenance Space (mm):", "ConfirmedEquipmentMaintenanceSpaceValueText"));
            selectedContent.Children.Add(BuildAhuCardBoundRow("Clearance Check:", "ConfirmedEquipmentClearanceCheckText"));
            selectedContent.Children.Add(BuildAhuValidationReasonsList());

            Button changeButton = BuildPrimaryButton("Change Equipment", "ChangeConfirmedEquipmentCommand");
            changeButton.Height = 38;
            changeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            changeButton.Margin = new Thickness(0, 16, 0, 0);
            selectedContent.Children.Add(changeButton);

            selectedCard.Child = selectedContent;

            // Keep AhuSubModules populated in the ViewModel for internal route analysis,
            // but do not show the dynamic sub-module list in this card.
            selectedCard.SetBinding(UIElement.VisibilityProperty, new Binding("IsAhuSubModuleConfigurationVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            return selectedCard;
        }

        private static FrameworkElement BuildAhuValidationReasonsList()
        {
            ItemsControl list = new ItemsControl
            {
                Margin = new Thickness(0, 6, 0, 0)
            };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("ConfirmedEquipmentValidationReasons"));
            list.SetBinding(UIElement.VisibilityProperty, new Binding("HasConfirmedEquipmentValidationReasons")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            list.ItemTemplate = BuildValidationReasonTemplate();
            return list;
        }

        private static DataTemplate BuildValidationReasonTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
    <Border Background=""#FFF7ED""
            BorderBrush=""#FED7AA""
            BorderThickness=""1""
            CornerRadius=""6""
            Padding=""8,6""
            Margin=""0,0,0,6"">
        <DockPanel LastChildFill=""True"">
            <Grid DockPanel.Dock=""Left""
                  Width=""18""
                  Height=""18""
                  Margin=""0,0,6,0""
                  VerticalAlignment=""Top"">
                <Ellipse Stroke=""#D97706""
                         StrokeThickness=""1.4"" />
                <Rectangle Width=""1.6""
                           Height=""6""
                           Fill=""#D97706""
                           RadiusX=""0.8""
                           RadiusY=""0.8""
                           VerticalAlignment=""Top""
                           Margin=""0,3,0,0"" />
                <Ellipse Width=""2""
                         Height=""2""
                         Fill=""#D97706""
                         VerticalAlignment=""Bottom""
                         Margin=""0,0,0,3"" />
            </Grid>
            <TextBlock Text=""{Binding}""
                       Foreground=""#92400E""
                       FontSize=""11""
                       TextWrapping=""Wrap"" />
        </DockPanel>
    </Border>
</DataTemplate>";

            return (DataTemplate)XamlReader.Parse(xaml);
        }

        private static FrameworkElement BuildAhuCardBoundRow(string labelText, string bindingPath)
        {
            StackPanel row = BuildAhuCardInlineRow();
            row.Children.Add(BuildAhuCardLabel(labelText));

            TextBlock value = BuildAhuCardValue();
            value.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            row.Children.Add(value);
            return row;
        }

        private static FrameworkElement BuildAhuCardStaticRow(string labelText, string valueText)
        {
            StackPanel row = BuildAhuCardInlineRow();
            row.Children.Add(BuildAhuCardLabel(labelText));
            row.Children.Add(BuildAhuCardValue(valueText));
            return row;
        }

        private static FrameworkElement BuildAhuCardAirflowWeightRow()
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel airflow = BuildAhuCardInlineRow();
            airflow.Margin = new Thickness(0);
            airflow.Children.Add(BuildAhuCardLabel("Airflow Rate:"));
            TextBlock airflowValue = BuildAhuCardValue();
            airflowValue.SetBinding(TextBlock.TextProperty, new Binding("ConfirmedEquipmentAirflowValueText"));
            airflow.Children.Add(airflowValue);
            row.Children.Add(airflow);

            StackPanel weight = BuildAhuCardInlineRow();
            weight.Margin = new Thickness(12, 0, 0, 0);
            weight.Children.Add(BuildAhuCardLabel("Weight (kg):"));
            TextBlock weightValue = BuildAhuCardValue();
            weightValue.SetBinding(TextBlock.TextProperty, new Binding("ConfirmedEquipmentWeightValueText"));
            weight.Children.Add(weightValue);
            Grid.SetColumn(weight, 1);
            row.Children.Add(weight);

            return row;
        }

        private static StackPanel BuildAhuCardInlineRow()
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private static TextBlock BuildAhuCardLabel(string text)
        {
            return new TextBlock
            {
                Text = text + " ",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 67, 82)),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static TextBlock BuildAhuCardValue(string text = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 66)),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static void AddAhuSubModuleTableColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Star) });
        }

        private static Border BuildAhuSubModuleHeaderCell(string text, int column)
        {
            Border cell = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(235, 241, 247)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 215, 226)),
                BorderThickness = new Thickness(1, 1, column == 3 ? 1 : 0, 1),
                Padding = new Thickness(10, 8, 8, 8)
            };
            cell.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(42, 70, 95)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(cell, column);
            return cell;
        }

        private static FrameworkElement BuildConnectivityLayoutCard()
        {
            StackPanel panel = new StackPanel();

            DockPanel header = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            Button toggleButton = BuildSecondaryButton(string.Empty, "ToggleConnectivityAdvancedCommand");
            toggleButton.SetValue(DockPanel.DockProperty, Dock.Right);
            toggleButton.Padding = new Thickness(10, 0, 10, 0);
            toggleButton.SetBinding(ContentControl.ContentProperty, new Binding("ConnectivityAdvancedToggleText"));
            header.Children.Add(toggleButton);

            header.Children.Add(new TextBlock
            {
                Text = "Connectivity Layout",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(header);

            StackPanel contentPanel = new StackPanel();
            contentPanel.SetBinding(UIElement.VisibilityProperty, new Binding("IsConnectivityExpanded")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            contentPanel.Children.Add(BuildInfoBanner("Confirm equipment to unlock duct and Pipework configuration."));
            contentPanel.Children.Add(BuildDuctWorkConfigurationCard());
            contentPanel.Children.Add(BuildPipeWorkConfigurationCard());
            panel.Children.Add(contentPanel);

            FrameworkElement card = BuildSectionCard(null, panel, false);
            card.SetBinding(UIElement.VisibilityProperty, new Binding("IsConnectivityLayoutVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            return card;
        }

        private static FrameworkElement BuildDuctWorkConfigurationCard()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildConnectivitySubCardTitle("Ductwork Configuration"));
            panel.Children.Add(BuildConnectivityDropdownPair(
                "SAD Size:",
                "DuctWorkSizeOptions",
                "CurrentEditor.SelectedSadSize",
                "EditSadSizeCommand",
                null,
                "Wall ID:",
                "CurrentEditor.WallOptions",
                "CurrentEditor.SelectedSadWallOption",
                "DisplayName"));
            panel.Children.Add(BuildConnectivityDropdownPair(
                "RAD Size:",
                "DuctWorkSizeOptions",
                "CurrentEditor.SelectedRadSize",
                "EditRadSizeCommand",
                null,
                "Wall ID:",
                "CurrentEditor.WallOptions",
                "CurrentEditor.SelectedRadWallOption",
                "DisplayName"));

            panel.Children.Add(BuildGeneratedWorkButtonRow(
                "DuctWorkActionButtonText",
                "CreateDuctWorkCommand",
                "CanCreateDuctWork",
                "RemoveDuctWorkCommand",
                "CanRemoveDuctWork",
                "IsDuctWorkGenerated"));

            return BuildConnectivityInnerCard(panel);
        }

        private static FrameworkElement BuildPipeWorkConfigurationCard()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildConnectivitySubCardTitle("Pipework Configuration"));
            panel.Children.Add(BuildConnectivityDropdownPair(
                "CHWS Size:",
                "PipeWorkSizeOptions",
                "CurrentEditor.SelectedChwsPipeSize",
                "EditChwsPipeSizeCommand",
                null,
                "Wall ID:",
                "CurrentEditor.WallOptions",
                "CurrentEditor.SelectedChwsWallOption",
                "DisplayName"));
            panel.Children.Add(BuildConnectivityDropdownPair(
                "CHWR Size:",
                "PipeWorkSizeOptions",
                "CurrentEditor.SelectedChwrPipeSize",
                "EditChwrPipeSizeCommand",
                null,
                "Wall ID:",
                "CurrentEditor.WallOptions",
                "CurrentEditor.SelectedChwrWallOption",
                "DisplayName"));

            panel.Children.Add(BuildGeneratedWorkButtonRow(
                "PipeWorkActionButtonText",
                "CreatePipeWorkCommand",
                "CanCreatePipeWork",
                "RemovePipeWorkCommand",
                "CanRemovePipeWork",
                "IsPipeWorkGenerated"));

            return BuildConnectivityInnerCard(panel);
        }

        private static FrameworkElement BuildGeneratedWorkButtonRow(
            string actionTextBindingPath,
            string actionCommandPath,
            string actionEnabledBindingPath,
            string removeCommandPath,
            string removeEnabledBindingPath,
            string removeVisibilityBindingPath)
        {
            Grid grid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button actionButton = BuildPrimaryButton(string.Empty, actionCommandPath);
            actionButton.Height = 36;
            actionButton.Margin = new Thickness(0);
            actionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            actionButton.SetBinding(ContentControl.ContentProperty, new Binding(actionTextBindingPath));
            actionButton.SetBinding(UIElement.IsEnabledProperty, new Binding(actionEnabledBindingPath));
            actionButton.Style = BuildConfirmEquipmentButtonStyle();
            Grid.SetColumn(actionButton, 0);
            grid.Children.Add(actionButton);

            Button removeButton = BuildSecondaryButton("Remove", removeCommandPath);
            removeButton.Height = 36;
            removeButton.MinWidth = 88;
            removeButton.Margin = new Thickness(8, 0, 0, 0);
            removeButton.HorizontalAlignment = HorizontalAlignment.Right;
            removeButton.SetBinding(UIElement.IsEnabledProperty, new Binding(removeEnabledBindingPath));
            removeButton.SetBinding(UIElement.VisibilityProperty, new Binding(removeVisibilityBindingPath)
            {
                Converter = new BooleanToVisibilityConverter()
            });
            Grid.SetColumn(removeButton, 1);
            grid.Children.Add(removeButton);

            return grid;
        }

        private static TextBlock BuildConnectivitySubCardTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static Border BuildConnectivityInnerCard(FrameworkElement content)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(215, 224, 233)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 0),
                Child = content
            };
        }

        private static FrameworkElement BuildConnectivityDropdownPair(
            string leftLabel,
            string leftItemsPath,
            string leftSelectedPath,
            string leftEditCommandPath,
            string leftDisplayMemberPath,
            string rightLabel,
            string rightItemsPath,
            string rightSelectedPath,
            string rightDisplayMemberPath)
        {
            Grid grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock left = BuildConnectivityFieldLabel(leftLabel);
            Grid.SetRow(left, 0);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            TextBlock right = BuildConnectivityFieldLabel(rightLabel);
            Grid.SetRow(right, 0);
            Grid.SetColumn(right, 3);
            grid.Children.Add(right);

            System.Windows.Controls.ComboBox leftCombo = BuildBoundConnectivityComboBox(leftItemsPath, leftSelectedPath, leftDisplayMemberPath);
            Grid.SetRow(leftCombo, 1);
            Grid.SetColumn(leftCombo, 0);
            grid.Children.Add(leftCombo);

            Button editButton = BuildConnectivitySizeEditButton(leftEditCommandPath);
            Grid.SetRow(editButton, 1);
            Grid.SetColumn(editButton, 1);
            grid.Children.Add(editButton);

            System.Windows.Controls.ComboBox rightCombo = BuildBoundConnectivityComboBox(rightItemsPath, rightSelectedPath, rightDisplayMemberPath);
            Grid.SetRow(rightCombo, 1);
            Grid.SetColumn(rightCombo, 3);
            grid.Children.Add(rightCombo);

            return grid;
        }

        private static Button BuildConnectivitySizeEditButton(string commandPath)
        {
            Button button = new Button
            {
                Width = 30,
                Height = 36,
                Margin = new Thickness(6, 0, 0, 4),
                Padding = new Thickness(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "Add custom size",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            button.SetBinding(UIElement.IsEnabledProperty, new Binding("IsConnectivityUnlocked"));
            button.Content = new IconBlock
            {
                Icon = IconChar.Pencil,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return button;
        }

        private static TextBlock BuildConnectivityFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(99, 115, 129)),
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static System.Windows.Controls.ComboBox BuildBoundConnectivityComboBox(string itemsPath, string selectedPath, string displayMemberPath)
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable = false,
                Margin = new Thickness(0, 0, 0, 4)
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding(selectedPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            comboBox.SetBinding(UIElement.IsEnabledProperty, new Binding("IsConnectivityUnlocked"));
            if (!string.IsNullOrWhiteSpace(displayMemberPath))
            {
                comboBox.DisplayMemberPath = displayMemberPath;
            }

            return comboBox;
        }

        private static FrameworkElement BuildDeliveryRouteCard()
        {
            Border card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 233)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.White,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildDeliveryRouteHeader());

            StackPanel contentPanel = new StackPanel();
            contentPanel.Margin = new Thickness(0, 10, 0, 0);
            contentPanel.SetBinding(UIElement.VisibilityProperty, new Binding("IsDeliveryRouteExpanded")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            contentPanel.Children.Add(BuildDeliveryRouteInfoBanner());
            contentPanel.Children.Add(BuildDeliveryRouteStatusRow("Start Point:", "DeliveryStartPointName", "DeliveryStartPointStatus"));
            contentPanel.Children.Add(BuildDeliveryRouteStatusRow("Target Room:", "DeliveryTargetName", "DeliveryTargetStatus"));
            contentPanel.Children.Add(BuildDeliveryRouteLabel("Start Point"));
            contentPanel.Children.Add(BuildDeliveryStartLiftComboBox());
            contentPanel.Children.Add(BuildDeliveryActionButton("Define Start Point", "DefineDeliveryStartPointCommand", false));
            contentPanel.Children.Add(BuildDeliveryRouteLabel("Target Room"));
            contentPanel.Children.Add(BuildDeliveryTargetRoomTextBlock());
            contentPanel.Children.Add(BuildDeliveryRouteHintText());
            contentPanel.Children.Add(BuildDeliveryActionButton("Generate Delivery Route", "GenerateDeliveryRouteCommand", true));
            FrameworkElement resultCard = BuildDeliveryRouteResultCard();
            resultCard.SetBinding(UIElement.VisibilityProperty, new Binding("IsDeliveryRouteResultVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            contentPanel.Children.Add(resultCard);

            panel.Children.Add(contentPanel);
            card.Child = panel;
            card.SetBinding(UIElement.VisibilityProperty, new Binding("IsDeliveryRouteCardVisible")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            return card;
        }

        private static FrameworkElement BuildDeliveryRouteHeader()
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0)
            };

            Button toggleButton = BuildSecondaryButton(string.Empty, "ToggleDeliveryRouteCommand");
            toggleButton.SetValue(DockPanel.DockProperty, Dock.Right);
            toggleButton.Padding = new Thickness(10, 0, 10, 0);
            toggleButton.SetBinding(ContentControl.ContentProperty, new Binding("DeliveryRouteToggleText"));
            row.Children.Add(toggleButton);

            row.Children.Add(new TextBlock
            {
                Text = "Delivery Route",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return row;
        }

        private static FrameworkElement BuildDeliveryRouteInfoBanner()
        {
            Border banner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(215, 229, 245)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            banner.Child = new TextBlock
            {
                Text = "Generate the layout before defining transport points and creating demo routes.",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            return banner;
        }

        private static FrameworkElement BuildDeliveryRouteStatusRow(string labelText, string valueBindingPath, string statusBindingPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = labelText,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 76, 84)),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            TextBlock value = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            value.SetBinding(TextBlock.TextProperty, new Binding(valueBindingPath));
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            TextBlock status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(110, 140, 180)),
                VerticalAlignment = VerticalAlignment.Center
            };
            status.SetBinding(TextBlock.TextProperty, new Binding(statusBindingPath));
            Grid.SetColumn(status, 2);
            row.Children.Add(status);

            return row;
        }

        private static FrameworkElement BuildDeliveryRouteLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(99, 115, 129)),
                Margin = new Thickness(0, 10, 0, 6)
            };
        }

        private static System.Windows.Controls.ComboBox BuildDeliveryStartLiftComboBox()
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable = false,
                DisplayMemberPath = "DisplayName",
                Margin = new Thickness(0, 0, 0, 6)
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("EditorLiftOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedDeliveryStartLift")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return comboBox;
        }

        private static System.Windows.Controls.ComboBox BuildDeliveryTargetRoomComboBox()
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable = false,
                DisplayMemberPath = "RoomName",
                Margin = new Thickness(0, 0, 0, 6)
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("EditorRoomOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedDeliveryTargetRoom")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return comboBox;
        }

        private static FrameworkElement BuildDeliveryTargetRoomTextBlock()
        {
            Border box = new Border
            {
                MinHeight = 36,
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(185, 190, 198)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                Margin = new Thickness(0, 0, 0, 6)
            };

            TextBlock textBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                TextWrapping = TextWrapping.Wrap
            };
            textBlock.SetBinding(TextBlock.TextProperty, new Binding("DeliveryTargetName"));
            box.Child = textBlock;
            return box;
        }

        private static FrameworkElement BuildDeliveryRouteHintText()
        {
            TextBlock hint = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 10)
            };
            hint.SetBinding(TextBlock.TextProperty, new Binding("DeliveryRouteHintText"));
            return hint;
        }

        private static FrameworkElement BuildDeliveryRouteResultCard()
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 249, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 210, 245)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 14, 0, 0)
            };

            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Route Passed",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            TextBlock message = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 62, 80)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("DeliveryRouteResultMessage"));
            stack.Children.Add(message);

            stack.Children.Add(BuildDeliveryRouteResultField("Route Length:", "DeliveryRouteLengthText"));
            stack.Children.Add(BuildDeliveryRouteResultFixedField("Status:", "Passed"));
            stack.Children.Add(BuildDeliveryRouteResultField("Disassembly:", "DeliveryRouteDisassemblyText"));
            stack.Children.Add(BuildDeliveryRouteResultField("Max Dims:", "DeliveryRouteMaxDimsText"));

            card.Child = stack;
            return card;
        }

        private static FrameworkElement BuildDeliveryRouteResultField(string label, string bindingPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
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
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            valueBlock.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);

            return row;
        }

        private static FrameworkElement BuildDeliveryRouteResultFixedField(string label, string value)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
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
                Text = value,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);

            return row;
        }

        private static Button BuildDeliveryActionButton(string text, string commandPath, bool primary)
        {
            Button button = primary ? BuildPrimaryButton(text, commandPath) : BuildSecondaryButton(text, commandPath);
            button.Height = primary ? 42 : 38;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 2);
            if (primary)
            {
                button.Style = BuildConfirmEquipmentButtonStyle();
            }

            return button;
        }

        private static FrameworkElement BuildEditorFooter()
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            panel.Children.Add(BuildFooterButton("Cancel", "CancelEditorCommand", false));
            panel.Children.Add(BuildFooterButton("Save", "SaveSolutionCommand", false));
            panel.Children.Add(BuildFooterButton("Save & Submit", "SaveAndSubmitSolutionCommand", true));
            return panel;
        }

        private static FrameworkElement BuildSectionCard(string title, FrameworkElement content, bool includeTitle = true)
        {
            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 233)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12)
            };

            StackPanel panel = new StackPanel();
            if (includeTitle && !string.IsNullOrWhiteSpace(title))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }
            panel.Children.Add(content);
            card.Child = panel;
            return card;
        }


        private static FrameworkElement BuildLayoutPlansHeaderRow()
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.SetValue(DockPanel.DockProperty, Dock.Right);

            Button newButton = BuildSecondaryButton("+ New Layout", "NewSolutionCommand");
            newButton.Margin = new Thickness(8, 0, 0, 0);
            newButton.SetBinding(UIElement.VisibilityProperty, new Binding("NewLayoutButtonVisibility"));
            buttons.Children.Add(newButton);

            Button compareButton = BuildSecondaryButton("Compare Mode", "EnterLayoutCompareModeCommand");
            compareButton.Margin = new Thickness(8, 0, 0, 0);
            compareButton.MinWidth = 110;
            compareButton.Visibility = Visibility.Collapsed;
            buttons.Children.Add(compareButton);

            Button cancelButton = BuildSecondaryButton("Cancel", "CancelLayoutCompareModeCommand");
            cancelButton.Margin = new Thickness(8, 0, 0, 0);
            cancelButton.MinWidth = 88;
            cancelButton.SetBinding(UIElement.VisibilityProperty, new Binding("CancelCompareButtonVisibility"));
            buttons.Children.Add(cancelButton);

            Button doneButton = BuildSecondaryButton("Done", "FinishLayoutCompareModeCommand");
            doneButton.Margin = new Thickness(8, 0, 0, 0);
            doneButton.MinWidth = 96;
            doneButton.SetBinding(ContentControl.ContentProperty, new Binding("DoneCompareButtonText"));
            doneButton.SetBinding(UIElement.VisibilityProperty, new Binding("DoneCompareButtonVisibility"));
            buttons.Children.Add(doneButton);

            row.Children.Add(buttons);

            row.Children.Add(new TextBlock
            {
                Text = "Layout Plans",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return row;
        }

        private static FrameworkElement BuildSectionHeaderRow(string title, string commandPath, string buttonText)
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            Button actionButton = BuildSecondaryButton(buttonText, commandPath);
            actionButton.SetValue(DockPanel.DockProperty, Dock.Right);
            row.Children.Add(actionButton);

            row.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return row;
        }

        private static DataTemplate BuildLayoutPlanTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
              xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Border Background=""{Binding CompareCardBackground}""
            BorderBrush=""{Binding CompareBorderBrush}""
            BorderThickness=""1""
            CornerRadius=""8""
            Padding=""14""
            Margin=""0,0,0,12"">
        <StackPanel>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""*"" />
                    <ColumnDefinition Width=""Auto"" />
                    <ColumnDefinition Width=""Auto"" />
                </Grid.ColumnDefinitions>

                <TextBlock Grid.Column=""0""
                           Text=""{Binding PlanName}""
                           Foreground=""#111827""
                           FontSize=""16""
                           FontWeight=""Bold""
                           VerticalAlignment=""Center""
                           TextTrimming=""CharacterEllipsis""
                           Margin=""0,0,12,0"" />

                <Border Grid.Column=""1""
                        Background=""#F4F9FF""
                        BorderBrush=""#8FC2F4""
                        BorderThickness=""1""
                        CornerRadius=""12""
                        Padding=""10,3""
                        Margin=""0,0,8,0""
                        VerticalAlignment=""Center"">
                    <TextBlock Text=""{Binding LayoutType}""
                               Foreground=""#1667B7""
                               FontSize=""11""
                               VerticalAlignment=""Center"" />
                </Border>

                <Border Grid.Column=""2""
                        Background=""#F4F9FF""
                        BorderBrush=""#8FC2F4""
                        BorderThickness=""1""
                        CornerRadius=""12""
                        Padding=""10,3""
                        VerticalAlignment=""Center"">
                    <TextBlock Text=""{Binding EquipmentTypeTagText}""
                               Foreground=""#1667B7""
                               FontSize=""11""
                               VerticalAlignment=""Center"" />
                </Border>
            </Grid>

            <TextBlock Text=""{Binding CreatedAtText}""
                       Foreground=""#7D8794""
                       FontSize=""11""
                       Margin=""0,4,0,10"" />

            <Border Height=""1""
                    Background=""#DDE5EF""
                    Margin=""0,0,0,12"" />

            <Grid Margin=""0,0,0,10"">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""Auto"" />
                    <ColumnDefinition Width=""*"" />
                    <ColumnDefinition Width=""Auto"" />
                </Grid.ColumnDefinitions>

                <TextBlock Grid.Column=""0""
                           Text=""Equipment:""
                           FontWeight=""Bold""
                           Foreground=""#111827""
                           FontSize=""12""
                           Margin=""0,0,6,0"" />

                <TextBlock Grid.Column=""1""
                           Text=""{Binding ModelName}""
                           Foreground=""#111827""
                           FontSize=""12""
                           TextWrapping=""Wrap""
                           Margin=""0,0,8,0"" />

                <Border Grid.Column=""2""
                        Background=""{Binding EquipmentValidationBadgeBackground}""
                        CornerRadius=""10""
                        Padding=""8,2""
                        VerticalAlignment=""Center"">
                    <Border.Style>
                        <Style TargetType=""Border"">
                            <Setter Property=""Visibility"" Value=""Collapsed"" />
                            <Style.Triggers>
                                <DataTrigger Binding=""{Binding HasEquipmentValidationResult}"" Value=""True"">
                                    <Setter Property=""Visibility"" Value=""Visible"" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Border.Style>
                    <TextBlock Text=""{Binding EquipmentValidationStatusText}""
                               Foreground=""White""
                               FontWeight=""Bold""
                               FontSize=""10"" />
                </Border>
            </Grid>

            <Grid Margin=""0,0,0,12"">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""1*"" />
                    <ColumnDefinition Width=""1*"" />
                </Grid.ColumnDefinitions>

                <Grid Grid.Column=""0"" Margin=""0,0,12,0"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""Auto"" />
                        <ColumnDefinition Width=""*"" />
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Column=""0""
                               Text=""Plant Room:""
                               FontWeight=""Bold""
                               Foreground=""#111827""
                               FontSize=""12""
                               Margin=""0,0,6,0"" />

                    <TextBlock Grid.Column=""1""
                               Text=""{Binding PlantRoom}""
                               Foreground=""#111827""
                               FontSize=""12""
                               TextWrapping=""Wrap"" />
                </Grid>

                <Grid Grid.Column=""1"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""Auto"" />
                        <ColumnDefinition Width=""*"" />
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Column=""0""
                               Text=""Piping:""
                               FontWeight=""Bold""
                               Foreground=""#111827""
                               FontSize=""12""
                               Margin=""0,0,6,0"" />

                    <TextBlock Grid.Column=""1""
                               Text=""{Binding PipingStatus}""
                               Foreground=""{Binding PipingStatusForeground}""
                               FontSize=""12""
                               TextWrapping=""Wrap"" />
                </Grid>
            </Grid>

            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""Auto"" />
                    <ColumnDefinition Width=""*"" />
                    <ColumnDefinition Width=""Auto"" />
                    <ColumnDefinition Width=""Auto"" />
                </Grid.ColumnDefinitions>

                <Button Grid.Column=""0""
                        Content=""{Binding CompareButtonText}""
                        Command=""{Binding CompareCommand}""
                        CommandParameter=""{Binding}""
                        Visibility=""{Binding CompareButtonVisibility}""
                        IsEnabled=""{Binding IsCompareButtonEnabled}""
                        Width=""104""
                        Height=""40""
                        HorizontalAlignment=""Left""
                        Margin=""0,0,10,0""
                        Background=""{Binding CompareButtonBackground}""
                        Foreground=""{Binding CompareButtonForeground}""
                        BorderBrush=""#D0D6DC""
                        FontSize=""14""
                        FontWeight=""Bold"" />

                <Button Grid.Column=""1""
                        Content=""Detail""
                        Command=""{Binding DetailCommand}""
                        CommandParameter=""{Binding}""
                        Height=""40""
                        HorizontalAlignment=""Stretch""
                        Background=""#1667B7""
                        Foreground=""White""
                        BorderBrush=""#1667B7""
                        FontSize=""14""
                        FontWeight=""Bold"" />

                <Button Grid.Column=""2""
                        Content=""Export""
                        Command=""{Binding ExportCommand}""
                        CommandParameter=""{Binding}""
                        Width=""96""
                        Height=""40""
                        Margin=""10,0,0,0""
                        HorizontalAlignment=""Right""
                        Background=""White""
                        Foreground=""#1667B7""
                        BorderBrush=""#D0D6DC""
                        FontSize=""14""
                        FontWeight=""Normal"" />

                <Button Grid.Column=""3""
                        Content=""Delete""
                        Command=""{Binding DeleteCommand}""
                        CommandParameter=""{Binding}""
                        Width=""104""
                        Height=""40""
                        Margin=""10,0,0,0""
                        HorizontalAlignment=""Right""
                        Background=""White""
                        Foreground=""#B42318""
                        BorderBrush=""#D0D6DC""
                        FontSize=""14""
                        FontWeight=""Normal"" />
            </Grid>
        </StackPanel>
    </Border>
</DataTemplate>";

            return (DataTemplate)XamlReader.Parse(xaml);
        }

        private static FrameworkElementFactory BuildTemplateText(string format, string bindingPath)
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(bindingPath)
            {
                StringFormat = format
            });
            text.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 4));
            text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(68, 76, 84)));
            return text;
        }

        private static FrameworkElementFactory BuildTemplateButton(string content, string commandPath)
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.ContentProperty, content);
            button.SetValue(Button.HeightProperty, 28.0);
            button.SetValue(Button.PaddingProperty, new Thickness(12, 0, 12, 0));
            button.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(208, 214, 220)));
            button.SetValue(Button.BackgroundProperty, Brushes.White);
            button.SetValue(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(54, 63, 72)));
            button.SetBinding(ButtonBase.CommandProperty, new Binding(commandPath));
            button.SetBinding(ButtonBase.CommandParameterProperty, new Binding());
            return button;
        }

        private static DataTemplate BuildEquipmentSelectionTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
              xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Button Command=""{Binding SelectCommand}""
            CommandParameter=""{Binding}""
            Background=""White""
            BorderBrush=""#E0E0E0""
            BorderThickness=""1""
            Padding=""0""
            Margin=""0,0,0,10""
            Cursor=""Hand""
            HorizontalContentAlignment=""Stretch""
            VerticalContentAlignment=""Stretch"">
        <Button.Template>
            <ControlTemplate TargetType=""Button"">
                <Border x:Name=""CardBorder""
                        Background=""White""
                        BorderBrush=""#E0E0E0""
                        BorderThickness=""1""
                        CornerRadius=""8""
                        Padding=""14"">
                    <StackPanel>
                        <DockPanel LastChildFill=""True"">
                            <Border x:Name=""SelectBadge""
                                    DockPanel.Dock=""Right""
                                    Background=""{Binding ValidationBadgeBackground}""
                                    BorderBrush=""{Binding ValidationBadgeBackground}""
                                    BorderThickness=""1""
                                    CornerRadius=""6""
                                    Padding=""12,6""
                                    Margin=""10,0,0,0"">
                                <TextBlock Text=""{Binding SelectButtonText}""
                                           Foreground=""White""
                                           FontWeight=""Bold""
                                           FontSize=""12""
                                           VerticalAlignment=""Center"" />
                            </Border>
                            <TextBlock Text=""{Binding DisplayName}""
                                       Foreground=""#111827""
                                       FontSize=""13""
                                       FontWeight=""SemiBold""
                                       TextWrapping=""Wrap""
                                       VerticalAlignment=""Center"" />
                        </DockPanel>
                        <TextBlock Text=""{Binding SizeText}""
                                   Margin=""0,8,0,0""
                                   Foreground=""#5A626A""
                                   FontSize=""12""
                                   TextWrapping=""Wrap"" />
                        <ItemsControl x:Name=""ValidationReasonsList""
                                      ItemsSource=""{Binding ValidationReasons}""
                                      Margin=""0,10,0,0""
                                      Visibility=""Collapsed"">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Background=""#FFF7ED""
                                            BorderBrush=""#FED7AA""
                                            BorderThickness=""1""
                                            CornerRadius=""6""
                                            Padding=""8,6""
                                            Margin=""0,0,0,6"">
                                        <DockPanel LastChildFill=""True"">
                                            <Grid DockPanel.Dock=""Left""
                                                  Width=""18""
                                                  Height=""18""
                                                  Margin=""0,0,6,0""
                                                  VerticalAlignment=""Top"">
                                                <Ellipse Stroke=""#D97706""
                                                         StrokeThickness=""1.4"" />
                                                <Rectangle Width=""1.6""
                                                           Height=""6""
                                                           Fill=""#D97706""
                                                           RadiusX=""0.8""
                                                           RadiusY=""0.8""
                                                           VerticalAlignment=""Top""
                                                           Margin=""0,3,0,0"" />
                                                <Ellipse Width=""2""
                                                         Height=""2""
                                                         Fill=""#D97706""
                                                         VerticalAlignment=""Bottom""
                                                         Margin=""0,0,0,3"" />
                                            </Grid>
                                            <TextBlock Text=""{Binding}""
                                                       Foreground=""#92400E""
                                                       FontSize=""11""
                                                       TextWrapping=""Wrap"" />
                                        </DockPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <!-- Customer-approved violation content is shown only in
                             ValidationReasonsList above.  Detailed diagnostic-only
                             values are written to the placement violation log. -->
                    </StackPanel>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                        <Setter TargetName=""CardBorder"" Property=""BorderBrush"" Value=""#9CC2EA"" />
                    </Trigger>
                    <DataTrigger Binding=""{Binding IsSelected}"" Value=""True"">
                        <Setter TargetName=""CardBorder"" Property=""Background"" Value=""#E1EFFA"" />
                        <Setter TargetName=""CardBorder"" Property=""BorderBrush"" Value=""#5EA0DB"" />
                    </DataTrigger>
                    <DataTrigger Binding=""{Binding HasValidationReasons}"" Value=""True"">
                        <Setter TargetName=""ValidationReasonsList"" Property=""Visibility"" Value=""Visible"" />
                    </DataTrigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Button.Template>
    </Button>
</DataTemplate>";

            return (DataTemplate)XamlReader.Parse(xaml);
        }

        private static DataTemplate BuildAhuSubModuleRowTemplate()
        {
            const string xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
              xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width=""20*"" />
            <ColumnDefinition Width=""35*"" />
            <ColumnDefinition Width=""35*"" />
            <ColumnDefinition Width=""10*"" />
        </Grid.ColumnDefinitions>
        <Border Grid.Column=""0"" BorderBrush=""#CDD7E2"" BorderThickness=""1,0,0,1"" Padding=""10,9,8,9"">
            <TextBlock Text=""{Binding SubModule}"" TextWrapping=""Wrap"" Foreground=""#111827"" FontSize=""12"" />
        </Border>
        <Border Grid.Column=""1"" BorderBrush=""#CDD7E2"" BorderThickness=""1,0,0,1"" Padding=""10,9,8,9"">
            <TextBlock Text=""{Binding Type}"" TextWrapping=""Wrap"" Foreground=""#111827"" FontSize=""12"" />
        </Border>
        <Border Grid.Column=""2"" BorderBrush=""#CDD7E2"" BorderThickness=""1,0,0,1"" Padding=""10,9,8,9"">
            <TextBlock Text=""{Binding DimensionsMm}"" TextWrapping=""Wrap"" Foreground=""#111827"" FontSize=""12"" />
        </Border>
        <Border Grid.Column=""3"" BorderBrush=""#CDD7E2"" BorderThickness=""1,0,1,1"" Padding=""8,9,8,9"">
            <TextBlock Text=""{Binding Seq}"" TextAlignment=""Center"" Foreground=""#111827"" FontSize=""12"" />
        </Border>
    </Grid>
</DataTemplate>";

            return (DataTemplate)XamlReader.Parse(xaml);
        }

        private static TextBlock BuildEditorLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = new SolidColorBrush(Color.FromRgb(68, 76, 84)),
                FontWeight = FontWeights.SemiBold
            };
        }

        private static System.Windows.Controls.TextBox BuildEditorTextBox(string bindingPath)
        {
            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
            {
                Height = 30,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            textBox.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return textBox;
        }

        private static System.Windows.Controls.ComboBox BuildEditorComboBox(string bindingPath, params string[] items)
        {
            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 30,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
                ItemsSource = items,
                IsEditable = false
            };
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return comboBox;
        }

        private static FrameworkElement BuildPlaceholderStatusRow(string statusText, string commandPath, string buttonText)
        {
            DockPanel panel = new DockPanel
            {
                Margin = new Thickness(0, 6, 0, 10)
            };

            Button button = BuildSecondaryButton(buttonText, commandPath);
            button.SetValue(DockPanel.DockProperty, Dock.Right);
            panel.Children.Add(button);

            panel.Children.Add(new TextBlock
            {
                Text = statusText,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 98, 106))
            });

            return panel;
        }

        private static FrameworkElement BuildBoundStatusRow(string textPath, string commandPath, string buttonText, string format)
        {
            DockPanel panel = new DockPanel
            {
                Margin = new Thickness(0, 6, 0, 10)
            };

            Button button = BuildSecondaryButton(buttonText, commandPath);
            button.SetValue(DockPanel.DockProperty, Dock.Right);
            panel.Children.Add(button);

            TextBlock textBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 98, 106))
            };
            textBlock.SetBinding(TextBlock.TextProperty, new Binding(textPath)
            {
                StringFormat = format
            });
            panel.Children.Add(textBlock);

            return panel;
        }

        private static Border BuildInfoBanner(string text)
        {
            Border border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(246, 248, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 233)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            border.Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 98, 106))
            };
            return border;
        }

        private static Button BuildPrimaryButton(string text, string commandPath)
        {
            Button button = new Button
            {
                Content = text,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(76, 142, 214)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 142, 214)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static Style BuildConfirmEquipmentButtonStyle()
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.HeightProperty, 38.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 105, 190))));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(30, 105, 190))));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildFlatConfirmButtonTemplate()));

            Trigger disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            // Disabled state: lighter blue plus slight opacity so it reads as locked.
            disabledTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(126, 174, 219))));
            disabledTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(126, 174, 219))));
            disabledTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
            disabledTrigger.Setters.Add(new Setter(Control.CursorProperty, Cursors.Arrow));
            style.Triggers.Add(disabledTrigger);

            return style;
        }

        private static ControlTemplate BuildFlatConfirmButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));

            FrameworkElementFactory contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));

            border.AppendChild(contentPresenter);
            template.VisualTree = border;
            return template;
        }

        private static Button BuildSecondaryButton(string text, string commandPath)
        {
            Button button = new Button
            {
                Content = text,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(208, 214, 220)),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(54, 63, 72))
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static Button BuildFooterButton(string text, string commandPath, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Width = string.Equals(text, "Save & Submit", StringComparison.OrdinalIgnoreCase) ? 128 : 96,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = primary ? new SolidColorBrush(Color.FromRgb(76, 142, 214)) : Brushes.White,
                BorderBrush = primary ? new SolidColorBrush(Color.FromRgb(76, 142, 214)) : new SolidColorBrush(Color.FromRgb(208, 214, 220)),
                Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(54, 63, 72))
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static DataTemplate BuildFamilyOptionTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("IsHighlighted")
            {
                Converter = new HighlightToBackgroundConverter()
            });
            border.SetBinding(Border.BorderBrushProperty, new Binding("IsHighlighted")
            {
                Converter = new HighlightToBorderBrushConverter()
            });
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8));

            FrameworkElementFactory row = new FrameworkElementFactory(typeof(DockPanel));

            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.ContentProperty, Loc.T("DockablePane.RoomDetail.CustomFamily.SetButton"));
            button.SetValue(Button.WidthProperty, 78.0);
            button.SetValue(Button.HeightProperty, 28.0);
            button.SetValue(Button.MarginProperty, new Thickness(8, 0, 0, 0));
            button.SetValue(DockPanel.DockProperty, Dock.Right);
            button.SetBinding(ButtonBase.CommandProperty, new Binding("SetCommand"));
            row.AppendChild(button);

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(40, 40, 40)));
            name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            name.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
            row.AppendChild(name);

            border.AppendChild(row);
            return new DataTemplate { VisualTree = border };
        }

        private static FrameworkElement BuildPipeAndDuctCard()
        {
            Border card = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.White,
                Padding = new Thickness(12)
            };

            StackPanel panel = new StackPanel();
            panel.Children.Add(BuildFlowRateRow());
            panel.Children.Add(BuildSectionHeader("DockablePane.RoomDetail.PipeSelection.Title"));
            panel.Children.Add(BuildSectionHeader("DockablePane.RoomDetail.PipeSelection.ChilledWater"));
            panel.Children.Add(BuildSingleInputRow("DockablePane.RoomDetail.PipeSelection.ChilledWaterSupply", "ChilledWaterSupplyText"));
            panel.Children.Add(BuildSingleInputRow("DockablePane.RoomDetail.PipeSelection.ChilledWaterReturn", "ChilledWaterReturnText"));
            panel.Children.Add(BuildSectionHeader("DockablePane.RoomDetail.PipeSelection.HotWater"));
            panel.Children.Add(BuildSingleInputRow("DockablePane.RoomDetail.PipeSelection.HotWaterSupply", "HotWaterSupplyText"));
            panel.Children.Add(BuildSingleInputRow("DockablePane.RoomDetail.PipeSelection.HotWaterReturn", "HotWaterReturnText"));
            panel.Children.Add(BuildSectionHeader("DockablePane.RoomDetail.DuctSelection.Title"));
            panel.Children.Add(BuildDoubleInputRow("SAD", "SadText1", "SadText2"));
            panel.Children.Add(BuildDoubleInputRow("RAD", "RadText1", "RadText2"));
            panel.Children.Add(BuildDoubleInputRow("FAD", "FadText1", "FadText2"));

            card.Child = panel;
            return card;
        }

        private static FrameworkElement BuildFlowRateRow()
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = "Flow Rate",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
            {
                Height = 28,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FlowRateOptions"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedFlowRate")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(comboBox, 1);
            row.Children.Add(comboBox);
            return row;
        }

        private static FrameworkElement BuildSectionHeader(string resourceKey)
        {
            return new TextBlock
            {
                Text = Loc.T(resourceKey),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static FrameworkElement BuildSingleInputRow(string labelKey, string bindingPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = Loc.T(labelKey),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66))
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            System.Windows.Controls.TextBox textBox = BuildTextBox(bindingPath);
            Grid.SetColumn(textBox, 1);
            row.Children.Add(textBox);
            return row;
        }

        private static FrameworkElement BuildDoubleInputRow(string labelText, string bindingPath1, string bindingPath2)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = labelText + ":",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66))
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            System.Windows.Controls.TextBox textBox1 = BuildTextBox(bindingPath1);
            Grid.SetColumn(textBox1, 1);
            row.Children.Add(textBox1);

            System.Windows.Controls.TextBox textBox2 = BuildTextBox(bindingPath2);
            Grid.SetColumn(textBox2, 3);
            row.Children.Add(textBox2);
            return row;
        }

        private static System.Windows.Controls.TextBox BuildTextBox(string bindingPath)
        {
            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
            {
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            textBox.SetBinding(System.Windows.Controls.TextBox.TextProperty, new Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return textBox;
        }

        private static FrameworkElement BuildPair(int rowIndex, string labelText, string valueBindingPath)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(0, 6, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(row, rowIndex);

            TextBlock label = new TextBlock
            {
                Text = ResolveLocalizedLabel(labelText, valueBindingPath) + ":",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66))
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            TextBlock value = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40))
            };
            value.SetBinding(TextBlock.TextProperty, new Binding(valueBindingPath));
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            return row;
        }

        private static string ResolveLocalizedLabel(string fallback, string valueBindingPath)
        {
            switch (valueBindingPath ?? string.Empty)
            {
                case "RoomName":
                    return Loc.T("DockablePane.RoomDetail.Label.Name");
                case "TargetRoomType":
                    return Loc.T("DockablePane.RoomDetail.Label.Type");
                case "AreaText":
                    return Loc.T("DockablePane.RoomDetail.Label.Area");
                case "LevelText":
                    return Loc.T("DockablePane.RoomDetail.Label.Level");
                case "StatusText":
                    return Loc.T("DockablePane.RoomDetail.Label.Status");
                case "BoundaryLayersText":
                    return Loc.T("DockablePane.RoomDetail.Label.Boundary");
                case "RoomKeyText":
                    return Loc.T("DockablePane.RoomDetail.Label.Key");
                case "CloseGapText":
                    return Loc.T("DockablePane.RoomDetail.Label.CloseGap");
                default:
                    return fallback ?? string.Empty;
            }
        }

        private sealed class ZeroCountToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new System.NotSupportedException();
            }
        }

        private sealed class PageModeToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (!(value is RoomDetailPageMode currentMode) || !(parameter is RoomDetailPageMode expectedMode))
                {
                    return Visibility.Collapsed;
                }

                return currentMode == expectedMode ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class HighlightToBackgroundConverter : IValueConverter
        {
            private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(225, 239, 250));
            private static readonly Brush NormalBrush = Brushes.White;

            public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return value is bool active && active ? ActiveBrush : NormalBrush;
            }

            public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new System.NotSupportedException();
            }
        }

        private sealed class HighlightToBorderBrushConverter : IValueConverter
        {
            private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(94, 160, 219));
            private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224));

            public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return value is bool active && active ? ActiveBrush : NormalBrush;
            }

            public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new System.NotSupportedException();
            }
        }
    }
}
