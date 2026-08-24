using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models.Rooms.Semantic
{
    public sealed class RoomSemanticRecord
    {
        public string Key { get; set; }

        public string RoomName { get; set; }

        public string RoomNumber { get; set; }

        public string TargetRoomType { get; set; }

        public string Status { get; set; }

        public double AreaM2 { get; set; }

        public double CloseGapMm { get; set; }

        public string BoundaryLayers { get; set; }

        public XYZ Centroid { get; set; }

        public BoundingBoxXYZ BBox { get; set; }

        public List<XYZ> LoopPoints { get; set; } = new List<XYZ>();

        public List<RoomBoundaryWallReference> BoundaryWalls { get; set; } = new List<RoomBoundaryWallReference>();
    }

    public sealed class RoomBoundaryWallReference
    {
        public int ElementId { get; set; }

        public string UniqueId { get; set; }

        public string DisplayName { get; set; }

        public string RevitName { get; set; }

        public double LengthMm { get; set; }
    }
}
