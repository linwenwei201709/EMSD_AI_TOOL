using Autodesk.Revit.UI;
using FontAwesome.Sharp;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstaclePaneProvider : IDockablePaneProvider
    {
        private const double InitialDockedPaneWidth = 320.0;
        private const double MinimumDockedPaneWidth = 300.0;
        private const double InitialDockedPaneMinHeight = 320.0;

        public FrameworkElement FrameworkElement { get; }

        public PathObstaclePaneProvider()
        {
            FrameworkElement = BuildView();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = FrameworkElement;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Left,
                MinimumWidth = (int)MinimumDockedPaneWidth,
                MinimumHeight = (int)InitialDockedPaneMinHeight
            };
        }

        private static FrameworkElement BuildView()
        {
            Grid root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                Margin = new Thickness(0),
                Width = InitialDockedPaneWidth,
                MinWidth = MinimumDockedPaneWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            root.Loaded += delegate { root.ClearValue(FrameworkElement.WidthProperty); };
            root.DataContext = PathObstacleRuntime.ViewModel;

            ScrollViewer scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(12, 12, 12, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel content = new StackPanel();
            content.Children.Add(BuildHeader());
            content.Children.Add(BuildList());
            content.Children.Add(BuildEmptyText());
            scrollViewer.Content = content;
            root.Children.Add(scrollViewer);

            return root;
        }

        private static FrameworkElement BuildHeader()
        {
            DockPanel header = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button create = new Button
            {
                Content = "+ Create Area",
                Width = 126,
                Height = 34,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(198, 207, 216)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(24, 42, 56))
            };
            create.SetBinding(Button.CommandProperty, new Binding("CreateAreaCommand"));
            create.SetBinding(UIElement.IsEnabledProperty, new Binding("CanEditItems"));
            actions.Children.Add(create);

            IconChar deleteAllIcon = ResolveFontAwesomeIcon(
                IconChar.Pencil,
                "TrashCan",
                "TrashAlt",
                "Trash");

            Button deleteAll = new Button
            {
                Width = 38,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(198, 207, 216)),
                BorderThickness = new Thickness(1),
                ToolTip = "Clear all restricted areas"
            };
            deleteAll.Content = new IconBlock
            {
                Icon = deleteAllIcon,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteAll.SetBinding(Button.CommandProperty, new Binding("DeleteAllCommand"));
            deleteAll.SetBinding(UIElement.IsEnabledProperty, new Binding("CanDeleteAll"));
            actions.Children.Add(deleteAll);

            DockPanel.SetDock(actions, Dock.Right);
            header.Children.Add(actions);

            header.Children.Add(new TextBlock
            {
                Text = "Restricted Area",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return header;
        }

        private static FrameworkElement BuildList()
        {
            ListBox list = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = BuildItemTemplate(),
                ItemContainerStyle = CreateFlatListBoxItemStyle()
            };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Items"));
            list.SetBinding(UIElement.IsEnabledProperty, new Binding("CanEditItems"));
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            return list;
        }

        private static DataTemplate BuildItemTemplate()
        {
            IconChar locationIcon = ResolveFontAwesomeIcon(
                IconChar.Pencil,
                "Crosshairs",
                "LocationCrosshairs",
                "Bullseye",
                "LocationDot",
                "MapMarkerAlt");
            IconChar deleteIcon = ResolveFontAwesomeIcon(
                IconChar.Pencil,
                "TrashCan",
                "TrashAlt",
                "Trash");

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.White);
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(216, 221, 226)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            border.SetValue(FrameworkElement.MinHeightProperty, 46.0);
            border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 6, 6));

            FrameworkElementFactory row = new FrameworkElementFactory(typeof(Grid));
            border.AppendChild(row);

            FrameworkElementFactory nameColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            nameColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            row.AppendChild(nameColumn);

            FrameworkElementFactory locateColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            locateColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            row.AppendChild(locateColumn);

            FrameworkElementFactory editColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            editColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            row.AppendChild(editColumn);

            FrameworkElementFactory deleteColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            deleteColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            row.AppendChild(deleteColumn);

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetBinding(TextBlock.ToolTipProperty, new Binding("Name"));
            name.SetValue(TextBlock.FontSizeProperty, 12.5);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.Normal);
            name.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(45, 62, 91)));
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            name.SetValue(Grid.ColumnProperty, 0);
            row.AppendChild(name);

            FrameworkElementFactory locate = BuildIconButtonFactory("Locate", "LocateCommand", 1);
            locate.AppendChild(BuildFontAwesomeIconFactory(
                locationIcon,
                14.0,
                Color.FromRgb(45, 62, 91)));
            row.AppendChild(locate);

            FrameworkElementFactory edit = BuildIconButtonFactory("Edit name", "EditCommand", 2);
            edit.AppendChild(BuildFontAwesomeIconFactory(
                IconChar.Pencil,
                14.0,
                Color.FromRgb(45, 62, 91)));
            row.AppendChild(edit);

            FrameworkElementFactory delete = BuildIconButtonFactory("Delete", "DeleteCommand", 3);
            delete.AppendChild(BuildFontAwesomeIconFactory(
                deleteIcon,
                14.0,
                Color.FromRgb(229, 57, 53)));
            row.AppendChild(delete);

            DataTemplate template = new DataTemplate(typeof(PathObstacleItemViewModel));
            template.VisualTree = border;
            return template;
        }

        private static FrameworkElementFactory BuildIconButtonFactory(string toolTip, string commandPath, int column)
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.WidthProperty, 32.0);
            button.SetValue(Button.HeightProperty, 26.0);
            button.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            button.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.SetValue(Button.PaddingProperty, new Thickness(0));
            button.SetValue(Button.BackgroundProperty, Brushes.White);
            button.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(198, 207, 216)));
            button.SetValue(Button.BorderThicknessProperty, new Thickness(1));
            button.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
            button.SetValue(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            button.SetValue(Button.ToolTipProperty, toolTip);
            button.SetValue(Grid.ColumnProperty, column);
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static FrameworkElementFactory BuildFontAwesomeIconFactory(
            IconChar icon,
            double fontSize,
            Color color)
        {
            FrameworkElementFactory iconBlock = new FrameworkElementFactory(typeof(IconBlock));
            iconBlock.SetValue(IconBlock.IconProperty, icon);
            iconBlock.SetValue(TextBlock.FontSizeProperty, fontSize);
            iconBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(color));
            iconBlock.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            iconBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            return iconBlock;
        }

        private static IconChar ResolveFontAwesomeIcon(IconChar fallback, params string[] names)
        {
            if (names != null)
            {
                foreach (string name in names)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (Enum.TryParse(name, true, out IconChar icon))
                    {
                        return icon;
                    }
                }
            }

            return fallback;
        }

        private static Style CreateFlatListBoxItemStyle()
        {
            Style style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = presenter }));

            return style;
        }

        private static FrameworkElement BuildEmptyText()
        {
            TextBlock empty = new TextBlock
            {
                Text = "No restricted areas found.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 36, 0, 0)
            };
            empty.SetBinding(UIElement.VisibilityProperty, new Binding("HasNoItems") { Converter = new BooleanToVisibilityConverter() });
            return empty;
        }
    }
}