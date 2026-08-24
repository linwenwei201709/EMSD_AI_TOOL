using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomRecognitionDeleteConfirmWindow : Window
    {
        public RoomRecognitionDeleteConfirmWindow(string message)
        {
            Title = "EMSD AI Tool";
            Width = 460;
            Height = 170;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock messageBlock = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(messageBlock, 0);
            root.Children.Add(messageBlock);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancel = CreateButton("Cancel");
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            buttons.Children.Add(cancel);

            Button confirm = CreateButton("Confirm");
            confirm.IsDefault = true;
            confirm.Click += delegate { DialogResult = true; };
            buttons.Children.Add(confirm);

            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            Content = root;
        }

        private static Button CreateButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 96,
                MinHeight = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
        }
    }
}
