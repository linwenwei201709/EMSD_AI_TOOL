using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms.Manual
{
    internal static class ManualRoomDoorValidator
    {
        private const double DoorBoundaryToleranceMm = 900.0;

        internal static bool HasDoor(
            Document doc,
            View activeView,
            ManualRoomRecord room,
            IList<Element> boundaryElements)
        {
            if (doc == null || room == null)
            {
                return false;
            }

            List<FamilyInstance> doors = CollectDoors(doc);
            if (doors.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[ManualRoomDoor] No door family instances found.");
                return false;
            }

            HashSet<ElementId> selectedWallIds = new HashSet<ElementId>(
                (boundaryElements ?? new List<Element>())
                .OfType<Wall>()
                .Select(x => x.Id));

            if (selectedWallIds.Count > 0)
            {
                foreach (FamilyInstance door in doors)
                {
                    Wall hostWall = door != null ? door.Host as Wall : null;
                    if (hostWall != null && selectedWallIds.Contains(hostWall.Id))
                    {
                        DiagnosticRecorder.AppendDebug("[ManualRoomDoor] Door found on selected boundary wall. DoorId=" + door.Id.IntegerValue);
                        return true;
                    }
                }
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(DoorBoundaryToleranceMm, UnitTypeId.Millimeters);
            foreach (FamilyInstance door in doors)
            {
                if (!IsLevelCompatible(door, room))
                {
                    continue;
                }

                XYZ point = ResolveDoorPoint(door, activeView);
                if (point == null)
                {
                    continue;
                }

                if (!IsInsideExpandedRoomBox(room, point, tolerance))
                {
                    continue;
                }

                if (IsNearRoomLoop(room.LoopPoints, point, tolerance))
                {
                    DiagnosticRecorder.AppendDebug("[ManualRoomDoor] Door found near manual room boundary. DoorId=" + door.Id.IntegerValue);
                    return true;
                }
            }

            DiagnosticRecorder.AppendDebug("[ManualRoomDoor] Missing door for manual room boundary.");
            return false;
        }

        private static List<FamilyInstance> CollectDoors(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .ToList();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ManualRoomDoor] Collect doors failed: " + ex.Message);
                return new List<FamilyInstance>();
            }
        }

        private static bool IsLevelCompatible(FamilyInstance door, ManualRoomRecord room)
        {
            if (door == null || room == null || room.LevelIdValue <= 0)
            {
                return true;
            }

            ElementId levelId = door.LevelId;
            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            return levelId.IntegerValue == room.LevelIdValue;
        }

        private static XYZ ResolveDoorPoint(FamilyInstance door, View activeView)
        {
            LocationPoint locationPoint = door != null ? door.Location as LocationPoint : null;
            if (locationPoint != null)
            {
                return locationPoint.Point;
            }

            BoundingBoxXYZ box = null;
            try
            {
                box = door != null ? door.get_BoundingBox(activeView) : null;
            }
            catch
            {
                box = null;
            }

            if (box == null)
            {
                try
                {
                    box = door != null ? door.get_BoundingBox(null) : null;
                }
                catch
                {
                    box = null;
                }
            }

            return box != null && box.Min != null && box.Max != null
                ? (box.Min + box.Max) * 0.5
                : null;
        }

        private static bool IsInsideExpandedRoomBox(ManualRoomRecord room, XYZ point, double tolerance)
        {
            BoundingBoxXYZ box = room != null ? room.BBox : null;
            if (box == null || box.Min == null || box.Max == null || point == null)
            {
                return true;
            }

            return point.X >= box.Min.X - tolerance &&
                   point.X <= box.Max.X + tolerance &&
                   point.Y >= box.Min.Y - tolerance &&
                   point.Y <= box.Max.Y + tolerance;
        }

        private static bool IsNearRoomLoop(IList<XYZ> loopPoints, XYZ point, double tolerance)
        {
            if (loopPoints == null || loopPoints.Count < 2 || point == null)
            {
                return false;
            }

            for (int i = 0; i < loopPoints.Count; i++)
            {
                XYZ a = loopPoints[i];
                XYZ b = loopPoints[(i + 1) % loopPoints.Count];
                if (DistancePointToSegmentXY(point, a, b) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static double DistancePointToSegmentXY(XYZ point, XYZ a, XYZ b)
        {
            if (point == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                return Math.Sqrt(Square(point.X - a.X) + Square(point.Y - a.Y));
            }

            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double x = a.X + t * dx;
            double y = a.Y + t * dy;
            return Math.Sqrt(Square(point.X - x) + Square(point.Y - y));
        }

        private static double Square(double value)
        {
            return value * value;
        }
    }
}
