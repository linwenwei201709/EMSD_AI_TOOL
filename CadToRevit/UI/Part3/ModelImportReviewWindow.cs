using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.Part3
{
    public class ModelImportReviewWindow : Window
    {
        private const string WindowTitle = "EMSD AI Tool";

        public ModelImportReviewWindow(UIApplication uiApp, string modelName)
        {
            Title = WindowTitle;
            Width = 760;
            Height = 360;
            MinWidth = 640;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.White;

            AttachOwner(uiApp);
            Content = BuildContent(modelName);
        }

        private UIElement BuildContent(string modelName)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(24)
            };

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border iconBorder = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = new SolidColorBrush(Color.FromRgb(33, 137, 220)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            iconBorder.Child = new TextBlock
            {
                Text = "i",
                Foreground = Brushes.White,
                FontSize = 42,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Light
            };
            Grid.SetColumn(iconBorder, 0);
            Grid.SetRow(iconBorder, 0);
            root.Children.Add(iconBorder);

            StackPanel contentPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            Grid.SetColumn(contentPanel, 1);
            Grid.SetRow(contentPanel, 0);
            root.Children.Add(contentPanel);

            contentPanel.Children.Add(new TextBlock
            {
                Text = "Review the active model before starting Part 3 planning.",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 18)
            });

            contentPanel.Children.Add(MakeHeader("Model Name"));
            contentPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(modelName) ? "(Unknown)" : modelName,
                FontSize = 15,
                Margin = new Thickness(0, 4, 0, 18)
            });

            contentPanel.Children.Add(MakeHeader("Rooms Detected"));
            contentPanel.Children.Add(new TextBlock
            {
                Text = "2 Rooms:",
                FontSize = 15,
                Margin = new Thickness(0, 4, 0, 8)
            });
            contentPanel.Children.Add(new TextBlock
            {
                Text = "a. AHU Room",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            });
            contentPanel.Children.Add(new TextBlock
            {
                Text = "b. Lift Lobby",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 18)
            });

            Border noteBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 235)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(247, 251, 255)),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 8)
            };
            noteBorder.Child = new TextBlock
            {
                Text = "You can use the Room Creation Tool to manage the AHU/PAU Room or Lift Lobbies.",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            contentPanel.Children.Add(noteBorder);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 22, 0, 0)
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 120,
                Height = 36,
                Margin = new Thickness(0, 0, 12, 0),
                IsCancel = true
            };
            cancelButton.Click += delegate
            {
                DialogResult = false;
                Close();
            };

            Button importButton = new Button
            {
                Content = "Import",
                Width = 120,
                Height = 36,
                IsDefault = true
            };
            importButton.Click += delegate
            {
                DialogResult = true;
                Close();
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(importButton);

            Grid.SetColumn(buttonPanel, 1);
            Grid.SetRow(buttonPanel, 1);
            root.Children.Add(buttonPanel);

            return root;
        }

        private void AttachOwner(UIApplication uiApp)
        {
            IntPtr ownerHandle = uiApp != null ? uiApp.MainWindowHandle : IntPtr.Zero;
            if (ownerHandle == IntPtr.Zero)
            {
                return;
            }

            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.Owner = ownerHandle;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private static TextBlock MakeHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0)
            };
        }
    }
}
