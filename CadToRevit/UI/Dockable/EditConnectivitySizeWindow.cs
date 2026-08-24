using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace CadToRevit.UI.Dockable
{
    public sealed class EditConnectivitySizeWindow : Window
    {
        private readonly bool _isDuctSize;
        private readonly TextBox _lengthBox;
        private readonly TextBox _widthBox;
        private readonly TextBox _diameterBox;
        private readonly TextBlock _errorText;

        public double LengthMm { get; private set; }

        public double WidthMm { get; private set; }

        public double DiameterMm { get; private set; }

        public EditConnectivitySizeWindow(string targetName, bool isDuctSize)
        {
            _isDuctSize = isDuctSize;
            Title = "Edit " + targetName + " Size:";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;

            StackPanel root = new StackPanel
            {
                Margin = new Thickness(18)
            };

            if (_isDuctSize)
            {
                _lengthBox = AddInput(root, "L (mm)");
                _widthBox = AddInput(root, "W (mm)");
            }
            else
            {
                _diameterBox = AddInput(root, "Diameter (mm)");
            }

            _errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 18, 60)),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(_errorText);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 74,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (sender, args) => DialogResult = false;
            buttons.Children.Add(cancelButton);

            Button okButton = new Button
            {
                Content = "OK",
                MinWidth = 74,
                Height = 32
            };
            okButton.Click += (sender, args) => Confirm();
            buttons.Children.Add(okButton);

            root.Children.Add(buttons);
            Content = root;
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

        private static TextBox AddInput(Panel root, string label)
        {
            root.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            TextBox textBox = new TextBox
            {
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            root.Children.Add(textBox);
            return textBox;
        }

        private void Confirm()
        {
            if (_isDuctSize)
            {
                if (!TryReadPositive(_lengthBox.Text, out double lengthMm) ||
                    !TryReadPositive(_widthBox.Text, out double widthMm))
                {
                    _errorText.Text = "Please enter positive numeric values.";
                    return;
                }

                LengthMm = lengthMm;
                WidthMm = widthMm;
            }
            else
            {
                if (!TryReadPositive(_diameterBox.Text, out double diameterMm))
                {
                    _errorText.Text = "Please enter a positive numeric value.";
                    return;
                }

                DiameterMm = diameterMm;
            }

            DialogResult = true;
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
    }
}
