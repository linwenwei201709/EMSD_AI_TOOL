using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models.Rooms
{
    public enum RoomBoundaryStatus
    {
        Closed,
        AutoClosed,
        Patched,
        NeedsFix
    }

    public sealed class RoomCandidate
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Number { get; set; }

        public double AreaM2 { get; set; }

        public RoomBoundaryStatus Status { get; set; }

        public double CloseGapMm { get; set; }

        // 中文注释：按闭合顺序存储边界点（XY平面，首尾闭合）。
        public List<XYZ> LoopPoints { get; set; } = new List<XYZ>();

        public XYZ Centroid { get; set; }

        public BoundingBoxXYZ BBox { get; set; }

        public string SourceLayer { get; set; }

        public bool Created { get; set; }

        public ElementId RevitRoomId { get; set; } = ElementId.InvalidElementId;
    }
}
