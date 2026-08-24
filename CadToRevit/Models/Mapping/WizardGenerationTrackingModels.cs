using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Mapping
{
    [DataContract]
    public sealed class WizardGenerationTrackingDto
    {
        [DataMember(Name = "SchemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "UpdatedAtUtc")]
        public string UpdatedAtUtc { get; set; }

        [DataMember(Name = "Rows")]
        public List<WizardGenerationRowRecord> Rows { get; set; } = new List<WizardGenerationRowRecord>();
    }

    [DataContract]
    public sealed class WizardGenerationRowRecord
    {
        [DataMember(Name = "RowKey")]
        public string RowKey { get; set; }

        [DataMember(Name = "RawLayerName")]
        public string RawLayerName { get; set; }

        [DataMember(Name = "Category")]
        public string Category { get; set; }

        [DataMember(Name = "LevelId")]
        public int LevelId { get; set; }

        [DataMember(Name = "DwgId")]
        public int DwgId { get; set; }

        [DataMember(Name = "RevitTypeName")]
        public string RevitTypeName { get; set; }

        [DataMember(Name = "MappingFingerprint")]
        public string MappingFingerprint { get; set; }

        [DataMember(Name = "GenerationBatchId")]
        public string GenerationBatchId { get; set; }

        [DataMember(Name = "LastGeneratedAtUtc")]
        public string LastGeneratedAtUtc { get; set; }

        [DataMember(Name = "ElementIds")]
        public List<int> ElementIds { get; set; } = new List<int>();

        [DataMember(Name = "LastSyncAction")]
        public string LastSyncAction { get; set; }

        [DataMember(Name = "LastSyncReason")]
        public string LastSyncReason { get; set; }

        [DataMember(Name = "GeneratedCount")]
        public int GeneratedCount { get; set; }

        [DataMember(Name = "LastSyncedAt")]
        public string LastSyncedAt { get; set; }
    }
}
