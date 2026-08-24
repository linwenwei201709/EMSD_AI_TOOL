using CadToRevit.Models.Units;
using CadToRevit.Services.Dwg;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CadToRevit.UI.Windows
{
    public sealed class DwgSourceUnitSelectionWindow : Window
    {
        private readonly ComboBox _unitCombo = new ComboBox();
        private readonly DwgUnitDetectionResult _detection;

        public DwgSourceUnitSelectionWindow(DwgUnitDetectionResult detection)
        {
            _detection = detection ?? new DwgUnitDetectionResult();
            SelectedSourceUnit = SourceUnit.Auto;
            EvidenceText = _detection.Evidence ?? string.Empty;
            InitializeComponent();
        }

        public SourceUnit SelectedSourceUnit { get; private set; }

        public string EvidenceText { get; private set; }

        public string SelectedEvidence
        {
            get { return EvidenceText; }
        }

        private void InitializeComponent()
        {
            Title = "DWG Source Unit";
            Width = 560;
            MinHeight = 360;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
            TextBlock titleBlock = new TextBlock
            {
                Text = "DWG Source Unit",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 40, 54))
            };
            header.Child = titleBlock;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid contentGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 22)
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border icon = CreateWarningIcon();
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);

            StackPanel content = new StackPanel
            {
                Margin = new Thickness(18, 0, 0, 0)
            };
            Grid.SetColumn(content, 1);
            contentGrid.Children.Add(content);

            content.Children.Add(CreateSection("Detected Unit", GetDetectedText()));

            string warningText = GetWarningText();
            if (_detection.HasConflict || !string.IsNullOrWhiteSpace(warningText))
            {
                content.Children.Add(CreateSection("Warning", warningText));
            }

            TextBlock sourceLabel = CreateLabel("Source Unit");
            sourceLabel.Margin = new Thickness(0, 12, 0, 8);
            content.Children.Add(sourceLabel);

            _unitCombo.Width = 220;
            _unitCombo.Height = 34;
            _unitCombo.HorizontalAlignment = HorizontalAlignment.Left;
            _unitCombo.HorizontalContentAlignment = HorizontalAlignment.Center;
            _unitCombo.VerticalContentAlignment = VerticalAlignment.Center;
            _unitCombo.Margin = new Thickness(0, 0, 0, 16);
            Style itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 6, 0, 6)));
            _unitCombo.ItemContainerStyle = itemStyle;
            _unitCombo.Items.Add(SourceUnit.Millimeter);
            _unitCombo.Items.Add(SourceUnit.Inch);
            _unitCombo.ItemTemplate = CreateCenteredItemTemplate();
            _unitCombo.SelectedItem = NormalizeSuggestedUnit(_detection.SuggestedUnit);
            content.Children.Add(_unitCombo);

            content.Children.Add(CreateSection(
                "Note",
                "If the DWG source unit is wrong, the imported model scale will be wrong. Please re-import the DWG if you need to change the unit later."));

            scroll.Content = contentGrid;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            Border footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Padding = new Thickness(24, 14, 24, 14)
            };
            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancel = CreateButton("Cancel", false);
            cancel.Click += delegate { DialogResult = false; };
            buttons.Children.Add(cancel);

            Button confirm = CreateButton("Confirm", true);
            confirm.IsDefault = true;
            confirm.Click += Confirm_Click;
            buttons.Children.Add(confirm);

            footer.Child = buttons;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private static StackPanel CreateSection(string label, string value)
        {
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(CreateLabel(label));
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "Unknown" : value,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(54, 63, 77)),
                Margin = new Thickness(0, 4, 0, 0)
            });
            return panel;
        }

        private static Border CreateWarningIcon()
        {
            Border circle = new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(26),
                Background = new SolidColorBrush(Color.FromRgb(244, 146, 35)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };

            circle.Child = new TextBlock
            {
                Text = "!",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            return circle;
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 46, 62))
            };
        }

        private static Button CreateButton(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Width = 120,
                Height = 38,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };

            if (primary)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(30, 115, 190));
                button.Foreground = Brushes.White;
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 115, 190));
            }

            return button;
        }

        private string GetDetectedText()
        {
            return _detection.IsResolved && _detection.DetectedUnit != SourceUnit.Auto
                ? _detection.DetectedUnit.ToString()
                : "Unknown";
        }

        private string GetWarningText()
        {
            if (_detection.DetectedUnit == SourceUnit.Feet || _detection.DetectedUnit == SourceUnit.Meter)
            {
                return "The DWG unit appears to be Feet/Meter, but this version only supports Millimeter and Inch for DWG import. Please confirm the source unit before importing.";
            }

            return _detection.WarningMessage ?? string.Empty;
        }

        private static SourceUnit NormalizeSuggestedUnit(SourceUnit unit)
        {
            return unit == SourceUnit.Inch ? SourceUnit.Inch : SourceUnit.Millimeter;
        }

        private static DataTemplate CreateCenteredItemTemplate()
        {
            FrameworkElementFactory textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetBinding(TextBlock.TextProperty, new Binding());
            textBlock.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            textBlock.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            textBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            return new DataTemplate
            {
                VisualTree = textBlock
            };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (!(_unitCombo.SelectedItem is SourceUnit unit) || unit == SourceUnit.Auto)
            {
                MessageBox.Show(
                    this,
                    "Please select a concrete DWG source unit before importing.",
                    "DWG Source Unit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SelectedSourceUnit = unit;
            EvidenceText = BuildEvidenceText(unit);
            DialogResult = true;
        }

        private string BuildEvidenceText(SourceUnit confirmedUnit)
        {
            string evidence = string.IsNullOrWhiteSpace(_detection.Evidence) ? "Unknown" : _detection.Evidence;
            evidence += "; Suggested=" + NormalizeSuggestedUnit(_detection.SuggestedUnit);
            evidence += "; UserConfirmed=" + confirmedUnit;
            string warningText = GetWarningText();
            if (!string.IsNullOrWhiteSpace(warningText))
            {
                evidence += "; Warning=" + warningText;
            }

            return evidence;
        }
    }
}
