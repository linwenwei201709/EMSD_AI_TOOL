using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.DeliveryRoutes
{
    [DataContract]
    public sealed class DeliveryRouteRecordDto
    {
        [DataMember]
        public string RouteId { get; set; } = string.Empty;

        [DataMember]
        public string RouteName { get; set; } = string.Empty;

        [DataMember]
        public string CreatedAt { get; set; } = string.Empty;

        [DataMember]
        public string UpdatedAt { get; set; } = string.Empty;

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
        public string EquipmentFamilyKey { get; set; } = string.Empty;

        [DataMember]
        public string EquipmentDisplayName { get; set; } = string.Empty;

        [DataMember]
        public int OriginalModelId { get; set; }

        [DataMember]
        public double AirflowM3s { get; set; }

        [DataMember]
        public bool IsSuccess { get; set; }

        [DataMember]
        public string StatusText { get; set; } = string.Empty;

        [DataMember]
        public string ApiMessage { get; set; } = string.Empty;

        [DataMember]
        public string ResultTitle { get; set; } = string.Empty;

        [DataMember]
        public string ResultMessage { get; set; } = string.Empty;

        [DataMember]
        public string FailureReasonText { get; set; } = string.Empty;

        [DataMember]
        public string ResponseBody { get; set; } = string.Empty;

        [DataMember]
        public double? PathLengthMeters { get; set; }

        [DataMember]
        public string RouteLengthText { get; set; } = string.Empty;

        [DataMember]
        public string DisassemblyText { get; set; } = string.Empty;

        [DataMember]
        public string MaxDimsText { get; set; } = string.Empty;

        [DataMember]
        public List<DeliveryRouteSubModuleDto> SubModules { get; set; } =
            new List<DeliveryRouteSubModuleDto>();
    }
}
