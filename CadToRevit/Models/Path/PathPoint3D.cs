namespace CadToRevit.Models.Path
{
    public sealed class PathPoint3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double? OrientationRadians { get; set; }

        public PathPoint3D()
        {
        }

        public PathPoint3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public PathPoint3D(double x, double y, double z, double? orientationRadians)
        {
            X = x;
            Y = y;
            Z = z;
            OrientationRadians = orientationRadians;
        }
    }
}
