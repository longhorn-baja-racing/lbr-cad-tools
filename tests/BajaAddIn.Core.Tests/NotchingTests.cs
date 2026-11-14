using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class NotchingTests
    {
        private static readonly FrameMember[] Members =
        {
            new FrameMember("LFS[1]", "LFS"),
            new FrameMember("LFS[2]", "LFS"),
            new FrameMember("RRH[1]", "RRH"),
            new FrameMember("SM[1]", "SM"),
            new FrameMember("SM[2]", "SM"),
        };

        private static readonly string[] Order = { "LFS", "RRH", "SM" };

        [Fact]
        public void Tube_ending_on_a_through_tube_is_coped_whatever_the_priority()
        {
            // RRH (higher priority) ends mid-span on SM (lower): RRH must be coped.
            var j = new FrameJoint("RRH[1]", "SM[1]", atEndOfA: true, atEndOfB: false);
            NotchPlan p = Notching.Plan(Members, new[] { j }, Order);
            NotchCut c = Assert.Single(p.Cuts);
            Assert.Equal("RRH[1]", c.Trimmed);
            Assert.Equal("SM[1]", c.Boundary);
            Assert.Equal(NotchReason.EndsOnThroughTube, c.Reason);
        }

        [Fact]
        public void At_a_node_the_lower_priority_tube_is_coped()
        {
            var j = new FrameJoint("SM[1]", "RRH[1]", true, true);
            NotchCut c = Assert.Single(Notching.Plan(Members, new[] { j }, Order).Cuts);
            Assert.Equal("SM[1]", c.Trimmed);
            Assert.Equal(NotchReason.Priority, c.Reason);

            // Reordering the groups flips it.
            c = Assert.Single(Notching.Plan(Members, new[] { j }, new[] { "SM", "LFS", "RRH" }).Cuts);
            Assert.Equal("RRH[1]", c.Trimmed);
        }

        [Fact]
        public void Override_swaps_a_node_but_not_a_through_joint()
        {
            var node = new FrameJoint("SM[1]", "RRH[1]", true, true);
            var through = new FrameJoint("SM[2]", "LFS[1]", true, false);
            NotchPlan p = Notching.Plan(Members, new[] { node, through }, Order, new HashSet<string> { node.Key, through.Key });
            NotchCut n = p.Cuts.Single(c => c.Joint == node);
            Assert.Equal("RRH[1]", n.Trimmed);
            Assert.Equal(NotchReason.Override, n.Reason);
            Assert.Equal("SM[2]", p.Cuts.Single(c => c.Joint == through).Trimmed);
            Assert.Equal(new[] { through.Key }, p.IgnoredOverrides);
        }

        [Fact]
        public void Same_group_joints_and_crossings_are_not_cut()
        {
            var sameGroup = new FrameJoint("LFS[1]", "LFS[2]", true, true);
            var crossing = new FrameJoint("SM[1]", "LFS[1]", false, false);
            NotchPlan p = Notching.Plan(Members, new[] { sameGroup, crossing }, Order);
            Assert.Empty(p.Cuts);
            Assert.Equal(new[] { crossing }, p.Crossings);
        }

        [Fact]
        public void Trim_order_puts_boundaries_first()
        {
            // SM[1] is coped against RRH[1], which is itself coped against LFS[1].
            FrameJoint[] joints =
            {
                new FrameJoint("SM[1]", "RRH[1]", true, false),
                new FrameJoint("RRH[1]", "LFS[1]", true, false),
            };
            NotchPlan p = Notching.Plan(Members, joints, Order);
            Assert.Equal(new[] { "RRH[1]", "SM[1]" }, p.TrimOrder.Select(t => t.Key));
            Assert.Equal(new[] { "LFS[1]" }, p.TrimOrder[0].Value);
        }

        [Fact]
        public void Trim_order_survives_a_cycle()
        {
            // Two tubes coped against each other at opposite ends.
            FrameJoint[] joints =
            {
                new FrameJoint("SM[1]", "RRH[1]", true, false),
                new FrameJoint("RRH[1]", "SM[1]", true, false),
            };
            NotchPlan p = Notching.Plan(Members, joints, Order);
            Assert.Equal(2, p.TrimOrder.Count);
            Assert.Equal("RRH[1]", p.TrimOrder[0].Key); // higher priority first when nothing is ready
        }

        [Fact]
        public void Default_order_uses_baja_member_names_then_tree_order()
        {
            List<string> order = Notching.DefaultOrder(new[] { "Gussets", "SM bracing", "RRH", "Floor Rails", "Cage" });
            Assert.Equal(new[] { "Floor Rails", "RRH", "SM bracing", "Gussets", "Cage" }, order);
        }

        [Fact]
        public void Saved_order_keeps_its_place_and_new_groups_are_added()
        {
            List<string> order = Notching.MergeOrder(new[] { "SM", "Gone", "RRH" }, new[] { "RRH", "SM", "LFS" });
            Assert.Equal(new[] { "SM", "RRH", "LFS" }, order);
        }

        [Fact]
        public void Properties_round_trip()
        {
            Assert.Equal(new[] { "A", "B C" }, Notching.SplitOrder(Notching.JoinOrder(new[] { "A", "B C" })));
            var j = new FrameJoint("Z[1]", "A[2]", true, true);
            Assert.Equal("A[2]|Z[1]", j.Key);
            Assert.Contains(j.Key, Notching.SplitSwaps(Notching.JoinSwaps(new[] { j.Key, "x|y" })));
        }

        [Theory]
        [InlineData(0, 1, 0.0001, 0.05, true)]  // overlap at the low end
        [InlineData(0, 1, 0.95, 0.9999, true)]  // overlap at the high end
        [InlineData(0, 1, 0.4, 0.5, false)]     // overlap in the middle
        public void Reaches_end(double tMin, double tMax, double oMin, double oMax, bool expected)
        {
            Assert.Equal(expected, Notching.ReachesEnd(tMin, tMax, oMin, oMax, 0.0005));
        }
    }
}
