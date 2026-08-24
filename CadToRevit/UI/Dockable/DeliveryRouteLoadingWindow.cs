using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public sealed class DeliveryRouteLoadingWindow : Window
    {
        public DeliveryRouteLoadingWindow()
        {
            Title = "EMSD AI Tool";
            Width = 360;
            Height = 160;
            MinWidth = 320;
            MinHeight = 140;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Brushes.White;
            Content = BuildContent();
        }

        private UIElement BuildContent()
        {
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Generating delivery route...",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Please wait.",
                FontSize = 14
            });

            return panel;
        }
    }
}
