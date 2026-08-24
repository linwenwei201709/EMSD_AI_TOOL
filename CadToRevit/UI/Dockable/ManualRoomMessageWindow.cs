using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class ManualRoomMessageWindow : Window
    {
        public ManualRoomMessageWindow(string title, string message)
        {
            Title = "EMSD AI Tool";
            Width = 460;
            Height = 210;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock titleBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(title) ? "Notice" : title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(titleBlock, 0);
            root.Children.Add(titleBlock);

            TextBlock messageBlock = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60))
            };
            Grid.SetRow(messageBlock, 1);
            root.Children.Add(messageBlock);

            Button confirm = new Button
            {
                Content = "Confirm",
                MinWidth = 104,
                MinHeight = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };
            confirm.Click += delegate { DialogResult = true; };
            Grid.SetRow(confirm, 2);
            root.Children.Add(confirm);

            Content = root;
        }

        public static void ShowUnclosedSpace()
        {
            ManualRoomMessageWindow window = new ManualRoomMessageWindow(
                "Unclosed Space",
                "The selected walls do not form a closed loop. Please select additional walls.");
            window.ShowDialog();
        }
    }
}
