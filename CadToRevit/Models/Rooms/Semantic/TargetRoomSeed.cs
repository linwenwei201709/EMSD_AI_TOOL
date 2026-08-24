using Autodesk.Revit.DB;

namespace CadToRevit.Models.Rooms.Semantic
{
    // Represents a target room text anchor persisted for delayed model-based recognition.
    public sealed class TargetRoomSeed
    {
        public string Key { get; set; }

        public string RoomName { get; set; }

        public string TargetRoomType { get; set; }

        public XYZ Position { get; set; }

        public ElementId LevelId { get; set; }

        public string SourceLayer { get; set; }

        public string RawText { get; set; }
    }
}
