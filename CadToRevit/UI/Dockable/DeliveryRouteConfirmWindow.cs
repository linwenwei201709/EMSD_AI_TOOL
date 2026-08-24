using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace CadToRevit.UI.Dockable
{
    public sealed class DeliveryRouteConfirmWindow : Window
    {
        private static readonly Color PrimaryBlue = Color.FromRgb(53, 105, 184);

        public DeliveryRouteConfirmWindow(string startLiftName, string targetRoomName)
        {
            Title = "Generate Delivery Route";
            Width = 520;
            Height = 330;
            MinWidth = 480;
            MinHeight = 310;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildContent(
                startLiftName ?? string.Empty,
                targetRoomName ?? string.Empty);

            TryAttachToRevitWindow();
        }

        private UIElement BuildContent(string startLiftName, string targetRoomName)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Generate Delivery Route",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            TextBlock description = new TextBlock
            {
                Text = "Review the selected route endpoints before generating.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                Margin = new Thickness(0, 6, 0, 14)
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            Border summaryCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 18)
            };

            StackPanel summary = new StackPanel();
            summary.Children.Add(BuildSummaryRow("Start Location", startLiftName));
            summary.Children.Add(BuildSummaryRow("Target Room", targetRoomName, false));
            summaryCard.Child = summary;
            Grid.SetRow(summaryCard, 2);
            root.Children.Add(summaryCard);

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button cancel = BuildActionButton("Cancel", false);
            cancel.IsCancel = true;
            cancel.Click += delegate
            {
                DialogResult = false;
                Close();
            };
            Grid.SetColumn(cancel, 0);
            buttons.Children.Add(cancel);

            Button generate = BuildActionButton("Generate", true);
            generate.IsDefault = true;
            generate.Click += delegate
            {
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(generate, 2);
            buttons.Children.Add(generate);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            return root;
        }

        private static FrameworkElement BuildSummaryRow(
            string label,
            string value,
            bool addBottomMargin = true)
        {
            Grid row = new Grid
            {
                Margin = addBottomMargin ? new Thickness(0, 0, 0, 10) : new Thickness(0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelBlock = new TextBlock
            {
                Text = label + ":",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 82, 98))
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            TextBlock valueBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            return row;
        }

        private static Button BuildActionButton(string text, bool primary)
        {
            SolidColorBrush blueBrush = new SolidColorBrush(PrimaryBlue);
            return new Button
            {
                Content = text,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Background = primary ? blueBrush : Brushes.White,
                Foreground = primary ? Brushes.White : blueBrush,
                BorderBrush = primary
                    ? blueBrush
                    : new SolidColorBrush(Color.FromRgb(205, 216, 229)),
                BorderThickness = new Thickness(1)
            };
        }

        private void TryAttachToRevitWindow()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = handle;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }

    public sealed class DeliveryRouteSaveConfirmWindow : Window
    {
        private static readonly Color PrimaryBlue = Color.FromRgb(53, 105, 184);

        public DeliveryRouteSaveConfirmWindow(
            string routeName,
            string startLiftName,
            string targetRoomName,
            bool isPassed)
        {
            Title = "Save Delivery Route";
            Width = 520;
            Height = 360;
            MinWidth = 480;
            MinHeight = 330;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildContent(
                routeName ?? string.Empty,
                startLiftName ?? string.Empty,
                targetRoomName ?? string.Empty,
                isPassed ? "Passed" : "Failed");

            TryAttachToRevitWindow();
        }

        private UIElement BuildContent(
            string routeName,
            string startLiftName,
            string targetRoomName,
            string status)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Save Delivery Route",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            TextBlock description = new TextBlock
            {
                Text = "Review the route information before saving.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                Margin = new Thickness(0, 6, 0, 14)
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            Border summaryCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 18)
            };

            StackPanel summary = new StackPanel();
            summary.Children.Add(BuildSummaryRow("Route Name", routeName));
            summary.Children.Add(BuildSummaryRow("Start Location", startLiftName));
            summary.Children.Add(BuildSummaryRow("Target Room", targetRoomName));
            summary.Children.Add(BuildSummaryRow("Status", status, false));
            summaryCard.Child = summary;
            Grid.SetRow(summaryCard, 2);
            root.Children.Add(summaryCard);

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button cancel = BuildActionButton("Cancel", false);
            cancel.IsCancel = true;
            cancel.Click += delegate
            {
                DialogResult = false;
                Close();
            };
            Grid.SetColumn(cancel, 0);
            buttons.Children.Add(cancel);

            Button save = BuildActionButton("Save", true);
            save.IsDefault = true;
            save.Click += delegate
            {
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(save, 2);
            buttons.Children.Add(save);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            return root;
        }

        private static FrameworkElement BuildSummaryRow(string label, string value, bool addBottomMargin = true)
        {
            Grid row = new Grid
            {
                Margin = addBottomMargin ? new Thickness(0, 0, 0, 10) : new Thickness(0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelBlock = new TextBlock
            {
                Text = label + ":",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 82, 98))
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            TextBlock valueBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            return row;
        }

        private static Button BuildActionButton(string text, bool primary)
        {
            SolidColorBrush blueBrush = new SolidColorBrush(PrimaryBlue);
            return new Button
            {
                Content = text,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Background = primary ? blueBrush : Brushes.White,
                Foreground = primary ? Brushes.White : blueBrush,
                BorderBrush = primary
                    ? blueBrush
                    : new SolidColorBrush(Color.FromRgb(205, 216, 229)),
                BorderThickness = new Thickness(1)
            };
        }

        private void TryAttachToRevitWindow()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = handle;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }


    public sealed class DeliveryRouteDeleteConfirmWindow : Window
    {
        private static readonly Color PrimaryBlue = Color.FromRgb(53, 105, 184);

        public DeliveryRouteDeleteConfirmWindow(string routeName)
        {
            Title = "Delete Delivery Route";
            Width = 500;
            Height = 300;
            MinWidth = 460;
            MinHeight = 280;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildContent(routeName ?? string.Empty);
            TryAttachToRevitWindow();
        }

        private UIElement BuildContent(string routeName)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Delete Delivery Route",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            TextBlock description = new TextBlock
            {
                Text = "This action cannot be undone.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 70, 70)),
                Margin = new Thickness(0, 6, 0, 14)
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 18)
            };
            card.Child = new TextBlock
            {
                Text = "Delete \"" + (string.IsNullOrWhiteSpace(routeName) ? "Delivery Route" : routeName) + "\"?",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 42, 49)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(card, 2);
            root.Children.Add(card);

            Grid buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button cancel = BuildActionButton("Cancel", false);
            cancel.IsCancel = true;
            cancel.Click += delegate
            {
                DialogResult = false;
                Close();
            };
            Grid.SetColumn(cancel, 0);
            buttons.Children.Add(cancel);

            Button delete = BuildActionButton("Delete", true);
            delete.IsDefault = true;
            delete.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            delete.BorderBrush = delete.Background;
            delete.Click += delegate
            {
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(delete, 2);
            buttons.Children.Add(delete);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            return root;
        }

        private static Button BuildActionButton(string text, bool primary)
        {
            SolidColorBrush blueBrush = new SolidColorBrush(PrimaryBlue);
            return new Button
            {
                Content = text,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Background = primary ? blueBrush : Brushes.White,
                Foreground = primary ? Brushes.White : blueBrush,
                BorderBrush = primary
                    ? blueBrush
                    : new SolidColorBrush(Color.FromRgb(205, 216, 229)),
                BorderThickness = new Thickness(1)
            };
        }

        private void TryAttachToRevitWindow()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = handle;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }


    public sealed class DeliveryRouteStartPointSelectionBarWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        private readonly TextBlock _instructionText;
        private IntPtr _revitMainWindowHandle;

        public DeliveryRouteStartPointSelectionBarWindow()
        {
            Title = "Pick Start Point";
            Width = 760;
            Height = 84;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            // IMPORTANT: keep this helper window behaviour identical to
            // PathObstacleDrawingBarWindow.  Restricted Area is already proven to
            // interrupt Revit PickPoint immediately from modeless buttons.  Do not
            // add WS_EX_NOACTIVATE/ShowActivated=false here because that creates a
            // different focus/input path from the working implementation.
            Topmost = false;
            Background = new SolidColorBrush(Color.FromRgb(32, 98, 185));

            Border border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(18, 72, 148)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 10, 16, 10)
            };

            DockPanel root = new DockPanel { LastChildFill = true };
            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(buttons, Dock.Right);

            Button cancel = CreatePickButton("Cancel");
            cancel.Click += delegate { RoomRecognitionPaneRuntime.RequestCancelDeliveryRouteStartPointSelection(); };
            buttons.Children.Add(cancel);

            Button confirm = CreatePickButton("Confirm");
            confirm.Click += delegate { RoomRecognitionPaneRuntime.RequestConfirmDeliveryRouteStartPointSelection(); };
            buttons.Children.Add(confirm);
            root.Children.Add(buttons);

            StackPanel text = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(new TextBlock
            {
                Text = "Pick Start Point",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            _instructionText = new TextBlock
            {
                Text = "Click anywhere in the view to set the starting point.",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 3, 16, 0)
            };
            text.Children.Add(_instructionText);
            root.Children.Add(text);
            border.Child = root;
            Content = border;

            Loaded += delegate { PositionNearTopCenter(); };
        }

        public void AttachToRevit(Autodesk.Revit.UI.UIApplication app)
        {
            _revitMainWindowHandle = app != null ? app.MainWindowHandle : IntPtr.Zero;
            if (_revitMainWindowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                new WindowInteropHelper(this).Owner = _revitMainWindowHandle;
            }
            catch
            {
                // Best effort only. The important fallback is keeping Topmost=false.
            }
        }

        public void SetInstruction(string message, bool isError)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Action update = delegate
            {
                _instructionText.Text = message.Trim();
                _instructionText.Foreground = isError
                    ? new SolidColorBrush(Color.FromRgb(255, 235, 160))
                    : Brushes.White;
            };

            if (Dispatcher != null && !Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(update);
                return;
            }

            update();
        }

        private static Button CreatePickButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 88,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 64, 96))
            };
        }

        private void PositionNearTopCenter()
        {
            try
            {
                NativeRect nativeRect;
                if (_revitMainWindowHandle != IntPtr.Zero &&
                    GetWindowRect(_revitMainWindowHandle, out nativeRect))
                {
                    Point topLeft = new Point(nativeRect.Left, nativeRect.Top);
                    Point bottomRight = new Point(nativeRect.Right, nativeRect.Bottom);

                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
                        topLeft = fromDevice.Transform(topLeft);
                        bottomRight = fromDevice.Transform(bottomRight);
                    }

                    double ownerWidth = Math.Max(0.0, bottomRight.X - topLeft.X);
                    Left = topLeft.X + Math.Max(20.0, (ownerWidth - Width) * 0.5);
                    Top = topLeft.Y + 120.0;
                    return;
                }
            }
            catch
            {
            }

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(20.0, (workArea.Width - Width) * 0.5);
            Top = workArea.Top + 120.0;
        }
    }
}
