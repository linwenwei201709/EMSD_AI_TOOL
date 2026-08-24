using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class RoomLayoutPlanDto
    {
        [DataMember]
        public string LayoutId { get; set; } = string.Empty;

        [DataMember]
        public string SolutionName { get; set; } = string.Empty;

        [DataMember]
        public string CreatedAt { get; set; } = string.Empty;

        [DataMember]
        public string UpdatedAt { get; set; } = string.Empty;

        [DataMember]
        public string RoomKey { get; set; } = string.Empty;

        [DataMember]
        public string RoomName { get; set; } = string.Empty;

        [DataMember]
        public string RoomType { get; set; } = string.Empty;

        [DataMember]
        public string AreaText { get; set; } = string.Empty;

        [DataMember]
        public string LevelText { get; set; } = string.Empty;

        [DataMember]
        public string RoomStatus { get; set; } = string.Empty;

        [DataMember]
        public string PlanningContext { get; set; } = string.Empty;

        [DataMember]
        public string EquipmentType { get; set; } = string.Empty;

        [DataMember]
        public string FlowRate { get; set; } = string.Empty;

        [DataMember]
        public string EquipmentFamilyKey { get; set; } = string.Empty;

        [DataMember]
        public string EquipmentDisplayName { get; set; } = string.Empty;

        [DataMember]
        public bool SizeEvaluationCompleted { get; set; }

        [DataMember]
        public bool EquipmentConfirmed { get; set; }

        [DataMember]
        public EquipmentPlacementValidationDto EquipmentValidation { get; set; }

        [DataMember]
        public double RoomLengthMm { get; set; }

        [DataMember]
        public double RoomWidthMm { get; set; }

        [DataMember]
        public double RoomHeightMm { get; set; }

        [DataMember]
        public double DoorWidthMm { get; set; }

        [DataMember]
        public double DoorHeightMm { get; set; }

        [DataMember]
        public double EquipmentLengthMm { get; set; }

        [DataMember]
        public double EquipmentWidthMm { get; set; }

        [DataMember]
        public double EquipmentHeightMm { get; set; }

        [DataMember]
        public double EquipmentWeightKg { get; set; }

        [DataMember]
        public double RequiredMaintenanceSpaceMm { get; set; }

        [DataMember]
        public string RequiredMaintenanceSpaceSide { get; set; } = string.Empty;

        [DataMember]
        public string SadSize { get; set; } = string.Empty;

        [DataMember]
        public LayoutWallSelectionDto SadWall { get; set; } = new LayoutWallSelectionDto();

        [DataMember]
        public string RadSize { get; set; } = string.Empty;

        [DataMember]
        public LayoutWallSelectionDto RadWall { get; set; } = new LayoutWallSelectionDto();

        [DataMember]
        public string ChwsPipeSize { get; set; } = string.Empty;

        [DataMember]
        public LayoutWallSelectionDto ChwsWall { get; set; } = new LayoutWallSelectionDto();

        [DataMember]
        public string ChwrPipeSize { get; set; } = string.Empty;

        [DataMember]
        public LayoutWallSelectionDto ChwrWall { get; set; } = new LayoutWallSelectionDto();

        [DataMember]
        public string SizeStatus { get; set; } = string.Empty;

        [DataMember]
        public string FitnessText { get; set; } = string.Empty;

        [DataMember]
        public string RouteLengthText { get; set; } = string.Empty;

        [DataMember]
        public LayoutDeliveryRouteDto DeliveryRoute { get; set; } =
            new LayoutDeliveryRouteDto();

        [DataMember]
        public LayoutGeneratedElementsDto ActiveGeneratedElements { get; set; } =
            new LayoutGeneratedElementsDto();
    }
}
