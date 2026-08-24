using CadToRevit.Models.Rooms.Semantic;

namespace CadToRevit.Services.Rooms.Manual
{
    public sealed class ManualRoomValidationRoomInfo
    {
        public RoomSemanticRecord Room { get; set; }

        public int LevelIdValue { get; set; } = -1;

        public string SourceType { get; set; }
    }
}
