using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CadToRevit.Infrastructure.UI
{
    public static class LocalizedDialogService
    {
        private const string DefaultTitle = "EMSD AI Tool";

        public static bool Confirm(UIApplication uiApp, string message, string title)
        {
            return ShowDialog(uiApp, title, message, MessageBoxImage.Question, true);
        }

        public static void Info(UIApplication uiApp, string message, string title = null)
        {
            ShowDialog(uiApp, title ?? Loc.T("Dialog.Info.Title"), message, MessageBoxImage.Information, false);
        }

        public static void Success(UIApplication uiApp, string message, string title = null)
        {
            ShowDialog(uiApp, title ?? Loc.T("Dialog.Info.Title"), message, MessageBoxImage.None, false);
        }

        public static void Warning(UIApplication uiApp, string message, string title = null)
        {
            ShowDialog(uiApp, title ?? Loc.T("Dialog.Warning.Title"), message, MessageBoxImage.Warning, false);
        }

        public static void Error(UIApplication uiApp, string message, string title = null)
        {
            ShowDialog(uiApp, title ?? Loc.T("Dialog.Error.Title"), message, MessageBoxImage.Error, false);
        }

        private static bool ShowDialog(UIApplication uiApp, string title, string message, MessageBoxImage icon, bool showYesNo)
        {
            Window dialog = BuildDialog(DefaultTitle, message, icon, showYesNo);
            AttachOwner(dialog, uiApp);
            bool? result = dialog.ShowDialog();
            return result == true;
        }

        private static Window BuildDialog(string title, string message, MessageBoxImage icon, bool showYesNo)
        {
            Window dialog = new Window
            {
                Title = title ?? string.Empty,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 420,
                MaxWidth = 680,
                ShowInTaskbar = false,
                Background = Brushes.White
            };

            Grid root = new Grid
            {
                Margin = new Thickness(24)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid contentGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 24)
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border iconBorder = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = GetIconBrush(icon),
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            iconBorder.Child = new TextBlock
            {
                Text = GetIconSymbol(icon),
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
                Button noButton = CreateButton(Loc.T("Common.No"), false, true);
                noButton.Click += delegate { dialog.DialogResult = false; };
                buttonPanel.Children.Add(noButton);

                Button yesButton = CreateButton(Loc.T("Common.Yes"), true, false);
                yesButton.Click += delegate { dialog.DialogResult = true; };
                buttonPanel.Children.Add(yesButton);
            }
            else
            {
                Button okButton = CreateButton(Loc.T("Common.OK"), true, true);
                okButton.Click += delegate { dialog.DialogResult = true; };
                buttonPanel.Children.Add(okButton);
            }

            Grid.SetRow(buttonPanel, 1);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog;
        }

        private static void AttachOwner(Window dialog, UIApplication uiApp)
        {
            IntPtr ownerHandle = uiApp != null ? uiApp.MainWindowHandle : IntPtr.Zero;
            if (ownerHandle == IntPtr.Zero)
            {
                return;
            }

            WindowInteropHelper helper = new WindowInteropHelper(dialog);
            helper.Owner = ownerHandle;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private static Button CreateButton(string text, bool isDefault, bool isCancel)
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

        private static Brush GetIconBrush(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    return new SolidColorBrush(Color.FromRgb(196, 61, 57));
                case MessageBoxImage.Warning:
                    return new SolidColorBrush(Color.FromRgb(225, 148, 39));
                case MessageBoxImage.Question:
                    return new SolidColorBrush(Color.FromRgb(49, 132, 219));
                case MessageBoxImage.None:
                    return new SolidColorBrush(Color.FromRgb(46, 177, 92));
                default:
                    return new SolidColorBrush(Color.FromRgb(49, 132, 219));
            }
        }

        private static string GetIconSymbol(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Warning:
                    return "!";
                case MessageBoxImage.Error:
                    return "X";
                case MessageBoxImage.Question:
                    return "?";
                case MessageBoxImage.None:
                    return "\u2713";
                default:
                    return "i";
            }
        }
    }
}
