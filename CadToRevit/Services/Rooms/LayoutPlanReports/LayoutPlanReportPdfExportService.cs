using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Services.Rooms.LayoutPlans;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.IO;

namespace CadToRevit.Services.Rooms.LayoutPlanReports
{
    internal static class LayoutPlanReportPdfExportService
    {
        internal static LayoutPlanReportPdfExportResult ExportTemporary(LayoutPlanReportPdfContext context)
        {
            RoomLayoutPlanPdfFontResolver.EnsureRegistered();
            if (context == null || context.Plan == null)
            {
                throw new InvalidOperationException("Layout plan not found.");
            }

            string directory = GetExportTempDirectory(context.Plan.LayoutId);
            Directory.CreateDirectory(directory);

            DateTime generatedAt = DateTime.Now;
            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(context.Plan.SolutionName) ? "Layout_Plan" : context.Plan.SolutionName);
            string fileName = "AHU_Layout_Plan_Report_" + safeName + "_" + generatedAt.ToString("yyyyMMdd_HHmmss") + ".pdf";
            string pdfPath = Path.Combine(directory, "layout_plan_report.pdf");

            using (PdfDocument document = new PdfDocument())
            {
                document.Info.Title = "AHU Layout Plan Report";
                PdfPage page1 = document.AddPage();
                page1.Width = XUnit.FromMillimeter(594);
                page1.Height = XUnit.FromMillimeter(420);
                using (XGraphics gfx = XGraphics.FromPdfPage(page1))
                {
                    DrawPage1(gfx, context, generatedAt);
                }

                PdfPage page2 = document.AddPage();
                page2.Width = XUnit.FromMillimeter(594);
                page2.Height = XUnit.FromMillimeter(420);
                using (XGraphics gfx = XGraphics.FromPdfPage(page2))
                {
                    DrawPage2(gfx, context);
                }

                document.Save(pdfPath);
            }

            return new LayoutPlanReportPdfExportResult
            {
                PdfPath = pdfPath,
                FileName = fileName,
                GeneratedAt = generatedAt
            };
        }

