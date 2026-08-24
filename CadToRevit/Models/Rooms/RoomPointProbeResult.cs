using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using System.Collections.Generic;

namespace CadToRevit.Models.Rooms
{
    public sealed class RoomPointProbeResult
    {
        public bool Success { get; set; }

        public bool HitNativeRoom { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }

        public string RoomName { get; set; }

        public string RoomNumber { get; set; }

        public string LevelName { get; set; }

        public double AreaM2 { get; set; }

        public XYZ PickPoint { get; set; }

        public ElementId LevelId { get; set; }

        public string StableRoomKey { get; set; }

        public List<XYZ> LoopPoints { get; set; } = new List<XYZ>();

        public List<ElementId> BoundaryElementIds { get; set; } = new List<ElementId>();

        public List<string> Warnings { get; set; } = new List<string>();

        public RoomSemanticRecord SemanticRecord { get; set; }
    }
}
