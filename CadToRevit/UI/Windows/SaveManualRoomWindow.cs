using CadToRevit.Services.Rooms.Manual;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Windows
{
    public sealed class SaveManualRoomWindow : Window
    {
        private readonly TextBox _roomNameBox;
        private readonly TextBox _roomNumberBox;
        private readonly ComboBox _roomTypeBox;
        private readonly TextBlock _validationText;

        public string RoomName => (_roomNameBox.Text ?? string.Empty).Trim();

        public string RoomNumber => (_roomNumberBox.Text ?? string.Empty).Trim();

        public string RoomType => _roomTypeBox.SelectedItem != null ? _roomTypeBox.SelectedItem.ToString() : string.Empty;

        public SaveManualRoomWindow(ManualRoomRecord draft, string defaultRoomName)
        {
            Title = "Save Manual Room";
            Width = 460;
            MinWidth = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;
            ShowInTaskbar = false;

            Grid root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel form = new StackPanel { Orientation = Orientation.Vertical };
            form.Children.Add(CreateTitle("Manual Room"));

            _roomNameBox = CreateTextBox(string.IsNullOrWhiteSpace(defaultRoomName) ? "Manual Room 001" : defaultRoomName);
            form.Children.Add(CreateLabel("Room Name"));
            form.Children.Add(_roomNameBox);

            _roomNumberBox = CreateTextBox(string.Empty);
            form.Children.Add(CreateLabel("Room Number"));
            form.Children.Add(_roomNumberBox);

            _roomTypeBox = new ComboBox
            {
                MinHeight = 30,
                Margin = new Thickness(0, 4, 0, 10)
            };
            _roomTypeBox.Items.Add("AHU Room");
            _roomTypeBox.Items.Add("General Room");
            _roomTypeBox.SelectedIndex = 0;
            form.Children.Add(CreateLabel("Room Type"));
            form.Children.Add(_roomTypeBox);

            form.Children.Add(CreateSummary(draft));
            Grid.SetRow(form, 0);
            root.Children.Add(form);

            _validationText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(196, 61, 57)),
                Margin = new Thickness(0, 4, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_validationText, 1);
            root.Children.Add(_validationText);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancel = CreateButton("Cancel", false, true);
            cancel.Click += delegate { DialogResult = false; };
            buttons.Children.Add(cancel);

            Button save = CreateButton("Save", true, false);
            save.Click += OnSaveClick;
            buttons.Children.Add(save);

            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RoomName))
            {
                _validationText.Text = "Room Name is required.";
                return;
            }

            DialogResult = true;
        }

        private static TextBlock CreateTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            };
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private static TextBox CreateTextBox(string text)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                MinHeight = 30,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(6, 3, 6, 3)
            };
        }

        private static TextBlock CreateSummary(ManualRoomRecord draft)
        {
            string level = draft != null && !string.IsNullOrWhiteSpace(draft.LevelName) ? draft.LevelName : "-";
            string area = draft != null ? draft.AreaM2.ToString("0.##") + " m2" : "-";
            string roomSize = ResolveRoomSizeText(draft);
            return new TextBlock
            {
                Text = "Level: " + level + "\nRoom Size: " + roomSize + "\nArea: " + area,
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(84, 96, 112)),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static string ResolveRoomSizeText(ManualRoomRecord draft)
        {
            if (draft == null || draft.BBox == null)
            {
                return "-";
            }

            double lengthMm = Math.Abs(draft.BBox.Max.X - draft.BBox.Min.X) * 304.8;
            double widthMm = Math.Abs(draft.BBox.Max.Y - draft.BBox.Min.Y) * 304.8;
            return Math.Round(lengthMm).ToString("0") + " mm x " + Math.Round(widthMm).ToString("0") + " mm";
        }

        private static Button CreateButton(string text, bool isDefault, bool isCancel)
        {
            return new Button
            {
                Content = text,
                MinWidth = 96,
                MinHeight = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 0, 14, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
        }
    }
}
