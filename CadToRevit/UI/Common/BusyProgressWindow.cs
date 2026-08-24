using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CadToRevit.UI.Common
{
    internal sealed class BusyProgressWindow : Window, IDisposable
    {
        private BusyProgressWindow(UIApplication uiApp, string title, string message)
        {
            Title = title ?? "Analyze Rooms";
            Width = 460;
            Height = 150;
            MinWidth = 420;
            MinHeight = 140;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ShowInTaskbar = false;
            Background = Brushes.White;

            AttachOwner(uiApp);
            Content = BuildContent(message);
        }

        public static BusyProgressWindow Show(UIApplication uiApp, string title, string message)
        {
            BusyProgressWindow window = new BusyProgressWindow(uiApp, title, message);
            window.Show();
            window.PumpUi();
            return window;
        }

        public void Dispose()
        {
            try
            {
                Close();
            }
            catch
            {
            }
        }

        private UIElement BuildContent(string message)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(22, 18, 22, 18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock text = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, 0);
            root.Children.Add(text);

            ProgressBar progress = new ProgressBar
            {
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0))
            };
            Grid.SetRow(progress, 1);
            root.Children.Add(progress);

            return root;
        }

        private void AttachOwner(UIApplication uiApp)
        {
            IntPtr ownerHandle = uiApp != null ? uiApp.MainWindowHandle : IntPtr.Zero;
            if (ownerHandle == IntPtr.Zero)
            {
                return;
            }

            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.Owner = ownerHandle;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private void PumpUi()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(o =>
                {
                    ((DispatcherFrame)o).Continue = false;
                    return null;
                }),
                frame);
            Dispatcher.PushFrame(frame);
        }
    }
}
