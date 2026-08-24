using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.Services.CadRuntime
{
    internal static class CadRuntimeUserMessage
    {
        private static bool _warningShownForCurrentProcess;

        internal static string Title
        {
            get { return CadRuntimeTarget.ProductName + " Not Detected"; }
        }

        internal static string Body
        {
            get
            {
                return CadRuntimeTarget.ProductName + " was not detected or is not available on this computer.\n\n" +
                       "Automatic DWG unit detection and DWG text recognition for rooms and lifts will be unavailable.\n\n" +
                       "You can continue importing the DWG. Please confirm the drawing unit manually. " +
                       "Rooms and lifts can also be added manually later.";
            }
        }

        internal static void ShowWarningOnce(UIApplication uiApp, CadRuntimeInfo runtimeInfo)
        {
            if (_warningShownForCurrentProcess)
            {
                return;
            }

            _warningShownForCurrentProcess = true;
            CadRuntimeWarningWindow window = new CadRuntimeWarningWindow(Title, Body);
            TrySetOwner(window, uiApp);
            window.ShowDialog();
        }

        private static void TrySetOwner(Window window, UIApplication uiApp)
        {
            if (window == null || uiApp == null)
            {
                return;
            }

            try
            {
                IntPtr handle = uiApp.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    new WindowInteropHelper(window).Owner = handle;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                // Best effort only. The warning can still be shown without an explicit owner.
            }
        }

        private sealed class CadRuntimeWarningWindow : Window
        {
            internal CadRuntimeWarningWindow(string title, string message)
            {
                Title = "EMSD AI Tool";
                Width = 560;
                MinHeight = 330;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = Brushes.White;
                ShowInTaskbar = false;

                Grid root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Border header = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                    Padding = new Thickness(24, 18, 24, 16)
                };
                header.Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(title) ? "AutoCAD Not Detected" : title,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(32, 40, 54))
                };
                Grid.SetRow(header, 0);
                root.Children.Add(header);

                Grid contentGrid = new Grid
                {
                    Margin = new Thickness(24, 22, 24, 24)
                };
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Border icon = new Border
                {
                    Width = 52,
                    Height = 52,
                    CornerRadius = new CornerRadius(26),
                    Background = new SolidColorBrush(Color.FromRgb(244, 146, 35)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                icon.Child = new TextBlock
                {
                    Text = "!",
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                contentGrid.Children.Add(icon);

                TextBlock messageBlock = new TextBlock
                {
                    Text = message ?? string.Empty,
                    FontSize = 14,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(54, 63, 77)),
                    Margin = new Thickness(18, 0, 0, 0)
                };
                Grid.SetColumn(messageBlock, 1);
                contentGrid.Children.Add(messageBlock);

                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                Border footer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    Padding = new Thickness(24, 14, 24, 14)
                };

                Button continueButton = new Button
                {
                    Content = "Continue",
                    Width = 120,
                    Height = 38,
                    Padding = new Thickness(12, 0, 12, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    IsDefault = true,
                    IsCancel = true,
                    Background = new SolidColorBrush(Color.FromRgb(30, 115, 190)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(30, 115, 190))
                };
                continueButton.Click += delegate
                {
                    DialogResult = true;
                };
                footer.Child = continueButton;

                Grid.SetRow(footer, 2);
                root.Children.Add(footer);

                Content = root;
            }
        }
    }
}
