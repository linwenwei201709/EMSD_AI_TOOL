using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models.Rooms;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WpfGrid = System.Windows.Controls.Grid;

namespace CadToRevit.UI
{
    internal sealed class FamilyLibraryManagerWindow : Window
    {
        private readonly FamilyLibraryManagerViewModel _viewModel;
        private DataGrid _dataGrid;

        public FamilyLibraryManagerWindow()
        {
            _viewModel = new FamilyLibraryManagerViewModel();
            _viewModel.Reload();

            DataContext = _viewModel;
            Title = Loc.T(LocalizedKeys.FamilyLibrary.Title);
            Width = 1320;
            Height = 760;
            MinWidth = 1100;
            MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            Content = BuildContent();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.HasUnsavedChanges)
            {
                if (!ShowLocalizedConfirmation(
                    Loc.T(LocalizedKeys.FamilyLibrary.UnsavedChangesClose),
                    MessageBoxImage.Warning))
                {
                    e.Cancel = true;
                }
            }

            base.OnClosing(e);
        }

        private FrameworkElement BuildContent()
        {
            WpfGrid root = new WpfGrid
            {
                Margin = new Thickness(22, 20, 22, 18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            FrameworkElement header = BuildHeader();
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            FrameworkElement toolbar = BuildToolbar();
            WpfGrid.SetRow(toolbar, 1);
            root.Children.Add(toolbar);

            Border gridBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(202, 210, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            };
            _dataGrid = BuildGrid();
            gridBorder.Child = _dataGrid;
            WpfGrid.SetRow(gridBorder, 2);
            root.Children.Add(gridBorder);

            DockPanel footer = BuildFooter();
            WpfGrid.SetRow(footer, 3);
            root.Children.Add(footer);

            return root;
        }

        private FrameworkElement BuildHeader()
        {
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(LocalizedKeys.FamilyLibrary.Title),
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 35, 52)),
                Margin = new Thickness(0, 0, 0, 7)
            });

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(LocalizedKeys.FamilyLibrary.Subtitle),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(82, 94, 110)),
                FontSize = 13
            });

            return panel;
        }

        private FrameworkElement BuildToolbar()
        {
            DockPanel panel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 12),
                LastChildFill = false
            };

            Button editButton = CreateToolbarButton("Edit", OnEditClick);
            editButton.ToolTip = "Select a family in the list, then click Edit.";
            panel.Children.Add(editButton);

            panel.Children.Add(CreateToolbarButton(
                LocalizedKeys.FamilyLibrary.RefreshButton,
                OnRefreshClick));

            TextBlock hint = new TextBlock
            {
                Text = "Select a row and click Edit, or double-click a row.",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 124)),
                Margin = new Thickness(8, 0, 0, 0)
            };
            panel.Children.Add(hint);

            return panel;
        }

        private DataGrid BuildGrid()
        {
            DataGrid grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                MinRowHeight = 36,
                RowHeight = 36,
                ColumnHeaderHeight = 38,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 230, 236)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Background = Brushes.White,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            grid.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(FamilyLibraryManagerViewModel.Items)));
            grid.SetBinding(DataGrid.SelectedItemProperty,
                new Binding(nameof(FamilyLibraryManagerViewModel.SelectedItem))
                {
                    Mode = BindingMode.TwoWay
                });
            grid.MouseDoubleClick += OnGridMouseDoubleClick;

            Style rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            grid.RowStyle = rowStyle;

            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            grid.CellStyle = cellStyle;

            Style textCellStyle = new Style(typeof(TextBlock));
            textCellStyle.Setters.Add(new Setter(
                TextBlock.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            textCellStyle.Setters.Add(new Setter(
                TextBlock.MarginProperty,
                new Thickness(8, 0, 8, 0)));

            Style displayNameStyle = new Style(typeof(TextBlock), textCellStyle);
            displayNameStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.DisplayName))));

            Style checkBoxStyle = new Style(typeof(CheckBox));
            checkBoxStyle.Setters.Add(new Setter(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            checkBoxStyle.Setters.Add(new Setter(
                FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Center));

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T(LocalizedKeys.FamilyLibrary.ColumnDisplayName),
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.DisplayName)),
                Width = new DataGridLength(360),
                MinWidth = 320,
                ElementStyle = displayNameStyle
            });
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = Loc.T(LocalizedKeys.FamilyLibrary.ColumnEnabled),
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.Enabled)),
                Width = new DataGridLength(80),
                ElementStyle = checkBoxStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Airflow (m³/s)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.AirflowM3s)),
                Width = new DataGridLength(105),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Total Length (mm)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.TotalLengthMm)),
                Width = new DataGridLength(125),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Width (mm)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.WidthMm)),
                Width = new DataGridLength(95),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Height (mm)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.HeightMm)),
                Width = new DataGridLength(95),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Weight (kg)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.WeightKg)),
                Width = new DataGridLength(95),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Required Maintenance Space (mm)",
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.RequiredMaintenanceSpaceMm)),
                Width = new DataGridLength(205),
                ElementStyle = textCellStyle
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T(LocalizedKeys.FamilyLibrary.ColumnSortOrder),
                Binding = new Binding(nameof(FamilyLibraryManagerItemViewModel.SortOrder)),
                Width = new DataGridLength(90),
                ElementStyle = textCellStyle
            });

            return grid;
        }

        private DockPanel BuildFooter()
        {
            DockPanel footer = new DockPanel
            {
                LastChildFill = false
            };

            TextBlock status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(72, 84, 100))
            };
            status.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(FamilyLibraryManagerViewModel.HasUnsavedChanges))
                {
                    Converter = new BooleanToStatusConverter()
                });
            DockPanel.SetDock(status, Dock.Left);
            footer.Children.Add(status);

            Button closeButton = new Button
            {
                Content = Loc.T(LocalizedKeys.Common.Cancel),
                MinWidth = 112,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true
            };
            closeButton.Click += (sender, args) => Close();
            DockPanel.SetDock(closeButton, Dock.Right);
            footer.Children.Add(closeButton);

            return footer;
        }

        private static Button CreateToolbarButton(string textKey, RoutedEventHandler handler)
        {
            string content = string.Equals(textKey, "Edit", StringComparison.Ordinal)
                ? textKey
                : Loc.T(textKey);

            Button button = new Button
            {
                Content = content,
                MinWidth = 108,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(16, 0, 16, 0)
            };
            button.Click += handler;
            return button;
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            OpenSelectedItemEditor();
        }

        private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && !(current is DataGridRow))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is DataGridRow && _viewModel.SelectedItem != null)
            {
                OpenSelectedItemEditor();
            }
        }

        private void OpenSelectedItemEditor()
        {
            FamilyLibraryManagerItemViewModel selected = _viewModel.SelectedItem;
            if (selected == null)
            {
                ShowLocalizedMessage("Please select a family to edit.", MessageBoxImage.Information);
                return;
            }

            FamilyLibraryManagerItemViewModel editableCopy = CloneItem(selected);
            FamilyLibraryEditWindow editor = new FamilyLibraryEditWindow(
                editableCopy,
                _viewModel.CatalogPath,
                editedItem => SaveEditedItem(editedItem, selected))
            {
                Owner = this
            };

            editor.ShowDialog();
        }

        private void SaveEditedItem(
            FamilyLibraryManagerItemViewModel editedItem,
            FamilyLibraryManagerItemViewModel targetItem)
        {
            FamilyLibraryManagerItemViewModel originalItem = CloneItem(targetItem);

            try
            {
                ApplyItem(editedItem, targetItem);
                _viewModel.Save();
                _dataGrid.Items.Refresh();
            }
            catch
            {
                ApplyItem(originalItem, targetItem);
                _dataGrid.Items.Refresh();
                throw;
            }
        }

        private static FamilyLibraryManagerItemViewModel CloneItem(
            FamilyLibraryManagerItemViewModel source)
        {
            FamilyLibraryManagerItemViewModel clone = new FamilyLibraryManagerItemViewModel
            {
                Key = source.Key,
                DisplayName = source.DisplayName,
                FileName = source.FileName,
                StoredFileName = source.StoredFileName,
                Enabled = source.Enabled,
                SortOrder = source.SortOrder,
                Description = source.Description,
                AirflowM3s = source.AirflowM3s,
                MbLengthMm = source.MbLengthMm,
                FilterLengthMm = source.FilterLengthMm,
                CoilLengthMm = source.CoilLengthMm,
                FanLengthMm = source.FanLengthMm,
                TotalLengthMm = source.TotalLengthMm,
                HeightMm = source.HeightMm,
                WidthMm = source.WidthMm,
                WeightKg = source.WeightKg,
                RequiredMaintenanceSpaceMm = source.RequiredMaintenanceSpaceMm,
                RequiredMaintenanceSpaceSide = source.RequiredMaintenanceSpaceSide,
                ValveChamberLengthMm = source.ValveChamberLengthMm,
                ValveChamberWidthMm = source.ValveChamberWidthMm,
                ElChamberLengthMm = source.ElChamberLengthMm,
                ElChamberWidthMm = source.ElChamberWidthMm,
                MaintenanceDoorSideMm = source.MaintenanceDoorSideMm,
                MaintenanceOtherSideMm = source.MaintenanceOtherSideMm,
                MaintenanceFrontBackMm = source.MaintenanceFrontBackMm,
                IsNew = source.IsNew,
                SourceFilePath = source.SourceFilePath
            };

            clone.ReplaceSubModules(source.SubModules.Select(CloneSubModule));
            clone.ReplaceMaintenanceSpaces(source.MaintenanceSpaces.Select(CloneMaintenanceSpace));
            return clone;
        }

        private static FamilyLibrarySubModuleItemViewModel CloneSubModule(
            FamilyLibrarySubModuleItemViewModel source)
        {
            if (source == null)
            {
                return null;
            }

            return new FamilyLibrarySubModuleItemViewModel
            {
                Sequence = source.Sequence,
                ModuleCode = source.ModuleCode,
                GridRow = source.GridRow,
                GridColumn = source.GridColumn,
                Name = source.Name,
                LengthMm = source.LengthMm,
                WidthMm = source.WidthMm,
                HeightMm = source.HeightMm,
                WeightKg = source.WeightKg,
                Photo = source.Photo
            };
        }

        private static FamilyLibraryMaintenanceSpaceItemViewModel CloneMaintenanceSpace(
            FamilyLibraryMaintenanceSpaceItemViewModel source)
        {
            if (source == null)
            {
                return null;
            }

            return new FamilyLibraryMaintenanceSpaceItemViewModel
            {
                Sequence = source.Sequence,
                MaintenanceCode = source.MaintenanceCode,
                Side = source.Side,
                DimensionMm = source.DimensionMm,
                IsWallSide = source.IsWallSide,
                IsDoorSide = source.IsDoorSide
            };
        }

        private static void ApplyItem(
            FamilyLibraryManagerItemViewModel source,
            FamilyLibraryManagerItemViewModel target)
        {
            target.DisplayName = source.DisplayName;
            target.Enabled = source.Enabled;
            target.SortOrder = source.SortOrder;
            target.Description = source.Description;
            target.AirflowM3s = source.AirflowM3s;
            target.MbLengthMm = source.MbLengthMm;
            target.FilterLengthMm = source.FilterLengthMm;
            target.CoilLengthMm = source.CoilLengthMm;
            target.FanLengthMm = source.FanLengthMm;
            target.TotalLengthMm = source.TotalLengthMm;
            target.HeightMm = source.HeightMm;
            target.WidthMm = source.WidthMm;
            target.WeightKg = source.WeightKg;
            target.RequiredMaintenanceSpaceMm = source.RequiredMaintenanceSpaceMm;
            target.RequiredMaintenanceSpaceSide = source.RequiredMaintenanceSpaceSide;
            target.ValveChamberLengthMm = source.ValveChamberLengthMm;
            target.ValveChamberWidthMm = source.ValveChamberWidthMm;
            target.ElChamberLengthMm = source.ElChamberLengthMm;
            target.ElChamberWidthMm = source.ElChamberWidthMm;
            target.MaintenanceDoorSideMm = source.MaintenanceDoorSideMm;
            target.MaintenanceOtherSideMm = source.MaintenanceOtherSideMm;
            target.MaintenanceFrontBackMm = source.MaintenanceFrontBackMm;
            target.ReplaceSubModules(source.SubModules.Select(CloneSubModule));
            target.ReplaceMaintenanceSpaces(source.MaintenanceSpaces.Select(CloneMaintenanceSpace));
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.HasUnsavedChanges)
            {
                if (!ShowLocalizedConfirmation(
                    Loc.T(LocalizedKeys.FamilyLibrary.UnsavedChangesRefresh),
                    MessageBoxImage.Warning))
                {
                    return;
                }
            }

            try
            {
                _viewModel.Reload();
            }
            catch (Exception ex)
            {
                ShowLocalizedMessage(ex.Message, MessageBoxImage.Error);
            }
        }

        private void ShowLocalizedMessage(string message, MessageBoxImage icon)
        {
            ShowLocalizedDialog(message, icon, false);
        }

        private bool ShowLocalizedConfirmation(string message, MessageBoxImage icon)
        {
            return ShowLocalizedDialog(message, icon, true);
        }

        private bool ShowLocalizedDialog(string message, MessageBoxImage icon, bool showYesNo)
        {
            Window dialog = new Window
            {
                Owner = this,
                Title = Loc.T(LocalizedKeys.FamilyLibrary.Title),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 420,
                MaxWidth = 680,
                ShowInTaskbar = false,
                Background = Brushes.White
            };

            WpfGrid root = new WpfGrid
            {
                Margin = new Thickness(24)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            WpfGrid contentGrid = new WpfGrid
            {
                Margin = new Thickness(0, 0, 0, 24)
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            Border iconBorder = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = GetDialogIconBrush(icon),
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            iconBorder.Child = new TextBlock
            {
                Text = GetDialogSymbolText(icon),
                Foreground = Brushes.White,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetColumn(iconBorder, 0);
            contentGrid.Children.Add(iconBorder);

            TextBlock messageBlock = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 560
            };
            Grid.SetColumn(messageBlock, 1);
            contentGrid.Children.Add(messageBlock);

            Grid.SetRow(contentGrid, 0);
            root.Children.Add(contentGrid);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (showYesNo)
            {
                Button noButton = CreateDialogButton(
                    Loc.T(LocalizedKeys.Common.No), false, true);
                noButton.Click += (sender, args) => dialog.DialogResult = false;
                buttonPanel.Children.Add(noButton);

                Button yesButton = CreateDialogButton(
                    Loc.T(LocalizedKeys.Common.Yes), true, false);
                yesButton.Click += (sender, args) => dialog.DialogResult = true;
                buttonPanel.Children.Add(yesButton);
            }
            else
            {
                Button okButton = CreateDialogButton(
                    Loc.T(LocalizedKeys.Common.Ok), true, true);
                okButton.Click += (sender, args) => dialog.DialogResult = true;
                buttonPanel.Children.Add(okButton);
            }

            Grid.SetRow(buttonPanel, 1);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            bool? result = dialog.ShowDialog();
            return result == true;
        }

        private static Button CreateDialogButton(
            string text,
            bool isDefault,
            bool isCancel)
        {
            return new Button
            {
                Content = text,
                MinWidth = 110,
                MinHeight = 34,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(16, 0, 16, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
        }

        private static Brush GetDialogIconBrush(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    return new SolidColorBrush(Color.FromRgb(196, 61, 57));
                case MessageBoxImage.Warning:
                    return new SolidColorBrush(Color.FromRgb(225, 148, 39));
                default:
                    return new SolidColorBrush(Color.FromRgb(49, 132, 219));
            }
        }

        private static string GetDialogSymbolText(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    return "X";
                case MessageBoxImage.Warning:
                    return "!";
                default:
                    return "i";
            }
        }

        private sealed class BooleanToStatusConverter : IValueConverter
        {
            public object Convert(
                object value,
                Type targetType,
                object parameter,
                System.Globalization.CultureInfo culture)
            {
                bool hasChanges = value is bool flag && flag;
                return hasChanges
                    ? Loc.T(LocalizedKeys.FamilyLibrary.StatusUnsaved)
                    : Loc.T(LocalizedKeys.FamilyLibrary.StatusSaved);
            }

            public object ConvertBack(
                object value,
                Type targetType,
                object parameter,
                System.Globalization.CultureInfo culture)
            {
                return Binding.DoNothing;
            }
        }
    }

    internal sealed class FamilyLibraryEditWindow : Window
    {
        private readonly FamilyLibraryManagerItemViewModel _item;
        private readonly string _catalogPath;
        private readonly Action<FamilyLibraryManagerItemViewModel> _saveAction;
        private readonly Dictionary<string, Button> _subModuleButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _maintenanceButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private DataGrid _subModuleDataGrid;
        private TextBlock _subModuleTotalDimensionsText;
        private TextBlock _subModuleTotalWeightText;
        private Canvas _maintenanceCanvas;
        private DataGrid _maintenanceDataGrid;
        private TextBlock _maintenanceHintText;
        private readonly Dictionary<string, string> _originalSubModulePhotos =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingSubModulePhotoSources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal FamilyLibraryEditWindow(
            FamilyLibraryManagerItemViewModel item,
            string catalogPath,
            Action<FamilyLibraryManagerItemViewModel> saveAction)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _catalogPath = catalogPath ?? string.Empty;
            _saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));

            foreach (FamilyLibrarySubModuleItemViewModel module in _item.SubModules)
            {
                if (module != null && !string.IsNullOrWhiteSpace(module.ModuleCode))
                {
                    _originalSubModulePhotos[module.ModuleCode] = module.Photo ?? string.Empty;
                }
            }

            DataContext = _item;
            Title = "Edit Family";
            Width = 1220;
            Height = 820;
            MinWidth = 1050;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Brushes.White;
            Content = BuildContent();
        }

        private FrameworkElement BuildContent()
        {
            WpfGrid root = new WpfGrid
            {
                Margin = new Thickness(22, 20, 22, 18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel header = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };
            header.Children.Add(new TextBlock
            {
                Text = "Edit Family",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 35, 52)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            TextBlock nameText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(82, 94, 110)),
                FontSize = 13
            };
            nameText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.DisplayName)));
            header.Children.Add(nameText);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            TabControl tabs = new TabControl
            {
                Margin = new Thickness(0, 0, 0, 16)
            };
            tabs.Items.Add(new TabItem
            {
                Header = "Basic Info",
                Content = WrapTab(BuildBasicInfoTab())
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Sub-Module",
                Content = WrapTab(BuildSubModuleTab())
            });
            tabs.Items.Add(new TabItem
            {
                Header = "Maintenance",
                Content = WrapTab(BuildMaintenance2Tab())
            });
            tabs.SelectionChanged += (sender, args) =>
            {
                TabItem selectedTab = tabs.SelectedItem as TabItem;
                if (selectedTab != null && string.Equals(selectedTab.Header as string, "Maintenance", StringComparison.Ordinal))
                {
                    RefreshMaintenanceSelector();
                }
            };
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancelButton = new Button
            {
                Content = Loc.T(LocalizedKeys.Common.Cancel),
                MinWidth = 112,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true
            };
            cancelButton.Click += (sender, args) => DialogResult = false;
            footer.Children.Add(cancelButton);

            Button saveButton = new Button
            {
                Content = Loc.T(LocalizedKeys.Common.Save),
                MinWidth = 112,
                MinHeight = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = true
            };
            saveButton.Click += OnSaveClick;
            footer.Children.Add(saveButton);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        private static FrameworkElement WrapTab(FrameworkElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
        }

        private FrameworkElement BuildBasicInfoTab()
        {
            WpfGrid grid = CreateEditorGrid(5);

            AddField(grid, 0, 0, "Key",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.Key), true));
            AddField(grid, 0, 2,
                Loc.T(LocalizedKeys.FamilyLibrary.FieldDisplayName),
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.DisplayName), false));

            AddField(grid, 1, 0,
                Loc.T(LocalizedKeys.FamilyLibrary.FieldFileName),
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.FileName), true));
            AddField(grid, 1, 2, "Airflow (m³/s)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.AirflowM3s), false));

            AddField(grid, 2, 0,
                Loc.T(LocalizedKeys.FamilyLibrary.FieldEnabled),
                CreateBoundCheckBox(nameof(FamilyLibraryManagerItemViewModel.Enabled)));
            AddField(grid, 2, 2,
                Loc.T(LocalizedKeys.FamilyLibrary.FieldSortOrder),
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.SortOrder), false));

            TextBox descriptionBox = CreateBoundTextBox(
                nameof(FamilyLibraryManagerItemViewModel.Description), false);
            descriptionBox.AcceptsReturn = true;
            descriptionBox.TextWrapping = TextWrapping.Wrap;
            descriptionBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            descriptionBox.MinHeight = 120;
            Grid.SetColumn(descriptionBox, 1);
            Grid.SetColumnSpan(descriptionBox, 3);
            Grid.SetRow(descriptionBox, 3);
            grid.Children.Add(CreateLabel(
                Loc.T(LocalizedKeys.FamilyLibrary.FieldDescription), 3, 0));
            grid.Children.Add(descriptionBox);

            TextBlock pathText = new TextBlock
            {
                Text = _catalogPath,
                Margin = new Thickness(8, 8, 8, 8),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(82, 94, 110))
            };
            Grid.SetColumn(pathText, 1);
            Grid.SetColumnSpan(pathText, 3);
            Grid.SetRow(pathText, 4);
            grid.Children.Add(CreateLabel(
                Loc.T(LocalizedKeys.FamilyLibrary.FieldCatalogPath), 4, 0));
            grid.Children.Add(pathText);

            return grid;
        }

        private FrameworkElement BuildDimensionsTab()
        {
            WpfGrid grid = CreateEditorGrid(7);
            AddField(grid, 0, 0, "Airflow (m³/s)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.AirflowM3s), false));
            AddField(grid, 0, 2, "Total Length (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.TotalLengthMm), false));
            AddField(grid, 1, 0, "Width (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.WidthMm), false));
            AddField(grid, 1, 2, "Height (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.HeightMm), false));
            AddField(grid, 2, 0, "MB (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.MbLengthMm), false));
            AddField(grid, 2, 2, "Filter (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.FilterLengthMm), false));
            AddField(grid, 3, 0, "Coil (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.CoilLengthMm), false));
            AddField(grid, 3, 2, "Fan (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.FanLengthMm), false));
            AddField(grid, 4, 0, "Valve Chamber Length (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.ValveChamberLengthMm), false));
            AddField(grid, 4, 2, "Valve Chamber Width (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.ValveChamberWidthMm), false));
            AddField(grid, 5, 0, "EL Chamber Length (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.ElChamberLengthMm), false));
            AddField(grid, 5, 2, "EL Chamber Width (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.ElChamberWidthMm), false));
            AddField(grid, 6, 0, "Weight (kg)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.WeightKg), false));
            return grid;
        }

        private FrameworkElement BuildSubModuleTab()
        {
            WpfGrid root = new WpfGrid
            {
                Margin = new Thickness(16, 14, 16, 14)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Keep the Sub-Module table compact like the Family Library list.
            // The table grows with its actual rows and scrolls only when it reaches MaxHeight,
            // instead of stretching to consume all remaining vertical space.
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Select Submodule",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(48, 60, 76)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            WpfGrid selector = new WpfGrid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 14)
            };

            for (int row = 0; row < 4; row++)
            {
                selector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int column = 0; column < 6; column++)
            {
                selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    int capturedRow = row;
                    int capturedColumn = column;
                    Button button = new Button
                    {
                        Width = 48,
                        Height = 48,
                        Margin = new Thickness(0, 0, 10, 10),
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold,
                        BorderThickness = new Thickness(1.5),
                        Tag = row + ":" + column
                    };
                    button.Click += (sender, args) =>
                        ToggleSubModuleCell(capturedRow, capturedColumn);
                    Grid.SetRow(button, row);
                    Grid.SetColumn(button, column);
                    selector.Children.Add(button);
                    _subModuleButtons[GetSubModuleCellKey(row, column)] = button;
                }
            }

            Grid.SetRow(selector, 1);
            root.Children.Add(selector);

            Border tableBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(202, 210, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _subModuleDataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserSortColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                SelectionMode = DataGridSelectionMode.Single,
                RowHeaderWidth = 0,
                BorderThickness = new Thickness(0),

                // Match the compact Family Library list instead of the previous
                // oversized prototype rows.
                MinRowHeight = 36,
                RowHeight = 36,
                ColumnHeaderHeight = 38,
                FontSize = 13,
                MaxHeight = 310,

                // Keep the table structure visible, but use the same light separators
                // and alternating row background as the Family Library list.
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 230, 236)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 230, 236)),
                Background = Brushes.White,
                RowBackground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            Style subModuleRowStyle = new Style(typeof(DataGridRow));
            subModuleRowStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            _subModuleDataGrid.RowStyle = subModuleRowStyle;

            Style subModuleCellStyle = new Style(typeof(DataGridCell));
            subModuleCellStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            subModuleCellStyle.Setters.Add(new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
            subModuleCellStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(6, 0, 6, 0)));
            _subModuleDataGrid.CellStyle = subModuleCellStyle;

            Style subModuleHeaderStyle = new Style(typeof(DataGridColumnHeader));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Left));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(7, 0, 7, 0)));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(248, 250, 252))));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(225, 230, 236))));
            // Draw the missing separator below the header row and keep the
            // column dividers consistent with the body grid lines.
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.BorderThicknessProperty,
                new Thickness(0, 0, 1, 1)));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.FontSizeProperty,
                13.0));
            subModuleHeaderStyle.Setters.Add(new Setter(
                Control.FontWeightProperty,
                FontWeights.Normal));
            _subModuleDataGrid.ColumnHeaderStyle = subModuleHeaderStyle;

            // Explicit styles are required for DataGridTextColumn because the generated
            // TextBlock/TextBox does not inherit vertical alignment from DataGridCell.
            Style subModuleTextStyle = new Style(typeof(TextBlock));
            subModuleTextStyle.Setters.Add(new Setter(
                TextBlock.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            subModuleTextStyle.Setters.Add(new Setter(
                TextBlock.MarginProperty,
                new Thickness(2, 0, 2, 0)));

            Style subModuleEditStyle = new Style(typeof(TextBox));
            subModuleEditStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            subModuleEditStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(2, 0, 2, 0)));
            _subModuleDataGrid.SetBinding(
                ItemsControl.ItemsSourceProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.SubModules)));

            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "MODULE",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.ModuleCode)),
                IsReadOnly = true,
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(82)
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.Name)),
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(1.3, DataGridLengthUnitType.Star)
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "L (mm)",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.LengthMm)),
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(92)
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "W (mm)",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.WidthMm)),
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(92)
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "H (mm)",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.HeightMm)),
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(92)
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Dimensions(mm)",
                Binding = new Binding(nameof(FamilyLibrarySubModuleItemViewModel.DimensionsMm))
                {
                    Mode = BindingMode.OneWay
                },
                IsReadOnly = true,
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                // About one-third shorter than the previous 1.6* width.
                Width = new DataGridLength(1.05, DataGridLengthUnitType.Star),
                MinWidth = 175
            });
            _subModuleDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Weight (kg)",
                Binding = CreateSubModuleBinding(nameof(FamilyLibrarySubModuleItemViewModel.WeightKg)),
                ElementStyle = subModuleTextStyle,
                EditingElementStyle = subModuleEditStyle,
                Width = new DataGridLength(100)
            });
            _subModuleDataGrid.Columns.Add(BuildSubModulePhotoColumn());

            tableBorder.Child = _subModuleDataGrid;
            Grid.SetRow(tableBorder, 2);
            root.Children.Add(tableBorder);

            WpfGrid totals = new WpfGrid
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            totals.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            totals.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            StackPanel totalDimensionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            totalDimensionsPanel.Children.Add(new TextBlock
            {
                Text = "Total Dimensions(mm) : ",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            _subModuleTotalDimensionsText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            _subModuleTotalDimensionsText.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.TotalSubModuleDimensionsText)));
            totalDimensionsPanel.Children.Add(_subModuleTotalDimensionsText);
            Grid.SetColumn(totalDimensionsPanel, 0);
            totals.Children.Add(totalDimensionsPanel);

            StackPanel totalWeightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            totalWeightPanel.Children.Add(new TextBlock
            {
                Text = "Total Weight (kg) : ",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            _subModuleTotalWeightText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            _subModuleTotalWeightText.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.TotalSubModuleWeightKg)));
            totalWeightPanel.Children.Add(_subModuleTotalWeightText);
            Grid.SetColumn(totalWeightPanel, 1);
            totals.Children.Add(totalWeightPanel);

            Grid.SetRow(totals, 3);
            root.Children.Add(totals);

            RefreshSubModuleSelector();
            return root;
        }

        private static Binding CreateSubModuleBinding(string path)
        {
            return new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
        }


        private DataGridTemplateColumn BuildSubModulePhotoColumn()
        {
            DataTemplate template = new DataTemplate(typeof(FamilyLibrarySubModuleItemViewModel));

            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 4, 0));

            FrameworkElementFactory fileName = new FrameworkElementFactory(typeof(TextBlock));
            fileName.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(FamilyLibrarySubModuleItemViewModel.PhotoFileName)));
            fileName.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            fileName.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            fileName.SetValue(FrameworkElement.MinWidthProperty, 66.0);
            fileName.SetValue(FrameworkElement.MaxWidthProperty, 94.0);
            fileName.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            panel.AppendChild(fileName);

            FrameworkElementFactory viewButton = CreatePhotoCellButtonFactory(
                "View",
                new RoutedEventHandler(OnSubModulePhotoViewClick),
                42);
            viewButton.SetBinding(
                UIElement.IsEnabledProperty,
                new Binding(nameof(FamilyLibrarySubModuleItemViewModel.HasPhoto)));
            panel.AppendChild(viewButton);

            FrameworkElementFactory replaceButton = CreatePhotoCellButtonFactory(
                null,
                new RoutedEventHandler(OnSubModulePhotoUploadOrReplaceClick),
                58);
            replaceButton.SetBinding(
                ContentControl.ContentProperty,
                new Binding(nameof(FamilyLibrarySubModuleItemViewModel.PhotoActionText)));
            panel.AppendChild(replaceButton);

            FrameworkElementFactory removeButton = CreatePhotoCellButtonFactory(
                "Remove",
                new RoutedEventHandler(OnSubModulePhotoRemoveClick),
                58);
            removeButton.SetBinding(
                UIElement.IsEnabledProperty,
                new Binding(nameof(FamilyLibrarySubModuleItemViewModel.HasPhoto)));
            panel.AppendChild(removeButton);

            template.VisualTree = panel;

            return new DataGridTemplateColumn
            {
                Header = "Photo",
                CellTemplate = template,
                IsReadOnly = true,
                // The Photo column intentionally has more room for:
                // "S1.png   View   Replace   Remove".
                Width = new DataGridLength(1.65, DataGridLengthUnitType.Star),
                MinWidth = 285
            };
        }

        private static FrameworkElementFactory CreatePhotoCellButtonFactory(
            string content,
            RoutedEventHandler clickHandler,
            double minWidth)
        {
            FrameworkElementFactory button = new FrameworkElementFactory(typeof(Button));
            if (content != null)
            {
                button.SetValue(ContentControl.ContentProperty, content);
            }

            button.SetValue(FrameworkElement.MinWidthProperty, minWidth);
            button.SetValue(FrameworkElement.HeightProperty, 26.0);
            button.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0));
            button.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
            button.SetValue(Control.FontSizeProperty, 12.0);
            button.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.SetValue(FrameworkElement.FocusableProperty, false);
            button.AddHandler(Button.ClickEvent, clickHandler);
            return button;
        }

        private void OnSubModulePhotoUploadOrReplaceClick(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            FamilyLibrarySubModuleItemViewModel module =
                element != null ? element.DataContext as FamilyLibrarySubModuleItemViewModel : null;
            if (module == null || string.IsNullOrWhiteSpace(module.ModuleCode))
            {
                return;
            }

            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Sub-Module Photo",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string extension = Path.GetExtension(dialog.FileName);
            if (!IsSupportedSubModulePhotoExtension(extension))
            {
                MessageBox.Show(
                    this,
                    "Please select a PNG, JPG, JPEG, BMP, GIF, TIF or TIFF image.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string relativePath = BuildSubModulePhotoRelativePath(
                _item.Key,
                module.ModuleCode,
                extension);
            string targetPath = ResolveSubModulePhotoAbsolutePath(relativePath);

            try
            {
                string sourceFullPath = Path.GetFullPath(dialog.FileName);
                string targetFullPath = Path.GetFullPath(targetPath);
                if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingSubModulePhotoSources.Remove(module.ModuleCode);
                }
                else
                {
                    // Stage the source path in memory. The file is copied into the
                    // plugin-managed folder only when the user presses Save, so Cancel
                    // does not change the existing photo.
                    _pendingSubModulePhotoSources[module.ModuleCode] = sourceFullPath;
                }

                module.Photo = relativePath;
                _subModuleDataGrid?.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Unable to prepare the selected photo. " + ex.Message,
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnSubModulePhotoViewClick(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            FamilyLibrarySubModuleItemViewModel module =
                element != null ? element.DataContext as FamilyLibrarySubModuleItemViewModel : null;
            if (module == null || string.IsNullOrWhiteSpace(module.Photo))
            {
                return;
            }

            string path;
            if (!_pendingSubModulePhotoSources.TryGetValue(module.ModuleCode ?? string.Empty, out path))
            {
                path = ResolveSubModulePhotoAbsolutePath(module.Photo);
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    this,
                    "The selected Sub-Module photo file could not be found.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Unable to open the photo. " + ex.Message,
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnSubModulePhotoRemoveClick(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            FamilyLibrarySubModuleItemViewModel module =
                element != null ? element.DataContext as FamilyLibrarySubModuleItemViewModel : null;
            if (module == null)
            {
                return;
            }

            _pendingSubModulePhotoSources.Remove(module.ModuleCode ?? string.Empty);
            module.Photo = string.Empty;
            _subModuleDataGrid?.Items.Refresh();
        }

        private static bool IsSupportedSubModulePhotoExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            switch (extension.ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                case ".tif":
                case ".tiff":
                    return true;
                default:
                    return false;
            }
        }

        private static string BuildSubModulePhotoRelativePath(
            string familyKey,
            string moduleCode,
            string extension)
        {
            string safeFamilyKey = SanitizePhotoPathSegment(
                string.IsNullOrWhiteSpace(familyKey) ? "unknown_family" : familyKey);
            string safeModuleCode = SanitizePhotoPathSegment(
                string.IsNullOrWhiteSpace(moduleCode) ? "S" : moduleCode);

            // Preserve the uploaded image format/extension exactly. No PNG conversion.
            string fileName = safeModuleCode + extension;
            return Path.Combine("SubModuleImages", safeFamilyKey, fileName)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string SanitizePhotoPathSegment(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] characters = (value ?? string.Empty).ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (invalid.Contains(characters[i]))
                {
                    characters[i] = '_';
                }
            }

            return new string(characters);
        }

        private static string ResolveSubModulePhotoAbsolutePath(string photo)
        {
            if (string.IsNullOrWhiteSpace(photo))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(photo))
            {
                return Path.GetFullPath(photo);
            }

            string relative = photo
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(
                RoomCustomFamilyCatalogFileService.GetLibraryDirectory(),
                relative));
        }

        private List<SubModulePhotoCommitState> CommitPendingSubModulePhotos()
        {
            List<SubModulePhotoCommitState> states = new List<SubModulePhotoCommitState>();

            try
            {
                foreach (KeyValuePair<string, string> pair in _pendingSubModulePhotoSources)
                {
                    FamilyLibrarySubModuleItemViewModel module = _item.SubModules
                        .FirstOrDefault(x =>
                            x != null &&
                            string.Equals(
                                x.ModuleCode,
                                pair.Key,
                                StringComparison.OrdinalIgnoreCase));
                    if (module == null || string.IsNullOrWhiteSpace(module.Photo))
                    {
                        continue;
                    }

                    string sourcePath = pair.Value;
                    string targetPath = ResolveSubModulePhotoAbsolutePath(module.Photo);
                    if (string.IsNullOrWhiteSpace(sourcePath) ||
                        string.IsNullOrWhiteSpace(targetPath) ||
                        !File.Exists(sourcePath))
                    {
                        throw new InvalidOperationException(
                            "The source photo for " + module.ModuleCode + " could not be found.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                    SubModulePhotoCommitState state = new SubModulePhotoCommitState
                    {
                        TargetPath = targetPath,
                        TargetExisted = File.Exists(targetPath)
                    };

                    if (state.TargetExisted)
                    {
                        state.BackupPath = Path.GetTempFileName();
                        File.Copy(targetPath, state.BackupPath, true);
                    }

                    File.Copy(sourcePath, targetPath, true);
                    states.Add(state);
                }

                return states;
            }
            catch
            {
                RollbackCommittedSubModulePhotos(states);
                throw;
            }
        }

        private void CleanupOldSubModulePhotosAfterSave()
        {
            foreach (KeyValuePair<string, string> original in _originalSubModulePhotos)
            {
                FamilyLibrarySubModuleItemViewModel current = _item.SubModules
                    .FirstOrDefault(x =>
                        x != null &&
                        string.Equals(
                            x.ModuleCode,
                            original.Key,
                            StringComparison.OrdinalIgnoreCase));

                string currentPhoto = current != null ? current.Photo ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(original.Value) &&
                    !string.Equals(original.Value, currentPhoto, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteManagedSubModulePhoto(original.Value);
                }
            }
        }

        private static void TryDeleteManagedSubModulePhoto(string photo)
        {
            if (string.IsNullOrWhiteSpace(photo))
            {
                return;
            }

            try
            {
                string managedRoot = Path.GetFullPath(Path.Combine(
                    RoomCustomFamilyCatalogFileService.GetLibraryDirectory(),
                    "SubModuleImages"));
                string candidate = ResolveSubModulePhotoAbsolutePath(photo);

                string rootWithSeparator = managedRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch
            {
                // Photo cleanup must never invalidate a successful catalog save.
            }
        }

        private static void CompleteSubModulePhotoCommit(
            IEnumerable<SubModulePhotoCommitState> states)
        {
            foreach (SubModulePhotoCommitState state in states ?? Enumerable.Empty<SubModulePhotoCommitState>())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(state.BackupPath) &&
                        File.Exists(state.BackupPath))
                    {
                        File.Delete(state.BackupPath);
                    }
                }
                catch
                {
                    // Best effort cleanup of temporary backups.
                }
            }
        }

        private static void RollbackCommittedSubModulePhotos(
            IEnumerable<SubModulePhotoCommitState> states)
        {
            foreach (SubModulePhotoCommitState state in
                (states ?? Enumerable.Empty<SubModulePhotoCommitState>()).Reverse())
            {
                try
                {
                    if (state.TargetExisted &&
                        !string.IsNullOrWhiteSpace(state.BackupPath) &&
                        File.Exists(state.BackupPath))
                    {
                        File.Copy(state.BackupPath, state.TargetPath, true);
                    }
                    else if (!state.TargetExisted && File.Exists(state.TargetPath))
                    {
                        File.Delete(state.TargetPath);
                    }
                }
                catch
                {
                    // Keep the original save error as the primary error.
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(state.BackupPath) &&
                        File.Exists(state.BackupPath))
                    {
                        File.Delete(state.BackupPath);
                    }
                }
                catch
                {
                }
            }
        }

        private sealed class SubModulePhotoCommitState
        {
            internal string TargetPath { get; set; }
            internal string BackupPath { get; set; }
            internal bool TargetExisted { get; set; }
        }

        private void ToggleSubModuleCell(int row, int column)
        {
            FamilyLibrarySubModuleItemViewModel existing = _item.SubModules
                .FirstOrDefault(x => x.GridRow == row && x.GridColumn == column);

            if (existing != null)
            {
                // To keep the route continuous, deleting an earlier module also removes
                // every module that was created after it.
                _item.RemoveSubModulesFromSequence(existing.Sequence);
                RefreshSubModuleSelector();
                return;
            }

            FamilyLibrarySubModuleItemViewModel last = _item.SubModules
                .OrderByDescending(x => x.Sequence)
                .FirstOrDefault();

            if (last != null)
            {
                int distance =
                    Math.Abs(last.GridRow - row) +
                    Math.Abs(last.GridColumn - column);
                if (distance != 1)
                {
                    return;
                }
            }

            int sequence = _item.SubModules.Count + 1;
            _item.AddSubModule(CreateSubModuleWithDefaults(sequence, row, column));

            RefreshSubModuleSelector();
        }

        /// <summary>
        /// Creates a new Sub-Module row. S1-S6 are pre-filled from the fixed AHU
        /// document defaults. The defaults are applied only at creation time, so any
        /// user-edited values already saved in catalog.json remain authoritative.
        /// </summary>
        private FamilyLibrarySubModuleItemViewModel CreateSubModuleWithDefaults(
            int sequence,
            int row,
            int column)
        {
            FamilyLibrarySubModuleItemViewModel result = new FamilyLibrarySubModuleItemViewModel
            {
                Sequence = sequence,
                ModuleCode = "S" + sequence,
                GridRow = row,
                GridColumn = column,
                Name = string.Empty,
                LengthMm = 0,
                WidthMm = 0,
                HeightMm = 0,
                WeightKg = 0,
                Photo = string.Empty
            };

            RoomCustomFamilySubModuleDto defaults;
            if (RoomCustomFamilyCatalogService.TryGetSubModuleDefault(
                _item.Key,
                sequence,
                out defaults) &&
                defaults != null)
            {
                result.Name = defaults.Name ?? string.Empty;
                result.LengthMm = defaults.LengthMm;
                result.WidthMm = defaults.WidthMm;
                result.HeightMm = defaults.HeightMm;
                result.WeightKg = defaults.WeightKg;
                result.Photo = defaults.Photo ?? string.Empty;
            }

            return result;
        }

        private void RefreshSubModuleSelector()
        {
            if (_subModuleButtons.Count == 0)
            {
                return;
            }

            FamilyLibrarySubModuleItemViewModel last = _item.SubModules
                .OrderByDescending(x => x.Sequence)
                .FirstOrDefault();

            foreach (KeyValuePair<string, Button> pair in _subModuleButtons)
            {
                Button button = pair.Value;
                string[] parts = pair.Key.Split(':');
                int row = int.Parse(parts[0]);
                int column = int.Parse(parts[1]);

                FamilyLibrarySubModuleItemViewModel selected = _item.SubModules
                    .FirstOrDefault(x => x.GridRow == row && x.GridColumn == column);

                if (selected != null)
                {
                    button.Content = selected.ModuleCode;
                    button.IsEnabled = true;
                    button.Foreground = new SolidColorBrush(Color.FromRgb(10, 91, 176));
                    button.Background = new SolidColorBrush(Color.FromRgb(210, 232, 255));
                    button.BorderBrush = new SolidColorBrush(Color.FromRgb(24, 119, 242));
                    button.ToolTip = selected.Sequence == _item.SubModules.Count
                        ? "Click again to remove " + selected.ModuleCode + "."
                        : "Click to remove " + selected.ModuleCode + " and all modules after it.";
                    continue;
                }

                bool canSelect = last == null ||
                                 Math.Abs(last.GridRow - row) +
                                 Math.Abs(last.GridColumn - column) == 1;

                button.Content = string.Empty;
                button.IsEnabled = canSelect;
                button.Foreground = new SolidColorBrush(Color.FromRgb(48, 60, 76));
                button.Background = canSelect
                    ? new SolidColorBrush(Color.FromRgb(242, 248, 255))
                    : new SolidColorBrush(Color.FromRgb(247, 249, 252));
                button.BorderBrush = canSelect
                    ? new SolidColorBrush(Color.FromRgb(133, 180, 230))
                    : new SolidColorBrush(Color.FromRgb(204, 214, 224));
                button.ToolTip = canSelect
                    ? "Click to add S" + (_item.SubModules.Count + 1) + "."
                    : (last == null
                        ? "Select the first sub-module."
                        : "The next module must be adjacent to " + last.ModuleCode + ".");
            }

            _subModuleDataGrid?.Items.Refresh();
        }

        private static string GetSubModuleCellKey(int row, int column)
        {
            return row + ":" + column;
        }

        private FrameworkElement BuildMaintenance2Tab()
        {
            WpfGrid root = new WpfGrid
            {
                Margin = new Thickness(16, 14, 16, 14)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = new TextBlock
            {
                Text = "Select Maintenance Space",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(48, 60, 76)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            StackPanel selectorHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 14)
            };

            _maintenanceHintText = new TextBlock
            {
                Text = "Please configure Sub-Module first.",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 101, 0)),
                Background = new SolidColorBrush(Color.FromRgb(255, 249, 230)),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            selectorHost.Children.Add(_maintenanceHintText);

            _maintenanceCanvas = new Canvas
            {
                Width = 390,
                Height = 290,
                Background = Brushes.Transparent
            };
            selectorHost.Children.Add(_maintenanceCanvas);
            Grid.SetRow(selectorHost, 1);
            root.Children.Add(selectorHost);

            Border tableBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(202, 210, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = Brushes.White
            };

            _maintenanceDataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserSortColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                SelectionMode = DataGridSelectionMode.Single,
                RowHeaderWidth = 0,
                BorderThickness = new Thickness(0),
                MinRowHeight = 36,
                RowHeight = 36,
                ColumnHeaderHeight = 38,
                FontSize = 13,
                MaxHeight = 220,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 230, 236)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 230, 236)),
                Background = Brushes.White,
                RowBackground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            Style rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            _maintenanceDataGrid.RowStyle = rowStyle;

            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            cellStyle.Setters.Add(new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
            cellStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(6, 0, 6, 0)));
            _maintenanceDataGrid.CellStyle = cellStyle;

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            headerStyle.Setters.Add(new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Left));
            headerStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(7, 0, 7, 0)));
            headerStyle.Setters.Add(new Setter(
                Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(248, 250, 252))));
            headerStyle.Setters.Add(new Setter(
                Control.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(225, 230, 236))));
            headerStyle.Setters.Add(new Setter(
                Control.BorderThicknessProperty,
                new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(
                Control.FontSizeProperty,
                13.0));
            headerStyle.Setters.Add(new Setter(
                Control.FontWeightProperty,
                FontWeights.Normal));
            _maintenanceDataGrid.ColumnHeaderStyle = headerStyle;

            Style textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(
                TextBlock.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            textStyle.Setters.Add(new Setter(
                TextBlock.MarginProperty,
                new Thickness(2, 0, 2, 0)));

            Style editStyle = new Style(typeof(TextBox));
            editStyle.Setters.Add(new Setter(
                Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            editStyle.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(2, 0, 2, 0)));

            _maintenanceDataGrid.SetBinding(
                ItemsControl.ItemsSourceProperty,
                new Binding(nameof(FamilyLibraryManagerItemViewModel.MaintenanceSpaces)));

            _maintenanceDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Maintenance",
                Binding = CreateMaintenanceBinding(nameof(FamilyLibraryMaintenanceSpaceItemViewModel.MaintenanceCode)),
                IsReadOnly = true,
                ElementStyle = textStyle,
                EditingElementStyle = editStyle,
                Width = new DataGridLength(120)
            });
            _maintenanceDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Dimensions(mm)",
                Binding = CreateMaintenanceBinding(nameof(FamilyLibraryMaintenanceSpaceItemViewModel.DimensionMm)),
                ElementStyle = textStyle,
                EditingElementStyle = editStyle,
                Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
            });
            _maintenanceDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "贴墙 / 对门",
                CellTemplate = CreateMaintenanceChoiceTemplate(),
                Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
            });

            tableBorder.Child = _maintenanceDataGrid;
            Grid.SetRow(tableBorder, 2);
            root.Children.Add(tableBorder);

            RefreshMaintenanceSelector();
            return root;
        }

        private static Binding CreateMaintenanceBinding(string path)
        {
            return new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
        }

        private static DataTemplate CreateMaintenanceChoiceTemplate()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory wallCheck = new FrameworkElementFactory(typeof(CheckBox));
            wallCheck.SetValue(ContentControl.ContentProperty, "贴墙");
            wallCheck.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            wallCheck.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 26, 0));
            wallCheck.SetBinding(
                ToggleButton.IsCheckedProperty,
                new Binding(nameof(FamilyLibraryMaintenanceSpaceItemViewModel.IsWallSide))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            panel.AppendChild(wallCheck);

            FrameworkElementFactory doorCheck = new FrameworkElementFactory(typeof(CheckBox));
            doorCheck.SetValue(ContentControl.ContentProperty, "对门");
            doorCheck.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            doorCheck.SetBinding(
                ToggleButton.IsCheckedProperty,
                new Binding(nameof(FamilyLibraryMaintenanceSpaceItemViewModel.IsDoorSide))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            panel.AppendChild(doorCheck);

            return new DataTemplate
            {
                VisualTree = panel
            };
        }

        private void ToggleMaintenanceSide(string side)
        {
            if (_item.SubModules.Count == 0)
            {
                return;
            }

            FamilyLibraryMaintenanceSpaceItemViewModel existing = _item.MaintenanceSpaces
                .FirstOrDefault(x => string.Equals(x.Side, side, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _item.RemoveMaintenanceSpaceBySide(side);
                RefreshMaintenanceSelector();
                return;
            }

            int sequence = _item.MaintenanceSpaces.Count + 1;
            _item.AddMaintenanceSpace(new FamilyLibraryMaintenanceSpaceItemViewModel
            {
                Sequence = sequence,
                MaintenanceCode = "M" + sequence,
                Side = side,
                DimensionMm = 0,
                IsWallSide = false,
                IsDoorSide = false
            });

            RefreshMaintenanceSelector();
        }

        private void RefreshMaintenanceSelector()
        {
            if (_maintenanceCanvas == null)
            {
                return;
            }

            _maintenanceCanvas.Children.Clear();
            _maintenanceButtons.Clear();

            List<FamilyLibrarySubModuleItemViewModel> subModules = _item.SubModules
                .OrderBy(x => x.Sequence)
                .ToList();

            if (subModules.Count == 0)
            {
                if (_maintenanceHintText != null)
                {
                    _maintenanceHintText.Visibility = Visibility.Visible;
                }

                _maintenanceCanvas.Height = 50;
                _maintenanceDataGrid?.Items.Refresh();
                return;
            }

            if (_maintenanceHintText != null)
            {
                _maintenanceHintText.Visibility = Visibility.Collapsed;
            }

            const double cellSize = 42;
            const double cellPitch = 52;
            const double outerPadding = 50;
            const double maintenanceDepth = 34;
            const double maintenanceGap = 8;

            _maintenanceCanvas.Width = 390;
            _maintenanceCanvas.Height = 290;

            int minRow = subModules.Min(x => x.GridRow);
            int maxRow = subModules.Max(x => x.GridRow);
            int minColumn = subModules.Min(x => x.GridColumn);
            int maxColumn = subModules.Max(x => x.GridColumn);

            double left = outerPadding + minColumn * cellPitch;
            double top = outerPadding + minRow * cellPitch;
            double right = outerPadding + maxColumn * cellPitch + cellSize;
            double bottom = outerPadding + maxRow * cellPitch + cellSize;

            foreach (FamilyLibrarySubModuleItemViewModel module in subModules)
            {
                Button moduleButton = new Button
                {
                    Content = module.ModuleCode,
                    Width = cellSize,
                    Height = cellSize,
                    IsEnabled = true,
                    IsHitTestVisible = false,
                    Focusable = false,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(10, 91, 176)),
                    Background = new SolidColorBrush(Color.FromRgb(210, 232, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(24, 119, 242)),
                    BorderThickness = new Thickness(1.5),
                    Opacity = 1.0
                };
                Canvas.SetLeft(moduleButton, outerPadding + module.GridColumn * cellPitch);
                Canvas.SetTop(moduleButton, outerPadding + module.GridRow * cellPitch);
                Panel.SetZIndex(moduleButton, 2);
                _maintenanceCanvas.Children.Add(moduleButton);
            }

            AddMaintenanceSideButton(
                "Top",
                left,
                top - maintenanceGap - maintenanceDepth,
                Math.Max(cellSize, right - left),
                maintenanceDepth);
            AddMaintenanceSideButton(
                "Bottom",
                left,
                bottom + maintenanceGap,
                Math.Max(cellSize, right - left),
                maintenanceDepth);
            AddMaintenanceSideButton(
                "Left",
                left - maintenanceGap - maintenanceDepth,
                top,
                maintenanceDepth,
                Math.Max(cellSize, bottom - top));
            AddMaintenanceSideButton(
                "Right",
                right + maintenanceGap,
                top,
                maintenanceDepth,
                Math.Max(cellSize, bottom - top));

            _maintenanceDataGrid?.Items.Refresh();
        }

        private void AddMaintenanceSideButton(
            string side,
            double left,
            double top,
            double width,
            double height)
        {
            FamilyLibraryMaintenanceSpaceItemViewModel selected = _item.MaintenanceSpaces
                .FirstOrDefault(x => string.Equals(x.Side, side, StringComparison.OrdinalIgnoreCase));

            bool isSelected = selected != null;
            Button button = new Button
            {
                Content = isSelected ? selected.MaintenanceCode : string.Empty,
                Width = width,
                Height = height,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isSelected
                    ? new SolidColorBrush(Color.FromRgb(213, 58, 112))
                    : new SolidColorBrush(Color.FromRgb(75, 104, 136)),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 221, 235))
                    : new SolidColorBrush(Color.FromRgb(242, 248, 255)),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 105, 160))
                    : new SolidColorBrush(Color.FromRgb(133, 180, 230)),
                BorderThickness = new Thickness(1.5),
                ToolTip = isSelected
                    ? "Click again to remove " + selected.MaintenanceCode + " (" + side + ")."
                    : "Click to add the " + side + " maintenance space."
            };

            button.Click += (sender, args) => ToggleMaintenanceSide(side);
            Canvas.SetLeft(button, left);
            Canvas.SetTop(button, top);
            Panel.SetZIndex(button, 1);
            _maintenanceCanvas.Children.Add(button);
            _maintenanceButtons[side] = button;
        }

        private FrameworkElement BuildMaintenanceTab()
        {
            WpfGrid grid = CreateEditorGrid(3);
            AddField(grid, 0, 0, "Required Maintenance Space (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.RequiredMaintenanceSpaceMm), false));
            AddField(grid, 0, 2, "Required Maintenance Space Side",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.RequiredMaintenanceSpaceSide), false));
            AddField(grid, 1, 0, "Door Side (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.MaintenanceDoorSideMm), false));
            AddField(grid, 1, 2, "Other Side (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.MaintenanceOtherSideMm), false));
            AddField(grid, 2, 0, "Front and Back (mm)",
                CreateBoundTextBox(nameof(FamilyLibraryManagerItemViewModel.MaintenanceFrontBackMm), false));
            return grid;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_item.DisplayName))
            {
                MessageBox.Show(
                    this,
                    Loc.T(LocalizedKeys.FamilyLibrary.DisplayNameRequired),
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            FamilyLibrarySubModuleItemViewModel invalidSubModule = _item.SubModules
                .OrderBy(x => x.Sequence)
                .FirstOrDefault(x =>
                    string.IsNullOrWhiteSpace(x.Name) ||
                    x.LengthMm <= 0 ||
                    x.WidthMm <= 0 ||
                    x.HeightMm <= 0 ||
                    x.WeightKg < 0);

            if (invalidSubModule != null)
            {
                MessageBox.Show(
                    this,
                    invalidSubModule.ModuleCode +
                    " requires Name and positive L/W/H values. Weight cannot be negative.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            FamilyLibraryMaintenanceSpaceItemViewModel invalidMaintenance = _item.MaintenanceSpaces
                .OrderBy(x => x.Sequence)
                .FirstOrDefault(x => x.DimensionMm <= 0 || x.IsWallSide && x.IsDoorSide);

            if (invalidMaintenance != null)
            {
                MessageBox.Show(
                    this,
                    invalidMaintenance.MaintenanceCode +
                    " requires a positive Dimensions(mm) value and cannot be both Wall Side and Door Side.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_item.MaintenanceSpaces.Count(x => x.IsDoorSide) > 1)
            {
                MessageBox.Show(
                    this,
                    "Only one maintenance space can be marked as Door Side.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_item.MaintenanceSpaces.Count(x => x.IsWallSide) > 3)
            {
                MessageBox.Show(
                    this,
                    "At most three maintenance spaces can be marked as Wall Side.",
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<SubModulePhotoCommitState> photoCommitStates = null;
            try
            {
                photoCommitStates = CommitPendingSubModulePhotos();
                _saveAction(_item);

                CleanupOldSubModulePhotosAfterSave();
                CompleteSubModulePhotoCommit(photoCommitStates);
                _pendingSubModulePhotoSources.Clear();

                MessageBox.Show(
                    this,
                    Loc.T(LocalizedKeys.FamilyLibrary.SaveSuccess),
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                RollbackCommittedSubModulePhotos(photoCommitStates);

                MessageBox.Show(
                    this,
                    ex.Message,
                    Loc.T(LocalizedKeys.FamilyLibrary.Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static TextBlock CreateLabel(string text, int row, int column)
        {
            TextBlock label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8),
                Foreground = new SolidColorBrush(Color.FromRgb(48, 60, 76))
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            return label;
        }

        private static void AddField(
            WpfGrid grid,
            int row,
            int column,
            string labelText,
            FrameworkElement editor)
        {
            grid.Children.Add(CreateLabel(labelText, row, column));
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, column + 1);
            grid.Children.Add(editor);
        }

        private static WpfGrid CreateEditorGrid(int rowCount)
        {
            WpfGrid grid = new WpfGrid
            {
                Margin = new Thickness(12, 16, 12, 12)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(190)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(190)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            for (int i = 0; i < rowCount; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            return grid;
        }

        private static TextBox CreateBoundTextBox(string path, bool isReadOnly)
        {
            TextBox textBox = new TextBox
            {
                Margin = new Thickness(8),
                MinHeight = 34,
                Padding = new Thickness(8, 5, 8, 5),
                IsReadOnly = isReadOnly,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            Binding binding = new Binding(path)
            {
                Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnExceptions = true,
                NotifyOnValidationError = true
            };
            textBox.SetBinding(TextBox.TextProperty, binding);
            return textBox;
        }

        private static CheckBox CreateBoundCheckBox(string path)
        {
            CheckBox checkBox = new CheckBox
            {
                Margin = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.SetBinding(CheckBox.IsCheckedProperty,
                new Binding(path)
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            return checkBox;
        }
    }
}