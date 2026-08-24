using CadToRevit.Services.ExportDrawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.Dialogs
{
    public sealed class ExportDrawingPdfPreviewWindow : Window
    {
        private readonly ExportDrawingPdfResult _exportResult;
        private WebView2 _webView;

        public ExportDrawingPdfPreviewWindow(ExportDrawingPdfResult exportResult)
        {
            _exportResult = exportResult ?? throw new ArgumentNullException(nameof(exportResult));

            Title = "Export Drawing Preview";
            ApplyInitialWindowSize();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            BuildWebViewContent();
            Loaded += OnLoaded;
        }

        public void SetRevitOwner()
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

        private void ApplyInitialWindowSize()
        {
            Rect workArea = SystemParameters.WorkArea;
            double targetWidth = workArea.Width * 0.85;
            double targetHeight = workArea.Height * 0.8;

            Width = Math.Min(Math.Max(targetWidth, 1000), workArea.Width);
            Height = Math.Min(Math.Max(targetHeight, 700), workArea.Height);
            MinWidth = Math.Min(1000, workArea.Width);
            MinHeight = Math.Min(700, workArea.Height);
        }

        private void BuildWebViewContent()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border toolbar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            toolbar.Child = buttons;

            buttons.Children.Add(CreateIconButton("Download PDF", "\xE896", SavePdf));
            buttons.Children.Add(CreateIconButton("Close", "\xE74D", CloseWindow));

            _webView = new WebView2();
            Grid.SetRow(_webView, 1);
            root.Children.Add(_webView);

            Content = root;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            try
            {
                if (_webView == null ||
                    string.IsNullOrWhiteSpace(_exportResult.PdfPath) ||
                    !File.Exists(_exportResult.PdfPath))
                {
                    throw new FileNotFoundException("Temporary PDF was not found.", _exportResult.PdfPath);
                }

                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EMSD AI Tool",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.Source = new Uri(_exportResult.PdfPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Export Drawing PDF WebView2 preview failed: " + ex);
                BuildFallbackContent();
            }
        }

        private void BuildFallbackContent()
        {
            Grid root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel content = new StackPanel { Orientation = Orientation.Vertical };
            Grid.SetRow(content, 0);
            root.Children.Add(content);

            content.Children.Add(new TextBlock
            {
                Text = "PDF generated successfully.",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 18)
            });

            content.Children.Add(CreateInfoText("File: " + _exportResult.FileName));
            content.Children.Add(CreateInfoText("Generated: " + _exportResult.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            content.Children.Add(CreateInfoText("Temporary PDF: " + _exportResult.PdfPath));

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            buttons.Children.Add(CreateButton("Open PDF", OpenPdf));
            buttons.Children.Add(CreateButton("Download PDF", SavePdf));
            buttons.Children.Add(CreateButton("Close", CloseWindow));

            Content = root;
        }

        private void OpenPdf(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _exportResult.PdfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export Drawing Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SavePdf(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save Export Drawing",
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = _exportResult.FileName,
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                File.Copy(_exportResult.PdfPath, dialog.FileName, true);
                MessageBox.Show(this, "PDF exported successfully.", "Export Drawing Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export Drawing Preview", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _webView?.Dispose();
            }
            catch
            {
            }

            base.OnClosed(e);
        }

        private static TextBlock CreateInfoText(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 44, 52)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static Button CreateButton(string text, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                MinWidth = 104,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0)
            };
            button.Click += handler;
            return button;
        }

        private static Button CreateIconButton(string text, string iconGlyph, RoutedEventHandler handler)
        {
            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            content.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            });

            content.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });

            Button button = new Button
            {
                Content = content,
                MinWidth = 124,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
            button.Click += handler;
            return button;
        }
    }
}
