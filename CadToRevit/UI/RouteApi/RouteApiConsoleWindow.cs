using Autodesk.Revit.UI;
using CadToRevit.Services.RouteApi;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CadToRevit.UI.RouteApi
{
    public class RouteApiConsoleWindow : Window
    {
        private static RouteApiConsoleWindow _instance;

        private readonly TextBlock _statusText;
        private readonly TextBlock _apiUrlText;
        private readonly TextBlock _exePathText;
        private readonly WpfTextBox _logTextBox;

        public static void ShowOrActivate(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsVisible)
            {
                _instance = new RouteApiConsoleWindow();
                _instance.SetOwner();
                _instance.Show();
                return;
            }

            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.Activate();
            Task.Run(() => RouteApiProcessService.RefreshStatusFromHealth());
        }

        private RouteApiConsoleWindow()
        {
            Title = "Route API Console";
            Width = 920;
            Height = 640;
            MinWidth = 760;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Grid root = new Grid();
            root.Margin = new Thickness(12);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel header = new StackPanel { Orientation = Orientation.Vertical };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            StackPanel statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(statusRow);

            statusRow.Children.Add(CreateLabel("Status:"));
            _statusText = CreateValueText();
            statusRow.Children.Add(_statusText);

            statusRow.Children.Add(CreateSpacer(24));
            statusRow.Children.Add(CreateLabel("API:"));
            _apiUrlText = CreateValueText();
            statusRow.Children.Add(_apiUrlText);

            StackPanel pathRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(pathRow);
            pathRow.Children.Add(CreateLabel("Executable:"));
            _exePathText = CreateValueText();
            pathRow.Children.Add(_exePathText);

            StackPanel buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(buttonRow);
            buttonRow.Children.Add(CreateButton("Start API", StartApi));
            buttonRow.Children.Add(CreateButton("Stop API", StopApi));
            buttonRow.Children.Add(CreateButton("Restart API", RestartApi));
            buttonRow.Children.Add(CreateButton("Health Check", HealthCheck));
            buttonRow.Children.Add(CreateButton("Clear Log", ClearLog));

            _logTextBox = new WpfTextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                Foreground = Brushes.WhiteSmoke,
                BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Padding = new Thickness(8)
            };
            Grid.SetRow(_logTextBox, 1);
            root.Children.Add(_logTextBox);

            Content = root;

            Loaded += OnLoaded;
            Closed += OnClosed;
            RefreshStatus(RouteApiProcessService.Status);
            _apiUrlText.Text = RouteApiProcessService.ApiUrl;
            _exePathText.Text = RouteApiProcessService.ResolveExecutablePath();
        }

        private void SetOwner()
        {
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RouteApiProcessService.LogReceived += OnLogReceived;
            RouteApiProcessService.StatusChanged += OnStatusChanged;
            LoadLogSnapshot();
            Task.Run(() => RouteApiProcessService.RefreshStatusFromHealth());
        }

        private void OnClosed(object sender, EventArgs e)
        {
            RouteApiProcessService.LogReceived -= OnLogReceived;
            RouteApiProcessService.StatusChanged -= OnStatusChanged;
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void StartApi(object sender, RoutedEventArgs e)
        {
            Task.Run(() => RouteApiProcessService.Start());
        }

        private void StopApi(object sender, RoutedEventArgs e)
        {
            Task.Run(() => RouteApiProcessService.Stop());
        }

        private void RestartApi(object sender, RoutedEventArgs e)
        {
            Task.Run(() => RouteApiProcessService.Restart());
        }

        private void HealthCheck(object sender, RoutedEventArgs e)
        {
            Task.Run(() => RouteApiProcessService.HealthCheck());
        }

        private void ClearLog(object sender, RoutedEventArgs e)
        {
            RouteApiProcessService.ClearLog();
            _logTextBox.Clear();
        }

        private void OnLogReceived(object sender, string line)
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(line)), DispatcherPriority.Background);
        }

        private void OnStatusChanged(object sender, RouteApiStatus status)
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshStatus(status)), DispatcherPriority.Background);
        }

        private void AppendLog(string line)
        {
            if (_logTextBox.Text.Length > 0)
            {
                _logTextBox.AppendText(Environment.NewLine);
            }

            _logTextBox.AppendText(line ?? string.Empty);
            _logTextBox.ScrollToEnd();
        }

        private void LoadLogSnapshot()
        {
            string[] lines = RouteApiProcessService.GetLogSnapshot();
            _logTextBox.Text = string.Join(Environment.NewLine, lines);
            _logTextBox.ScrollToEnd();
        }

        private void RefreshStatus(RouteApiStatus status)
        {
            _statusText.Text = GetStatusText(status);
            _statusText.Foreground = status == RouteApiStatus.Running
                ? Brushes.ForestGreen
                : status == RouteApiStatus.ExternalStaleRunning || status == RouteApiStatus.RunningExternal
                    ? Brushes.DarkOrange
                    : status == RouteApiStatus.Error
                        ? Brushes.Firebrick
                        : Brushes.DimGray;
        }

        private static string GetStatusText(RouteApiStatus status)
        {
            if (status == RouteApiStatus.ExternalStaleRunning || status == RouteApiStatus.RunningExternal)
            {
                return "External/Stale Running";
            }

            return status.ToString();
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }

        private static TextBlock CreateValueText()
        {
            return new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private static FrameworkElement CreateSpacer(double width)
        {
            return new Border { Width = width };
        }

        private static Button CreateButton(string text, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                MinWidth = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            button.Click += handler;
            return button;
        }
    }
}
