using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class LayoutWallSelectionDto
    {
        [DataMember]
        public int ElementId { get; set; } = -1;

        [DataMember]
        public string UniqueId { get; set; } = string.Empty;

        [DataMember]
        public string DisplayName { get; set; } = string.Empty;

        [DataMember]
        public string RevitName { get; set; } = string.Empty;

        [DataMember]
        public double LengthMm { get; set; }
    }
}
