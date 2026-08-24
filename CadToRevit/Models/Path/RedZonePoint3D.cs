using System.Collections.Generic;

namespace CadToRevit.Models.Path
{
    public sealed class RedZonePoint3D
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public double CellSizeMm { get; set; }

        public List<string> Reasons { get; set; } = new List<string>();
    }
}
