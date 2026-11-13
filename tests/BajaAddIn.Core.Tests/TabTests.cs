using System;
using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class TabTests
    {
        private const double In = Units.MetersPerInch;

        [Fact]
        public void Lap_belt_defaults_follow_the_table_and_pass()
        {
            TabInputs t = TabInputs.Defaults(TabUse.LapBelt);
            Assert.Equal(7.0 / 16 * In, t.Bolt, 12);
            Assert.Equal((7.0 / 16 + 1.0 / 64) * In, t.Hole, 12);
            Assert.Equal(2.2 * t.Bolt, t.EndDiameter, 12);
            Assert.Equal(3 * t.Bolt, t.BaseWidth, 12);
            Assert.Equal(1.0 * In, t.Length, 12);
            Assert.Equal(0.090 * In, t.Thickness, 12);
            Assert.True(t.DoubleShear);
            Assert.Empty(TabRules.Check(t));
        }

        [Theory]
        [InlineData(TabUse.AntiSubBelt)]
        [InlineData(TabUse.Seat)]
        [InlineData(TabUse.Custom)]
        public void Defaults_pass_for_presets_with_a_bolt_size(TabUse use)
        {
            Assert.Empty(TabRules.Check(TabInputs.Defaults(use)));
        }

        [Theory]
        [InlineData(TabUse.FireExtinguisher)]
        [InlineData(TabUse.FuelTank)]
        public void Check_bracket_presets_only_ask_to_check_the_bolt(TabUse use)
        {
            TabIssue issue = Assert.Single(TabRules.Check(TabInputs.Defaults(use)));
            Assert.Equal(TabIssueLevel.Warning, issue.Level);
            Assert.Contains("check it", issue.Message);
        }

        [Fact]
        public void Each_rule_warns()
        {
            TabInputs t = TabInputs.Defaults(TabUse.Seat);
            t.Bolt = 0.2 * In;
            t.Hole = 0.3 * In;
            t.EndDiameter = 0.4 * In;
            t.BaseWidth = 0.5 * In;
            t.Length = 2.5 * In;
            t.Thickness = 0.1 * In;
            List<string> messages = TabRules.Check(t).Select(i => i.Message).ToList();
            Assert.All(TabRules.Check(t), i => Assert.Equal(TabIssueLevel.Warning, i.Level));
            Assert.Contains(messages, m => m.Contains("at least 0.25 in (D1)"));
            Assert.Contains(messages, m => m.Contains("B.12.2.1"));
            Assert.Contains(messages, m => m.Contains("2.2 x D1"));
            Assert.Contains(messages, m => m.Contains("3 x D1"));
            Assert.Contains(messages, m => m.Contains("at most 2 in long"));
            Assert.Contains(messages, m => m.Contains("0.125 in thick"));
        }

        [Fact]
        public void Single_shear_lap_belt_warns()
        {
            TabInputs t = TabInputs.Defaults(TabUse.LapBelt);
            t.DoubleShear = false;
            Assert.Contains(TabRules.Check(t), i => i.Message.Contains("double shear"));
        }

        [Fact]
        public void Custom_skips_rule_checks_but_not_geometry()
        {
            TabInputs t = TabInputs.Defaults(TabUse.Custom);
            t.Thickness = 0.01 * In;
            Assert.Empty(TabRules.Check(t));
            t.Length = t.EndDiameter / 2;
            Assert.Equal(TabIssueLevel.Error, Assert.Single(TabRules.Check(t)).Level);
        }

        [Fact]
        public void Geometry_errors()
        {
            TabInputs t = TabInputs.Defaults(TabUse.Seat);
            t.Hole = t.EndDiameter;
            Assert.Contains(TabRules.Check(t), i => i.Level == TabIssueLevel.Error && i.Message.Contains("hole must be smaller"));

            t = TabInputs.Defaults(TabUse.Seat);
            t.Length = t.EndDiameter / 2;
            Assert.Contains(TabRules.Check(t), i => i.Level == TabIssueLevel.Error && i.Message.Contains("runs into the base"));

            t = TabInputs.Defaults(TabUse.LapBelt);
            t.ContourTubeDiameter = 1.25 * In; // narrower than B = 1.3125 in
            Assert.Contains(TabRules.Check(t), i => i.Level == TabIssueLevel.Error && i.Message.Contains("narrower"));

            t = TabInputs.Defaults(TabUse.Seat);
            t.ContourTubeDiameter = 0.5 * In;
            Assert.Contains(TabRules.Check(t), i => i.Level == TabIssueLevel.Error && i.Message.Contains("narrower"));

            t = TabInputs.Defaults(TabUse.Custom);
            t.Bolt = 0;
            Assert.Contains(TabRules.Check(t), i => i.Message.Contains("greater than zero"));
        }

        [Fact]
        public void Contoured_base_to_a_1_5_in_tube_is_fine()
        {
            TabInputs t = TabInputs.Defaults(TabUse.LapBelt);
            t.ContourTubeDiameter = 1.5 * In;
            Assert.Empty(TabRules.Check(t));
            double r = 0.75 * In, b = t.BaseWidth / 2;
            Assert.Equal(r - Math.Sqrt(r * r - b * b), TabGeometry.ContourDepth(t), 12);
        }

        [Theory]
        [InlineData(TabUse.LapBelt, 0.0)]
        [InlineData(TabUse.Seat, 0.0)]
        [InlineData(TabUse.LapBelt, 1.5)]
        public void Tangent_point_is_on_the_circle_and_tangent(TabUse use, double tubeIn)
        {
            TabInputs t = TabInputs.Defaults(use);
            t.ContourTubeDiameter = tubeIn * In;
            (double x, double y) = TabGeometry.RightTangentPoint(t);
            double r = t.EndDiameter / 2;
            // On the circle.
            Assert.Equal(r, Math.Sqrt(x * x + (y - t.Length) * (y - t.Length)), 12);
            // Radius is perpendicular to the side line from the corner.
            double dot = x * (x - t.BaseWidth / 2) + (y - t.Length) * y;
            Assert.Equal(0, dot, 12);
            // The outer one.
            Assert.True(x > 0);
        }

        [Theory]
        [InlineData(TabUse.LapBelt, 0.0)]
        [InlineData(TabUse.FireExtinguisher, 0.0)]
        [InlineData(TabUse.LapBelt, 1.5)]
        public void Area_matches_a_sampled_outline(TabUse use, double tubeIn)
        {
            TabInputs t = TabInputs.Defaults(use);
            t.ContourTubeDiameter = tubeIn * In;
            double expected = SampledArea(t);
            Assert.Equal(expected, TabGeometry.Area(t), 9);
        }

        /// <summary>Shoelace area of a finely sampled outline, minus the hole.</summary>
        private static double SampledArea(TabInputs t)
        {
            const int n = 20000;
            double b = t.BaseWidth / 2, r = t.EndDiameter / 2, l = t.Length;
            (double tx, double ty) = TabGeometry.RightTangentPoint(t);
            var pts = new List<(double, double)>();

            // Base, left to right: straight or the tube arc (cut upward).
            if (t.ContourTubeDiameter > 0)
            {
                double rt = t.ContourTubeDiameter / 2, c = Math.Sqrt(rt * rt - b * b);
                double a0 = Math.Atan2(c, -b), a1 = Math.Atan2(c, b);
                for (int i = 0; i <= n; i++)
                {
                    double a = a0 + (a1 - a0) * i / n;
                    pts.Add((rt * Math.Cos(a), rt * Math.Sin(a) - c));
                }
            }
            else
            {
                pts.Add((-b, 0));
                pts.Add((b, 0));
            }

            // Right side up to the tangent point, then over the top to the left one.
            double start = Math.Atan2(ty - l, tx), end = Math.PI - start;
            for (int i = 0; i <= n; i++)
            {
                double a = start + (end - start) * i / n;
                pts.Add((r * Math.Cos(a), l + r * Math.Sin(a)));
            }

            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                (double x1, double y1) = pts[i];
                (double x2, double y2) = pts[(i + 1) % pts.Count];
                area += x1 * y2 - x2 * y1;
            }
            return Math.Abs(area) / 2 - Math.PI * t.Hole * t.Hole / 4;
        }

        [Fact]
        public void Describe()
        {
            Assert.Equal("Lap belt tab, double shear, 0.438 in bolt, 0.09 in thick",
                TabRules.Describe(TabInputs.Defaults(TabUse.LapBelt)));
        }
    }
}
