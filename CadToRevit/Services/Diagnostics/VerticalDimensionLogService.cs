using CadToRevit.Models;
using System;
using System.IO;
using System.Text;

namespace CadToRevit.Services.Diagnostics
{
    public static class VerticalDimensionLogService
    {
        public static string Write(
            VerticalDimensionSettings settings,
            DoorCreateResult door,
            WindowCreateResult window)
        {
            try
            {
                string logRoot = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(logRoot, "m11_vertical_stats_" + stamp + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Vertical Settings");
                sb.AppendLine("WallHeightMm=" + (settings?.WallHeightMm ?? 0).ToString("F1"));
                sb.AppendLine("WallBaseOffsetMm=" + (settings?.WallBaseOffsetMm ?? 0).ToString("F1"));
                sb.AppendLine("DoorHeightMm=" + (settings?.DoorHeightMm ?? 0).ToString("F1"));
                sb.AppendLine("DoorSillHeightMm=" + (settings?.DoorSillHeightMm ?? 0).ToString("F1"));
                sb.AppendLine("DoorHeadHeightMmNotUsed=True");
                sb.AppendLine("WindowHeightMm=" + (settings?.WindowHeightMm ?? 0).ToString("F1"));
                sb.AppendLine("WindowSillHeightMm=" + (settings?.WindowSillHeightMm ?? 0).ToString("F1"));
                sb.AppendLine("WindowHeadHeightMm=" + (settings?.WindowHeadHeightMm ?? 0).ToString("F1"));
                sb.AppendLine();
                sb.AppendLine("Door Stats");
                sb.AppendLine("Created=" + (door?.CreatedDoors ?? 0));
                sb.AppendLine("Skipped=" + (door?.SkippedDoors ?? 0));
                sb.AppendLine("HeightSetSuccess=" + (door?.HeightSetSuccessCount ?? 0));
                sb.AppendLine("HeightSetFailed=" + (door?.HeightSetFailedCount ?? 0));
                sb.AppendLine();
                sb.AppendLine("Window Stats");
                sb.AppendLine("Created=" + (window?.CreatedCount ?? 0));
                sb.AppendLine("Skipped=" + (window?.SkippedCount ?? 0));
                sb.AppendLine("HeightSetSuccess=" + (window?.HeightSetSuccessCount ?? 0));
                sb.AppendLine("HeightSetFailed=" + (window?.HeightSetFailedCount ?? 0));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
