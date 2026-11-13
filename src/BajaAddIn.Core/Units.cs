using System;

namespace Lbr.CadTools.Core
{
    /// <summary>
    /// Unit conversion at the UI boundary. The SolidWorks API always works in
    /// meters and radians; everything else is converted here.
    /// </summary>
    public static class Units
    {
        public const double MetersPerMillimeter = 0.001;
        public const double MetersPerInch = 0.0254;

        public static double MillimetersToMeters(double mm) => mm * MetersPerMillimeter;

        public static double MetersToMillimeters(double m) => m / MetersPerMillimeter;

        public static double InchesToMeters(double inches) => inches * MetersPerInch;

        public static double MetersToInches(double m) => m / MetersPerInch;

        public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

        /// <summary>Converts a length in the given unit to meters.</summary>
        public static double ToMeters(double value, LengthUnit unit)
        {
            switch (unit)
            {
                case LengthUnit.Meter: return value;
                case LengthUnit.Millimeter: return MillimetersToMeters(value);
                case LengthUnit.Inch: return InchesToMeters(value);
                default: throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        /// <summary>Converts a length in meters to the given unit.</summary>
        public static double FromMeters(double meters, LengthUnit unit)
        {
            switch (unit)
            {
                case LengthUnit.Meter: return meters;
                case LengthUnit.Millimeter: return MetersToMillimeters(meters);
                case LengthUnit.Inch: return MetersToInches(meters);
                default: throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }
    }

    public enum LengthUnit
    {
        Meter,
        Millimeter,
        Inch,
    }
}
