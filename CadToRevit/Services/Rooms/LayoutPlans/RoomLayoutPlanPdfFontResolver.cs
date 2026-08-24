using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;

namespace CadToRevit.Services.Rooms.LayoutPlans
{
    public sealed class RoomLayoutPlanPdfFontResolver : IFontResolver
    {
        private const string RegularFace = "LayoutPlanPdf#Regular";
        private const string BoldFace = "LayoutPlanPdf#Bold";
        private const string ItalicFace = "LayoutPlanPdf#Italic";
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, byte[]> FontCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static void EnsureRegistered()
        {
            lock (SyncRoot)
            {
                if (GlobalFontSettings.FontResolver == null)
                {
                    GlobalFontSettings.FontResolver = new RoomLayoutPlanPdfFontResolver();
                }
            }
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (isBold)
            {
                return new FontResolverInfo(BoldFace);
            }

            if (isItalic)
            {
                return new FontResolverInfo(ItalicFace);
            }

            return new FontResolverInfo(RegularFace);
        }

        public byte[] GetFont(string faceName)
        {
            lock (SyncRoot)
            {
                byte[] bytes;
                if (FontCache.TryGetValue(faceName, out bytes))
                {
                    return bytes;
                }

                string path = ResolveFontPath(faceName);
                bytes = File.ReadAllBytes(path);
                FontCache[faceName] = bytes;
                return bytes;
            }
        }

        private static string ResolveFontPath(string faceName)
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.Equals(faceName, BoldFace, StringComparison.OrdinalIgnoreCase))
            {
                return FirstExisting(fonts, "arialbd.ttf", "segoeuib.ttf", "calibrib.ttf", "timesbd.ttf");
            }

            if (string.Equals(faceName, ItalicFace, StringComparison.OrdinalIgnoreCase))
            {
                return FirstExisting(fonts, "ariali.ttf", "segoeuii.ttf", "calibrii.ttf", "timesi.ttf", "arial.ttf", "segoeui.ttf");
            }

            return FirstExisting(fonts, "arial.ttf", "segoeui.ttf", "calibri.ttf", "times.ttf");
        }

        private static string FirstExisting(string directory, params string[] names)
        {
            foreach (string name in names)
            {
                string path = Path.Combine(directory ?? string.Empty, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new FileNotFoundException("No suitable PDF font file was found in the Windows Fonts directory.");
        }
    }
}
