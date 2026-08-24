using CadToRevit.Commands;
using CadToRevit.Infrastructure.Localization;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CadToRevit.UI.Dockable
{
    internal sealed class PreviewGenerationProgressWindow : Window, IGenerationProgressReporter, IDisposable
    {
        private readonly TextBlock _stageText = new TextBlock();
        private readonly TextBlock _detailText = new TextBlock();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Button _cancelButton = new Button();

        public bool IsCancellationRequested { get; private set; }

        public PreviewGenerationProgressWindow()
        {
            Title = Loc.T("Progress.Title");
            Width = 620;
            Height = 132;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ShowInTaskbar = false;
            Topmost = true;

            SourceInitialized += (s, e) => AttachOwnerAndPlaceNearRevitTop();

            Grid root = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _stageText.Text = Loc.T("Progress.Ready");
            _stageText.FontSize = 15;
            _stageText.FontWeight = FontWeights.SemiBold;
            _stageText.VerticalAlignment = VerticalAlignment.Center;
            _stageText.Margin = new Thickness(0, 0, 12, 6);
            Grid.SetRow(_stageText, 0);
            Grid.SetColumn(_stageText, 0);
            root.Children.Add(_stageText);

            _cancelButton.Content = Loc.T("Progress.Cancel");
            _cancelButton.Width = 84;
            _cancelButton.Height = 26;
            _cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
            _cancelButton.VerticalAlignment = VerticalAlignment.Center;
            _cancelButton.Margin = new Thickness(8, 0, 0, 6);
            _cancelButton.Click += (s, e) =>
            {
                // English comment: Signal cancellation and disable further clicks.
                IsCancellationRequested = true;
                _cancelButton.IsEnabled = false;
                _cancelButton.Content = Loc.T("Progress.Cancelling");
            };
            Grid.SetRow(_cancelButton, 0);
            Grid.SetColumn(_cancelButton, 1);
            root.Children.Add(_cancelButton);

            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Height = 18;
            _progressBar.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            _progressBar.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(_progressBar, 1);
            Grid.SetColumn(_progressBar, 0);
            Grid.SetColumnSpan(_progressBar, 2);
            root.Children.Add(_progressBar);

            _detailText.Text = Loc.T("Progress.DetailDefault");
            _detailText.FontSize = 13;
            _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
            _detailText.Margin = new Thickness(0);
            Grid.SetRow(_detailText, 2);
            Grid.SetColumn(_detailText, 0);
            Grid.SetColumnSpan(_detailText, 2);
            root.Children.Add(_detailText);

            Content = root;
        }

        public void UpdateProgress(string stage, int current, int total, string detail)
        {
            _stageText.Text = stage ?? string.Empty;
            _detailText.Text = detail ?? string.Empty;
            int safeTotal = total <= 0 ? 1 : total;
            int safeCurrent = current < 0 ? 0 : Math.Min(current, safeTotal);
            _progressBar.Value = Math.Round((safeCurrent * 100.0) / safeTotal);
            PumpUi();
        }

        public void Dispose()
        {
            Close();
        }

        private void AttachOwnerAndPlaceNearRevitTop()
        {
            IntPtr ownerHandle = GetRevitMainWindowHandle();
            if (ownerHandle != IntPtr.Zero)
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }

            if (ownerHandle != IntPtr.Zero && GetWindowRect(ownerHandle, out RECT rect))
            {
                Point topLeft = ToDeviceIndependentPoint(rect.Left, rect.Top);
                Point bottomRight = ToDeviceIndependentPoint(rect.Right, rect.Bottom);
                double ownerWidth = Math.Max(0, bottomRight.X - topLeft.X);
                double ownerHeight = Math.Max(0, bottomRight.Y - topLeft.Y);

                Left = topLeft.X + Math.Max(20, (ownerWidth - Width) * 0.42);
                Top = topLeft.Y + Math.Min(Math.Max(120, ownerHeight * 0.14), 190);
            }
            else
            {
                Rect area = SystemParameters.WorkArea;
                Left = area.Left + Math.Max(20, (area.Width - Width) * 0.42);
                Top = area.Top + 150;
            }
        }

        private Point ToDeviceIndependentPoint(double x, double y)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
            }

            return new Point(x, y);
        }

        private static IntPtr GetRevitMainWindowHandle()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                return handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static void PumpUi()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(o =>
                {
                    ((DispatcherFrame)o).Continue = false;
                    return null;
                }),
                frame);
            Dispatcher.PushFrame(frame);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
