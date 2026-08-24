using CadToRevit.Models.Rooms.LayoutPlans;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Rooms.LayoutPlans
{
    public sealed class RoomLayoutPlanPdfExportContext
    {
        public RoomLayoutPlanDto Plan { get; set; }

        public string MainViewImagePath { get; set; }

        public string KeyPlanImagePath { get; set; }
    }

    public sealed class RoomLayoutPlanPdfExportResult
    {
        public string PdfPath { get; set; }

        public string FileName { get; set; }

        public DateTime GeneratedAt { get; set; }
    }

    public static class RoomLayoutPlanPdfExportService
    {
        public static RoomLayoutPlanPdfExportResult ExportTemporary(RoomLayoutPlanPdfExportContext context)
        {
            RoomLayoutPlanPdfFontResolver.EnsureRegistered();

            if (context == null || context.Plan == null)
            {
                throw new InvalidOperationException("Layout plan not found.");
            }

            string directory = GetExportTempDirectory();
            Directory.CreateDirectory(directory);

            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(context.Plan.SolutionName)
                ? "Layout_Plan"
                : context.Plan.SolutionName);
            DateTime generatedAt = DateTime.Now;
            string fileName = "AHU_Delivery_Route_Plan_" + safeName + "_" + generatedAt.ToString("yyyyMMdd_HHmmss") + ".pdf";
            string pdfPath = Path.Combine(directory, "preview_" + context.Plan.LayoutId + "_" + generatedAt.ToString("yyyyMMdd_HHmmss") + ".pdf");

            using (PdfDocument document = new PdfDocument())
            {
                document.Info.Title = "AHU Delivery Route Plan";

                // Page 1 - existing delivery route report layout.
                PdfPage page1 = document.AddPage();
                page1.Width = XUnit.FromMillimeter(594);
                page1.Height = XUnit.FromMillimeter(420);
                using (XGraphics gfx = XGraphics.FromPdfPage(page1))
                {
                    DrawPage(gfx, context, generatedAt);
                }

                // Page 2 - full top view of the DWG/model with the current route boxes.
                // Reuse the same Revit TOP-3D export used by the Key Plan so the DWG,
                // target room and route DirectShapes stay in one Revit coordinate system.
                PdfPage page2 = document.AddPage();
                page2.Width = XUnit.FromMillimeter(594);
                page2.Height = XUnit.FromMillimeter(420);
                using (XGraphics gfx = XGraphics.FromPdfPage(page2))
                {
                    DrawFullTopViewPage(gfx, context.KeyPlanImagePath);
                }

                // Page 3 - fixed customer image stored under the plugin solution Images folder.
                PdfPage page3 = document.AddPage();
                page3.Width = XUnit.FromMillimeter(594);
                page3.Height = XUnit.FromMillimeter(420);
                using (XGraphics gfx = XGraphics.FromPdfPage(page3))
                {
                    DrawFixedThirdPage(gfx, ResolvePdf3ImagePath());
                }

                document.Save(pdfPath);
            }

            return new RoomLayoutPlanPdfExportResult
            {
                PdfPath = pdfPath,
                FileName = fileName,
                GeneratedAt = generatedAt
            };
        }

        public static string GetExportTempDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EMSD AI Tool",
                "LayoutPlanExports");
        }

        private static void DrawFullTopViewPage(XGraphics gfx, string topViewImagePath)
        {
            // Keep a very small page margin only. The exported image itself already contains
            // the source drawing border/title block, so no additional report frame is added.
            XRect imageRect = RectMm(3, 3, 588, 414);
            DrawImageOrPlaceholder(
                gfx,
                topViewImagePath,
                imageRect,
                "Top view image unavailable.",
                false);
        }

        private static void DrawFixedThirdPage(XGraphics gfx, string imagePath)
        {
            // Page 3 is a fixed image page. Preserve the full image without stretching/cropping.
            XRect pageRect = RectMm(0, 0, 594, 420);
            DrawImageOrPlaceholder(
                gfx,
                imagePath,
                pageRect,
                @"Images\pdf3.png not found.",
                false);
        }

        private static string ResolvePdf3ImagePath()
        {
            const string imageName = "pdf3.png";
            List<string> roots = new List<string>();

            AddSearchRoot(roots, AppDomain.CurrentDomain.BaseDirectory);
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(typeof(RoomLayoutPlanPdfExportService).Assembly.Location);
                AddSearchRoot(roots, assemblyDirectory);
            }
            catch
            {
            }

            try
            {
                AddSearchRoot(roots, Environment.CurrentDirectory);
            }
            catch
            {
            }

            foreach (string root in roots)
            {
                DirectoryInfo current = null;
                try
                {
                    current = new DirectoryInfo(root);
                }
                catch
                {
                }

                // Local debugging normally runs from bin\... while Images is at the solution/project root.
                // Installed builds commonly place Images beside the plugin DLL. Searching parent folders
                // supports both layouts without hard-coding a developer machine path.
                for (int depth = 0; current != null && depth < 8; depth++)
                {
                    string candidate = Path.Combine(current.FullName, "Images", imageName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    current = current.Parent;
                }
            }

            return string.Empty;
        }

        private static void AddSearchRoot(List<string> roots, string path)
        {
            if (roots == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!roots.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
            {
                roots.Add(path);
            }
        }

        private static void DrawPage(XGraphics gfx, RoomLayoutPlanPdfExportContext context, DateTime generatedAt)
        {
            RoomLayoutPlanDto plan = context.Plan;
            XFont titleFont = new XFont("Arial", 22, XFontStyleEx.Bold);
            XFont sectionFont = new XFont("Arial", 15.5, XFontStyleEx.Bold);
            XFont tableHeaderFont = new XFont("Arial", 13.5, XFontStyleEx.Bold);
            XFont labelFont = new XFont("Arial", 13.2, XFontStyleEx.Bold);
            XFont textFont = new XFont("Arial", 13.0, XFontStyleEx.Regular);
            XFont notesFont = new XFont("Arial", 12.5, XFontStyleEx.Regular);
            XPen borderPen = new XPen(XColors.Black, 0.75);
            XPen thinPen = new XPen(XColors.Gray, 0.45);

            gfx.DrawRectangle(borderPen, RectMm(10, 10, 574, 400));
            DrawTitle(gfx, "AHU DELIVERY ROUTE PLAN", titleFont, Mm(15), Mm(12));
            XSize titleSize = gfx.MeasureString("AHU DELIVERY ROUTE PLAN", titleFont);
            gfx.DrawLine(borderPen, Mm(15), Mm(24), Mm(15) + titleSize.Width, Mm(24));

            XRect mainRect = RectMm(12, 26, 378, 274);
            DrawMainViewImageOrPlaceholder(gfx, context.MainViewImagePath, mainRect, "3D route view image unavailable.");

            XRect scheduleRect = RectMm(395, 25, 180, 85);
            DrawModuleSchedule(gfx, AhuSubModuleScheduleService.BuildForPlan(plan), scheduleRect, tableHeaderFont, textFont, thinPen);

            XRect legendRect = RectMm(405, 118, 150, 68);
            DrawLegend(gfx, legendRect, sectionFont, textFont, borderPen);

            XRect notesRect = RectMm(405, 194, 170, 104);
            DrawNotes(gfx, notesRect, sectionFont, notesFont);

            XRect routeInfoRect = RectMm(15, 305, 145, 75);
            DrawRouteInfo(gfx, plan, routeInfoRect, labelFont, textFont, thinPen);

            XRect keyPlanRect = RectMm(180, 305, 165, 100);
            DrawSectionHeader(gfx, "KEY PLAN (NOT TO SCALE)", sectionFont, keyPlanRect, borderPen);
            DrawImageOrPlaceholder(gfx, context.KeyPlanImagePath, Inset(keyPlanRect, Mm(1.5), Mm(15.5), Mm(1.5), Mm(1.5)), "Key plan image unavailable.", false);

            XRect titleBlockRect = RectMm(380, 305, 204, 105);
            DrawTitleBlock(gfx, plan, generatedAt, titleBlockRect, labelFont, textFont, thinPen);
        }

        private static void DrawModuleSchedule(XGraphics gfx, List<AhuSubModuleScheduleRow> rows, XRect rect, XFont labelFont, XFont textFont, XPen thinPen)
        {
            XFont titleFont = new XFont("Arial", 15.5, XFontStyleEx.Bold);
            XFont headerFont = new XFont("Arial", 12.5, XFontStyleEx.Bold);
            XFont bodyFont = new XFont("Arial", 12.5, XFontStyleEx.Regular);
            XPen tablePen = new XPen(XColors.Black, 0.7);
            XBrush headerBrush = new XSolidBrush(XColor.FromArgb(245, 245, 245));

            gfx.DrawString("AHU SUB-MODULE SCHEDULE", titleFont, XBrushes.Black, rect.Left, rect.Top + Mm(5.5));

            double x = rect.Left;
            double y = rect.Top + Mm(13);
            double w = rect.Width;
            int rowCount = Math.Max(1, rows != null ? rows.Count : 0);
            double headerH = Mm(13);
            double availableBodyH = Math.Max(Mm(18), rect.Bottom - y - headerH);
            double rowH = Math.Min(Mm(17.5), availableBodyH / rowCount);
            double[] cols = { Mm(42), Mm(50), Mm(42), Mm(17), w - Mm(151) };
            string[] headers = { "SUB-MODULE", "TYPE", "DIMENSIONS (mm)", "QTY.", "REMARKS" };
            DrawScheduleRow(gfx, x, y, cols, headerH, headers, headerFont, tablePen, headerBrush, true, true);
            y += headerH;

            foreach (AhuSubModuleScheduleRow row in rows)
            {
                string[] values =
                {
                    NormalizeScheduleText(row.SubModule),
                    NormalizeScheduleText(row.Type),
                    NormalizeScheduleText(row.DimensionsMm),
                    NormalizeScheduleText(row.Quantity),
                    NormalizeScheduleText(row.Remarks)
                };
                DrawScheduleRow(gfx, x, y, cols, rowH, values, bodyFont, tablePen, null, false, false);
                y += rowH;
            }
        }

        private static void DrawRouteInfo(XGraphics gfx, RoomLayoutPlanDto plan, XRect rect, XFont labelFont, XFont textFont, XPen thinPen)
        {
            LayoutDeliveryRouteDto route = plan.DeliveryRoute ?? new LayoutDeliveryRouteDto();
            string routeLength = !string.IsNullOrWhiteSpace(route.RouteLengthText)
                ? route.RouteLengthText
                : (!string.IsNullOrWhiteSpace(plan.RouteLengthText) ? plan.RouteLengthText : "-");
            string target = !string.IsNullOrWhiteSpace(route.TargetRoomName) ? route.TargetRoomName : plan.RoomName;
            string start = string.Equals(route.StartLocationType, "Point", StringComparison.OrdinalIgnoreCase)
                ? (!string.IsNullOrWhiteSpace(route.StartPointName) ? route.StartPointName : "Start Point")
                : (!string.IsNullOrWhiteSpace(route.StartLiftName) ? route.StartLiftName : "-");
            string disassembly = AhuSubModuleScheduleService.BuildForPlan(plan).Count + " SUB-MODULES";
            string status = route.HasRoute ? "PASSED" : "-";

            XPen tablePen = new XPen(XColors.Black, 0.75);
            XFont titleFont = new XFont("Arial", 13.5, XFontStyleEx.Bold);
            XFont rowLabelFont = new XFont("Arial", 11.6, XFontStyleEx.Bold);
            XFont rowTextFont = new XFont("Arial", 11.6, XFontStyleEx.Regular);
            XStringFormat leftCenter = new XStringFormat
            {
                Alignment = XStringAlignment.Near,
                LineAlignment = XLineAlignment.Center
            };

            double titleH = Mm(16);
            double rowH = (rect.Height - titleH) / 5.0;
            double colSplitX = rect.Left + Mm(74);
            double bodyTop = rect.Top + titleH;

            gfx.DrawRectangle(tablePen, rect);
            gfx.DrawLine(tablePen, rect.Left, bodyTop, rect.Right, bodyTop);
            gfx.DrawLine(tablePen, colSplitX, bodyTop, colSplitX, rect.Bottom);
            for (int i = 1; i < 5; i++)
            {
                double y = bodyTop + rowH * i;
                gfx.DrawLine(tablePen, rect.Left, y, rect.Right, y);
            }

            gfx.DrawString("ROUTE INFORMATION", titleFont, XBrushes.Black, Inset(new XRect(rect.Left, rect.Top, rect.Width, titleH), Mm(6), 0, Mm(3), 0), leftCenter);

            string[] labels =
            {
                "START POINT:",
                "TARGET ROOM:",
                "TOTAL ROUTE LENGTH:",
                "DISASSEMBLY:",
                "STATUS:"
            };
            string[] values =
            {
                start,
                target,
                routeLength,
                disassembly,
                status
            };

            for (int i = 0; i < labels.Length; i++)
            {
                double y = bodyTop + rowH * i;
                XRect labelCell = new XRect(rect.Left, y, colSplitX - rect.Left, rowH);
                XRect valueCell = new XRect(colSplitX, y, rect.Right - colSplitX, rowH);
                gfx.DrawString(labels[i], rowLabelFont, XBrushes.Black, Inset(labelCell, Mm(6), 0, Mm(3), 0), leftCenter);
                gfx.DrawString(string.IsNullOrWhiteSpace(values[i]) ? "-" : values[i], rowTextFont, XBrushes.Black, Inset(valueCell, Mm(6), 0, Mm(3), 0), leftCenter);
            }
        }

        private static void DrawNotes(XGraphics gfx, XRect rect, XFont titleFont, XFont textFont)
        {
            double x = rect.Left;
            double y = rect.Top + Mm(4);
            double rowH = Mm(7.4);
            gfx.DrawString("NOTES:", titleFont, XBrushes.Black, x, y);
            y += Mm(9);

            gfx.DrawString("1. ALL DIMENSIONS ARE IN MILLIMETRES.", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("2. CONTRACTOR SHALL VERIFY ALL DIMENSIONS ON SITE", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   BEFORE SUBMITTING THE TENDER.", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("3. ALL DOORS, CORRIDORS, LIFTS AND OPENINGS ALONG", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   THE DELIVERY ROUTE SHALL BE CHECKED AND", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   CONFIRMED BY CONTRACTOR PRIOR TO INSTALLATION.", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("4. ADEQUATE PROTECTION SHALL BE PROVIDED TO", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   FINISHED SURFACES ALONG THE DELIVERY ROUTE.", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("5. THE DELIVERY ROUTE SHOWN IS BASED ON THE ASSUMED", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   PATH. CONTRACTOR SHALL SUBMIT ALTERNATIVE ROUTE", textFont, XBrushes.Black, x, y);
            y += rowH;
            gfx.DrawString("   IF NECESSARY FOR APPROVAL.", textFont, XBrushes.Black, x, y);
        }

        private static void DrawTitleBlock(XGraphics gfx, RoomLayoutPlanDto plan, DateTime date, XRect rect, XFont labelFont, XFont textFont, XPen thinPen)
        {
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

            XRect logoCell = new XRect(rect.Left, rect.Top, leftW, topH);
            DrawPdfLogo(gfx, Inset(logoCell, Mm(8), Mm(3), Mm(8), Mm(3)));

            XFont titleBlockTitleFont = new XFont("Arial", 14.5, XFontStyleEx.Bold);
            XRect titleCell = new XRect(rightX, rect.Top, rect.Right - rightX, topH);
            double titleX = titleCell.Left + Mm(14);
            gfx.DrawString("AHU DELIVERY ROUTE PLAN", titleBlockTitleFont, XBrushes.Black, titleX, titleCell.Top + Mm(8.5));
            gfx.DrawString("(TENDER DRAWING)", titleBlockTitleFont, XBrushes.Black, titleX, titleCell.Top + Mm(16.0));

            XFont fieldLabelFont = new XFont("Arial", 9.8, XFontStyleEx.Bold);
            XFont fieldTextFont = new XFont("Arial", 9.8, XFontStyleEx.Regular);
            double rowH = (bottomY - titleBottomY - Mm(7)) / 7.0;
            double rowY = titleBottomY + Mm(5.5);
            double labelX = rightX + Mm(5);
            double colonX = rightX + Mm(43);
            double valueX = rightX + Mm(50);
            double lineEndX = rect.Right - Mm(11);

            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "PROJECT", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "LOCATION", plan.LevelText ?? string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DRAWING NO.", "MEP-AHU-DR-001", fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DATE", date.ToString("yyyy-MM-dd"), fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "SCALE", "1 : 100", fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "DRAWN BY", string.Empty, fieldLabelFont, fieldTextFont); rowY += rowH;
            DrawTitleBlockField(gfx, labelX, colonX, valueX, lineEndX, rowY, "CHECKED BY", string.Empty, fieldLabelFont, fieldTextFont);

            XFont bottomNoteFont = new XFont("Arial", 9.2, XFontStyleEx.Bold);
            XRect noteCell = new XRect(rightX, bottomY, a2X - rightX, bottomH);
            gfx.DrawString("ALL DIMENSIONS TO BE VERIFIED ON SITE", bottomNoteFont, XBrushes.Black, noteCell.Left + Mm(4), noteCell.Top + Mm(12));

            XFont a2Font = new XFont("Arial", 20, XFontStyleEx.Bold);
            XFont a2SubFont = new XFont("Arial", 9.5, XFontStyleEx.Bold);
            XRect a2Cell = new XRect(a2X, bottomY, a2W, bottomH);
            gfx.DrawString("A2", a2Font, XBrushes.Black, new XRect(a2Cell.Left, a2Cell.Top + Mm(2), a2Cell.Width, Mm(10)), XStringFormats.Center);
            gfx.DrawString("(594 x 420)", a2SubFont, XBrushes.Black, new XRect(a2Cell.Left, a2Cell.Top + Mm(12), a2Cell.Width, Mm(6)), XStringFormats.Center);
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
                gfx.DrawLine(new XPen(XColors.LightGray, 0.75), valueX, baselineY - Mm(1.4), lineEndX, baselineY - Mm(1.4));
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
                            gfx.DrawImage(image, FitImage(image.PixelWidth, image.PixelHeight, rect));
                            return;
                        }
                    }
                }
            }
            catch
            {
            }

            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "PDF_LOGO.png");
            if (File.Exists(logoPath))
            {
                using (XImage image = XImage.FromFile(logoPath))
                {
                    gfx.DrawImage(image, FitImage(image.PixelWidth, image.PixelHeight, rect));
                    return;
                }
            }

            gfx.DrawString("EMSD", new XFont("Arial", 18, XFontStyleEx.Bold), XBrushes.Black, rect, XStringFormats.Center);
        }

        private static void DrawTableRow(XGraphics gfx, double x, double y, double[] cols, double h, string[] values, XFont font, XPen pen)
        {
            double current = x;
            for (int i = 0; i < cols.Length; i++)
            {
                gfx.DrawRectangle(pen, current, y, cols[i], h);
                gfx.DrawString(Truncate(values.Length > i ? values[i] : string.Empty, 30), font, XBrushes.Black, new XRect(current + Mm(1.5), y + Mm(2), cols[i] - Mm(3), h - Mm(2)), XStringFormats.TopLeft);
                current += cols[i];
            }
        }

        private static void DrawScheduleRow(
            XGraphics gfx,
            double x,
            double y,
            double[] cols,
            double h,
            string[] values,
            XFont font,
            XPen pen,
            XBrush background,
            bool isHeader,
            bool allCenter)
        {
            double current = x;
            for (int i = 0; i < cols.Length; i++)
            {
                XRect cell = new XRect(current, y, cols[i], h);
                if (background != null)
                {
                    gfx.DrawRectangle(background, cell);
                }

                gfx.DrawRectangle(pen, cell);
                string text = values != null && values.Length > i ? values[i] : string.Empty;
                bool center = allCenter || i == 2 || i == 3 || i == 4;
                DrawScheduleCellText(gfx, cell, text, font, center, i == 1 && !isHeader);
                current += cols[i];
            }
        }

        private static void DrawScheduleCellText(XGraphics gfx, XRect cell, string text, XFont font, bool center, bool allowWrap)
        {
            XRect inner = Inset(cell, Mm(2), Mm(1.5), Mm(2), Mm(1.5));
            string[] lines = allowWrap
                ? WrapScheduleText(gfx, text, font, inner.Width, 2)
                : new[] { text ?? string.Empty };
            double lineH = font.GetHeight();
            double totalH = lineH * lines.Length;
            double y = inner.Top + Math.Max(0.0, (inner.Height - totalH) * 0.5) + lineH * 0.78;
            XStringFormat format = center ? XStringFormats.Center : XStringFormats.TopLeft;

            foreach (string line in lines)
            {
                XRect lineRect = center
                    ? new XRect(inner.Left, y - lineH * 0.78, inner.Width, lineH)
                    : new XRect(inner.Left, y - lineH * 0.78, inner.Width, lineH);
                gfx.DrawString(line, font, XBrushes.Black, lineRect, format);
                y += lineH;
            }
        }

        private static string[] WrapScheduleText(XGraphics gfx, string text, XFont font, double maxWidth, int maxLines)
        {
            string value = text ?? string.Empty;
            string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return new[] { string.Empty };
            }

            List<string> lines = new List<string>();
            string current = string.Empty;
            foreach (string word in words)
            {
                string candidate = string.IsNullOrWhiteSpace(current) ? word : current + " " + word;
                if (gfx.MeasureString(candidate, font).Width <= maxWidth || string.IsNullOrWhiteSpace(current))
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
                if (lines.Count >= maxLines - 1)
                {
                    break;
                }
            }

            if (lines.Count < maxLines && !string.IsNullOrWhiteSpace(current))
            {
                lines.Add(current);
            }

            return lines.Count == 0 ? new[] { value } : lines.ToArray();
        }

        private static string NormalizeScheduleText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "-"
                : value.ToUpperInvariant();
        }

        private static void DrawKeyValue(XGraphics gfx, double x, double y, string label, string value, XFont labelFont, XFont textFont)
        {
            DrawKeyValue(gfx, x, y, label, value, labelFont, textFont, Mm(48));
        }

        private static void DrawKeyValue(XGraphics gfx, double x, double y, string label, string value, XFont labelFont, XFont textFont, double valueOffset)
        {
            gfx.DrawString(label, labelFont, XBrushes.Black, x, y);
            gfx.DrawString(string.IsNullOrWhiteSpace(value) ? "-" : value, textFont, XBrushes.Black, x + valueOffset, y);
        }

        private static void DrawLegend(XGraphics gfx, XRect rect, XFont titleFont, XFont textFont, XPen borderPen)
        {
            gfx.DrawRectangle(borderPen, rect);
            gfx.DrawString("LEGEND", titleFont, XBrushes.Black, rect.Left + Mm(6), rect.Top + Mm(9));

            double x = rect.Left + Mm(8);
            double centerY = rect.Top + Mm(26);
            double rowH = Mm(8.7);
            double icon = Mm(5.5); 
            double textX = x + Mm(18);
            double textW = rect.Right - textX - Mm(5);
            XPen thinBlack = new XPen(XColors.Black, 1.0);

            DrawCenteredLegendText(gfx, "START POINT", textFont, textX, centerY, textW);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(65, 132, 219)), x, centerY - icon * 0.5, icon, icon);

            centerY += rowH;
            DrawCenteredLegendText(gfx, "TARGET ROOM", textFont, textX, centerY, textW);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(68, 190, 92)), x, centerY - icon * 0.5, icon, icon);

            centerY += rowH;
            DrawCenteredLegendText(gfx, "DELIVERY ROUTE", textFont, textX, centerY, textW);
            XRect routeIcon = new XRect(x, centerY - icon * 0.5, icon, icon);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(70, 255, 183, 97)), routeIcon);
            XPen routeBorder = new XPen(XColor.FromArgb(255, 255, 155, 59), 0.7) { DashStyle = XDashStyle.Dash };
            gfx.DrawRectangle(routeBorder, routeIcon);

            centerY += rowH;
            DrawCenteredLegendText(gfx, "DIRECTION OF MOVEMENT", textFont, textX, centerY, textW);
            double arrowY = centerY;
            gfx.DrawLine(thinBlack, x, arrowY, x + Mm(9), arrowY);
            gfx.DrawLine(thinBlack, x + Mm(9), arrowY, x + Mm(6), arrowY - Mm(2.5));
            gfx.DrawLine(thinBlack, x + Mm(9), arrowY, x + Mm(6), arrowY + Mm(2.5));

            centerY += rowH;
            DrawCenteredLegendText(gfx, "DOOR / OPENING", textFont, textX, centerY, textW);
            DrawDoorOpeningLegendIcon(gfx, x, centerY);
        }

        private static void DrawCenteredLegendText(XGraphics gfx, string text, XFont font, double x, double centerY, double width)
        {
            gfx.DrawString(
                text,
                font,
                XBrushes.Black,
                new XRect(x, centerY - Mm(4), width, Mm(8)),
                XStringFormats.CenterLeft);
        }

        private static void DrawDoorOpeningLegendIcon(XGraphics gfx, double x, double centerY)
        {
            // Draw a compact architectural door/opening symbol for the PDF legend:
            // a small L-shaped frame plus a quarter-swing arc. Keep it inside the
            // same visual row height as the other legend icons so it aligns with text.
            XPen doorPen = new XPen(XColor.FromArgb(150, 150, 150), 0.75);

            double iconW = Mm(7.0);
            double iconH = Mm(7.0);
            double left = x;
            double bottom = centerY + iconH * 0.5;

            // Use one shared radius for the frame and the swing arc, so the
            // vertical jamb, threshold and quarter-arc meet exactly at the same
            // endpoints. This avoids the small gap that appeared between the arc
            // endpoint and the bottom threshold line.
            double hingeX = left;
            double hingeY = bottom;
            double radius = Math.Min(iconW, iconH) - Mm(1.0);
            double frameTopY = hingeY - radius;
            double frameEndX = hingeX + radius;

            // Door frame: left jamb + bottom threshold.
            gfx.DrawLine(doorPen, hingeX, frameTopY, hingeX, hingeY);
            gfx.DrawLine(doorPen, hingeX, hingeY, frameEndX, hingeY);

            // Door swing: quarter arc centered at the hinge. Use a Bezier quarter
            // circle instead of DrawArc to avoid oversized/offset arcs. Start/end
            // points match the frame endpoints above.
            double k = 0.5522847498307936;

            double startX = hingeX;
            double startY = frameTopY;
            double c1X = hingeX + k * radius;
            double c1Y = frameTopY;
            double c2X = frameEndX;
            double c2Y = hingeY - k * radius;
            double endX = frameEndX;
            double endY = hingeY;

            gfx.DrawBezier(doorPen, startX, startY, c1X, c1Y, c2X, c2Y, endX, endY);
        }

        private static void DrawSectionHeader(XGraphics gfx, string title, XFont font, XRect rect, XPen pen)
        {
            gfx.DrawRectangle(pen, rect);
            gfx.DrawLine(pen, rect.Left, rect.Top + Mm(14), rect.Right, rect.Top + Mm(14));
            gfx.DrawString(title, font, XBrushes.Black, rect.Left + Mm(5), rect.Top + Mm(9));
        }

        private static void DrawTitle(XGraphics gfx, string title, XFont font, double x, double y)
        {
            gfx.DrawString(title, font, XBrushes.Black, x, y + Mm(7));
        }

        private static void DrawImageOrPlaceholder(XGraphics gfx, string imagePath, XRect rect, string placeholder, bool cropToFill)
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                using (XImage image = XImage.FromFile(imagePath))
                {
                    XRect target = cropToFill
                        ? FitImageToCover(image.PixelWidth, image.PixelHeight, rect)
                        : FitImage(image.PixelWidth, image.PixelHeight, rect);
                    XGraphicsState state = gfx.Save();
                    gfx.IntersectClip(rect);
                    gfx.DrawImage(image, target);
                    gfx.Restore(state);
                }
                return;
            }

            gfx.DrawRectangle(new XPen(XColors.Gray, 0.4), rect);
            gfx.DrawString(placeholder, new XFont("Arial", 13, XFontStyleEx.Italic), XBrushes.Gray, rect, XStringFormats.Center);
        }

        private static void DrawMainViewImageOrPlaceholder(XGraphics gfx, string imagePath, XRect rect, string placeholder)
        {
            string drawPath = CropMainViewWhitespace(imagePath);
            if (!string.IsNullOrWhiteSpace(drawPath) && File.Exists(drawPath))
            {
                using (XImage image = XImage.FromFile(drawPath))
                {
                    XRect target = FitImageTopLeft(image.PixelWidth, image.PixelHeight, rect, 1.10);
                    XGraphicsState state = gfx.Save();
                    gfx.IntersectClip(rect);
                    gfx.DrawImage(image, target);
                    gfx.Restore(state);
                }
                return;
            }

            gfx.DrawRectangle(new XPen(XColors.Gray, 0.4), rect);
            gfx.DrawString(placeholder, new XFont("Arial", 13, XFontStyleEx.Italic), XBrushes.Gray, rect, XStringFormats.Center);
        }

        private static XRect FitImage(double imageW, double imageH, XRect bounds)
        {
            if (imageW <= 0 || imageH <= 0)
            {
                return bounds;
            }

            double scale = Math.Min(bounds.Width / imageW, bounds.Height / imageH);
            double w = imageW * scale;
            double h = imageH * scale;
            return new XRect(bounds.Left + (bounds.Width - w) * 0.5, bounds.Top + (bounds.Height - h) * 0.5, w, h);
        }

        private static XRect FitImageTopLeft(double imageW, double imageH, XRect bounds, double scaleFactor)
        {
            if (imageW <= 0 || imageH <= 0)
            {
                return bounds;
            }

            double scale = Math.Min(bounds.Width / imageW, bounds.Height / imageH);
            scale *= Math.Max(1.0, scaleFactor);
            return new XRect(bounds.Left, bounds.Top, imageW * scale, imageH * scale);
        }

        private static string CropMainViewWhitespace(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return imagePath;
            }

            try
            {
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(imagePath))
                {
                    int minX = bitmap.Width;
                    int minY = bitmap.Height;
                    int maxX = -1;
                    int maxY = -1;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            System.Drawing.Color color = bitmap.GetPixel(x, y);
                            if (IsMainViewContentPixel(color))
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }

                    if (maxX <= minX || maxY <= minY)
                    {
                        return imagePath;
                    }

                    int margin = Math.Max(16, Math.Min(bitmap.Width, bitmap.Height) / 80);
                    minX = Math.Max(0, minX - margin);
                    minY = Math.Max(0, minY - margin);
                    maxX = Math.Min(bitmap.Width - 1, maxX + margin);
                    maxY = Math.Min(bitmap.Height - 1, maxY + margin);
                    System.Drawing.Rectangle cropRect = System.Drawing.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                    if (cropRect.Width >= bitmap.Width * 0.99 && cropRect.Height >= bitmap.Height * 0.99)
                    {
                        return imagePath;
                    }

                    using (System.Drawing.Bitmap cropped = bitmap.Clone(cropRect, bitmap.PixelFormat))
                    {
                        string croppedPath = Path.Combine(
                            Path.GetDirectoryName(imagePath),
                            Path.GetFileNameWithoutExtension(imagePath) + "_main_cropped.png");
                        cropped.Save(croppedPath, System.Drawing.Imaging.ImageFormat.Png);
                        return croppedPath;
                    }
                }
            }
            catch
            {
                return imagePath;
            }
        }

        private static bool IsMainViewContentPixel(System.Drawing.Color color)
        {
            if (color.A < 16)
            {
                return false;
            }

            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            return max < 248 || max - min > 14;
        }

        private static XRect FitImageToCover(double imageW, double imageH, XRect bounds)
        {
            if (imageW <= 0 || imageH <= 0)
            {
                return bounds;
            }

            double scale = Math.Max(bounds.Width / imageW, bounds.Height / imageH);
            double w = imageW * scale;
            double h = imageH * scale;
            return new XRect(bounds.Left + (bounds.Width - w) * 0.5, bounds.Top + (bounds.Height - h) * 0.5, w, h);
        }

        private static XRect Inset(XRect rect, double left, double top, double right, double bottom)
        {
            return new XRect(rect.Left + left, rect.Top + top, rect.Width - left - right, rect.Height - top - bottom);
        }

        private static double Mm(double value)
        {
            return XUnit.FromMillimeter(value).Point;
        }

        private static XRect RectMm(double x, double y, double width, double height)
        {
            return new XRect(Mm(x), Mm(y), Mm(width), Mm(height));
        }

        private static string SanitizeFileName(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "Layout_Plan" : value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '_');
            }

            return safe.Replace(' ', '_');
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 1) + ".";
        }
    }
}
