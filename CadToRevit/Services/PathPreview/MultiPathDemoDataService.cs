using CadToRevit.Models.Path;
using System.Collections.Generic;

namespace CadToRevit.Services.PathPreview
{
    internal static class MultiPathDemoDataService
    {
        internal static List<PathPolyline> BuildDemoPaths()
        {
            return new List<PathPolyline>
            {
                new PathPolyline
                {
                    PathId = "PICKED_DEMO_001_A",
                    CoordinateBase = "InternalOrigin",
                    Frame = "IfcLocal",
                    Unit = "mm",
                    Points =
                    {
                        new PathPoint3D(53405, 42982.2, 0.8),
                        new PathPoint3D(53405, 39495, -0),
                        new PathPoint3D(39980, 39595, -0),
                        new PathPoint3D(39980, 36395, -0)//,
                        //new PathPoint3D(33921.4, 39545.0, -0),
                        //new PathPoint3D(34001.8, 43695.0, -0),
                        //new PathPoint3D(24143.6, 43995.0, -0)
                    }
                },
                new PathPolyline
                {
                    PathId = "PICKED_DEMO_001_B",
                    CoordinateBase = "InternalOrigin",
                    Frame = "IfcLocal",
                    Unit = "mm",
                    Points =
                    {
                        new PathPoint3D(18107, 35858, -0),
                        new PathPoint3D(18330, 37290, -0),
                        new PathPoint3D(37859, 37292, -0)
                        //new PathPoint3D(51171.5, 39149.9, -0),
                        //new PathPoint3D(32580.0, 39395.0, -0),
                        //new PathPoint3D(32380.0, 34295.0, -0)
                    }
                }
            };
        }
    }
}
