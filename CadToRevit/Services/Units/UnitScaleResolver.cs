using CadToRevit.Models.Units;

namespace CadToRevit.Services.Units
{
    public static class UnitScaleResolver
    {
        public static UnitContext Resolve(SourceUnit sourceUnit)
        {
            switch (sourceUnit)
            {
                case SourceUnit.Feet:
                    return Build(sourceUnit, 1.0, "UserSelectedFeet");
                case SourceUnit.Inch:
                    return Build(sourceUnit, 1.0 / 12.0, "UserSelectedInch");
                case SourceUnit.Millimeter:
                    return Build(sourceUnit, 1.0 / 304.8, "UserSelectedMillimeter");
                case SourceUnit.Meter:
                    return Build(sourceUnit, 1.0 / 0.3048, "UserSelectedMeter");
                case SourceUnit.Auto:
                default:
                    return Build(SourceUnit.Feet, 1.0, "AutoAssumeFeet");
            }
        }

        private static UnitContext Build(SourceUnit sourceUnit, double scaleToFeet, string evidence)
        {
            return new UnitContext
            {
                SourceUnit = sourceUnit,
                ScaleToFeet = scaleToFeet,
                Confidence = 1.0,
                Evidence = evidence
            };
        }
    }
}
