using System.Collections.Generic;

namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// Sync plan generated from current selected rows and history tracking rows.
    /// </summary>
    public sealed class SyncPlan
    {
        public List<MapRow> RowsToCreate { get; } = new List<MapRow>();

        public List<MapRow> RowsToRebuild { get; } = new List<MapRow>();

        public List<WizardGenerationRowRecord> RowsToDelete { get; } = new List<WizardGenerationRowRecord>();

        public List<MapRow> RowsToSkip { get; } = new List<MapRow>();
    }
}