        internal static string GetExportTempDirectory(string layoutId)
        {
            string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(layoutId) ? "LayoutPlan" : layoutId);
            return Path.Combine(Path.GetTempPath(), "EMSD_AI_Tool", "LayoutPlanReport", safeId);
        }

        private static void DrawPage1(XGraphics gfx, LayoutPlanReportPdfContext context, DateTime generatedAt)
        {
            XPen borderPen = new XPen(XColors.Black, 0.75);
            XPen thinPen = new XPen(XColors.Black, 0.45);
            // A2 report is normally viewed/printed at a much larger physical size than A4.
            // Keep report text deliberately larger so Schedule / Notes / Info remain legible
            // when the full A2 sheet is fitted to screen.
            XFont titleFont = new XFont("Arial", 28.0, XFontStyleEx.Bold);
            XFont sectionFont = new XFont("Arial", 19.0, XFontStyleEx.Bold);
            XFont headerFont = new XFont("Arial", 16.5, XFontStyleEx.Bold);
            XFont textFont = new XFont("Arial", 16.0, XFontStyleEx.Regular);

            gfx.DrawRectangle(borderPen, RectMm(10, 10, 574, 400));
            gfx.DrawString("Layout Plan Report", titleFont, XBrushes.Black, Mm(16), Mm(24));

            // Match the combined Room Information + Key Plan width exactly:
            // 210 mm + 6 mm gap + 150 mm = 366 mm. The previous 365 mm width made the
            // upper Main 3D frame finish 1 mm short of the lower panels. Draw the report
            // frame in PDF coordinates so it remains aligned even when the exported PNG has
            // a different aspect ratio and is fitted with white margins.
            XRect mainRect = RectMm(16, 36, 366, 245);
            DrawImageOrPlaceholder(gfx, context.Main3DImagePath, mainRect, "Main 3D Layout View unavailable.", false);
            gfx.DrawRectangle(borderPen, mainRect);

            XRect scheduleRect = RectMm(392, 36, 176, 82);
            DrawSchedule(gfx, context.ReportData != null ? context.ReportData.ScheduleRows : null, scheduleRect, sectionFont, headerFont, textFont, thinPen);

            // Keep NOTES compact and anchor it to the same bottom line as the Main 3D view.
            // 104 mm -> 78 mm (25% shorter), so the box just contains the note text while
            // leaving a clean visual gap below the MEP schedule. Main 3D bottom = 281 mm.
            XRect notesRect = RectMm(392, 203, 176, 78);
            DrawNotes(gfx, notesRect, sectionFont, borderPen);

            XRect infoRect = RectMm(16, 290, 210, 110);
            DrawInfo(gfx, context.ReportData != null ? context.ReportData.InformationRows : null, infoRect, sectionFont, headerFont, textFont, thinPen);

            XRect keyRect = RectMm(232, 290, 150, 110);
            DrawSectionBox(gfx, "Key Plan", keyRect, sectionFont, borderPen);
            DrawImageOrPlaceholder(gfx, context.KeyPlanImagePath, Inset(keyRect, Mm(2), Mm(14), Mm(2), Mm(2)), "Key Plan unavailable.", false);

            // Keep all three lower panels on exactly the same top/bottom grid line.
            // The previous title block started at Y=238 mm while Room Information and
            // Key Plan started at Y=290 mm, which made the right-hand panel visibly taller.
            XRect titleBlockRect = RectMm(392, 290, 176, 110);
            DrawTitleBlock(gfx, context.Plan, generatedAt, titleBlockRect, sectionFont, textFont, thinPen);

            CadToRevit.Services.Diagnostics.DiagnosticRecorder.AppendDebug("[LayoutPlanReport] PDF page1 created.");
        }

        private static void DrawPage2(XGraphics gfx, LayoutPlanReportPdfContext context)
        {
            XPen borderPen = new XPen(XColors.Black, 0.75);
            gfx.DrawRectangle(borderPen, RectMm(10, 10, 574, 400));
            DrawImageOrPlaceholder(gfx, context.OverallTopViewImagePath, RectMm(16, 16, 562, 388), "Overall Top View unavailable.", false);
            CadToRevit.Services.Diagnostics.DiagnosticRecorder.AppendDebug("[LayoutPlanReport] PDF page2 created.");
        }

        private static void DrawSchedule(XGraphics gfx, List<LayoutPlanReportScheduleRow> rows, XRect rect, XFont titleFont, XFont headerFont, XFont textFont, XPen pen)
        {
            DrawSectionBox(gfx, "MEP & Pipework Schedule", rect, titleFont, pen);
            // Start the table exactly on the section-header divider. The previous +16 mm
            // start left a 3 mm strip below the +13 mm header line, so two horizontal lines
            // were visible under "MEP & Pipework Schedule". Reusing the same Y coordinate
            // removes that extra line/gap and lets the table fill the box cleanly.
            double y = rect.Top + Mm(13);
            List<LayoutPlanReportScheduleRow> scheduleRows = rows ?? new List<LayoutPlanReportScheduleRow>();

            int tableRowCount = Math.Max(1, scheduleRows.Count + 1); // +1 header row
            double rowH = Math.Max(Mm(1), (rect.Bottom - y) / tableRowCount);
            double[] cols = { Mm(34), Mm(76), rect.Width - Mm(110) };
            DrawTableRow(gfx, rect.Left, y, cols, rowH, new[] { "System", "Size", "Target" }, headerFont, pen);
            y += rowH;
            foreach (LayoutPlanReportScheduleRow row in scheduleRows)
            {
                string sizeText = string.IsNullOrWhiteSpace(row.Size) ||
                                  string.Equals(row.Size.Trim(), "Select", StringComparison.OrdinalIgnoreCase)
                    ? "-"
                    : row.Size;

                DrawTableRow(gfx, rect.Left, y, cols, rowH, new[] { row.System, sizeText, row.Target }, textFont, pen);
                y += rowH;
            }
        }

        private static void DrawInfo(XGraphics gfx, List<LayoutPlanReportInfoRow> rows, XRect rect, XFont titleFont, XFont labelFont, XFont textFont, XPen pen)
        {
            DrawSectionBox(gfx, "Room & Equipment Information", rect, titleFont, pen);
            List<LayoutPlanReportInfoRow> infoRows = rows ?? new List<LayoutPlanReportInfoRow>();
            if (infoRows.Count == 0)
            {
                return;
            }

            // Start immediately below the 13 mm section header and divide the remaining
            // height equally between all information rows. This produces the row separator
            // lines used by the approved prototype and keeps the last row on the panel border.
            double y = rect.Top + Mm(13);
            double rowH = (rect.Bottom - y) / infoRows.Count;

            // "Required Maintenance Space" is the longest label. The old 76 mm label
            // column let the label run into its value. Give labels a wider fixed column
            // while leaving ample room for values such as "1200 mm (Access Side)".
            double labelW = Mm(100);
            double labelX = rect.Left + Mm(4);
            double valueX = rect.Left + labelW;

            for (int i = 0; i < infoRows.Count; i++)
            {
                LayoutPlanReportInfoRow row = infoRows[i];
                XRect labelRect = new XRect(labelX, y, labelW - Mm(6), rowH);
                XRect valueRect = new XRect(valueX, y, rect.Right - valueX - Mm(4), rowH);

                gfx.DrawString((row.Label ?? string.Empty) + " :", labelFont, XBrushes.Black, labelRect, XStringFormats.CenterLeft);
                gfx.DrawString(string.IsNullOrWhiteSpace(row.Value) ? "-" : row.Value, textFont, XBrushes.Black, valueRect, XStringFormats.CenterLeft);

                y += rowH;

                // Do not redraw the outer bottom border; draw separators only between rows.
                if (i < infoRows.Count - 1)
                {
                    gfx.DrawLine(pen, rect.Left, y, rect.Right, y);
                }
            }
        }

        private static void DrawNotes(XGraphics gfx, XRect rect, XFont titleFont, XPen pen)
        {
            DrawSectionBox(gfx, "NOTES", rect, titleFont, pen);

            // Notes are intentionally only slightly heavier than the normal report body text.
            // Arial has no reliable SemiBold face in the PDF runtime, so use Bold at a slightly
            // smaller size to get a modest weight increase without making the notes look heavy.
            XFont notesFont = new XFont("Arial", 15.0, XFontStyleEx.Bold);
            double x = rect.Left + Mm(5);
            double y = rect.Top + Mm(20);
            double rowH = Mm(7.7);
            string[] lines =
            {
                "1. ALL DIMENSIONS ARE IN MILLIMETRES.",
                "2. CONTRACTOR SHALL VERIFY ALL DIMENSIONS ON",
                "   SITE BEFORE INSTALLATION.",
                "3. THE LAYOUT SHOWN IS BASED ON AI",
                "   OPTIMIZATION. CONTRACTOR SHALL ENSURE",
                "   ADEQUATE MAINTENANCE CLEARANCE."
            };

            foreach (string line in lines)
            {
                gfx.DrawString(line, notesFont, XBrushes.Black, x, y);
                y += rowH;
            }
        }

        private static void DrawTitleBlock(XGraphics gfx, RoomLayoutPlanDto plan, DateTime date, XRect rect, XFont titleFont, XFont textFont, XPen pen)
        {
            gfx.DrawRectangle(pen, rect);
            double leftW = Mm(58);
            double headerH = Mm(26);
            double footerH = Mm(28);
            gfx.DrawLine(pen, rect.Left + leftW, rect.Top, rect.Left + leftW, rect.Bottom);
            gfx.DrawLine(pen, rect.Left, rect.Top + headerH, rect.Right, rect.Top + headerH);
            DrawPdfLogo(gfx, Inset(new XRect(rect.Left, rect.Top, leftW, headerH), Mm(4), Mm(3), Mm(4), Mm(3)));

            XFont titleBlockFont = new XFont("Arial", 15.5, XFontStyleEx.Bold);
            gfx.DrawString("AHU LAYOUT PLAN", titleBlockFont, XBrushes.Black, rect.Left + leftW + Mm(7), rect.Top + Mm(10));
            gfx.DrawString("(TENDER DRAWING)", titleBlockFont, XBrushes.Black, rect.Left + leftW + Mm(7), rect.Top + Mm(18));

            double rightPanelLeft = rect.Left + leftW;
            double contentTop = rect.Top + headerH;
            double contentBottom = rect.Bottom - footerH;
            double labelX = rightPanelLeft + Mm(6);
            double labelColumnW = Mm(45);
            double valueX = labelX + labelColumnW;
            double valueRight = rect.Right - Mm(5);
            string[,] fields =
            {
                { "Project", "" },
                { "Location", plan != null ? plan.LevelText ?? string.Empty : string.Empty },
                { "Drawing No.", "" },
                { "Date", date.ToString("yyyy-MM-dd") },
                { "Scale", "NTS" },
                { "Generated By", "EMSD AI Tools" },
                { "Checked By", "" }
            };

            // The title block now has the same 110 mm height as the other two lower panels.
            // Fit all seven metadata rows into the available middle band instead of using
            // the old fixed 12 mm step (which pushed Generated/Checked By into the footer).
            XFont fieldFont = new XFont("Arial", 13.5, XFontStyleEx.Regular);
            int fieldCount = fields.GetLength(0);
            double rowH = (contentBottom - contentTop) / fieldCount;
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                double rowTop = contentTop + (i * rowH);
                XRect labelRect = new XRect(labelX, rowTop, labelColumnW, rowH);
                XRect valueRect = new XRect(valueX, rowTop, Math.Max(0, valueRight - valueX), rowH);
                gfx.DrawString(fields[i, 0] + ":", fieldFont, XBrushes.Black, labelRect, XStringFormats.CenterLeft);
                gfx.DrawString(string.IsNullOrWhiteSpace(fields[i, 1]) ? "-" : fields[i, 1], fieldFont, XBrushes.Black, valueRect, XStringFormats.CenterLeft);
            }

            double footerTop = rect.Bottom - footerH;
            gfx.DrawLine(pen, rect.Left, footerTop, rect.Right, footerTop);

            // The prototype keeps the logo column blank in the footer. Both the verification
            // note and the A2 paper-size mark belong to the right-hand footer area. Split that
            // right-hand footer into a wide note cell and a compact A2 cell, then center the
            // contents inside their own cells so they cannot drift into the logo column.
            // Keep the A2 cell deliberately narrow (26 mm, about one-third narrower than the
            // previous 39 mm cell) so the verification-note cell gains more horizontal padding.
            double a2CellW = Mm(26);
            double a2CellLeft = rect.Right - a2CellW;
            gfx.DrawLine(pen, a2CellLeft, footerTop, a2CellLeft, rect.Bottom);

            XRect footerNoteRect = new XRect(rightPanelLeft, footerTop, a2CellLeft - rightPanelLeft, footerH);
            XRect footerA2Rect = new XRect(a2CellLeft, footerTop, a2CellW, footerH);

            gfx.DrawString(
                "ALL DIMENSIONS TO BE VERIFIED ON SITE",
                new XFont("Arial", 10.8, XFontStyleEx.Bold),
                XBrushes.Black,
                footerNoteRect,
                XStringFormats.Center);

            XFont a2Font = new XFont("Arial", 20, XFontStyleEx.Bold);
            XFont paperSizeFont = new XFont("Arial", 9.8, XFontStyleEx.Bold);
            XRect a2TopRect = new XRect(footerA2Rect.Left, footerA2Rect.Top + Mm(2), footerA2Rect.Width, Mm(14));
            XRect a2BottomRect = new XRect(footerA2Rect.Left, footerA2Rect.Top + Mm(14), footerA2Rect.Width, footerA2Rect.Height - Mm(14));
            gfx.DrawString("A2", a2Font, XBrushes.Black, a2TopRect, XStringFormats.Center);
            gfx.DrawString("(594x420)", paperSizeFont, XBrushes.Black, a2BottomRect, XStringFormats.Center);
        }

        private static void DrawSectionBox(XGraphics gfx, string title, XRect rect, XFont titleFont, XPen pen)
        {
            gfx.DrawRectangle(pen, rect);
            gfx.DrawLine(pen, rect.Left, rect.Top + Mm(13), rect.Right, rect.Top + Mm(13));
            gfx.DrawString(title, titleFont, XBrushes.Black, rect.Left + Mm(4), rect.Top + Mm(9));
        }

        private static void DrawTableRow(XGraphics gfx, double x, double y, double[] cols, double h, string[] values, XFont font, XPen pen)
        {
            double current = x;
            for (int i = 0; i < cols.Length; i++)
            {
                XRect cell = new XRect(current, y, cols[i], h);
                gfx.DrawRectangle(pen, cell);

                // Schedule cells follow the prototype: text remains left aligned, while the
                // baseline is vertically centred within each row instead of sitting near the top.
                XRect textRect = new XRect(cell.Left + Mm(2), cell.Top, Math.Max(0, cell.Width - Mm(4)), cell.Height);
                gfx.DrawString(values != null && values.Length > i ? values[i] ?? "-" : "-", font, XBrushes.Black, textRect, XStringFormats.CenterLeft);
                current += cols[i];
            }
        }

        private static void DrawImageOrPlaceholder(XGraphics gfx, string imagePath, XRect rect, string placeholder, bool cover)
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                using (XImage image = XImage.FromFile(imagePath))
                {
                    XRect target = FitImage(image.PixelWidth, image.PixelHeight, rect);
                    gfx.DrawImage(image, target);
                }
                return;
            }

            gfx.DrawRectangle(new XPen(XColors.Gray, 0.4), rect);
            gfx.DrawString(placeholder, new XFont("Arial", 12, XFontStyleEx.Italic), XBrushes.Gray, rect, XStringFormats.Center);
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

            gfx.DrawString("EMSD", new XFont("Arial", 16, XFontStyleEx.Bold), XBrushes.Black, rect, XStringFormats.Center);
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

        private static XRect Inset(XRect rect, double left, double top, double right, double bottom)
        {
            return new XRect(rect.Left + left, rect.Top + top, rect.Width - left - right, rect.Height - top - bottom);
        }

        private static XRect RectMm(double x, double y, double width, double height)
        {
            return new XRect(Mm(x), Mm(y), Mm(width), Mm(height));
        }

        private static double Mm(double value)
        {
            return XUnit.FromMillimeter(value).Point;
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
    }
}
