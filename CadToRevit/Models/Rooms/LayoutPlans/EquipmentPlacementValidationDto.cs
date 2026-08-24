using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class EquipmentPlacementValidationDto
    {
        [DataMember]
        public bool HasResult { get; set; }

        [DataMember]
        public bool IsValid { get; set; }

        [DataMember]
        public string Status { get; set; } = string.Empty;

        [DataMember]
        public List<string> Reasons { get; set; } = new List<string>();

        [DataMember]
        public string Source { get; set; } = string.Empty;
    }
}
