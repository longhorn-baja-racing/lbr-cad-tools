using System;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class SuspensionTests
    {
        private const double Rad = Math.PI / 180;

        /// <summary>
        /// Builds a corner whose alignment is known: spin axis from camber and
        /// toe, steering axis from caster and KPI (vehicle axes, meters).
        /// </summary>
        private static CornerPoints Build(Corner corner, double camber, double toe, double caster, double kpi,
                                          double x = 0, double halfTrack = 0.6)
        {
            int side = corner == Corner.FL || corner == Corner.RL ? 1 : -1;
            double c = camber * Rad, t = toe * Rad;
            var spin = new Vec3(Math.Sin(t) * Math.Cos(c), side * Math.Cos(t) * Math.Cos(c), -Math.Sin(c));
            var wc = new Vec3(x, side * halfTrack, 0.3);
            var steer = new Vec3(-Math.Tan(caster * Rad), -side * Math.Tan(kpi * Rad), 1).Unit();
            var lbj = new Vec3(x + 0.01, side * (halfTrack - 0.05), 0.15);
            return new CornerPoints
            {
                Corner = corner,
                WheelCenter = wc,
                SpinAxisPoint = wc - spin * 0.1,
                LowerBallJoint = lbj,
                UpperBallJoint = lbj + steer * 0.2,
                TireRadius = 0.3,
            };
        }

        [Theory]
        [InlineData(Corner.FL, -2.0, 0.5, 8.0, 10.0)]
        [InlineData(Corner.FR, -2.0, 0.5, 8.0, 10.0)]
        [InlineData(Corner.FL, 1.5, -1.0, -3.0, 0.0)]
        [InlineData(Corner.RR, 0.0, 0.0, 0.0, 12.0)]
        public void Recovers_the_angles_it_was_built_with(Corner corner, double camber, double toe, double caster, double kpi)
        {
            CornerGeometry g = SuspensionMath.Corner(Build(corner, camber, toe, caster, kpi));
            Assert.Equal(camber, g.Camber, 9);
            Assert.Equal(toe, g.Toe, 9);
            Assert.Equal(caster, g.Caster, 9);
            Assert.Equal(kpi, g.Kpi, 9);
        }

        [Fact]
        public void Scrub_and_trail_for_a_simple_case()
        {
            // No camber or toe: contact patch straight below the wheel center.
            CornerGeometry g = SuspensionMath.Corner(Build(Corner.FL, 0, 0, 0, 10));
            Assert.Equal(0, g.ContactPatch.Z, 12);
            Assert.Equal(0.6, g.ContactPatch.Y, 12);
            // Axis leans inboard going up, so at the ground (0.15 below the LBJ) it is 0.15 tan(KPI) outboard.
            double expected = 0.6 - (0.55 + 0.15 * Math.Tan(10 * Rad));
            Assert.Equal(expected, g.ScrubRadius, 12);
            Assert.Equal(0.01, g.MechanicalTrail, 12);
        }

        [Fact]
        public void Caster_gives_positive_trail()
        {
            CornerGeometry g = SuspensionMath.Corner(Build(Corner.FR, 0, 0, 10, 0));
            // LBJ is 10 mm ahead of the contact patch; the axis tilts back going up, so forward going down.
            Assert.Equal(0.01 + 0.15 * Math.Tan(10 * Rad), g.MechanicalTrail, 12);
        }

        [Fact]
        public void Camber_moves_the_contact_patch_in_the_wheel_plane()
        {
            CornerGeometry g = SuspensionMath.Corner(Build(Corner.FL, -3, 0, 0, 0));
            Assert.Equal(0.3 - 0.3 * Math.Cos(3 * Rad), g.ContactPatch.Z, 12);
        }

        [Fact]
        public void Upper_joint_below_lower_is_rejected()
        {
            CornerPoints p = Build(Corner.FL, 0, 0, 0, 0);
            (p.UpperBallJoint, p.LowerBallJoint) = (p.LowerBallJoint, p.UpperBallJoint);
            Assert.Throws<ArgumentException>(() => SuspensionMath.Corner(p));
        }

        [Fact]
        public void Static_toe_is_not_a_turn()
        {
            CornerGeometry fl = SuspensionMath.Corner(Build(Corner.FL, -1, 0.5, 5, 8, x: 1.4));
            CornerGeometry fr = SuspensionMath.Corner(Build(Corner.FR, -1, 0.5, 5, 8, x: 1.4));
            SteeringGeometry s = SuspensionMath.Steering(fl, fr, 0);
            Assert.True(s.Straight);
            // Camber with toe moves the contact patch slightly fore/aft of the wheel center.
            Assert.Equal(1.4, s.Wheelbase, 4);
        }

        [Fact]
        public void Ideal_ackermann_is_100_percent_and_parallel_is_0()
        {
            const double wheelbase = 1.4;
            // Steering axes vertical so the kingpin track is easy: 2 * 0.55.
            double track = 1.1;
            double outer = 20;
            double idealInner = Math.Atan(1 / (1 / Math.Tan(outer * Rad) - track / wheelbase)) / Rad;

            // Left turn: left wheel is inner; steer + left = toe -inner on the left, toe +outer on the right.
            CornerGeometry fl = SuspensionMath.Corner(Build(Corner.FL, 0, -idealInner, 0, 0, x: wheelbase));
            CornerGeometry fr = SuspensionMath.Corner(Build(Corner.FR, 0, outer, 0, 0, x: wheelbase));
            SteeringGeometry s = SuspensionMath.Steering(fl, fr, 0);
            Assert.False(s.Straight);
            Assert.Equal(idealInner, s.InnerSteer, 9);
            Assert.Equal(outer, s.OuterSteer, 9);
            Assert.Equal(100, s.AckermannPercent, 6);

            fl = SuspensionMath.Corner(Build(Corner.FL, 0, -outer, 0, 0, x: wheelbase));
            s = SuspensionMath.Steering(fl, fr, 0);
            Assert.Equal(0, s.AckermannPercent, 6);
        }

        [Fact]
        public void Turning_radius_of_the_outer_wheel()
        {
            // Right turn with the right wheel inner; outer (left) wheel steered -20 deg.
            const double wheelbase = 1.5;
            CornerGeometry fl = SuspensionMath.Corner(Build(Corner.FL, 0, 20, 0, 0, x: wheelbase));
            CornerGeometry fr = SuspensionMath.Corner(Build(Corner.FR, 0, -25, 0, 0, x: wheelbase));
            SteeringGeometry s = SuspensionMath.Steering(fl, fr, 0);
            // Outer axle line meets the rear axle wheelbase / tan(20) to the side; radius = wheelbase / sin(20).
            Assert.Equal(wheelbase / Math.Sin(20 * Rad), s.TurningRadius, 9);
        }

        [Theory]
        [InlineData("HP_UBJ_FL", "UBJ", Corner.FL)]
        [InlineData("hp_wc_rr", "WC", Corner.RR)]
        public void Parses_hardpoint_names(string name, string point, Corner corner)
        {
            Assert.True(Hardpoints.TryParse(name, out string p, out Corner c));
            Assert.Equal(point, p);
            Assert.Equal(corner, c);
            Assert.Equal("HP_UBJ_FL", Hardpoints.Name(Hardpoints.Ubj, Corner.FL));
        }

        [Theory]
        [InlineData("Point1")]
        [InlineData("HP_UBJ")]
        [InlineData("HP_UBJ_XX")]
        [InlineData("HP_UBJ_FL_2")]
        public void Rejects_other_names(string name)
        {
            Assert.False(Hardpoints.TryParse(name, out _, out _));
        }

        [Fact]
        public void Maps_solidworks_axes_to_vehicle_axes()
        {
            VehicleAxes axes = VehicleAxes.SolidWorksDefault;
            Vec3 v = axes.ToVehicle(new Vec3(1, 2, 3)); // model X = left, Y = up, Z = forward
            Assert.Equal(3, v.X, 12);
            Assert.Equal(1, v.Y, 12);
            Assert.Equal(2, v.Z, 12);

            v = new VehicleAxes("-X", "+Z").ToVehicle(new Vec3(1, 2, 3));
            Assert.Equal(-1, v.X, 12);
            Assert.Equal(-2, v.Y, 12); // left = up x forward = Z x -X = -Y
            Assert.Equal(3, v.Z, 12);

            Assert.Throws<ArgumentException>(() => new VehicleAxes("+X", "-X"));
            Assert.Throws<ArgumentException>(() => new VehicleAxes("forward", "+Y"));
        }

        [Fact]
        public void Report_lists_corners_and_turning()
        {
            CornerGeometry fl = SuspensionMath.Corner(Build(Corner.FL, -1, 0, 6, 9, x: 1.4));
            CornerGeometry fr = SuspensionMath.Corner(Build(Corner.FR, -1, 0, 6, 9, x: 1.4));
            string r = SuspensionMath.Report(new[] { fr, fl }, SuspensionMath.Steering(fl, fr, 0));
            Assert.Contains("FL       -1.00    0.00    6.00   9.00", r);
            Assert.True(r.IndexOf("FL", StringComparison.Ordinal) < r.IndexOf("FR", StringComparison.Ordinal));
            Assert.Contains("Front wheels straight", r);
        }
    }
}
