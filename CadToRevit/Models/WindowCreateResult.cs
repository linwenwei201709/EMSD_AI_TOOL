using System.Collections.Generic;

namespace CadToRevit.Models
{
    /// <summary>
    /// 窗创建阶段统计结果。
    /// </summary>
    public class WindowCreateResult
    {
        /// <summary>窗候选总数。</summary>
        public int TotalCandidates { get; set; }

        /// <summary>成功创建数量。</summary>
        public int CreatedCount { get; set; }

        /// <summary>跳过数量。</summary>
        public int SkippedCount { get; set; }

        /// <summary>窗宽参数写入成功数量。</summary>
        public int WidthSetSuccessCount { get; set; }

        /// <summary>窗宽参数写入失败数量。</summary>
        public int WidthSetFailedCount { get; set; }

        /// <summary>窗高参数写入成功数量。</summary>
        public int HeightSetSuccessCount { get; set; }

        /// <summary>窗高参数写入失败数量。</summary>
        public int HeightSetFailedCount { get; set; }

        /// <summary>用于创建的窗族类型名称。</summary>
        public string WindowSymbolName { get; set; }

        /// <summary>JSON 诊断日志路径。</summary>
        public string JsonLogPath { get; set; }

        /// <summary>CSV 诊断日志路径。</summary>
        public string CsvLogPath { get; set; }

        /// <summary>按原因分类的跳过数量统计。</summary>
        public Dictionary<string, int> SkipByReason { get; } = new Dictionary<string, int>();

        /// <summary>Created window instance ids.</summary>
        public List<int> CreatedElementIds { get; set; } = new List<int>();
    }
}
