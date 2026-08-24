using CadToRevit.Infrastructure.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CadToRevit.UI
{
    internal sealed class HelpNoticeWindow : Window
    {
        private const string PlaceholderImage1 = "【此次插入图片1】";

        private HelpNoticeWindow(string title, string textContent, string image1Path)
        {
            Title = title;
            Width = 1750;
            Height = 1750;
            MinWidth = 1000;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Normal;
            Background = Brushes.White;

            // Keep requested large size, but clamp to current screen work area to avoid clipped/off-screen windows.
            Rect work = SystemParameters.WorkArea;
            MaxWidth = Math.Max(900, work.Width - 20);
            MaxHeight = Math.Max(700, work.Height - 20);
            Width = 900;// Math.Min(Width, MaxWidth);
            Height = Math.Min(Height, MaxHeight);

            Grid root = new Grid
            {
                Margin = new Thickness(16)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            BuildContent(content, textContent, image1Path);
            scroll.Content = content;
            Loaded += (s, e) => scroll.ScrollToTop();
            Grid.SetRow(scroll, 0);
            root.Children.Add(scroll);

            Button close = new Button
            {
                Content = "关闭",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            close.Click += (s, e) => Close();
            Grid.SetRow(close, 1);
            root.Children.Add(close);

            Content = root;
        }

        public static HelpNoticeWindow CreateFromProjectNotice()
        {
            string title = "插件使用注意事项";
            string cultureName = (LocalizationService.CurrentCulture == null ? string.Empty : LocalizationService.CurrentCulture.Name) ?? string.Empty;
            string noticeFile = ResolveNoticeFileName(cultureName);
            string docPath = FindFileFromRoots(new[]
            {
                Path.Combine("Doc", noticeFile)
            });

            string imagePath = FindFileFromRoots(new[]
            {
                Path.Combine("images", "1.png")
            });

            string text = "未找到帮助文档：" + Path.Combine("Doc", noticeFile);
            if (!string.IsNullOrWhiteSpace(docPath) && File.Exists(docPath))
            {
                text = File.ReadAllText(docPath);
            }

            return new HelpNoticeWindow(title, text, imagePath);
        }

        private static string ResolveNoticeFileName(string cultureName)
        {
            if (!string.IsNullOrWhiteSpace(cultureName))
            {
                if (cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
                {
                    return "Notice_CHT.txt";
                }

                if (cultureName.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                {
                    return "Notice_CHS.txt";
                }
            }

            return "Notice_ENU.txt";
        }

        private static void BuildContent(Panel panel, string textContent, string image1Path)
        {
            List<string> lines = (textContent ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n')
                .ToList();

            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (line.IndexOf(PlaceholderImage1, StringComparison.Ordinal) >= 0)
                {
                    AddImageBlock(panel, image1Path);
                    continue;
                }

                TextBlock tb = new TextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = Brushes.Black
                };

                if (i == 0)
                {
                    tb.FontSize = 28;
                    tb.FontWeight = FontWeights.Bold;
                    tb.Margin = new Thickness(0, 0, 0, 16);
                }
                else
                {
                    tb.FontSize = 19;
                }

                panel.Children.Add(tb);
            }
        }

        private static void AddImageBlock(Panel panel, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                TextBlock tip = new TextBlock
                {
                    Text = "图片未找到：images/1.png",
                    FontSize = 17,
                    Foreground = Brushes.DarkRed,
                    Margin = new Thickness(0, 8, 0, 12)
                };
                panel.Children.Add(tip);
                return;
            }

            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
            bmp.EndInit();

            Image img = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxHeight = 700,
                Margin = new Thickness(0, 8, 0, 12)
            };
            panel.Children.Add(img);
        }

        private static string FindFileFromRoots(IEnumerable<string> relativeCandidates)
        {
            if (relativeCandidates == null)
            {
                return null;
            }

            List<string> roots = BuildSearchRoots();
            foreach (string root in roots)
            {
                foreach (string relative in relativeCandidates)
                {
                    if (string.IsNullOrWhiteSpace(relative))
                    {
                        continue;
                    }

                    string full = Path.Combine(root, relative);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
            }

            return null;
        }

        private static List<string> BuildSearchRoots()
        {
            List<string> roots = new List<string>();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                roots.Add(baseDir);
            }

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                roots.Add(asmDir);
            }

            string current = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(current))
            {
                roots.Add(current);
            }

            List<string> expanded = new List<string>();
            foreach (string root in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                expanded.Add(root);
                try
                {
                    DirectoryInfo di = new DirectoryInfo(root);
                    for (int i = 0; i < 5 && di != null; i++)
                    {
                        di = di.Parent;
                        if (di != null)
                        {
                            expanded.Add(di.FullName);
                        }
                    }
                }
                catch
                {
                    // Keep best-effort search only.
                }
            }

            return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
