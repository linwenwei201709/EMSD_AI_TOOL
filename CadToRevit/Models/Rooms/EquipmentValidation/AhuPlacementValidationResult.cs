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
    }
}
