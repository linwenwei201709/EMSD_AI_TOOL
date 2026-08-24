using Autodesk.Revit.DB;

namespace CadToRevit.Models.Cad
{
    public sealed class CadText
    {
        public string RawLayerName { get; set; }

        public string Text { get; set; }

        public XYZ Position { get; set; }

        public double RotationRad { get; set; }

        public double RawCadX { get; set; }

        public double RawCadY { get; set; }

        public double RawCadZ { get; set; }

        public double CadFeetX { get; set; }

        public double CadFeetY { get; set; }

        public double CadFeetZ { get; set; }
    }
}
