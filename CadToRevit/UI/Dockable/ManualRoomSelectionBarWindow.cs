using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CadToRevit.UI.Dockable
{
    public sealed class ManualRoomSelectionBarWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public ManualRoomSelectionBarWindow()
            : this(
                "Room Creation Mode",
                "Room Creation Mode:",
                "Please click to select the wall / column elements that form the room boundary.")
        {
        }

        public ManualRoomSelectionBarWindow(string windowTitle, string headerText, string descriptionText)
        {
            Title = string.IsNullOrWhiteSpace(windowTitle) ? "Room Creation Mode" : windowTitle;
            Width = 760;
            Height = 72;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
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
            cancel.Click += delegate { _ = RoomRecognitionPaneRuntime.RequestCancelManualRoomSelectionAsync(); };
            buttons.Children.Add(cancel);

            Button finish = CreateButton("Finish Selection");
            finish.Content = CreateFinishSelectionContent();
            finish.Click += delegate { _ = RoomRecognitionPaneRuntime.RequestFinishManualRoomSelectionAsync(); };
            buttons.Children.Add(finish);

            root.Children.Add(buttons);

            StackPanel text = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(headerText) ? "Room Creation Mode:" : headerText,
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(descriptionText)
                    ? "Please click to select the wall / column elements that form the room boundary."
                    : descriptionText,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 3, 16, 0)
            });

            root.Children.Add(text);
            border.Child = root;
            Content = border;

            Loaded += delegate { PositionNearTopCenter(); };
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

        private static UIElement CreateFinishSelectionContent()
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
                Text = "Finish Selection",
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
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(20.0, (workArea.Width - Width) * 0.5);
            Top = workArea.Top + 120.0;
        }
    }
}
