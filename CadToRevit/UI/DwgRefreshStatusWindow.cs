using CadToRevit.Infrastructure.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI
{
    internal sealed class DwgRefreshStatusWindow : Window
    {
        private DwgRefreshStatusWindow(string message)
        {
            Title = Loc.T("Dialog.DwgRefresh.Title");
            Width = 520;
            Height = 230;
            MinWidth = 420;
            MinHeight = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;

            Grid root = new Grid
            {
                Margin = new Thickness(20)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border icon = CreateIcon(message);
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);

            TextBlock content = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(content, 1);
            contentGrid.Children.Add(content);
            Grid.SetRow(contentGrid, 0);
            root.Children.Add(contentGrid);

            Button closeButton = new Button
            {
                Content = Loc.T("Common.Close"),
                Width = 110,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            closeButton.Click += (sender, args) => Close();
            Grid.SetRow(closeButton, 1);
            root.Children.Add(closeButton);

            Content = root;
        }

        private static Border CreateIcon(string message)
        {
            bool isUpdated = string.Equals(message, Loc.T("Dialog.DwgRefresh.Updated"), System.StringComparison.OrdinalIgnoreCase);
            Border icon = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = isUpdated
                    ? new SolidColorBrush(Color.FromRgb(225, 148, 39))
                    : new SolidColorBrush(Color.FromRgb(49, 132, 219)),
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.Child = new TextBlock
            {
                Text = isUpdated ? "!" : "i",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            return icon;
        }

        public static void ShowMessage(string message)
        {
            DwgRefreshStatusWindow window = new DwgRefreshStatusWindow(message);
            window.ShowDialog();
        }
    }
}
