using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.Part3
{
    public class Part3MessageWindow : Window
    {
        private const string WindowTitle = "EMSD AI Tool";

        private Part3MessageWindow(UIApplication uiApp, string message)
        {
            Title = WindowTitle;
            Width = 520;
            Height = 220;
            MinWidth = 420;
            MinHeight = 180;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.White;

            AttachOwner(uiApp);
            Content = BuildContent(message);
        }

        public static void ShowMessage(UIApplication uiApp, string message)
        {
            Part3MessageWindow window = new Part3MessageWindow(uiApp, message);
            window.ShowDialog();
        }

        private UIElement BuildContent(string message)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(24)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock text = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(text, 0);
            root.Children.Add(text);

            Button okButton = new Button
            {
                Content = "OK",
                Width = 110,
                Height = 34,
                IsDefault = true,
                IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            okButton.Click += delegate
            {
                DialogResult = true;
                Close();
            };
            Grid.SetRow(okButton, 1);
            root.Children.Add(okButton);

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
    }
}
