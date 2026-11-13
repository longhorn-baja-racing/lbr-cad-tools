using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class PointCsvTests
    {
        private const double Tol = 1e-12;

        private static void AssertPoint(Point3 p, double x, double y, double z)
        {
            Assert.Equal(x, p.X, Tol);
            Assert.Equal(y, p.Y, Tol);
            Assert.Equal(z, p.Z, Tol);
        }

        [Fact]
        public void Reads_plain_rows_in_millimeters()
        {
            PointCsvReadResult r = PointCsv.Read("1,2,3\n4,5,6\n", LengthUnit.Millimeter);
            Assert.Empty(r.Errors);
            Assert.False(r.HasHeader);
            Assert.Equal(2, r.Points.Count);
            AssertPoint(r.Points[0], 0.001, 0.002, 0.003);
            AssertPoint(r.Points[1], 0.004, 0.005, 0.006);
        }

        [Fact]
        public void Converts_inches()
        {
            PointCsvReadResult r = PointCsv.Read("1,0,-2", LengthUnit.Inch);
            AssertPoint(r.Points.Single(), 0.0254, 0, -0.0508);
        }

        [Fact]
        public void Two_columns_default_z_to_zero()
        {
            PointCsvReadResult r = PointCsv.Read("10,20", LengthUnit.Meter);
            AssertPoint(r.Points.Single(), 10, 20, 0);
        }

        [Fact]
        public void Skips_header_blank_and_comment_lines_and_handles_crlf()
        {
            PointCsvReadResult r = PointCsv.Read("# exported\r\nX,Y,Z\r\n\r\n1,2,3\r\n   \r\n", LengthUnit.Meter);
            Assert.Empty(r.Errors);
            Assert.True(r.HasHeader);
            AssertPoint(r.Points.Single(), 1, 2, 3);
        }

        [Fact]
        public void Header_names_pick_columns()
        {
            string csv = "Name,Z,Note,X,Y\nA,3,front,1,2\nB,6,rear,4,5\n";
            PointCsvReadResult r = PointCsv.Read(csv, LengthUnit.Meter);
            Assert.Empty(r.Errors);
            AssertPoint(r.Points[0], 1, 2, 3);
            AssertPoint(r.Points[1], 4, 5, 6);
        }

        [Theory]
        [InlineData("1;2;3")]
        [InlineData("1\t2\t3")]
        [InlineData("  1   2  3 ")]
        [InlineData("\"1\", \"2\" ,\"3\"")]
        [InlineData("1e0,2.0E+0,0.3e1")]
        public void Accepts_other_separators_quotes_and_exponents(string line)
        {
            PointCsvReadResult r = PointCsv.Read(line, LengthUnit.Meter);
            Assert.Empty(r.Errors);
            AssertPoint(r.Points.Single(), 1, 2, 3);
        }

        [Fact]
        public void Reports_bad_rows_with_line_numbers_and_keeps_good_ones()
        {
            PointCsvReadResult r = PointCsv.Read("1,2,3\n1,abc,3\n1\n1,2,3,4\n", LengthUnit.Meter);
            Assert.Single(r.Points);
            Assert.Equal(3, r.Errors.Count);
            Assert.StartsWith("Line 2:", r.Errors[0]);
            Assert.Contains("'abc'", r.Errors[0]);
            Assert.StartsWith("Line 3:", r.Errors[1]);
            Assert.StartsWith("Line 4:", r.Errors[2]);
        }

        [Fact]
        public void Named_header_allows_extra_columns_but_needs_the_named_ones()
        {
            PointCsvReadResult r = PointCsv.Read("X,Y,Z,Name\n1,2,3,A\n1,2\n", LengthUnit.Meter);
            AssertPoint(r.Points.Single(), 1, 2, 3);
            Assert.StartsWith("Line 3:", r.Errors.Single());
        }

        [Fact]
        public void Only_the_first_line_can_be_a_header()
        {
            PointCsvReadResult r = PointCsv.Read("1,2,3\nX,Y,Z\n", LengthUnit.Meter);
            Assert.Single(r.Points);
            Assert.Single(r.Errors);
        }

        [Fact]
        public void Empty_input_gives_no_points_and_no_errors()
        {
            PointCsvReadResult r = PointCsv.Read("", LengthUnit.Meter);
            Assert.Empty(r.Points);
            Assert.Empty(r.Errors);
            Assert.Empty(PointCsv.Read(null, LengthUnit.Meter).Points);
        }

        [Theory]
        [InlineData("X (mm),Y (mm),Z (mm)\n1,2,3", LengthUnit.Millimeter)]
        [InlineData("x [in], y [in], z [in]", LengthUnit.Inch)]
        [InlineData("X (inches),Y (inches)", LengthUnit.Inch)]
        [InlineData("# note\nX (m),Y (m),Z (m)", LengthUnit.Meter)]
        public void Detects_unit_from_header(string csv, LengthUnit expected)
        {
            Assert.Equal(expected, PointCsv.DetectUnit(csv));
        }

        [Theory]
        [InlineData("1,2,3")]
        [InlineData("X,Y,Z")]
        [InlineData("X (mm),Y (in),Z (mm)")]
        [InlineData("")]
        public void No_unit_when_missing_or_mixed(string csv)
        {
            Assert.Null(PointCsv.DetectUnit(csv));
        }

        [Fact]
        public void Writes_header_and_rows_in_unit()
        {
            string csv = PointCsv.Write(new[] { new Point3(0.001, -0.0025, 0), new Point3(1, 0, -0.0) }, LengthUnit.Millimeter);
            Assert.Equal("X (mm),Y (mm),Z (mm)\r\n1,-2.5,0\r\n1000,0,0\r\n", csv);
        }

        [Fact]
        public void Write_then_read_round_trips()
        {
            var points = new[] { new Point3(0.123456789, -0.5, 2.25), new Point3(-1e-6, 0, 0.0254) };
            foreach (LengthUnit unit in new[] { LengthUnit.Meter, LengthUnit.Millimeter, LengthUnit.Inch })
            {
                string csv = PointCsv.Write(points, unit);
                Assert.Equal(unit, PointCsv.DetectUnit(csv));
                PointCsvReadResult r = PointCsv.Read(csv, unit);
                Assert.Empty(r.Errors);
                Assert.Equal(2, r.Points.Count);
                for (int i = 0; i < points.Length; i++)
                {
                    Assert.Equal(points[i].X, r.Points[i].X, 1e-9);
                    Assert.Equal(points[i].Y, r.Points[i].Y, 1e-9);
                    Assert.Equal(points[i].Z, r.Points[i].Z, 1e-9);
                }
            }
        }
    }
}
