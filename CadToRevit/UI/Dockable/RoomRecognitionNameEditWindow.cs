using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomRecognitionNameEditWindow : Window
    {
        private readonly TextBox _nameBox;

        public string EditedName { get; private set; }

        public RoomRecognitionNameEditWindow(string label, string currentName)
            : this(label, currentName, "Confirm")
        {
        }

        public RoomRecognitionNameEditWindow(string label, string currentName, string confirmButtonText)
        {
            Title = "EMSD AI Tool";
            Width = 420;
            Height = 190;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            ShowInTaskbar = false;

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock labelBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(label) ? "Name" : label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(labelBlock, 0);
            root.Children.Add(labelBlock);

            _nameBox = new TextBox
            {
                Text = currentName ?? string.Empty,
                FontSize = 14,
                MinHeight = 30,
                Padding = new Thickness(6, 3, 6, 3)
            };
            Grid.SetRow(_nameBox, 1);
            root.Children.Add(_nameBox);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancel = CreateButton("Cancel");
            cancel.Click += delegate
            {
                DialogResult = false;
            };
            buttons.Children.Add(cancel);

            Button confirm = CreateButton(string.IsNullOrWhiteSpace(confirmButtonText) ? "Confirm" : confirmButtonText);
            confirm.IsDefault = true;
            confirm.Click += delegate
            {
                string value = (_nameBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                EditedName = value;
                DialogResult = true;
            };
            buttons.Children.Add(confirm);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            Content = root;

            Loaded += delegate
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
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
