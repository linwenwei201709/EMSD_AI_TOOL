using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.Semantic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class RevitRoomSemanticFocusService
    {
        private const double FocusPaddingMm = 6000.0;

        public static void Focus(UIDocument uiDoc, RoomSemanticRecord room)
        {
            if (uiDoc == null || room == null)
            {
                return;
            }

            UIView uiView = uiDoc.GetOpenUIViews()
                .FirstOrDefault(x => x != null && x.ViewId == uiDoc.ActiveView.Id);
            if (uiView == null)
            {
                return;
            }

            BoundingBoxXYZ box = BuildRoomBox(room);
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            double pad = UnitUtils.ConvertToInternalUnits(FocusPaddingMm, UnitTypeId.Millimeters);
            XYZ min = new XYZ(box.Min.X - pad, box.Min.Y - pad, box.Min.Z - 1.0);
            XYZ max = new XYZ(box.Max.X + pad, box.Max.Y + pad, box.Max.Z + 1.0);

            // Avoid invalid zoom rectangles by enforcing a tiny minimum extent.
            const double minSpan = 1e-3;
            if (max.X - min.X < minSpan)
            {
                double spanPadX = (minSpan - (max.X - min.X)) * 0.5;
                min = new XYZ(min.X - spanPadX, min.Y, min.Z);
                max = new XYZ(max.X + spanPadX, max.Y, max.Z);
            }

            if (max.Y - min.Y < minSpan)
            {
                double spanPadY = (minSpan - (max.Y - min.Y)) * 0.5;
                min = new XYZ(min.X, min.Y - spanPadY, min.Z);
                max = new XYZ(max.X, max.Y + spanPadY, max.Z);
            }

            uiView.ZoomAndCenterRectangle(min, max);
        }

        private static BoundingBoxXYZ BuildRoomBox(RoomSemanticRecord room)
        {
            if (room == null)
            {
                return null;
            }

            if (room.BBox != null && room.BBox.Min != null && room.BBox.Max != null)
            {
                return room.BBox;
            }

            var points = room.LoopPoints ?? new System.Collections.Generic.List<XYZ>();
            if (points.Count > 0)
            {
                double minX = points.Min(p => p.X);
                double minY = points.Min(p => p.Y);
                double minZ = points.Min(p => p.Z);
                double maxX = points.Max(p => p.X);
                double maxY = points.Max(p => p.Y);
                double maxZ = points.Max(p => p.Z);
                return new BoundingBoxXYZ
                {
                    Min = new XYZ(minX, minY, minZ),
                    Max = new XYZ(maxX, maxY, maxZ)
                };
            }

            if (room.Centroid != null)
            {
                const double halfSizeFeet = 3.0;
                return new BoundingBoxXYZ
                {
                    Min = new XYZ(room.Centroid.X - halfSizeFeet, room.Centroid.Y - halfSizeFeet, room.Centroid.Z - 1.0),
                    Max = new XYZ(room.Centroid.X + halfSizeFeet, room.Centroid.Y + halfSizeFeet, room.Centroid.Z + 1.0)
                };
            }

            return null;
        }
    }
}
