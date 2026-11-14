using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class DrawingLayoutTests
    {
        // ANSI B (17 x 11 in) in meters.
        private const double W = 0.4318, H = 0.2794;

        [Fact]
        public void Small_part_is_drawn_larger_than_full_size()
        {
            DrawingPlan p = DrawingLayout.Plan(0.03, 0.02, 0.01, W, H);
            Assert.True(p.Scale > 1, p.ScaleText);
        }

        [Fact]
        public void Large_part_is_scaled_down_to_a_standard_scale()
        {
            DrawingPlan p = DrawingLayout.Plan(1.2, 0.8, 0.6, W, H); // a frame-sized model
            Assert.True(p.Scale < 1);
            Assert.Contains(DrawingLayout.StandardScales, s => s.Num == p.Numerator && s.Den == p.Denominator);
        }

        [Theory]
        [InlineData(0.03, 0.02, 0.01)]
        [InlineData(0.3, 0.05, 0.1)]
        [InlineData(1.2, 0.8, 0.6)]
        [InlineData(0.05, 0.4, 0.05)]
        public void Views_stay_on_the_sheet_and_above_the_title_block(double x, double y, double z)
        {
            DrawingPlan p = DrawingLayout.Plan(x, y, z, W, H);
            double k = p.Scale, iso = new[] { x, y, z }.Max();
            ViewSpot front = p.Views.Single(v => v.Orientation == "*Front");
            ViewSpot top = p.Views.Single(v => v.Orientation == "*Top");
            ViewSpot right = p.Views.Single(v => v.Orientation == "*Right");
            ViewSpot isoView = p.Views.Single(v => v.Orientation == "*Isometric");

            Assert.True(front.Y - k * y / 2 >= H * 0.2, "front above the title block");
            Assert.True(top.Y + k * z / 2 <= H, "top view on the sheet");
            Assert.True(right.X + k * z / 2 <= W, "right view on the sheet");
            Assert.True(isoView.X + k * iso / 2 <= W + 1e-9, "iso on the sheet");
            Assert.Equal(front.X, top.X, 9);   // third angle: top straight above front
            Assert.Equal(front.Y, right.Y, 9); // right straight beside front
            Assert.True(top.Y > front.Y && right.X > front.X);
        }

        [Theory]
        [InlineData("CHA_0002_FrontHoop_REV01_V01", "dCHA_0002_FrontHoop_REV01_V01")]
        [InlineData("aERG_0001_PedalBox_REV02_V03", "dERG_0001_PedalBox_REV02_V03")]
        [InlineData("POW_0012_CVTEngine_REVSTD", "dPOW_0012_CVTEngine_REVSTD")]
        [InlineData("Bracket", "Bracket")]
        public void Drawing_name_follows_the_convention(string model, string drawing)
        {
            Assert.Equal(drawing, DrawingLayout.DrawingName(model));
        }

        [Fact]
        public void Title_properties_come_from_the_name()
        {
            var p = DrawingLayout.TitleProperties("aDYN_0006_SuspensionControlArmLeft_REV01_V02");
            Assert.Equal("DYN_0006", p["PartNo"]);
            Assert.Equal("Suspension Control Arm Left", p["Description"]);
            Assert.Equal("REV01", p["Revision"]);
            Assert.Equal("V02", p["Version"]);
            Assert.Equal("CVT Engine", DrawingLayout.SplitWords("CVTEngine"));
            Assert.Equal("Bracket", DrawingLayout.TitleProperties("Bracket")["PartNo"]);
        }
    }
}
