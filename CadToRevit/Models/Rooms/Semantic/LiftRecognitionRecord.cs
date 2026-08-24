using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models.Rooms.Semantic
{
    public sealed class LiftRecognitionRecord
    {
        public string Key { get; set; }

        public string LiftName { get; set; }

        public string LiftKind { get; set; }

        public XYZ Position { get; set; }

        public ElementId LevelId { get; set; }

        public string SourceLayer { get; set; }

        public string RawText { get; set; }

        public string LiftId { get; set; }

        public string LiftType { get; set; }

        public string Dimension { get; set; }

        public string DoorSize { get; set; }

        public string Capacity { get; set; }

        // Geometry resolved from the fixed CAD lift layer DT001.
        // Position remains the primary lift center after resolution. Raw text position is only a seed.
        public List<XYZ> BoundaryPoints { get; set; } = new List<XYZ>();

        public XYZ VirtualDoorStart { get; set; }

        public XYZ VirtualDoorEnd { get; set; }

        public ElementId VirtualDoorHostWallId { get; set; } = ElementId.InvalidElementId;

        public double VirtualDoorWidthMm { get; set; }

        public double VirtualDoorHeightMm { get; set; } = 2100.0;

        public double VirtualDoorSillMm { get; set; } = 0.0;

        public string GeometrySourceLayer { get; set; }
    }
}
