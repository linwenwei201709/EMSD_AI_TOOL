using System;

namespace CadToRevit.Services.Api
{
    // Single boundary for the public API coordinate contract. Revit stores
    // model coordinates in feet; the Route/Room-Fit APIs use IFC millimetres.
    // Keeping this adapter additive avoids changing the colleague's model
    // coordinate calculations while making the API boundary explicit.
    public static class IfcMillimeterCoordinateAdapter
    {
        public const double MillimetersPerFoot = 304.8;

        public static double FeetToMillimeters(double feet)
        {
            return feet * MillimetersPerFoot;
        }

        public static double MillimetersToFeet(double millimeters)
        {
            return millimeters / MillimetersPerFoot;
        }

        public static double RevitRadiansToApiRadians(double revitRadians)
        {
            return NormalizeRadians(-revitRadians);
        }

        public static double ApiRadiansToRevitRadians(double apiRadians)
        {
            return NormalizeRadians(-apiRadians);
        }

        public static double NormalizeRadians(double angle)
        {
            while (angle > Math.PI) angle -= Math.PI * 2.0;
            while (angle < -Math.PI) angle += Math.PI * 2.0;
            return angle;
        }
    }
}
