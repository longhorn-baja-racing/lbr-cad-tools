using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Notcher
{
    /// <summary>
    /// Tube Notcher options (issue #11): member group priority, the planned cut
    /// at every joint with a per-joint swap, and warnings. The plan updates as
    /// the order or swaps change.
    /// </summary>
    internal sealed class NotcherDialog : ToolForm
    {
        private readonly FrameNotcher.Frame frame;
        private readonly ListBox groups = new ListBox { IntegralHeight = false };
        private readonly ListView cuts;
        private readonly Label status;
        private readonly Button notch;
        private readonly HashSet<string> swaps;
        private bool filling;

        public NotcherDialog(FrameNotcher.Frame frame, List<string> order, HashSet<string> swaps)
            : base("Tube Notcher",
                "Copes every tube end to fit the tubes it meets, in the order you set. " +
                "Run it again after changing the frame; it replaces its own cuts.",
                ToolIcon.Notcher, ToolGroup.VehicleDesign, 640)
        {
            this.frame = frame;
            this.swaps = new HashSet<string>(swaps, StringComparer.Ordinal);
            SuspendLayout();

            // ---- Priority ----
            groups.Size = new Size(300, 150);
            groups.Margin = new Padding(0, 4, 10, 0);
            foreach (string g in order)
            {
                groups.Items.Add(g);
            }
            Button up = SmallButton("Move up");
            Button down = SmallButton("Move down");
            Button reset = SmallButton("Default order");
            up.Click += (s, e) => MoveGroup(-1);
            down.Click += (s, e) => MoveGroup(1);
            reset.Click += (s, e) =>
            {
                groups.Items.Clear();
                foreach (string g in Notching.DefaultOrder(frame.Groups))
                {
                    groups.Items.Add(g);
                }
                Replan();
            };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            buttons.Controls.AddRange(new Control[] { up, down, reset });
            Label orderNote = Note(
                "Top = most important; it stays whole. Where two tubes both end at the same node, the lower one is coped around the higher one.\n\n" +
                "A tube that ends on the side of another tube is always the one coped, whatever the order.", ContentWidth - 300 - 10 - 130);
            orderNote.Margin = new Padding(12, 6, 0, 0);
            AddSection("Priority (one row per structural member feature)", null, Row(groups, buttons, orderNote));

            // ---- Joints ----
            cuts = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Size = new Size(ContentWidth, 210),
                Margin = new Padding(0, 4, 0, 0),
            };
            cuts.Columns.Add("Swap", 52);
            cuts.Columns.Add("Coped tube", 180);
            cuts.Columns.Add("Cut around", 180);
            cuts.Columns.Add("Why", ContentWidth - 52 - 360 - 24);
            cuts.ItemCheck += OnSwap;
            AddSection("Cuts",
                "Tick Swap to flip one node the other way round (only where both tubes end at the node).",
                cuts);

            status = StatusBox(ContentWidth);
            AddSection("Check", null, status);

            notch = AddButton("Notch", DialogResult.OK, primary: true);
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = notch;
            CancelButton = cancel;

            groups.SelectedIndex = groups.Items.Count > 0 ? 0 : -1;
            Replan();
            ResumeLayout(true);
        }

        /// <summary>Group order chosen, highest first.</summary>
        public List<string> Order => groups.Items.Cast<string>().ToList();

        /// <summary>Joint keys whose node decision is swapped.</summary>
        public HashSet<string> Swaps => swaps;

        /// <summary>The plan for <see cref="Order"/> and <see cref="Swaps"/>.</summary>
        public NotchPlan Plan { get; private set; }

        private static Button SmallButton(string text) => new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(120, 28),
            Margin = new Padding(0, 0, 0, 6),
        };

        private void MoveGroup(int step)
        {
            int i = groups.SelectedIndex;
            int j = i + step;
            if (i < 0 || j < 0 || j >= groups.Items.Count)
            {
                return;
            }
            object item = groups.Items[i];
            groups.Items.RemoveAt(i);
            groups.Items.Insert(j, item);
            groups.SelectedIndex = j;
            Replan();
        }

        private void OnSwap(object sender, ItemCheckEventArgs e)
        {
            if (filling)
            {
                return;
            }
            var cut = (NotchCut)cuts.Items[e.Index].Tag;
            if (cut.Reason == NotchReason.EndsOnThroughTube)
            {
                e.NewValue = CheckState.Unchecked; // a through tube cannot be coped
                return;
            }
            if (e.NewValue == CheckState.Checked)
            {
                swaps.Add(cut.Joint.Key);
            }
            else
            {
                swaps.Remove(cut.Joint.Key);
            }
            BeginInvoke(new Action(Replan));
        }

        private void Replan()
        {
            // Drop swaps for joints that no longer exist so they do not pile up in the part.
            var keys = new HashSet<string>(frame.Joints.Select(j => j.Key), StringComparer.Ordinal);
            swaps.RemoveWhere(k => !keys.Contains(k));
            Plan = Notching.Plan(frame.Members, frame.Joints, Order, swaps);

            filling = true;
            cuts.BeginUpdate();
            cuts.Items.Clear();
            foreach (NotchCut c in Plan.Cuts.OrderBy(c => c.Trimmed, StringComparer.Ordinal))
            {
                string why = c.Reason == NotchReason.EndsOnThroughTube
                    ? "Ends on the side of it"
                    : c.Reason == NotchReason.Override ? "Node, swapped by hand" : "Node: lower priority";
                var item = new ListViewItem(new[] { "", c.Trimmed, c.Boundary, why })
                {
                    Tag = c,
                    Checked = c.Reason == NotchReason.Override,
                    ForeColor = c.Reason == NotchReason.EndsOnThroughTube ? Muted : Brand.Ink,
                };
                cuts.Items.Add(item);
            }
            cuts.EndUpdate();
            filling = false;

            var lines = new List<string>
            {
                $"{frame.Members.Count} tubes in {frame.Groups.Count} groups, {frame.Joints.Count} joints between groups: " +
                $"{Plan.Cuts.Count} cuts in {Plan.TrimOrder.Count} trim feature(s).",
            };
            if (frame.OldNotches > 0)
            {
                lines.Add($"Replaces the {frame.OldNotches} notch feature(s) from the last run.");
            }
            if (Plan.Crossings.Count > 0)
            {
                lines.Add($"Not cut, the tubes cross with neither ending there: " +
                          string.Join(", ", Plan.Crossings.Select(j => j.A + " / " + j.B)) + ". Split one of them at the crossing if it should be notched.");
            }
            if (frame.OtherTrims > 0)
            {
                lines.Add($"The part has {frame.OtherTrims} Trim/Extend feature(s) made by hand. They stay; delete any that trim the same joints.");
            }
            if (frame.Skipped.Count > 0)
            {
                lines.Add("Skipped (not a round or rectangular tube): " + string.Join(", ", frame.Skipped) + ".");
            }
            bool warn = Plan.Crossings.Count > 0 || frame.OtherTrims > 0 || frame.Skipped.Count > 0;
            SetStatus(status, string.Join("\n", lines), warn ? Caution : Good, warn ? CautionBack : GoodBack);
            notch.Enabled = Plan.TrimOrder.Count > 0;
        }
    }
}
