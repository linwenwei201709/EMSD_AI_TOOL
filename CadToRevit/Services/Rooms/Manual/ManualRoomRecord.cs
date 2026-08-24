using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using System.Collections.Generic;

namespace CadToRevit.Services.Rooms.Manual
{
    public sealed class ManualRoomRecord
    {
        public string Key { get; set; }

        public string RoomName { get; set; }

        public string RoomNumber { get; set; }

        public string RoomType { get; set; }

        public string SourceType { get; set; } = "Manual";

        public int LevelIdValue { get; set; } = -1;

        public string LevelName { get; set; }

        public string BoundarySignature { get; set; }

        public double AreaM2 { get; set; }

        public XYZ Centroid { get; set; }

        public BoundingBoxXYZ BBox { get; set; }

        public List<XYZ> LoopPoints { get; set; } = new List<XYZ>();

        public List<RoomBoundaryWallReference> BoundaryWalls { get; set; } = new List<RoomBoundaryWallReference>();

        public string CreatedAt { get; set; }

        public RoomSemanticRecord ToSemanticRecord()
        {
            return new RoomSemanticRecord
            {
                Key = Key ?? string.Empty,
                RoomName = RoomName ?? string.Empty,
                RoomNumber = RoomNumber ?? string.Empty,
                TargetRoomType = RoomType ?? string.Empty,
                Status = string.IsNullOrWhiteSpace(SourceType) ? "Manual" : SourceType,
                AreaM2 = AreaM2,
                CloseGapMm = 0.0,
                BoundaryLayers = "Manual",
                Centroid = Centroid,
                BBox = BBox,
                LoopPoints = LoopPoints ?? new List<XYZ>(),
                BoundaryWalls = BoundaryWalls ?? new List<RoomBoundaryWallReference>()
            };
        }
    }
}
