using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleNoticeWindow : Window
    {
        private PathObstacleNoticeWindow(string title, string message)
        {
            Title = title;
            Width = 420;
            Height = 190;
            MinWidth = 360;
            MinHeight = 170;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.White;

            Grid root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock text = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, 0);
            root.Children.Add(text);

            Button ok = new Button
            {
                Content = "OK",
                Width = 96,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                IsCancel = true
            };
            ok.Click += delegate { Close(); };
            Grid.SetRow(ok, 1);
            root.Children.Add(ok);

            Content = root;
        }

        public static void Show(string title, string message)
        {
            PathObstacleNoticeWindow window = new PathObstacleNoticeWindow(title, message);
            window.ShowDialog();
        }
    }
}
