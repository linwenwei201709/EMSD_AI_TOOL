using Autodesk.Revit.DB;

namespace CadToRevit.Models.Rooms.Semantic
{
    public sealed class RoomLabel
    {
        public string RawText { get; set; }

        public string RoomName { get; set; }

        public string RoomNumber { get; set; }

        public string TargetRoomType { get; set; }

        public string SourceLayer { get; set; }

        public XYZ Position { get; set; }
    }
}
