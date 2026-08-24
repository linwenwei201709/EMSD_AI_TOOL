using CadToRevit.Services.Rooms.Lifts;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class LiftEditWindow : Window
    {
        private readonly TextBox _nameBox;
        private readonly TextBox _internalLengthBox;
        private readonly TextBox _internalWidthBox;
        private readonly TextBox _internalHeightBox;
        private readonly TextBox _doorWidthBox;
        private readonly TextBox _doorHeightBox;
        private readonly TextBox _capacityBox;
        private readonly TextBlock _errorText;

        public string EditedName { get; private set; }

        public LiftDisplayOverride DisplayOverride { get; private set; }

        public LiftEditWindow(string liftKey, string currentName, LiftDisplayInfo currentInfo)
        {
            Title = "EMSD AI Tool";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.White;

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Edit Lift",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            Grid form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(form, 0, "Lift Name:", out _nameBox, currentName);
            AddRow(form, 1, "Internal L (mm):", out _internalLengthBox, FormatNumber(currentInfo?.InternalLengthMm));
            AddRow(form, 2, "Internal W (mm):", out _internalWidthBox, FormatNumber(currentInfo?.InternalWidthMm));
            AddRow(form, 3, "Internal H (mm):", out _internalHeightBox, FormatNumber(currentInfo?.InternalHeightMm));
            AddRow(form, 4, "Door W (mm):", out _doorWidthBox, FormatNumber(currentInfo?.DoorWidthMm));
            AddRow(form, 5, "Door H (mm):", out _doorHeightBox, FormatNumber(currentInfo?.DoorHeightMm));
            AddRow(form, 6, "Capacity (kg):", out _capacityBox, FormatNumber(currentInfo?.CapacityKg));
            Grid.SetRow(form, 1);
            root.Children.Add(form);

            _errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 18, 60)),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_errorText, 2);
            root.Children.Add(_errorText);

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
            confirm.Click += delegate { Confirm(liftKey); };
            buttons.Children.Add(confirm);
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            Content = root;
            Loaded += delegate
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape)
                {
                    DialogResult = false;
                }
            };
        }

        public void SetRevitOwner()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = handle;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    return;
                }
            }
            catch
            {
            }

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void Confirm(string liftKey)
        {
            string name = (_nameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _errorText.Text = "Lift Name is required.";
                return;
            }

            if (!TryReadPositive(_internalLengthBox.Text, out double internalLength) ||
                !TryReadPositive(_internalWidthBox.Text, out double internalWidth) ||
                !TryReadPositive(_internalHeightBox.Text, out double internalHeight) ||
                !TryReadPositive(_doorWidthBox.Text, out double doorWidth) ||
                !TryReadPositive(_doorHeightBox.Text, out double doorHeight) ||
                !TryReadPositive(_capacityBox.Text, out double capacity))
            {
                _errorText.Text = "Please enter positive numeric values.";
                return;
            }

            EditedName = name;
            DisplayOverride = new LiftDisplayOverride
            {
                LiftKey = liftKey ?? string.Empty,
                InternalLengthMm = internalLength,
                InternalWidthMm = internalWidth,
                InternalHeightMm = internalHeight,
                DoorWidthMm = doorWidth,
                DoorHeightMm = doorHeight,
                CapacityKg = capacity
            };
            DialogResult = true;
        }

        private static void AddRow(Grid form, int rowIndex, string label, out TextBox textBox, string value)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 10)
            };
            Grid.SetRow(labelBlock, rowIndex);
            Grid.SetColumn(labelBlock, 0);
            form.Children.Add(labelBlock);

            textBox = new TextBox
            {
                Text = value ?? string.Empty,
                FontSize = 13,
                MinHeight = 30,
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(textBox, rowIndex);
            Grid.SetColumn(textBox, 1);
            form.Children.Add(textBox);
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

        private static bool TryReadPositive(string text, out double value)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static string FormatNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
        }
    }

    public sealed class LiftDisplayInfo
    {
        public double? InternalLengthMm { get; set; }

        public double? InternalWidthMm { get; set; }

        public double? InternalHeightMm { get; set; }

        public double? DoorWidthMm { get; set; }

        public double? DoorHeightMm { get; set; }

        public double? CapacityKg { get; set; }
    }
}
