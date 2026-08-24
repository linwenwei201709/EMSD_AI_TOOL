using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class RoomLayoutPlanStorePayload
    {
        [DataMember]
        public string Version { get; set; } = "1.0";

        [DataMember]
        public string UpdatedAt { get; set; } = string.Empty;

        [DataMember]
        public Dictionary<string, string> ActiveLayoutIdByRoomKey { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [DataMember]
        public List<RoomLayoutPlanDto> Plans { get; set; } =
            new List<RoomLayoutPlanDto>();
    }
}
