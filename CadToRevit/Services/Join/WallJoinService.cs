using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Join
{
    public static class WallJoinService
    {
        public static int JoinNearbyWalls(Document doc, List<Wall> walls, double bboxTolFt = 0.2)
        {
            if (doc == null || walls == null || walls.Count < 2)
            {
                return 0;
            }

            List<ElementId> wallIds = walls
                .Where(x => x != null && x.IsValidObject)
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();
            if (wallIds.Count < 2)
            {
                return 0;
            }

            int joinCount = 0;
            for (int i = 0; i < wallIds.Count; i++)
            {
                Wall a = doc.GetElement(wallIds[i]) as Wall;
                if (!IsValidWall(a))
                {
                    continue;
                }

                BoundingBoxXYZ boxA = TryGetBoundingBox(a);
                if (boxA == null)
                {
                    continue;
                }

                Outline outlineA = ExpandOutline(new Outline(boxA.Min, boxA.Max), bboxTolFt);
                for (int j = i + 1; j < wallIds.Count; j++)
                {
                    Wall b = doc.GetElement(wallIds[j]) as Wall;
                    if (!IsValidWall(b))
                    {
                        continue;
                    }

                    BoundingBoxXYZ boxB = TryGetBoundingBox(b);
                    if (boxB == null)
                    {
                        continue;
                    }

                    Outline outlineB = ExpandOutline(new Outline(boxB.Min, boxB.Max), bboxTolFt);
                    if (!outlineA.Intersects(outlineB, 0))
                    {
                        continue;
                    }

                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, a, b))
                        {
                            continue;
                        }

                        JoinGeometryUtils.JoinGeometry(doc, a, b);
                        joinCount++;
                    }
                    catch
                    {
                    }
                }
            }

            return joinCount;
        }

        private static bool IsValidWall(Wall wall)
        {
            return wall != null && wall.IsValidObject;
        }

        private static BoundingBoxXYZ TryGetBoundingBox(Wall wall)
        {
            if (!IsValidWall(wall))
            {
                return null;
            }

            try
            {
                return wall.get_BoundingBox(null);
            }
            catch
            {
                return null;
            }
        }

        private static Outline ExpandOutline(Outline outline, double tolFt)
        {
            XYZ min = outline.MinimumPoint;
            XYZ max = outline.MaximumPoint;
            XYZ delta = new XYZ(tolFt, tolFt, tolFt);
            return new Outline(min - delta, max + delta);
        }
    }
}
