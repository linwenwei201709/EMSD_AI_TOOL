using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class LayoutElementRefDto
    {
        [DataMember]
        public int ElementId { get; set; } = -1;

        [DataMember]
        public string UniqueId { get; set; } = string.Empty;

        [DataMember]
        public string CategoryName { get; set; } = string.Empty;

        [DataMember]
        public string Name { get; set; } = string.Empty;
    }
}
