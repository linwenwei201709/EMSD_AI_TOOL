using CadToRevit.Services.Rooms.LayoutPlanReports;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CadToRevit.UI.Dialogs
{
    public sealed class LayoutPlanReportPdfPreviewWindow : Window
    {
        private readonly LayoutPlanReportPdfExportResult _exportResult;
        private WebView2 _webView;

        public LayoutPlanReportPdfPreviewWindow(LayoutPlanReportPdfExportResult exportResult)
        {
            _exportResult = exportResult ?? throw new ArgumentNullException(nameof(exportResult));
            Title = "AHU Layout Plan Report Preview";
            Width = 1100;
            Height = 760;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            BuildContent();
            Loaded += OnLoaded;
        }

        private void BuildContent()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            toolbar.Children.Add(CreateButton("Download PDF", SavePdf));
            toolbar.Children.Add(CreateButton("Close", CloseWindow));
            root.Children.Add(toolbar);

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
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EMSD AI Tool", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.Source = new Uri(_exportResult.PdfPath);
            }
            catch
            {
                Process.Start(new ProcessStartInfo { FileName = _exportResult.PdfPath, UseShellExecute = true });
            }
        }

        public void SetRevitOwner()
        {
            try
            {
                new WindowInteropHelper(this).Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
            }
        }

        private void SavePdf(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save AHU Layout Plan Report",
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

            File.Copy(_exportResult.PdfPath, dialog.FileName, true);
            MessageBox.Show(this, "PDF exported successfully.", "AHU Layout Plan Report Preview", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private static Button CreateButton(string text, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                MinWidth = 120,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            button.Click += handler;
            return button;
        }
    }
}
