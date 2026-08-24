using CadToRevit.Models.Path;

namespace CadToRevit.Services.PathPreview
{
    internal static class DemoPathDataService
    {
        internal static PathPolyline BuildDemoPath(PathPreviewModelAnchorService.ModelAnchorInfo anchor)
        {
            return new PathPolyline
            {
                PathId = "PICKED_DEMO_001",
                CoordinateBase = "InternalOrigin",
                Frame = "IfcLocal",
                Unit = "mm",
                Points =
                {
                    // Latest captured demo path in InternalOrigin / IfcLocal / mm.
                    new PathPoint3D(57712.3, 43745.0, -156.6),
                    new PathPoint3D(57712.3, 40145.0, -156.6),
                    new PathPoint3D(52155.0, 40145.0, -156.6),
                    new PathPoint3D(52155.0, 39495.0, -156.6),
                    new PathPoint3D(37623.4, 39645.0, -156.6),
                    new PathPoint3D(31228.3, 39645.0, -156.6),
                    new PathPoint3D(30170.1, 37495.0, -156.6),
                    new PathPoint3D(16188.6, 37495.0, -156.6),
                    new PathPoint3D(17034.3, 45415.0, -156.6),
                    new PathPoint3D(21980.0, 45215.0, -156.6)

                    //new PathPoint3D(20070, 42595, -0),
                    //   new PathPoint3D(19749, 42474, -0),
                    //   new PathPoint3D(18906, 42095, -0),
                    //   new PathPoint3D(18072, 41695, -0),
                    //   new PathPoint3D(17247, 41272, -0),
                    //   new PathPoint3D(16681, 40706, -0),
                    //    new PathPoint3D(16370, 39835, -0),
                    //    new PathPoint3D(16295, 38970, -0),
                    //    new PathPoint3D(16649, 38116, -0),
                    //     new PathPoint3D(17144, 37869, -0),
                    //     new PathPoint3D(17851, 38576, -0),
                    //     new PathPoint3D(18843, 38595, -0),
                    //     new PathPoint3D(19801, 38695, -0),
                    //     new PathPoint3D(20801, 38695, -0),
                    //     new PathPoint3D(21801, 38695, -0),
                    //     new PathPoint3D(22801, 38695, -0),
                    //     new PathPoint3D(23801, 38695, -0),
                    //     new PathPoint3D(24801, 38695, -0),
                    //     new PathPoint3D(25801, 38695, -0),
                    //     new PathPoint3D(26801, 38695, -0),
                    //     new PathPoint3D(27801, 38695, -0),
                    //     new PathPoint3D(28801, 38695, -0),
                    //     new PathPoint3D(29801, 38695, -0),
                    //     new PathPoint3D(30801, 38695, -0),
                    //      new PathPoint3D(31760, 38795, -0),
                    //      new PathPoint3D(32529, 39354, -0),
                    //      new PathPoint3D(33470, 39495, -0),
                    //      new PathPoint3D(34470, 39495, -0),
                    //      new PathPoint3D(35470, 39495, -0),
                    //      new PathPoint3D(36470, 39495, -0),
                    //      new PathPoint3D(37470, 39495, -0),
                    //      new PathPoint3D(38470, 39495, -0),
                    //      new PathPoint3D(39470, 39495, -0),
                    //      new PathPoint3D(40470, 39495, -0),
                    //      new PathPoint3D(41470, 39495, -0),
                    //      new PathPoint3D(42470, 39495, -0),
                    //      new PathPoint3D(43470, 39495, -0),
                    //      new PathPoint3D(44470, 39495, -0)
                }
            };
        }
    }
}
