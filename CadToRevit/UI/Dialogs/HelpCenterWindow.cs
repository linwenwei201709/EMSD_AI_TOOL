using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.Dialogs
{
    /// <summary>
    /// Modeless local HTML help center hosted inside Revit with WebView2.
    /// The help site is deployed beside the add-in under: Help\index.html.
    /// </summary>
    public sealed class HelpCenterWindow : Window
    {
        private const string VirtualHostName = "emsd-help.local";
        private const double DefaultWindowWidth = 1100.0;
        private const double SideWindowWidthFactor = 0.80;
        private const double SideWindowHeightFactor = 0.90;
        private const uint MonitorDefaultToNearest = 0x00000002;

        private static HelpCenterWindow _instance;

        private readonly WebView2 _webView;
        private readonly TextBlock _statusText;
        private string _helpFolder;
        private string _indexFile;
        private IntPtr _ownerHandle;

        public static void ShowOrActivate(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsVisible)
            {
                _instance = new HelpCenterWindow(uiApp);
                _instance.Show();
                return;
            }

            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.ApplyRightSidePlacement();
            _instance.Activate();
        }

        private HelpCenterWindow(UIApplication uiApp)
        {
            Title = "EMSD AI Tool - Help Center";
            // Keep Help as a normal modeless window (not a dockable pane),
            // but make it narrower and place it against the far-right edge.
            Width = DefaultWindowWidth * SideWindowWidthFactor;
            Height = 720;
            MinWidth = 850;
            MinHeight = 550;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Brushes.White;

            AttachOwner(uiApp);
            ApplyRightSidePlacement();

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border toolbar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8)
            };
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            DockPanel toolbarPanel = new DockPanel();
            toolbar.Child = toolbarPanel;

            StackPanel toolbarButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(toolbarButtons, Dock.Right);
            toolbarPanel.Children.Add(toolbarButtons);

            TextBlock title = new TextBlock
            {
                Text = "EMSD AI Tool Help Center",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                VerticalAlignment = VerticalAlignment.Center
            };
            toolbarPanel.Children.Add(title);

            Button reloadButton = CreateToolbarButton("Reload");
            reloadButton.Click += delegate { ReloadHelp(); };
            toolbarButtons.Children.Add(reloadButton);

            Button browserButton = CreateToolbarButton("Open in Browser");
            browserButton.Click += delegate { OpenHelpInBrowser(); };
            toolbarButtons.Children.Add(browserButton);

            _webView = new WebView2();
            Grid.SetRow(_webView, 1);
            root.Children.Add(_webView);

            Border statusBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            Grid.SetRow(statusBar, 2);
            root.Children.Add(statusBar);

            _statusText = new TextBlock
            {
                Text = "Loading local help...",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
            };
            statusBar.Child = _statusText;

            Content = root;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Re-apply after the HWND is created so DPI/monitor information is final.
            ApplyRightSidePlacement();

            if (!ResolveHelpFiles())
            {
                ShowMissingHelpMessage();
                return;
            }

            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EMSD AI Tool",
                    "WebView2");

                Directory.CreateDirectory(userDataFolder);

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await _webView.EnsureCoreWebView2Async(environment);

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    _helpFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                NavigateToHelpHome();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Help Center WebView2 initialization failed: " + ex);
                _statusText.Text = "WebView2 could not be initialized. Opening the local help in your browser...";
                OpenHelpInBrowser();
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }

                _webView.Dispose();
            }
            catch
            {
            }

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void NavigateToHelpHome()
        {
            if (_webView.CoreWebView2 == null)
            {
                return;
            }

            _statusText.Text = "Loading local help...";
            _webView.CoreWebView2.Navigate("https://" + VirtualHostName + "/index.html");
        }

        private void ReloadHelp()
        {
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Reload();
            }
            else
            {
                NavigateToHelpHome();
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _statusText.Text = e.IsSuccess
                ? "Local help loaded."
                : "The help page could not be loaded. Use Open in Browser to view the local files.";
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Uri uri;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri))
            {
                return;
            }

            if (string.Equals(uri.Host, VirtualHostName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Cancel = true;
            OpenExternalUri(e.Uri);
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternalUri(e.Uri);
        }

        private bool ResolveHelpFiles()
        {
            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                return false;
            }

            _helpFolder = Path.Combine(assemblyDirectory, "Help");
            _indexFile = Path.Combine(_helpFolder, "index.html");
            return Directory.Exists(_helpFolder) && File.Exists(_indexFile);
        }

        private void OpenHelpInBrowser()
        {
            if (string.IsNullOrWhiteSpace(_indexFile) || !File.Exists(_indexFile))
            {
                if (!ResolveHelpFiles())
                {
                    ShowMissingHelpMessage();
                    return;
                }
            }

            OpenExternalUri(_indexFile);
        }

        private static void OpenExternalUri(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Help Center could not open external target: " + ex);
            }
        }

        private void ShowMissingHelpMessage()
        {
            string expected = string.IsNullOrWhiteSpace(_indexFile)
                ? Path.Combine("<plugin folder>", "Help", "index.html")
                : _indexFile;

            _statusText.Text = "Local help files were not found: " + expected;
            TaskDialog.Show(
                "EMSD AI Tool",
                "The local Help Center files were not found.\n\nExpected location:\n" + expected);
        }

        private static Button CreateToolbarButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 30,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private void AttachOwner(UIApplication uiApp)
        {
            try
            {
                _ownerHandle = uiApp != null ? uiApp.MainWindowHandle : IntPtr.Zero;
                if (_ownerHandle == IntPtr.Zero)
                {
                    _ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
                }

                if (_ownerHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = _ownerHandle;
                }
            }
            catch
            {
                _ownerHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Positions this modeless Help window at the far-right side of the same monitor
        /// as Revit. Height is 90% of the monitor work area and the original 1100 DIP
        /// width is reduced by one fifth (1100 -> 880 DIP).
        /// </summary>
        private void ApplyRightSidePlacement()
        {
            try
            {
                Rect workArea;
                double dpiScale = 1.0;

                IntPtr referenceHandle = _ownerHandle;
                if (referenceHandle == IntPtr.Zero)
                {
                    referenceHandle = new WindowInteropHelper(this).Handle;
                }

                if (referenceHandle != IntPtr.Zero && TryGetMonitorWorkArea(referenceHandle, out RECT workPx))
                {
                    dpiScale = GetWindowDpiScale(referenceHandle);
                    workArea = new Rect(
                        workPx.Left / dpiScale,
                        workPx.Top / dpiScale,
                        Math.Max(1, workPx.Right - workPx.Left) / dpiScale,
                        Math.Max(1, workPx.Bottom - workPx.Top) / dpiScale);
                }
                else
                {
                    // Safe fallback for unusual host/window-handle states.
                    workArea = SystemParameters.WorkArea;
                }

                double targetWidth = DefaultWindowWidth * SideWindowWidthFactor;
                targetWidth = Math.Min(targetWidth, workArea.Width);

                double targetHeight = workArea.Height * SideWindowHeightFactor;
                targetHeight = Math.Min(targetHeight, workArea.Height);

                Width = targetWidth;
                Height = targetHeight;
                Left = workArea.Right - targetWidth;
                Top = workArea.Top + ((workArea.Height - targetHeight) / 2.0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Help Center side placement failed: " + ex);
            }
        }

        private static bool TryGetMonitorWorkArea(IntPtr windowHandle, out RECT workArea)
        {
            workArea = default(RECT);

            IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            workArea = info.rcWork;
            return true;
        }

        private static double GetWindowDpiScale(IntPtr windowHandle)
        {
            try
            {
                uint dpi = GetDpiForWindow(windowHandle);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch
            {
            }

            return 1.0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
