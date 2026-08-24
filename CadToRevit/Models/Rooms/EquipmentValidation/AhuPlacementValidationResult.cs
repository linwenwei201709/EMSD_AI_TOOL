using System.Collections.Generic;

namespace CadToRevit.Models.Rooms.EquipmentValidation
{
    public sealed class AhuPlacementValidationResult
    {
        public bool HasResult { get; set; }

        public bool IsValid { get; set; }

        public string Status { get; set; }

        public List<string> Reasons { get; set; } = new List<string>();

        public string Source { get; set; }

        public string RawResponse { get; set; }

        // Absolute IFC/Revit XY direction returned by Python.
        // When present, Revit placement uses this angle with the fixed 180°
        // AHU family offset; legacy Service Side orientation is bypassed.
        public double? OrientationDeg { get; set; }

        public double PlacementPointXmm { get; set; }

        public double PlacementPointYmm { get; set; }

        public bool? PhysicalFit { get; set; }

        public bool? MaintenanceFit { get; set; }

        public string MaintenanceReason { get; set; }

        public bool? CurrentPlacementFit { get; set; }

        public bool? FeasiblePlacementFound { get; set; }

        public bool? Repositioned { get; set; }

        public bool CanInsert { get; set; }

        public string ViolationType { get; set; }
    }

    public static class AhuPlacementValidationStatuses
    {
        public const string Valid = "Valid";
        public const string ValidAfterRepositioning = "Valid After Repositioning";
        public const string CurrentPlacementInvalid = "Current Placement Invalid";
        public const string NoFeasiblePlacement = "No Feasible Placement";
        public const string MaintenanceClearanceInsufficient = "Maintenance Clearance Insufficient";
        public const string BoundaryDataInvalid = "Boundary Data Invalid";
        public const string ApiError = "API Error";

        public static bool IsMaintenanceWarning(string status)
        {
            return string.Equals(status, MaintenanceClearanceInsufficient, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnavailable(string status)
        {
            return string.Equals(status, BoundaryDataInvalid, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, ApiError, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBlocking(string status, string violationType)
        {
            return string.Equals(status, NoFeasiblePlacement, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Oversized", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(violationType, "PhysicalDimensionOversized", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(violationType, "PhysicalOversized", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
