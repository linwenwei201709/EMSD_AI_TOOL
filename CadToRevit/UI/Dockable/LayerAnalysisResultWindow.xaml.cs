using CadToRevit.Infrastructure.Localization;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadToRevit.UI.Dockable
{
    public class LayerAnalysisResultWindow : Window
    {
        public LayerAnalysisResultWindow(LayerStandardAnalyzeResult result, string dwgName)
        {
            LayerAnalysisResultViewData data = LayerAnalysisResultViewData.Build(result, dwgName);
            Title = Loc.T("LayerAnalysis.WindowTitle");
            Width = 800;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            Content = BuildContent(data);
        }

        private static FrameworkElement BuildContent(LayerAnalysisResultViewData data)
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            StackPanel host = new StackPanel { Margin = new Thickness(16) };
            scroll.Content = host;

            host.Children.Add(BuildCollapsibleSection(Loc.T("LayerAnalysis.StandardTitle"), data.RuleDescriptions, false));
            host.Children.Add(BuildSection(data.DwgLine, new[] { Loc.T("LayerAnalysis.ValidationCompleted"), data.SummaryLine }));
            host.Children.Add(BuildSection(Loc.T("LayerAnalysis.ValidLayers"), data.ValidDisplayLines));
            host.Children.Add(BuildSection(Loc.T("LayerAnalysis.InvalidLayers"), data.InvalidDisplayLines, includeBottomMargin: false));

            Grid.SetRow(scroll, 0);
            root.Children.Add(scroll);

            DockPanel footer = new DockPanel
            {
                Margin = new Thickness(16, 8, 16, 16),
                LastChildFill = false
            };
            Button closeButton = new Button
            {
                Content = Loc.T("LayerAnalysis.Close"),
                MinWidth = 100,
                Height = 30,
                IsDefault = true,
                IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButton.Click += (s, e) =>
            {
                Window window = Window.GetWindow(closeButton);
                window?.Close();
            };
            DockPanel.SetDock(closeButton, Dock.Right);
            footer.Children.Add(closeButton);
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            return root;
        }

        private static Border BuildCollapsibleSection(string title, IEnumerable<string> lines, bool isExpandedByDefault, bool includeBottomMargin = true)
        {
            Border section = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = includeBottomMargin ? new Thickness(0, 0, 0, 10) : new Thickness(0)
            };

            StackPanel panel = new StackPanel();
            StackPanel header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock toggleIcon = new TextBlock
            {
                Text = isExpandedByDefault ? "▾" : "▸",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            TextBlock titleText = new TextBlock
            {
                Text = title ?? string.Empty,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button toggleButton = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Content = header,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            header.Children.Add(toggleIcon);
            header.Children.Add(titleText);
            panel.Children.Add(toggleButton);

            StackPanel contentPanel = new StackPanel
            {
                Visibility = isExpandedByDefault ? Visibility.Visible : Visibility.Collapsed
            };
            foreach (string line in lines ?? Enumerable.Empty<string>())
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = line ?? string.Empty,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            panel.Children.Add(contentPanel);

            // Toggle only this section body and keep the rest of window unchanged.
            toggleButton.Click += (s, e) =>
            {
                bool expand = contentPanel.Visibility != Visibility.Visible;
                contentPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                toggleIcon.Text = expand ? "▾" : "▸";
            };

            section.Child = panel;
            return section;
        }

        private static Border BuildSection(string title, IEnumerable<string> lines, bool includeBottomMargin = true)
        {
            Border section = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = includeBottomMargin ? new Thickness(0, 0, 0, 10) : new Thickness(0)
            };

            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title ?? string.Empty,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (string line in lines ?? Enumerable.Empty<string>())
            {
                panel.Children.Add(new TextBlock
                {
                    Text = line ?? string.Empty,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            section.Child = panel;
            return section;
        }

        private sealed class LayerAnalysisResultViewData
        {
            public List<string> RuleDescriptions { get; set; } = new List<string>();
            public string DwgLine { get; set; }
            public string SummaryLine { get; set; }
            public List<string> ValidDisplayLines { get; set; } = new List<string>();
            public List<string> InvalidDisplayLines { get; set; } = new List<string>();

            public static LayerAnalysisResultViewData Build(LayerStandardAnalyzeResult result, string dwgName)
            {
                LayerStandardAnalyzeResult safe = result ?? new LayerStandardAnalyzeResult();
                List<LayerStandardMatchItem> valid = safe.Matches.Where(x => x != null && x.IsValid).ToList();
                List<LayerStandardMatchItem> invalid = safe.Matches.Where(x => x != null && !x.IsValid).ToList();
                return new LayerAnalysisResultViewData
                {
                    RuleDescriptions = LayerStandardAnalyzer.BuildRuleDescriptions(),
                    DwgLine = Loc.T("LayerAnalysis.DwgFormat", string.IsNullOrWhiteSpace(dwgName) ? Loc.T("LayerAnalysis.Unknown") : dwgName),
                    SummaryLine = Loc.T("LayerAnalysis.SummaryFormat", safe.TotalLayers, safe.ValidLayers),
                    ValidDisplayLines = valid.Count > 0
                        ? valid.Select(x => x.LayerName + " : " + x.MatchedStandard).ToList()
                        : new List<string> { Loc.T("LayerAnalysis.None") },
                    InvalidDisplayLines = invalid.Count > 0
                        ? invalid.Select(x => x.LayerName + " : " + Loc.T("LayerAnalysis.UnmatchedStandard")).ToList()
                        : new List<string> { Loc.T("LayerAnalysis.None") }
                };
            }
        }
    }
}
