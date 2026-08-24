using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    internal sealed class CreateLayoutContextWindow : Window
    {
        private readonly Border _newBuildingCard;
        private readonly Border _rmaaCard;
        private readonly Border _ahuCard;
        private readonly Border _pauCard;
        private readonly RadioButton _ahuRadio;
        private readonly RadioButton _pauRadio;

        public string SelectedPlanningContext { get; private set; } = "New Building Design";

        public string SelectedEquipmentType { get; private set; } = "AHU";

        public CreateLayoutContextWindow()
        {
            Title = "Create AHU / PAU Layout";
            Width = 900;
            Height = 430;
            MinWidth = 860;
            MinHeight = 400;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;

            Grid root = new Grid
            {
                Margin = new Thickness(22, 22, 22, 18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock intro = new TextBlock
            {
                Text = "Select the planning context before the 3D workspace opens. This controls whether room bounds can be resized.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(intro, 0);
            root.Children.Add(intro);

            Grid planningGrid = CreateTwoColumnGrid(new Thickness(0, 0, 0, 12));
            _newBuildingCard = CreatePlanningCard(
                "New Building Design",
                "Allows spatial resizing and dynamic room-bound adjustments.",
                () =>
                {
                    SelectedPlanningContext = "New Building Design";
                    UpdateVisualState();
                });
            _rmaaCard = CreatePlanningCard(
                "RMAA / Replacement",
                "Uses immutable room bounds and filters equipment to fit existing constraints.",
                () =>
                {
                    SelectedPlanningContext = "RMAA / Replacement";
                    UpdateVisualState();
                });
            Grid.SetColumn(_newBuildingCard, 0);
            Grid.SetColumn(_rmaaCard, 2);
            planningGrid.Children.Add(_newBuildingCard);
            planningGrid.Children.Add(_rmaaCard);
            Grid.SetRow(planningGrid, 1);
            root.Children.Add(planningGrid);

            TextBlock equipmentTitle = new TextBlock
            {
                Text = "Select Equipment Type",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(equipmentTitle, 2);
            root.Children.Add(equipmentTitle);

            Grid equipmentGrid = CreateTwoColumnGrid(new Thickness(0, 0, 0, 12));
            _ahuRadio = new RadioButton { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            _pauRadio = new RadioButton { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            _ahuCard = CreateEquipmentCard(_ahuRadio, "Air Handling Unit (AHU)", () =>
            {
                SelectedEquipmentType = "AHU";
                UpdateVisualState();
            });
            _pauCard = CreateEquipmentCard(_pauRadio, "Primary Air Handling Unit (PAU)", () =>
            {
                SelectedEquipmentType = "PAU";
                UpdateVisualState();
            });
            Grid.SetColumn(_ahuCard, 0);
            Grid.SetColumn(_pauCard, 2);
            equipmentGrid.Children.Add(_ahuCard);
            equipmentGrid.Children.Add(_pauCard);
            Grid.SetRow(equipmentGrid, 3);
            root.Children.Add(equipmentGrid);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 110,
                Height = 30,
                IsCancel = true,
                Margin = new Thickness(0, 0, 10, 0)
            };
            cancelButton.Click += (sender, args) =>
            {
                DialogResult = false;
                Close();
            };

            Button initializeButton = new Button
            {
                Content = "Initialize Workspace",
                Width = 160,
                Height = 30,
                IsDefault = true
            };
            initializeButton.Click += (sender, args) =>
            {
                DialogResult = true;
                Close();
            };

            footer.Children.Add(cancelButton);
            footer.Children.Add(initializeButton);
            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            Content = root;
            UpdateVisualState();
        }

        private static Grid CreateTwoColumnGrid(Thickness margin)
        {
            Grid grid = new Grid
            {
                Margin = margin
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private Border CreatePlanningCard(string title, string description, Action onClick)
        {
            Border card = new Border
            {
                Height = 150,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(22, 18, 22, 16),
                Background = Brushes.White,
                Cursor = Cursors.Hand
            };

            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 40, 58)),
                Margin = new Thickness(0, 0, 0, 28)
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 100, 120)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21
            });

            card.Child = stack;
            card.MouseLeftButtonUp += (sender, args) => onClick();
            return card;
        }

        private Border CreateEquipmentCard(RadioButton radioButton, string title, Action onClick)
        {
            Border card = new Border
            {
                Height = 64,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(18, 0, 18, 0),
                Background = Brushes.White,
                Cursor = Cursors.Hand
            };

            StackPanel row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(radioButton);
            row.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 40, 58)),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            card.Child = row;
            card.MouseLeftButtonUp += (sender, args) => onClick();
            return card;
        }

        private void UpdateVisualState()
        {
            ApplyCardState(_newBuildingCard, SelectedPlanningContext == "New Building Design");
            ApplyCardState(_rmaaCard, SelectedPlanningContext == "RMAA / Replacement");
            ApplyCardState(_ahuCard, SelectedEquipmentType == "AHU");
            ApplyCardState(_pauCard, SelectedEquipmentType == "PAU");

            _ahuRadio.IsChecked = SelectedEquipmentType == "AHU";
            _pauRadio.IsChecked = SelectedEquipmentType == "PAU";
        }

        private static void ApplyCardState(Border card, bool selected)
        {
            card.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(45, 125, 245))
                : new SolidColorBrush(Color.FromRgb(220, 226, 232));
            card.Background = selected
                ? new SolidColorBrush(Color.FromRgb(245, 249, 255))
                : Brushes.White;
        }
    }
}
