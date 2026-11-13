using System;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class UnitsTests
    {
        private const int Precision = 12;

        [Fact]
        public void OneInchIsExactly25Point4Millimeters()
        {
            Assert.Equal(0.0254, Units.InchesToMeters(1.0), Precision);
            Assert.Equal(25.4, Units.MetersToMillimeters(Units.InchesToMeters(1.0)), Precision);
        }

        [Fact]
        public void MillimetersToMeters()
        {
            Assert.Equal(0.0315, Units.MillimetersToMeters(31.5), Precision);
        }

        [Theory]
        [InlineData(LengthUnit.Meter)]
        [InlineData(LengthUnit.Millimeter)]
        [InlineData(LengthUnit.Inch)]
        public void ToMetersAndBackRoundTrips(LengthUnit unit)
        {
            const double value = 1.315;
            Assert.Equal(value, Units.FromMeters(Units.ToMeters(value, unit), unit), Precision);
        }

        [Fact]
        public void DegreesToRadians()
        {
            Assert.Equal(Math.PI / 2.0, Units.DegreesToRadians(90.0), Precision);
            Assert.Equal(-1.5, Units.RadiansToDegrees(Units.DegreesToRadians(-1.5)), Precision);
        }

        [Fact]
        public void UnknownUnitThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Units.ToMeters(1.0, (LengthUnit)99));
        }
    }
}
