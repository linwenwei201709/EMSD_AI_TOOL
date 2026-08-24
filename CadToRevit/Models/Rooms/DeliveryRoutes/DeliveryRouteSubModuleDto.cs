using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.DeliveryRoutes
{
    [DataContract]
    public sealed class DeliveryRouteSubModuleDto
    {
        [DataMember]
        public int Sequence { get; set; }

        [DataMember]
        public string SubModule { get; set; } = string.Empty;

        [DataMember]
        public string Type { get; set; } = string.Empty;

        [DataMember]
        public string DimensionsMm { get; set; } = string.Empty;
    }
}
