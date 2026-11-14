using System;
using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class TabPlacementTests
    {
        private const double In = Units.MetersPerInch;

        // A 1 in tube along assembly X, axis through (0, 0, 0), clicked on top.
        private static readonly TabMount TopOfTube =
            TabMount.Round(new Vec3(0.2, 0.5 * In, 0), new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0.5 * In);

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.True((expected - actual).Length < 1e-9, $"expected {expected.X}, {expected.Y}, {expected.Z} got {actual.X}, {actual.Y}, {actual.Z}");
        }

        private static TabInputs Single()
        {
            TabInputs t = TabInputs.Defaults(TabUse.Seat);
            t.DoubleShear = false;
            t.TubeDiameter = 1 * In;
            return t;
        }

        [Fact]
        public void Along_the_tube_the_base_sits_on_top_and_runs_with_the_axis()
        {
            TabInputs t = Single();
            TabFrame f = TabPlacement.Place(t, TopOfTube, TabOrientation.AlongTube);
            Near(new Vec3(1, 0, 0), f.X);
            Near(new Vec3(0, 1, 0), f.Y);
            Near(new Vec3(0, 0, 1), f.Z);
            // Base middle on the tube top under the click, plate centered on it.
            Near(new Vec3(0.2, 0.5 * In, -t.Thickness / 2), f.Origin);
            // Hole center (part 0, L, t/2) is straight above the click.
            Near(new Vec3(0.2, 0.5 * In + t.Length, 0), f.ToAssembly(new Vec3(0, t.Length, t.Thickness / 2)));
        }

        [Fact]
        public void Across_the_tube_the_plate_is_square_to_the_axis()
        {
            TabInputs t = Single();
            TabFrame f = TabPlacement.Place(t, TopOfTube, TabOrientation.AcrossTube);
            Near(new Vec3(1, 0, 0), f.Z);
            Near(new Vec3(0, 1, 0), f.Y);
            Near(f.X.Cross(f.Y), f.Z);
        }

        [Fact]
        public void Contoured_base_forces_across_and_puts_the_arc_center_on_the_axis()
        {
            TabInputs t = Single();
            t.ContourTubeDiameter = t.TubeDiameter;
            Assert.Equal(TabOrientation.AcrossTube, TabPlacement.Effective(t, TopOfTube, TabOrientation.AlongTube));
            TabFrame f = TabPlacement.Place(t, TopOfTube, TabOrientation.AlongTube);
            double rt = t.ContourTubeDiameter / 2, b = t.BaseWidth / 2;
            double c = Math.Sqrt(rt * rt - b * b);
            // Part point (0, -c) is the arc center; it must land on the tube axis.
            Vec3 center = f.ToAssembly(new Vec3(0, -c, 0));
            Assert.True(Math.Abs(center.Y) < 1e-9 && Math.Abs(center.Z) < 1e-9);
            // Base corners touch the tube surface.
            Vec3 corner = f.ToAssembly(new Vec3(b, 0, 0));
            Assert.Equal(rt, Math.Sqrt(corner.Y * corner.Y + corner.Z * corner.Z), 9);
        }

        [Fact]
        public void Double_shear_spaced_by_the_tube_straddles_it()
        {
            TabInputs t = TabInputs.Defaults(TabUse.LapBelt);
            t.TubeDiameter = 1 * In;
            t.GapFromTube = true;
            Assert.True(TabPlacement.Straddles(t, TopOfTube, TabOrientation.AlongTube));
            TabFrame f = TabPlacement.Place(t, TopOfTube, TabOrientation.AlongTube);
            // Base line level with the axis; inner faces of the plates tangent to the tube sides.
            Vec3 innerFirst = f.ToAssembly(new Vec3(0, 0, t.Thickness));
            Vec3 innerSecond = f.ToAssembly(new Vec3(0, 0, t.Thickness + t.TabGap));
            Assert.Equal(0, innerFirst.Y, 9);
            Assert.Equal(0.5 * In, Math.Abs(innerFirst.Z), 9);
            Assert.Equal(0.5 * In, Math.Abs(innerSecond.Z), 9);
            Assert.True(innerFirst.Z * innerSecond.Z < 0, "plates on opposite sides");
        }

        [Fact]
        public void Flat_face_uses_the_normal_and_the_edge_direction()
        {
            TabInputs t = Single();
            var m = TabMount.Flat(new Vec3(0.1, 0.2, 0.3), new Vec3(0, 0, 2), new Vec3(0, 1, 0.5));
            TabFrame f = TabPlacement.Place(t, m, TabOrientation.AlongTube);
            Near(new Vec3(0, 0, 1), f.Y);
            Near(new Vec3(0, 1, 0), f.X);
            Near(f.X.Cross(f.Y), f.Z);
            Near(new Vec3(0.1, 0.2, 0.3) - f.Z * (t.Thickness / 2), f.Origin);
        }

        [Fact]
        public void Transform_rows_are_the_part_axes_then_translation()
        {
            var f = new TabFrame(new Vec3(1, 2, 3), new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
            Assert.Equal(new double[] { 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 2, 3, 1, 0, 0, 0 }, f.ToSolidWorks());
        }

        [Fact]
        public void File_name_takes_the_first_free_number()
        {
            Assert.Equal("Tab-Seat-3", TabPlacement.FileName(TabUse.Seat, "Pedal Box", new string[0], n => n == "Tab-Seat-1" || n == "Tab-Seat-2"));
        }

        [Fact]
        public void File_name_follows_the_team_convention_of_the_assembly()
        {
            string[] known = { "aERG_0001_PedalBox_REV02_V03", "ERG_0004_Pedal_REV02_V01", "CHA_0050_Frame_REV02_V01" };
            Assert.Equal("ERG_0005_SeatTab_REV02_V01", TabPlacement.FileName(TabUse.Seat, "aERG_0001_PedalBox_REV02_V03", known, n => false));
            // A file already on disk with that name moves to the next number.
            Assert.Equal("ERG_0006_LapBeltTab_REV02_V01",
                TabPlacement.FileName(TabUse.LapBelt, "aERG_0001_PedalBox_REV02_V03", known, n => n == "ERG_0005_LapBeltTab_REV02_V01"));
            Assert.True(NamingConvention.CheckOne(new NamedFile("ERG_0005_SeatTab_REV02_V01", CadFileKind.Part), NamingConvention.DefaultTeams.Keys.ToList()).Ok);
        }

        [Fact]
        public void Pick_distance_from_axis_is_the_radius_on_the_surface()
        {
            Assert.Equal(0.5 * In, TopOfTube.PickDistanceFromAxis(), 12);
        }
    }
}
