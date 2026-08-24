using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleConfirmWindow : Window
    {
        private PathObstacleConfirmWindow(string title, string message)
        {
            Title = "EMSD AI Tool";
            Width = 520;
            Height = 250;
            MinWidth = 460;
            MinHeight = 230;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            AllowsTransparency = false;
            Background = Brushes.White;

            Border shell = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(208, 213, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0)
            };

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel body = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 22)
            };
            Grid.SetRow(body, 0);

            body.Children.Add(new TextBlock
            {
                Text = title ?? string.Empty,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            body.Children.Add(new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
            });
            layout.Children.Add(body);

            Border footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(225, 228, 232)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(28, 18, 28, 18)
            };
            Grid.SetRow(footer, 1);

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button cancel = CreateButton("Cancel", false);
            cancel.IsCancel = true;
            cancel.Click += delegate
            {
                DialogResult = false;
                Close();
            };
            Grid.SetColumn(cancel, 0);
            buttons.Children.Add(cancel);

            Button confirm = CreateButton("Confirm", true);
            confirm.IsDefault = true;
            confirm.Click += delegate
            {
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(confirm, 2);
            buttons.Children.Add(confirm);

            footer.Child = buttons;
            layout.Children.Add(footer);

            shell.Child = layout;
            Content = shell;
        }

        public static bool Confirm(string title, string message)
        {
            PathObstacleConfirmWindow window = new PathObstacleConfirmWindow(title, message);
            return window.ShowDialog() == true;
        }

        private static Button CreateButton(string text, bool primary)
        {
            Color blue = Color.FromRgb(23, 112, 196);
            return new Button
            {
                Content = text,
                Height = 38,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Background = primary ? new SolidColorBrush(blue) : Brushes.White,
                Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                BorderBrush = primary
                    ? new SolidColorBrush(blue)
                    : new SolidColorBrush(Color.FromRgb(214, 219, 226)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
        }
    }
}