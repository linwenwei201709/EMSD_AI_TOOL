using CadToRevit.Services.Rooms.LayoutPlans;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.ExportDrawing
{
    public static class ExportDrawingPdfService
    {
        public static ExportDrawingPdfResult ExportTemporary(
            string projectName,
            IList<ExportDrawingViewImage> views)
        {
            RoomLayoutPlanPdfFontResolver.EnsureRegistered();

            if (views == null || views.Count != 5)
            {
                throw new InvalidOperationException("Five drawing views are required.");
            }

            string directory = GetTempDirectory();
            Directory.CreateDirectory(directory);

            DateTime generatedAt = DateTime.Now;
            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(projectName)
                ? "EMSD_Model"
                : projectName);
            string fileName = safeName + "_Drawing_" + generatedAt.ToString("yyyyMMdd_HHmmss") + ".pdf";
            string pdfPath = Path.Combine(directory, "preview_" + generatedAt.ToString("yyyyMMdd_HHmmss_fff") + ".pdf");

            using (PdfDocument document = new PdfDocument())
            {
                document.Info.Title = "Export Drawing";
                foreach (ExportDrawingViewImage view in views.OrderBy(x => x.PageNumber))
                {
                    PdfPage page = document.AddPage();
                    page.Width = XUnit.FromMillimeter(594);
                    page.Height = XUnit.FromMillimeter(420);

                    using (XGraphics gfx = XGraphics.FromPdfPage(page))
                    {
                        DrawPage(gfx, view);
                    }
                }

                if (document.Pages.Count != 5)
                {
                    throw new InvalidOperationException("Drawing PDF must contain exactly five pages.");
                }

                document.Save(pdfPath);
            }

            return new ExportDrawingPdfResult
            {
                PdfPath = pdfPath,
                FileName = fileName,
                GeneratedAt = generatedAt
            };
        }

        public static string GetTempDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EMSD AI Tool",
                "ExportDrawing");
        }

        private static void DrawPage(XGraphics gfx, ExportDrawingViewImage view)
        {
            XPen borderPen = new XPen(XColors.Black, 0.75);
            XPen thinPen = new XPen(XColors.Gray, 0.45);

            // Keep the same A2 engineering-drawing proportions used by the
            // existing Delivery Route PDF so the Export Drawing sheets share
            // one consistent visual language.
            XRect pageFrame = RectMm(10, 10, 574, 400);
            XRect titleRect = RectMm(15, 10, 560, 18);
            XRect titleBlockRect = RectMm(380, 305, 204, 105);

            // Tall views use the left side and leave the bottom-right title
            // block clear; wide views use the full width above the title block.
            XRect mainImageRect = SelectMainImageRect(
                view.ImagePath,
                RectMm(12, 32, 362, 373),
                RectMm(12, 32, 566, 268));

            DrawPageFrame(gfx, pageFrame, borderPen);
            DrawViewTitle(gfx, NormalizeTitle(view.ViewName), titleRect);
            DrawMainViewImage(gfx, view.ImagePath, mainImageRect, thinPen);
            DrawEngineeringTitleBlock(gfx, titleBlockRect, borderPen, thinPen);
        }

        private static XRect SelectMainImageRect(string imagePath, XRect leftTallRect, XRect topWideRect)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return leftTallRect;
            }

            try
            {
                using (XImage image = XImage.FromFile(imagePath))
                {
                    double leftArea = GetFitArea(image, leftTallRect);
                    double topArea = GetFitArea(image, topWideRect);
                    return topArea > leftArea ? topWideRect : leftTallRect;
                }
            }
            catch
            {
                return leftTallRect;
            }
        }

        private static double GetFitArea(XImage image, XRect rect)
        {
            double insetWidth = Math.Max(0, rect.Width - Mm(8));
            double insetHeight = Math.Max(0, rect.Height - Mm(8));
            double ratio = Math.Min(insetWidth / image.PixelWidth, insetHeight / image.PixelHeight);
            return image.PixelWidth * ratio * image.PixelHeight * ratio;
        }

        private static void DrawPageFrame(XGraphics gfx, XRect pageFrame, XPen borderPen)
        {
            gfx.DrawRectangle(borderPen, pageFrame);
        }

        private static void DrawViewTitle(XGraphics gfx, string title, XRect rect)
        {
            XFont titleFont = new XFont("Arial", 20, XFontStyleEx.Bold);
            gfx.DrawString(title, titleFont, XBrushes.Black, rect.Left, rect.Top + Mm(11));
            XSize size = gfx.MeasureString(title, titleFont);
            gfx.DrawLine(new XPen(XColors.Black, 0.65), rect.Left, rect.Top + Mm(15), rect.Left + size.Width, rect.Top + Mm(15));
        }

        private static void DrawMainViewImage(XGraphics gfx, string imagePath, XRect rect, XPen thinPen)
        {
            gfx.DrawRectangle(thinPen, rect);
            DrawImageFit(gfx, imagePath, Inset(rect, Mm(4), Mm(4), Mm(4), Mm(4)));
        }

        private static void DrawImageFit(XGraphics gfx, string imagePath, XRect rect)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                XFont font = new XFont("Arial", 14, XFontStyleEx.Regular);
                gfx.DrawString("Model view image unavailable.", font, XBrushes.Gray, rect, XStringFormats.Center);
                return;
            }

            using (XImage image = XImage.FromFile(imagePath))
            {
                double ratio = Math.Min(rect.Width / image.PixelWidth, rect.Height / image.PixelHeight);
                double width = image.PixelWidth * ratio;
                double height = image.PixelHeight * ratio;
                double x = rect.Left + (rect.Width - width) * 0.5;
                double y = rect.Top + (rect.Height - height) * 0.5;
                gfx.DrawImage(image, x, y, width, height);
            }
        }

        private static void DrawEngineeringTitleBlock(
            XGraphics gfx,
            XRect rect,
            XPen borderPen,
            XPen thinPen)
        {
            // Match the proven title-block structure used by
            // RoomLayoutPlanPdfExportService (Delivery Route PDF).
            XPen tablePen = new XPen(XColors.Black, 0.75);

            double leftW = Mm(78);
            double topH = Mm(22);
            double bottomH = Mm(20);
            double a2W = Mm(30);

            double rightX = rect.Left + leftW;
            double titleBottomY = rect.Top + topH;
            double bottomY = rect.Bottom - bottomH;
            double a2X = rect.Right - a2W;

            gfx.DrawRectangle(tablePen, rect);
            gfx.DrawLine(tablePen, rightX, rect.Top, rightX, rect.Bottom);
            gfx.DrawLine(tablePen, rect.Left, titleBottomY, rect.Right, titleBottomY);
            gfx.DrawLine(tablePen, rect.Left, bottomY, rect.Right, bottomY);
            gfx.DrawLine(tablePen, a2X, bottomY, a2X, rect.Bottom);

            // EMSD logo area - same source/fallback strategy as Delivery Route.
            XRect logoCell = new XRect(rect.Left, rect.Top, leftW, topH);
            DrawPdfLogo(gfx, Inset(logoCell, Mm(8), Mm(3), Mm(8), Mm(3)));

            // Drawing title.
            XFont titleFont = new XFont("Arial", 14.5, XFontStyleEx.Bold);
            XRect titleCell = new XRect(rightX, rect.Top, rect.Right - rightX, topH);
            gfx.DrawString(
                "EXPORT DRAWING",
                titleFont,
                XBrushes.Black,
                titleCell,
                XStringFormats.Center);

            // Information rows. Values intentionally stay blank in this phase.
            XFont fieldLabelFont = new XFont("Arial", 9.8, XFontStyleEx.Bold);
            XFont fieldTextFont = new XFont("Arial", 9.8, XFontStyleEx.Regular);

            double rowH = (bottomY - titleBottomY - Mm(7)) / 7.0;
            double rowY = titleBottomY + Mm(5.5);
            double labelX = rightX + Mm(5);
            double colonX = rightX + Mm(43);
            double valueX = rightX + Mm(50);
            double lineEndX = rect.Right - Mm(11);

            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "PROJECT", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "LOCATION", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DRAWING NO.", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DATE", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "SCALE", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DRAWN BY", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "CHECKED BY", string.Empty, fieldLabelFont, fieldTextFont);

            // Same bottom note and paper-size block as the Delivery Route PDF.
            XFont bottomNoteFont = new XFont("Arial", 9.2, XFontStyleEx.Bold);
            XRect noteCell = new XRect(rightX, bottomY, a2X - rightX, bottomH);
            gfx.DrawString(
                "ALL DIMENSIONS TO BE VERIFIED ON SITE",
                bottomNoteFont,
                XBrushes.Black,
                noteCell.Left + Mm(4),
                noteCell.Top + Mm(12));

            XFont a2Font = new XFont("Arial", 20, XFontStyleEx.Bold);
            XFont a2SubFont = new XFont("Arial", 9.5, XFontStyleEx.Bold);
            XRect a2Cell = new XRect(a2X, bottomY, a2W, bottomH);

            gfx.DrawString(
                "A2",
                a2Font,
                XBrushes.Black,
                new XRect(a2Cell.Left, a2Cell.Top + Mm(2), a2Cell.Width, Mm(10)),
                XStringFormats.Center);

            gfx.DrawString(
                "(594 x 420)",
                a2SubFont,
                XBrushes.Black,
                new XRect(a2Cell.Left, a2Cell.Top + Mm(12), a2Cell.Width, Mm(6)),
                XStringFormats.Center);
        }

        private static void DrawTitleBlockField(
            XGraphics gfx,
            double labelX,
            double colonX,
            double valueX,
            double lineEndX,
            double baselineY,
            string label,
            string value,
            XFont labelFont,
            XFont textFont)
        {
            gfx.DrawString(label, labelFont, XBrushes.Black, labelX, baselineY);
            gfx.DrawString(":", labelFont, XBrushes.Black, colonX, baselineY);

            if (string.IsNullOrWhiteSpace(value))
            {
                gfx.DrawLine(
                    new XPen(XColors.LightGray, 0.75),
                    valueX,
                    baselineY - Mm(1.4),
                    lineEndX,
                    baselineY - Mm(1.4));
                return;
            }

            gfx.DrawString(value, textFont, XBrushes.Black, valueX, baselineY);
        }

        private static void DrawPdfLogo(XGraphics gfx, XRect rect)
        {
            try
            {
                System.Drawing.Bitmap bitmap = global::CadToRevit.ResourceIcons.PDF_LOGO;
                if (bitmap != null)
                {
                    using (MemoryStream stream = new MemoryStream())
                    {
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        stream.Position = 0;

                        using (XImage image = XImage.FromStream(stream))
                        {
                            gfx.DrawImage(
                                image,
                                FitImage(image.PixelWidth, image.PixelHeight, rect));
                            return;
                        }
                    }
                }
            }
            catch
            {
            }

            string logoPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "PDF_LOGO.png");

            if (File.Exists(logoPath))
            {
                using (XImage image = XImage.FromFile(logoPath))
                {
                    gfx.DrawImage(
                        image,
                        FitImage(image.PixelWidth, image.PixelHeight, rect));
                    return;
                }
            }

            gfx.DrawString(
                "EMSD",
                new XFont("Arial", 18, XFontStyleEx.Bold),
                XBrushes.Black,
                rect,
                XStringFormats.Center);
        }

        private static XRect FitImage(double imageW, double imageH, XRect bounds)
        {
            if (imageW <= 0 || imageH <= 0)
            {
                return bounds;
            }

            double scale = Math.Min(
                bounds.Width / imageW,
                bounds.Height / imageH);

            double width = imageW * scale;
            double height = imageH * scale;

            return new XRect(
                bounds.Left + (bounds.Width - width) * 0.5,
                bounds.Top + (bounds.Height - height) * 0.5,
                width,
                height);
        }

        private static string NormalizeTitle(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToUpperInvariant();
        }

        private static string SanitizeFileName(string value)
        {
            string safe = value ?? string.Empty;
            foreach (char c in Path.GetInvalidFileNameChars())
            {  
                safe = safe.Replace(c, '_');
            }

            safe = safe.Trim();
            return string.IsNullOrWhiteSpace(safe) ? "EMSD_Model" : safe;
        }

        private static XRect RectMm(double x, double y, double width, double height)
        {
            return new XRect(Mm(x), Mm(y), Mm(width), Mm(height));
        }

        private static XRect Inset(XRect rect, double left, double top, double right, double bottom)
        {
            return new XRect(
                rect.Left + left,
                rect.Top + top,
                Math.Max(0, rect.Width - left - right),
                Math.Max(0, rect.Height - top - bottom));
        }

        private static double Mm(double value)
        {
            return XUnit.FromMillimeter(value).Point;
        }
    }
}