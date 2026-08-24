using CadToRevit.Models.Units;

namespace CadToRevit.Services.Dwg
{
    public sealed class DwgSourceUnitContext
    {
        public DwgSourceUnitContext(SourceUnit sourceUnit, double scaleToFeet, string evidence)
        {
            SourceUnit = sourceUnit;
            ScaleToFeet = scaleToFeet;
            Evidence = string.IsNullOrWhiteSpace(evidence) ? "Unknown" : evidence;
        }

        public SourceUnit SourceUnit { get; }

        public double ScaleToFeet { get; }

        public string Evidence { get; }
    }

    public static class DwgSourceUnitContextFactory
    {
        public static DwgSourceUnitContext Create(SourceUnit sourceUnit, string evidence)
        {
            switch (sourceUnit)
            {
                case SourceUnit.Inch:
                    return new DwgSourceUnitContext(SourceUnit.Inch, 1.0 / 12.0, evidence);
                case SourceUnit.Feet:
                    return new DwgSourceUnitContext(SourceUnit.Feet, 1.0, evidence);
                case SourceUnit.Meter:
                    return new DwgSourceUnitContext(SourceUnit.Meter, 1.0 / 0.3048, evidence);
                case SourceUnit.Millimeter:
                default:
                    return new DwgSourceUnitContext(SourceUnit.Millimeter, 1.0 / 304.8, evidence);
            }
        }
    }
}
