using System.Collections.Generic;

namespace CadToRevit.Models.Path
{
    public sealed class PathPolyline
    {
        public string PathId { get; set; }
        public string CoordinateBase { get; set; }
        public string Frame { get; set; }
        public string Unit { get; set; }
        public double BoxLengthMm { get; set; }
        public double BoxWidthMm { get; set; }
        public double BoxHeightMm { get; set; }
        public List<PathPoint3D> Points { get; set; } = new List<PathPoint3D>();
    }
}
