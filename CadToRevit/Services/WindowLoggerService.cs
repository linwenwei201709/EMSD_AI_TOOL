using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services
{
    public static class WindowLoggerService
    {
        public static void Write(WindowCreateResult result, System.Collections.Generic.IEnumerable<WindowCandidate> candidates)
        {
            string root = DiagnosticRecorder.GetLogDirectory();
            Directory.CreateDirectory(root);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string jsonPath = Path.Combine(root, "window_candidates_" + stamp + ".json");
            string csvPath = Path.Combine(root, "window_candidates_" + stamp + ".csv");

            File.WriteAllText(jsonPath, BuildJson(result, candidates), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(candidates), Encoding.UTF8);

            result.JsonLogPath = jsonPath;
            result.CsvLogPath = csvPath;
        }

        private static string BuildJson(WindowCreateResult result, System.Collections.Generic.IEnumerable<WindowCandidate> candidates)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"summary\": {");
            sb.AppendLine("    \"totalCandidates\": " + result.TotalCandidates + ",");
            sb.AppendLine("    \"created\": " + result.CreatedCount + ",");
            sb.AppendLine("    \"skipped\": " + result.SkippedCount);
            sb.AppendLine("  },");
            sb.AppendLine("  \"candidates\": [");

            WindowCandidate[] arr = candidates == null
                ? new WindowCandidate[0]
                : candidates.ToArray();
            for (int i = 0; i < arr.Length; i++)
            {
                WindowCandidate c = arr[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"id\": " + c.CandidateId + ",");
                sb.AppendLine("      \"rule\": \"" + Escape(c.RuleId) + "\",");
                sb.AppendLine("      \"widthMm\": " + c.WidthMm.ToString("F3") + ",");
                sb.AppendLine("      \"hostWallId\": " + c.HostWallId + ",");
                sb.AppendLine("      \"matchDistMm\": " + c.MatchDistMm.ToString("F3") + ",");
                sb.AppendLine("      \"status\": \"" + Escape(c.Status) + "\",");
                sb.AppendLine("      \"failReason\": \"" + Escape(c.FailReason) + "\",");
                sb.AppendLine("      \"createdElementId\": " + c.CreatedElementId + ",");
                sb.AppendLine("      \"segmentIds\": [" + string.Join(", ", c.SegmentIds ?? new System.Collections.Generic.List<int>()) + "]");
                sb.Append("    }");
                if (i < arr.Length - 1)
                {
                    sb.Append(",");
                }

                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildCsv(System.Collections.Generic.IEnumerable<WindowCandidate> candidates)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CandidateId,Rule,WidthMm,HostWallId,MatchDistMm,Status,FailReason,CreatedElementId,SegmentIds");
            if (candidates != null)
            {
                foreach (WindowCandidate c in candidates)
                {
                    sb.AppendLine(string.Join(",",
                        c.CandidateId,
                        Q(c.RuleId),
                        c.WidthMm.ToString("F3"),
                        c.HostWallId,
                        c.MatchDistMm.ToString("F3"),
                        Q(c.Status),
                        Q(c.FailReason),
                        c.CreatedElementId,
                        Q(string.Join("|", c.SegmentIds ?? new System.Collections.Generic.List<int>()))));
                }
            }

            return sb.ToString();
        }

        private static string Escape(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Q(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "\"\"";
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
