using Autodesk.Revit.UI;
using CadToRevit.UI.Dockable;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleDrawingBarWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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

        public PathObstacleDrawingBarWindow()
        {
            Title = "Drawing Restricted Area";
            Width = 760;
            Height = 78;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
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

            Button cancel = CreateButton("Cancel");
            cancel.Click += delegate { PathObstacleRuntime.RequestCancelDrawing(); };
            buttons.Children.Add(cancel);

            Button finish = CreateButton("Finish");
            finish.Content = CreateFinishContent();
            finish.Click += delegate { PathObstacleRuntime.RequestFinishDrawing(); };
            buttons.Children.Add(finish);

            root.Children.Add(buttons);

            StackPanel text = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(new TextBlock
            {
                Text = "Drawing Restricted Area:",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            _instructionText = new TextBlock
            {
                Text = "Click at least 3 points on the floor to draw a polygon.",
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

        public void AttachToRevit(UIApplication app)
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
                // Owner assignment is best-effort. The window remains non-topmost
                // even if Revit does not expose a valid owner handle.
            }
        }

        public void SetInstruction(string message, bool isError)
        {
            string value = string.IsNullOrWhiteSpace(message)
                ? "Click at least 3 points on the floor to draw a polygon."
                : message.Trim();

            Action update = delegate
            {
                _instructionText.Text = value;
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

        private static Button CreateButton(string text)
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

        private static UIElement CreateFinishContent()
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Image icon = CreateSuccessIcon();
            icon.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(icon);
            panel.Children.Add(new TextBlock
            {
                Text = "Finish",
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private static Image CreateSuccessIcon()
        {
            IntPtr hBitmap = ResourceIcons.success_16x16.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                source.Freeze();

                return new Image
                {
                    Source = source,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            finally
            {
                DeleteObject(hBitmap);
            }
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
                    if (source != null &&
                        source.CompositionTarget != null)
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
                // Fall back to the current monitor work area.
            }

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(20.0, (workArea.Width - Width) * 0.5);
            Top = workArea.Top + 120.0;
        }
    }
}
