using System.Collections.Generic;

namespace CadToRevit.Models.Rooms.EquipmentValidation
{
    public sealed class AhuPlacementValidationRequest
    {
        public string SessionId { get; set; }

        public int FamilyId { get; set; }

        public string FamilyKey { get; set; }

        public string RoomKey { get; set; }

        public double RoomLengthMm { get; set; }

        public double RoomWidthMm { get; set; }

        public double RoomHeightMm { get; set; }

        public double DoorWidthMm { get; set; }

        public double DoorHeightMm { get; set; }

        public double UsableAreaM2 { get; set; }

        // Room identification point sent to Python /api/check_room_fit.
        // For the current phase this is the same Revit room-center point
        // used as the actual AHU placement point.
        public double PointInRoomXmm { get; set; }

        public double PointInRoomYmm { get; set; }

        // Explicit device placement point used by Python for fit checking.
        // This must match the point used later by Revit family placement.
        public double PlacementPointXmm { get; set; }

        public double PlacementPointYmm { get; set; }

        // Keep null so Python chooses the door-based orientation.
        // The returned orientation_deg is then applied by Revit placement.
        public double? Orientation { get; set; }

        // Optional room-fit contract extensions. These are additive so the
        // existing colleague UI/request callers remain source-compatible.
        public string EvaluationMode { get; set; }

        public bool UseMaintenanceSpace { get; set; } = true;

        // Physical fit and maintenance clearance are reported separately.
        public bool EvaluateMaintenanceSpace { get; set; } = true;

        // Optional AHU-local side that must face the room door.
        // Values sent to Python are: top / bottom / left / right.
        // This is derived from Maintenance2 by finding the single M row
        // marked as Door Side; M1/M2/M3/M4 itself is not treated as a direction.
        public string DoorFacingSide { get; set; }

        public List<string> DoorFacingSideOptions { get; set; } = new List<string>();

        // IFC-mm direction vector parallel to the selected room door.
        public double[] DoorDirection { get; set; }

        // Optional AHU-local sides that are configured to sit against room walls.
        // Values sent to Python are: top / bottom / left / right.
        // Zero to three sides are allowed.  The list is derived from Maintenance2
        // rows marked as Wall Side; M1/M2/M3/M4 itself is not treated as a direction.
        public List<string> WallFacingSides { get; set; } = new List<string>();

        // Complete Maintenance2 definition for the current AHU.
        // One object is sent for every configured maintenance side so Python receives
        // the actual clearance dimension together with its wall/door meaning.
        // Side values sent to Python are: top / bottom / left / right.
        public List<AhuPlacementMaintenanceSpaceRequest> MaintenanceSpaces { get; set; } =
            new List<AhuPlacementMaintenanceSpaceRequest>();

        // Optional local 2D footprint of each configured AHU Sub-Module.
        // Coordinates are in millimetres. S1 top-left is always (0, 0),
        // X grows to the right (Length) and Y grows downward (Width).
        // UI grid gaps are intentionally treated as 0 mm.
        public List<AhuPlacementSubModuleRequest> SubModules { get; set; } =
            new List<AhuPlacementSubModuleRequest>();

        // Same DTO used by the route API. Room-fit callers may leave this empty.
        public List<CadToRevit.Services.PathPreview.RestrictedAreaRequestItem> RestrictedAreas { get; set; } =
            new List<CadToRevit.Services.PathPreview.RestrictedAreaRequestItem>();
    }

    public sealed class AhuPlacementMaintenanceSpaceRequest
    {
        public string Maintenance { get; set; }

        public string Side { get; set; }

        public double DimensionMm { get; set; }

        public bool IsWallSide { get; set; }

        public bool IsDoorSide { get; set; }
    }

    public sealed class AhuPlacementSubModuleRequest
    {
        public string Module { get; set; }

        // Human-readable Sub-Module name configured in Family Library,
        // for example: Mixing Box / Filter Chamber / Coil Section.
        public string Name { get; set; }

        public List<AhuPlacementPoint2D> Points { get; set; } =
            new List<AhuPlacementPoint2D>();
    }

    public sealed class AhuPlacementPoint2D
    {
        public AhuPlacementPoint2D()
        {
        }

        public AhuPlacementPoint2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; set; }

        public double Y { get; set; }
    }
}
