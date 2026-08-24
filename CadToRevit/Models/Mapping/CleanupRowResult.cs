using System.Collections.Generic;

namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// Cleanup result for one tracked mapping row.
    /// </summary>
    public sealed class CleanupRowResult
    {
        public string RowKey { get; set; }

        public int RequestedCount { get; set; }

        public int ExistingCount { get; set; }

        public int DeletedCount { get; set; }

        public List<int> MissingElementIds { get; set; } = new List<int>();

        public List<int> SkippedForeignElementIds { get; set; } = new List<int>();

        public List<int> SkippedDetachedElementIds { get; set; } = new List<int>();

        public List<int> DeletedElementIds { get; set; } = new List<int>();

        public List<int> AllowedDependentDeletedElementIds { get; set; } = new List<int>();

        public List<int> DangerousForeignDeletedElementIds { get; set; } = new List<int>();

        public List<string> ForeignDeleteDecisionLogs { get; set; } = new List<string>();

        public bool HasWarning { get; set; }

        public string WarningMessage { get; set; }
    }
}
