using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public sealed class RoomWallCreateOptions
    {
        public bool CreateWalls { get; set; }

        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;

        public double WallHeightMm { get; set; } = 4000.0;

        public double MinWallSegmentMm { get; set; } = 600.0;

        public bool AvoidDuplicateWalls { get; set; } = true;
    }

    public sealed class RoomCreateResult
    {
        public int CreatedRoomCount { get; set; }

        public List<ElementId> CreatedSeparationLineIds { get; set; } = new List<ElementId>();

        public Dictionary<string, ElementId> RoomKeyToRevitRoomId { get; set; } = new Dictionary<string, ElementId>();

        public List<ElementId> CreatedWallIds { get; set; } = new List<ElementId>();

        public int CreatedWallCount { get; set; }

        public int FailedWallCount { get; set; }
    }

    public static class RevitRoomCreateService
    {
        public static RoomCreateResult Create(
            Document doc,
            Level level,
            List<RoomCandidate> candidates,
            RoomWallCreateOptions wallOptions = null)
        {
            RoomCreateResult result = new RoomCreateResult();
            if (doc == null || level == null)
            {
                return result;
            }

            List<RoomCandidate> valid = (candidates ?? new List<RoomCandidate>())
                .Where(x => x != null &&
                            (x.Status == RoomBoundaryStatus.Closed ||
                             x.Status == RoomBoundaryStatus.AutoClosed ||
                             x.Status == RoomBoundaryStatus.Patched) &&
                            x.LoopPoints != null &&
                            x.LoopPoints.Count >= 4)
                .ToList();
            if (valid.Count == 0)
            {
                return result;
            }

            View planView = FindPlanViewForLevel(doc, level.Id);
            if (planView == null)
            {
                return result;
            }

            wallOptions = wallOptions ?? new RoomWallCreateOptions();
            WallType wallType = wallOptions.WallTypeId == ElementId.InvalidElementId
                ? null
                : doc.GetElement(wallOptions.WallTypeId) as WallType;
            bool canCreateWalls = wallOptions.CreateWalls && wallType != null;
            double wallHeightFt = UnitUtils.ConvertToInternalUnits(Math.Max(wallOptions.WallHeightMm, 100.0), UnitTypeId.Millimeters);
            double minWallSegmentFt = UnitUtils.ConvertToInternalUnits(Math.Max(wallOptions.MinWallSegmentMm, 1.0), UnitTypeId.Millimeters);
            double duplicateOffsetTolFt = UnitUtils.ConvertToInternalUnits(15.0, UnitTypeId.Millimeters);
            const double duplicateOverlapRatio = 0.80;
            const double duplicateAngleTolRad = 2.0 * Math.PI / 180.0;
            List<Line> existingWallLines = canCreateWalls && wallOptions.AvoidDuplicateWalls
                ? CollectWallCenterLines(doc)
                : new List<Line>();

            using (Transaction tx = new Transaction(doc, "CadToRevit Room Recognition Generate"))
            {
                tx.Start();
                SketchPlane sketchPlane = SketchPlane.Create(
                    doc,
                    Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
                foreach (RoomCandidate c in valid)
                {
                    CurveArray arr = new CurveArray();
                    List<Line> roomSegments = new List<Line>();
                    for (int i = 0; i < c.LoopPoints.Count - 1; i++)
                    {
                        XYZ p0 = c.LoopPoints[i];
                        XYZ p1 = c.LoopPoints[i + 1];
                        if (p0.DistanceTo(p1) <= 1e-9)
                        {
                            continue;
                        }

                        Line segment = Line.CreateBound(
                            new XYZ(p0.X, p0.Y, level.Elevation),
                            new XYZ(p1.X, p1.Y, level.Elevation));
                        arr.Append(segment);
                        roomSegments.Add(segment);
                    }

                    if (arr.Size < 3)
                    {
                        continue;
                    }

                    ModelCurveArray lines = doc.Create.NewRoomBoundaryLines(sketchPlane, arr, planView);
                    foreach (ModelCurve line in lines)
                    {
                        if (line != null)
                        {
                            result.CreatedSeparationLineIds.Add(line.Id);
                        }
                    }

                    UV uv = new UV(c.Centroid.X, c.Centroid.Y);
                    Autodesk.Revit.DB.Architecture.Room room = doc.Create.NewRoom(level, uv);
                    if (room != null)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Name))
                        {
                            room.Name = c.Name;
                        }

                        if (!string.IsNullOrWhiteSpace(c.Number))
                        {
                            room.Number = c.Number;
                        }

                        c.Created = true;
                        c.RevitRoomId = room.Id;
                        result.RoomKeyToRevitRoomId[c.Key] = room.Id;
                        result.CreatedRoomCount++;
                    }

                    if (!canCreateWalls)
                    {
                        continue;
                    }

                    // 中文注释：墙体创建与房间创建解耦，单段失败不影响整体事务提交。
                    foreach (Line segment in roomSegments.Where(x => x.Length >= minWallSegmentFt))
                    {
                        try
                        {
                            if (wallOptions.AvoidDuplicateWalls &&
                                HasOverlappedWallSegment(segment, existingWallLines, duplicateAngleTolRad, duplicateOffsetTolFt, duplicateOverlapRatio))
                            {
                                continue;
                            }

                            Wall wall = Wall.Create(doc, segment, wallType.Id, level.Id, wallHeightFt, 0.0, false, false);
                            if (wall != null)
                            {
                                result.CreatedWallIds.Add(wall.Id);
                                result.CreatedWallCount++;
                                if (wallOptions.AvoidDuplicateWalls)
                                {
                                    existingWallLines.Add(segment);
                                }
                            }
                        }
                        catch
                        {
                            result.FailedWallCount++;
                        }
                    }
                }

                tx.Commit();
            }

            return result;
        }

        private static List<Line> CollectWallCenterLines(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Select(x => x.Location as LocationCurve)
                .Where(x => x != null && x.Curve is Line)
                .Select(x => x.Curve as Line)
                .Where(x => x != null && x.Length > 1e-9)
                .ToList();
        }

        private static bool HasOverlappedWallSegment(
            Line candidate,
            List<Line> existing,
            double angleTolRad,
            double offsetTolFt,
            double minOverlapRatio)
        {
            foreach (Line line in existing ?? new List<Line>())
            {
                if (line == null)
                {
                    continue;
                }

                if (IsNearCollinearOverlap(candidate, line, angleTolRad, offsetTolFt, minOverlapRatio))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNearCollinearOverlap(
            Line a,
            Line b,
            double angleTolRad,
            double offsetTolFt,
            double minOverlapRatio)
        {
            if (a == null || b == null || a.Length <= 1e-9 || b.Length <= 1e-9)
            {
                return false;
            }

            XYZ da = (a.GetEndPoint(1) - a.GetEndPoint(0)).Normalize();
            XYZ db = (b.GetEndPoint(1) - b.GetEndPoint(0)).Normalize();
            double dot = Math.Abs(da.DotProduct(db));
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
            if (angle > angleTolRad)
            {
                return false;
            }

            double d0 = b.Distance(a.GetEndPoint(0));
            double d1 = b.Distance(a.GetEndPoint(1));
            if (Math.Max(d0, d1) > offsetTolFt)
            {
                return false;
            }

            XYZ origin = b.GetEndPoint(0);
            XYZ axis = db;
            double a0 = axis.DotProduct(a.GetEndPoint(0) - origin);
            double a1 = axis.DotProduct(a.GetEndPoint(1) - origin);
            double b0 = 0.0;
            double b1 = b.Length;
            double aMin = Math.Min(a0, a1);
            double aMax = Math.Max(a0, a1);
            double overlap = Math.Max(0.0, Math.Min(aMax, b1) - Math.Max(aMin, b0));
            if (overlap <= 1e-9)
            {
                return false;
            }

            double baseLen = Math.Min(a.Length, b.Length);
            return baseLen > 1e-9 && overlap / baseLen >= minOverlapRatio;
        }

        private static ViewPlan FindPlanViewForLevel(Document doc, ElementId levelId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(x => x != null && !x.IsTemplate && x.GenLevel != null && x.GenLevel.Id == levelId)
                .OrderBy(x => x.Name)
                .FirstOrDefault();
        }
    }
}
