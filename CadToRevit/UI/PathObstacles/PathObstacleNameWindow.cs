using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleNameWindow : Window
    {
        private readonly WpfTextBox _nameTextBox;
        private readonly string _defaultName;

        public string ObstacleName { get; private set; }

        public PathObstacleNameWindow(UIApplication uiApp, string defaultName)
        {
            _defaultName = string.IsNullOrWhiteSpace(defaultName) ? "Obstacle 001" : defaultName.Trim();
            ObstacleName = _defaultName;

            Title = "Save Path Obstacle";
            Width = 420;
            Height = 210;
            MinWidth = 380;
            MinHeight = 190;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            AttachOwner(uiApp);

            Grid root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = "Obstacle Name:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(label, 0);
            root.Children.Add(label);

            _nameTextBox = new WpfTextBox
            {
                Text = _defaultName,
                FontSize = 14,
                Height = 32,
                Padding = new Thickness(8, 4, 8, 4)
            };
            Grid.SetRow(_nameTextBox, 1);
            root.Children.Add(_nameTextBox);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            buttons.Children.Add(CreateButton("Save", Save));
            buttons.Children.Add(CreateButton("Cancel", Cancel));

            Content = root;
            Loaded += delegate
            {
                _nameTextBox.Focus();
                _nameTextBox.SelectAll();
            };
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            string value = (_nameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                PathObstacleNoticeWindow.Show("Save Path Obstacle", "Obstacle name cannot be empty.");
                return;
            }

            ObstacleName = value;
            DialogResult = true;
            Close();
        }

        private void Cancel(object sender, RoutedEventArgs e)
        {
            ObstacleName = _defaultName;
            DialogResult = false;
            Close();
        }

        private static Button CreateButton(string text, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                Width = 96,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0)
            };
            button.Click += handler;
            return button;
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
    }
}
