using Autodesk.Revit.DB;
using CadToRevit.Models;
using System.Collections.Generic;

namespace CadToRevit.Services
{
    public static class NearestWallMatcher
    {
        public static bool TryMatch(
            WindowCandidate candidate,
            List<Wall> walls,
            double maxDistMm,
            out Wall wall,
            out XYZ projectedPoint,
            out double distMm)
        {
            wall = null;
            projectedPoint = null;
            distMm = double.MaxValue;

            if (candidate == null || candidate.CenterPoint == null || walls == null || walls.Count == 0)
            {
                return false;
            }

            foreach (Wall w in walls)
            {
                LocationCurve loc = w.Location as LocationCurve;
                Line line = loc?.Curve as Line;
                if (line == null)
                {
                    continue;
                }

                IntersectionResult prj = line.Project(candidate.CenterPoint);
                if (prj == null || prj.XYZPoint == null)
                {
                    continue;
                }

                double dMm = UnitUtils.ConvertFromInternalUnits(prj.Distance, UnitTypeId.Millimeters);
                if (dMm < distMm)
                {
                    distMm = dMm;
                    wall = w;
                    projectedPoint = prj.XYZPoint;
                }
            }

            return wall != null && distMm <= maxDistMm;
        }
    }
}
