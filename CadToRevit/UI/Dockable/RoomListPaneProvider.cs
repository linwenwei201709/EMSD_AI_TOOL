using Autodesk.Revit.UI;
using FontAwesome.Sharp;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomListPaneProvider : IDockablePaneProvider
    {
        private const double InitialDockedPaneWidth = 320.0;
        private const double MinimumDockedPaneWidth = 300.0;
        private const double InitialDockedPaneMinHeight = 320.0;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public FrameworkElement FrameworkElement { get; }

        public RoomListPaneProvider()
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
            root.Loaded += (sender, args) =>
            {
                // Use Width only as the first desired docked size.
                // After Revit creates the pane, clear it so users can freely resize it.
                root.ClearValue(FrameworkElement.WidthProperty);
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            ScrollViewer scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(8, 10, 8, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scrollViewer, 0);

            StackPanel contentPanel = new StackPanel();
            contentPanel.Children.Add(BuildSectionHeader("Room List", "+ Create Room", "CreateRoomCommand", "AutoDetectRoomsCommand"));

            ListBox rooms = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            rooms.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Rooms"));
            rooms.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedRoom") { Mode = BindingMode.TwoWay });
            rooms.ItemTemplate = BuildItemTemplate();
            rooms.ItemContainerStyle = CreateFlatListBoxItemStyle();
            rooms.PreviewMouseWheel += OnNestedListPreviewMouseWheel;
            ScrollViewer.SetVerticalScrollBarVisibility(rooms, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(rooms, ScrollBarVisibility.Disabled);
            contentPanel.Children.Add(rooms);
            contentPanel.Children.Add(BuildLiftSection());

            scrollViewer.Content = contentPanel;
            root.Children.Add(scrollViewer);

            root.DataContext = RoomRecognitionPaneRuntime.ListViewModel;
            return root;
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
            style.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnListBoxItemPreviewMouseLeftButtonDown)));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = presenter }));

            return style;
        }

        private static void OnListBoxItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e == null || IsInsideButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            FrameworkElement element = sender as FrameworkElement;
            if (element == null)
            {
                return;
            }

            RoomListItemViewModel room = element.DataContext as RoomListItemViewModel;
            if (room != null)
            {
                RoomRecognitionPaneRuntime.OnListRoomSelected(room);
                e.Handled = true;
                return;
            }

            LiftListItemViewModel lift = element.DataContext as LiftListItemViewModel;
            if (lift != null)
            {
                RoomRecognitionPaneRuntime.OnListLiftSelected(lift);
                e.Handled = true;
            }
        }

        private static void OnNestedListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e == null || e.Handled)
            {
                return;
            }

            ScrollViewer parentScrollViewer = FindVisualParent<ScrollViewer>(sender as DependencyObject);
            if (parentScrollViewer == null)
            {
                return;
            }

            int configuredLines = SystemParameters.WheelScrollLines;
            double scrollAmount;
            if (configuredLines < 0)
            {
                scrollAmount = Math.Max(48.0, parentScrollViewer.ViewportHeight);
            }
            else
            {
                scrollAmount = Math.Max(48.0, configuredLines * 16.0);
            }

            double targetOffset = e.Delta > 0
                ? parentScrollViewer.VerticalOffset - scrollAmount
                : parentScrollViewer.VerticalOffset + scrollAmount;

            parentScrollViewer.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = child;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                T matched = current as T;
                if (matched != null)
                {
                    return matched;
                }
            }

            return null;
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static FrameworkElement BuildSectionHeader(string titleText, string createText, string createCommandPath, string autoCommandPath)
        {
            DockPanel header = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(actions, Dock.Right);
            actions.Children.Add(CreateHeaderButton(createText, createCommandPath, 126));
            actions.Children.Add(CreateRecognitionHeaderButton(autoCommandPath));
            header.Children.Add(actions);

            header.Children.Add(new TextBlock
            {
                Text = titleText,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return header;
        }

        private static Button CreateHeaderButton(string content, string commandPath, double width)
        {
            Button button = new Button
            {
                Content = content,
                Width = width,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(198, 207, 216)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(24, 42, 56))
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static Button CreateRecognitionHeaderButton(string commandPath)
        {
            Button button = new Button
            {
                Content = CreateRecognitionIcon(),
                Width = 36,
                Height = 32,
                Margin = new Thickness(8, 0, 12, 0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(198, 207, 216)),
                BorderThickness = new Thickness(1)
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static Image CreateRecognitionIcon()
        {
            System.Drawing.Bitmap bitmap = global::CadToRevit.ResourceIcons.recognition_32x32;
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(20, 20));
                source.Freeze();

                return new Image
                {
                    Source = source,
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private static DataTemplate BuildItemTemplate()
        {
            FrameworkElementFactory container = new FrameworkElementFactory(typeof(Border));
            container.SetValue(Border.StyleProperty, CreateCardBorderStyle(false, new Thickness(10, 10, 10, 10)));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

            stack.AppendChild(BuildCardTitleRowFactory());
            stack.AppendChild(BuildRoomFieldFactory("RoomSizeLine", new Thickness(0, 8, 0, 0)));
            stack.AppendChild(BuildRoomFieldFactory("DoorSizeLine", new Thickness(0, 4, 0, 0)));
            stack.AppendChild(BuildRoomFieldFactory("AreaSummaryLine", new Thickness(0, 4, 0, 0)));

            container.AppendChild(stack);
            return new DataTemplate { VisualTree = container };
        }

        private static Style CreateCardBorderStyle(bool rounded, Thickness padding)
        {
            Style style = new Style(typeof(Border));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(224, 224, 224))));
            style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Border.MarginProperty, new Thickness(0, 0, 8, 10)));
            style.Setters.Add(new Setter(Border.PaddingProperty, padding));

            if (rounded)
            {
                style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(6)));
            }

            DataTrigger selectedTrigger = new DataTrigger
            {
                Binding = new Binding("IsSelected"),
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0, 120, 215))));
            selectedTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2)));
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 245, 255))));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static FrameworkElementFactory BuildCardTitleRowFactory()
        {
            FrameworkElementFactory row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetValue(DockPanel.LastChildFillProperty, true);

            FrameworkElementFactory actions = new FrameworkElementFactory(typeof(StackPanel));
            actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actions.SetValue(DockPanel.DockProperty, Dock.Right);
            actions.AppendChild(BuildCardActionButtonFactory("Edit", "EditCommand"));
            actions.AppendChild(BuildCardActionButtonFactory("Delete", "DeleteCommand"));
            row.AppendChild(actions);

            FrameworkElementFactory title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            title.SetValue(TextBlock.FontSizeProperty, 14.0);
            title.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(45, 62, 91)));
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            row.AppendChild(title);
            return row;
        }

        private static FrameworkElementFactory BuildCardActionButtonFactory(string text, string commandPath)
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.FontSizeProperty, 11.0);
            button.SetValue(Button.HeightProperty, 32.0);
            button.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
            button.SetValue(Button.BackgroundProperty, Brushes.White);
            button.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(198, 207, 216)));
            button.SetValue(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(24, 42, 56)));
            button.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (string.Equals(text, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                button.SetValue(Button.WidthProperty, 34.0);
                button.SetValue(Button.MinWidthProperty, 34.0);
                button.SetValue(Button.PaddingProperty, new Thickness(0));
                button.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
                button.SetValue(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center);
                button.SetValue(Button.ToolTipProperty, "Delete");

                IconChar deleteIcon = ResolveFontAwesomeIcon(
                    IconChar.Pencil,
                    "TrashCan",
                    "TrashAlt",
                    "Trash");

                FrameworkElementFactory icon = new FrameworkElementFactory(typeof(IconBlock));
                icon.SetValue(IconBlock.IconProperty, deleteIcon);
                icon.SetValue(TextBlock.FontSizeProperty, 15.0);
                icon.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(229, 57, 53)));
                icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                button.AppendChild(icon);
            }
            else
            {
                button.SetValue(Button.ContentProperty, text);
                button.SetValue(Button.WidthProperty, 60.0);
                button.SetValue(Button.MinWidthProperty, 60.0);
            }

            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
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

        private static FrameworkElementFactory BuildRoomFieldFactory(string bindingPath, Thickness margin)
        {
            FrameworkElementFactory field = new FrameworkElementFactory(typeof(TextBlock));
            field.SetValue(TextBlock.MarginProperty, margin);
            field.SetValue(TextBlock.FontSizeProperty, 12.0);
            field.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(90, 102, 116)));
            field.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            return field;
        }

        private static FrameworkElement BuildLiftSection()
        {
            StackPanel section = new StackPanel
            {
                Margin = new Thickness(0, 16, 0, 0)
            };

            section.Children.Add(BuildSectionHeader("Lift List", "+ Create Lift", "CreateLiftCommand", "AutoDetectLiftsCommand"));

            ListBox lifts = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            lifts.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Lifts"));
            lifts.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedLift") { Mode = BindingMode.TwoWay });
            lifts.ItemTemplate = BuildLiftTemplate();
            lifts.ItemContainerStyle = CreateFlatListBoxItemStyle();
            lifts.PreviewMouseWheel += OnNestedListPreviewMouseWheel;
            ScrollViewer.SetVerticalScrollBarVisibility(lifts, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(lifts, ScrollBarVisibility.Disabled);
            section.Children.Add(lifts);

            return section;
        }

        private static DataTemplate BuildLiftTemplate()
        {
            FrameworkElementFactory container = new FrameworkElementFactory(typeof(Border));
            container.SetValue(Border.StyleProperty, CreateCardBorderStyle(true, new Thickness(12)));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));

            stack.AppendChild(BuildCardTitleRowFactory());
            stack.AppendChild(BuildRoomFieldFactory("LiftInternalLine", new Thickness(0, 8, 0, 0)));
            stack.AppendChild(BuildRoomFieldFactory("DoorSizeLine", new Thickness(0, 4, 0, 0)));
            stack.AppendChild(BuildRoomFieldFactory("CapacityLine", new Thickness(0, 4, 0, 0)));

            container.AppendChild(stack);
            return new DataTemplate { VisualTree = container };
        }

        private static FrameworkElementFactory BuildLiftFieldFactory(string label, string bindingPath)
        {
            FrameworkElementFactory row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetValue(DockPanel.LastChildFillProperty, true);
            row.SetValue(DockPanel.MarginProperty, new Thickness(0, 0, 0, 4));

            FrameworkElementFactory labelBlock = new FrameworkElementFactory(typeof(TextBlock));
            labelBlock.SetValue(TextBlock.TextProperty, label);
            labelBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
            labelBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(90, 102, 116)));
            labelBlock.SetValue(TextBlock.WidthProperty, 90.0);
            labelBlock.SetValue(DockPanel.DockProperty, Dock.Left);

            FrameworkElementFactory valueBlock = new FrameworkElementFactory(typeof(TextBlock));
            valueBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
            valueBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(90, 102, 116)));
            valueBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            valueBlock.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));

            row.AppendChild(labelBlock);
            row.AppendChild(valueBlock);
            return row;
        }
    }
}