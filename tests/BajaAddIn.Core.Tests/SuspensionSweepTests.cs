using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class SuspensionSweepTests
    {
        private const double In = Units.MetersPerInch;

        private static CornerGeometry G(Corner c, double wcZ, double camber, double toe) => new CornerGeometry
        {
            Corner = c,
            WheelCenter = new Vec3(1.5, 0.6, wcZ),
            Camber = camber,
            Toe = toe,
        };

        [Fact]
        public void Values_include_both_ends()
        {
            Assert.Equal(new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }, SuspensionSweep.Values(0, 1, 5));
            Assert.Throws<System.ArgumentException>(() => SuspensionSweep.Values(0, 1, 1));
        }

        [Fact]
        public void Slope_is_least_squares()
        {
            Assert.Equal(2.0, SuspensionSweep.Slope(new[] { 0.0, 1, 2, 3 }, new[] { 1.0, 3, 5, 7 }), 12);
            Assert.Equal(0.0, SuspensionSweep.Slope(new[] { 1.0, 1 }, new[] { 0.0, 5 }));
        }

        [Fact]
        public void Summary_gives_camber_gain_and_bump_steer_per_inch_of_travel()
        {
            // Wheel rises 1 in per step; camber goes 1 deg more negative and toe 0.1 deg out per inch.
            var reference = new[] { G(Corner.FL, 0.3, -1, 0) };
            var steps = Enumerable.Range(-2, 5).Select(i =>
                new SweepStep(i, new[] { G(Corner.FL, 0.3 + i * In, -1 - i, -0.1 * i) })).ToList();
            SweepSummary s = Assert.Single(SuspensionSweep.Summarize(steps, reference));
            Assert.Equal(-1.0, s.CamberGainPerInch, 9);
            Assert.Equal(-0.1, s.BumpSteerPerInch, 9);
            Assert.Equal(-2 * In, s.TravelMin, 12);
            Assert.Equal(2 * In, s.TravelMax, 12);
            Assert.Equal(-3, s.CamberMin, 12);
            Assert.Equal(1, s.CamberMax, 12);
        }

        [Fact]
        public void Csv_has_a_column_set_per_corner()
        {
            var reference = new[] { G(Corner.FL, 0.3, -1, 0), G(Corner.FR, 0.3, -1, 0) };
            var steps = new List<SweepStep>
            {
                new SweepStep(0.1, new[] { G(Corner.FL, 0.31, -1.5, 0.1), G(Corner.FR, 0.3, -1, 0) }),
                new SweepStep(0.2, new[] { G(Corner.FL, 0.32, -2, 0.2) }),
            };
            string[] lines = SuspensionSweep.Csv(steps, reference, "Shock", "mm", v => v * 1000)
                .Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Shock (mm),FL travel (mm),FL camber (deg)", lines[0]);
            Assert.Equal(15, lines[0].Split(',').Length);
            Assert.StartsWith("100,10,-1.5,0.1,", lines[1]);
            Assert.EndsWith(",,,,,,,", lines[2]); // FR missing at that step
        }
    }
}
