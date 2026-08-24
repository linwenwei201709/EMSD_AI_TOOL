using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class LayoutDeliveryRouteDto
    {
        [DataMember]
        public bool HasRoute { get; set; }

        [DataMember]
        public string StartLiftKey { get; set; } = string.Empty;

        [DataMember]
        public string StartLiftName { get; set; } = string.Empty;

        [DataMember]
        public string StartLocationType { get; set; } = string.Empty;

        [DataMember]
        public string StartPointName { get; set; } = string.Empty;

        [DataMember]
        public double? StartPointXmm { get; set; }

        [DataMember]
        public double? StartPointYmm { get; set; }

        [DataMember]
        public double? StartPointZmm { get; set; }

        [DataMember]
        public string TargetRoomKey { get; set; } = string.Empty;

        [DataMember]
        public string TargetRoomName { get; set; } = string.Empty;

        [DataMember]
        public string ResponseBody { get; set; } = string.Empty;

        [DataMember]
        public double PathLengthMeters { get; set; }

        [DataMember]
        public string RouteLengthText { get; set; } = string.Empty;

        [DataMember]
        public string ResultMessage { get; set; } = string.Empty;

        [DataMember]
        public string GeneratedAt { get; set; } = string.Empty;
    }
}
