using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleEditNameWindow : Window
    {
        private readonly WpfTextBox _nameTextBox;
        private readonly string _currentName;

        public string EditedName { get; private set; }

        public PathObstacleEditNameWindow(string currentName)
        {
            _currentName = string.IsNullOrWhiteSpace(currentName) ? "Restricted Area" : currentName.Trim();
            EditedName = _currentName;

            Title = "Edit Restricted Area";
            Width = 420;
            Height = 210;
            MinWidth = 380;
            MinHeight = 190;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;

            Grid root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Edit Restricted Area",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            StackPanel field = new StackPanel();
            Grid.SetRow(field, 1);
            root.Children.Add(field);

            field.Children.Add(new TextBlock
            {
                Text = "Name",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _nameTextBox = new WpfTextBox
            {
                Text = _currentName,
                FontSize = 14,
                Height = 32,
                Padding = new Thickness(8, 4, 8, 4)
            };
            field.Children.Add(_nameTextBox);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            buttons.Children.Add(CreateButton("Cancel", Cancel, true));
            buttons.Children.Add(CreateButton("Save", Save, false));

            Content = root;
            Loaded += delegate
            {
                _nameTextBox.Focus();
                _nameTextBox.SelectAll();
            };
            KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Save(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Cancel(sender, e);
                e.Handled = true;
            }
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            string value = (_nameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                PathObstacleNoticeWindow.Show("Edit Restricted Area", "Please enter a restricted area name.");
                return;
            }

            EditedName = value;
            DialogResult = true;
            Close();
        }

        private void Cancel(object sender, RoutedEventArgs e)
        {
            EditedName = _currentName;
            DialogResult = false;
            Close();
        }

        private static Button CreateButton(string text, RoutedEventHandler handler, bool isCancel)
        {
            Button button = new Button
            {
                Content = text,
                Width = 96,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = isCancel,
                IsDefault = !isCancel
            };
            button.Click += handler;
            return button;
        }
    }
}
