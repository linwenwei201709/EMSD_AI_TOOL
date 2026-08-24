using Autodesk.Revit.UI;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleManagerWindow : Window
    {
        private static PathObstacleManagerWindow _instance;
        private static double? _lastLeft;
        private static double? _lastTop;
        private readonly PathObstacleManagerViewModel _viewModel = new PathObstacleManagerViewModel();

        public static void ShowOrActivate(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsVisible)
            {
                _instance = new PathObstacleManagerWindow(uiApp);
                _instance.Show();
                PathObstacleRuntime.RequestRefresh();
                return;
            }

            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.Activate();
            PathObstacleRuntime.RequestRefresh();
        }

        internal static void UpdateRecords(IEnumerable<PathObstacleRecord> records)
        {
            PathObstacleManagerWindow window = _instance;
            if (window == null)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(new Action(delegate
            {
                window._viewModel.SetRecords(records);
            }));
        }

        private PathObstacleManagerWindow(UIApplication uiApp)
        {
            Title = "Path Obstacle Manager";
            Width = 400;
            Height = 680;
            MinWidth = 400;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Brushes.White;
            DataContext = _viewModel;
            AttachOwner(uiApp);
            ApplyInitialPosition();

            Content = BuildContent();
            LocationChanged += delegate
            {
                _lastLeft = Left;
                _lastTop = Top;
            };
            Closed += delegate
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                }
            };
        }

        private UIElement BuildContent()
        {
            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            TextBlock title = new TextBlock
            {
                Text = "Path Obstacle Manager",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            Button refresh = new Button
            {
                Content = "Refresh",
                Width = 96,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            refresh.Click += delegate { PathObstacleRuntime.RequestRefresh(); };
            Grid.SetRow(refresh, 1);
            root.Children.Add(refresh);

            Grid body = new Grid();
            Grid.SetRow(body, 2);
            root.Children.Add(body);

            ListView list = new ListView
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
                BorderThickness = new Thickness(1)
            };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Records"));
            list.ItemTemplate = BuildItemTemplate();
            body.Children.Add(list);

            TextBlock empty = new TextBlock
            {
                Text = "No path obstacles found.",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Binding emptyBinding = new Binding("HasNoRecords")
            {
                Converter = new BooleanToVisibilityConverter()
            };
            empty.SetBinding(VisibilityProperty, emptyBinding);
            body.Children.Add(empty);

            return root;
        }

        private static DataTemplate BuildItemTemplate()
        {
            FrameworkElementFactory row = new FrameworkElementFactory(typeof(Grid));
            row.SetValue(MarginProperty, new Thickness(6, 5, 6, 5));

            FrameworkElementFactory nameColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            nameColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(170));
            row.AppendChild(nameColumn);

            FrameworkElementFactory locateColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            locateColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(74));
            row.AppendChild(locateColumn);

            FrameworkElementFactory deleteColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            deleteColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(74));
            row.AppendChild(deleteColumn);

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            Binding nameBinding = new Binding("Name");
            name.SetBinding(TextBlock.TextProperty, nameBinding);
            name.SetBinding(TextBlock.ToolTipProperty, new Binding("Name"));
            name.SetValue(TextBlock.FontSizeProperty, 13.5);
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            name.SetValue(Grid.ColumnProperty, 0);
            row.AppendChild(name);

            FrameworkElementFactory locate = new FrameworkElementFactory(typeof(Button));
            locate.SetValue(Button.ContentProperty, "Locate");
            locate.SetValue(Button.WidthProperty, 64.0);
            locate.SetValue(Button.HeightProperty, 28.0);
            locate.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
            locate.SetValue(Grid.ColumnProperty, 1);
            locate.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnLocateClicked));
            row.AppendChild(locate);

            FrameworkElementFactory delete = new FrameworkElementFactory(typeof(Button));
            delete.SetValue(Button.ContentProperty, "Delete");
            delete.SetValue(Button.WidthProperty, 64.0);
            delete.SetValue(Button.HeightProperty, 28.0);
            delete.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
            delete.SetValue(Grid.ColumnProperty, 2);
            delete.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnDeleteClicked));
            row.AppendChild(delete);

            DataTemplate template = new DataTemplate(typeof(PathObstacleRecord));
            template.VisualTree = row;
            return template;
        }

        private static void OnLocateClicked(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            PathObstacleRecord record = element != null ? element.DataContext as PathObstacleRecord : null;
            if (record != null)
            {
                PathObstacleRuntime.RequestLocate(record);
            }
        }

        private static void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            PathObstacleRecord record = element != null ? element.DataContext as PathObstacleRecord : null;
            if (record == null)
            {
                return;
            }

            if (PathObstacleConfirmWindow.Confirm(
                "Path Obstacle Manager",
                "Delete this path obstacle?\nThis action will remove the obstacle from the model."))
            {
                PathObstacleRuntime.RequestDelete(record);
            }
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
        }

        private void ApplyInitialPosition()
        {
            Rect workArea = SystemParameters.WorkArea;

            if (_lastLeft.HasValue && _lastTop.HasValue)
            {
                Left = Math.Max(workArea.Left, Math.Min(_lastLeft.Value, workArea.Right - Width));
                Top = Math.Max(workArea.Top, Math.Min(_lastTop.Value, workArea.Bottom - Height));
                return;
            }

            Left = workArea.Left + 32;
            Top = workArea.Top + 140;

            if (Left + Width > workArea.Right)
            {
                Left = Math.Max(workArea.Left, workArea.Right - Width);
            }

            if (Top + Height > workArea.Bottom)
            {
                Top = Math.Max(workArea.Top, workArea.Bottom - Height);
            }
        }
    }
}
