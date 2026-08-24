using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.DeliveryRoutes
{
    [DataContract]
    public sealed class DeliveryRouteStorePayload
    {
        [DataMember]
        public string Version { get; set; } = "1.0";

        [DataMember]
        public string UpdatedAt { get; set; } = string.Empty;

        [DataMember]
        public List<DeliveryRouteRecordDto> Routes { get; set; } =
            new List<DeliveryRouteRecordDto>();
    }
}
