using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Diagnostics
{
    public sealed class GenerationProfilingData
    {
        public string LayerSummary { get; set; }

        public int RawSegmentCount { get; set; }

        public int FilteredSegmentCount { get; set; }

        public int CandidateDoubleLineCount { get; set; }

        public int CandidateSingleLineCount { get; set; }

        public int WallCreatedCount { get; set; }

        public int SkippedCount { get; set; }

        public bool SafeModeEnabled { get; set; }

        public bool IsCanceled { get; set; }

        public string ExceptionMessage { get; set; }

        public long BuildMs { get; set; }

        public long FilterMs { get; set; }

        public long RecognizeMs { get; set; }

        public long CreateMs { get; set; }

        public long JoinMs { get; set; }

        public long TotalMs { get; set; }

        public List<long> CommitBatchMs { get; set; } = new List<long>();

        public List<string> SkipReasons { get; set; } = new List<string>();
    }

    public static class ProfilingLogService
    {
        public static string LastLogPath { get; private set; }

        public static string Write(GenerationProfilingData data)
        {
            try
            {
                string logRoot = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(logRoot, "m11_3_profiling_" + stamp + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("LayerSummary=" + (data?.LayerSummary ?? string.Empty));
                sb.AppendLine("RawSegmentCount=" + (data?.RawSegmentCount ?? 0));
                sb.AppendLine("FilteredSegmentCount=" + (data?.FilteredSegmentCount ?? 0));
                sb.AppendLine("CandidateDoubleLineCount=" + (data?.CandidateDoubleLineCount ?? 0));
                sb.AppendLine("CandidateSingleLineCount=" + (data?.CandidateSingleLineCount ?? 0));
                sb.AppendLine("WallCreatedCount=" + (data?.WallCreatedCount ?? 0));
                sb.AppendLine("SkippedCount=" + (data?.SkippedCount ?? 0));
                sb.AppendLine("SafeModeEnabled=" + (data?.SafeModeEnabled ?? false));
                sb.AppendLine("IsCanceled=" + (data?.IsCanceled ?? false));
                sb.AppendLine("ExceptionMessage=" + (data?.ExceptionMessage ?? string.Empty));
                sb.AppendLine();
                sb.AppendLine("T1_BuildMs=" + (data?.BuildMs ?? 0));
                sb.AppendLine("T2_FilterMs=" + (data?.FilterMs ?? 0));
                sb.AppendLine("T3_RecognizeMs=" + (data?.RecognizeMs ?? 0));
                sb.AppendLine("T4_CreateMs=" + (data?.CreateMs ?? 0));
                sb.AppendLine("T5_JoinMs=" + (data?.JoinMs ?? 0));
                sb.AppendLine("TTotalMs=" + (data?.TotalMs ?? 0));
                sb.AppendLine("CommitBatchMs=" + string.Join(",", (data?.CommitBatchMs ?? new List<long>()).Select(x => x.ToString())));
                List<string> topReasons = (data?.SkipReasons ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => g.Key + ":" + g.Count())
                    .ToList();
                sb.AppendLine("TopSkipReasons=" + string.Join(" | ", topReasons));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                LastLogPath = path;
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
