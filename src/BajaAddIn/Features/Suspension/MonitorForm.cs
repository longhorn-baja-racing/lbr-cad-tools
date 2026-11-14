using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Suspension
{
    /// <summary>
    /// Modeless window showing the live suspension numbers: a card per corner,
    /// a steering card, and the change from a baseline when one is set.
    /// </summary>
    internal sealed class MonitorForm : ToolForm
    {
        private readonly SuspensionMonitor monitor;
        private readonly Cards cards;
        private readonly Label status;
        private readonly Button baseline;
        private MonitorReading reference;

        public MonitorForm(SuspensionMonitor monitor)
            : this(monitor.Title, monitor)
        {
        }

        /// <summary>The window without a model behind it (previews); <paramref name="monitor"/> may be null.</summary>
        internal MonitorForm(string title, SuspensionMonitor monitor)
            : base("Suspension Monitor",
                "Updates as you drag, steer, or rebuild " + title + ". Set a baseline to see how much each number changes.",
                ToolIcon.SuspensionMonitor, ToolGroup.VehicleDesign, 620)
        {
            this.monitor = monitor;
            SuspendLayout();
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            cards = new Cards { Size = new Size(ContentWidth, 330), Margin = new Padding(0, 8, 0, 0) };
            Body.Controls.Add(cards);
            status = Note("", ContentWidth);
            status.Margin = new Padding(0, 8, 0, 0);
            Body.Controls.Add(status);

            baseline = AddButton("Set baseline", left: true);
            baseline.Click += (s, e) =>
            {
                SetBaseline(reference == null ? Current : null);
            };
            Button find = AddButton("Find points again", left: true);
            find.Click += (s, e) =>
            {
                monitor?.FindPoints();
                monitor?.Update();
            };
            Button copy = AddButton("Copy", left: true);
            copy.Click += (s, e) => Clipboard.SetText(ReportText(Current));
            Button close = AddButton("Close", DialogResult.Cancel, primary: true);
            close.Click += (s, e) => Close();
            CancelButton = close;

            if (monitor != null)
            {
                monitor.Updated += ShowReading;
                monitor.Lost += () =>
                {
                    status.Text = "Stopped: the document closed or could not be read. Open the monitor again.";
                    status.ForeColor = Caution;
                };
            }
            ResumeLayout(true);
        }

        /// <summary>The reading on screen.</summary>
        private MonitorReading Current { get; set; }

        /// <summary>Changes are shown against <paramref name="r"/>; null clears the baseline.</summary>
        internal void SetBaseline(MonitorReading r)
        {
            reference = r;
            baseline.Text = reference == null ? "Set baseline" : "Clear baseline";
            ShowReading(Current);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (monitor != null)
            {
                monitor.Updated -= ShowReading;
                monitor.Dispose();
            }
            base.OnFormClosed(e);
        }

        public void ShowReading(MonitorReading r)
        {
            if (r == null || IsDisposed)
            {
                return;
            }
            Current = r;
            cards.Show(r, reference);
            string when = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string problems = r.Problems.Count == 0 ? "" : "  " + string.Join(" ", r.Problems.Take(3)) + (r.Problems.Count > 3 ? " ..." : "");
            status.Text = r.Corners.Count == 0
                ? $"No complete corner among {r.PointCount} HP_ point(s).{problems} Add points named like HP_UBJ_FL, then Find points again."
                : $"Live: {r.PointCount} HP_ points, updated {when} ({r.Took.TotalMilliseconds:0} ms).{problems}";
            status.ForeColor = r.Problems.Count == 0 ? Muted : Caution;
        }

        private static string ReportText(MonitorReading r)
        {
            if (r == null)
            {
                return "";
            }
            var sb = new StringBuilder();
            sb.AppendLine(SuspensionMath.Report(r.Corners, r.Steering));
            return sb.ToString();
        }

        /// <summary>Painted cards: one per corner in car layout (fronts on top), then steering.</summary>
        private sealed class Cards : Control
        {
            private MonitorReading reading;
            private MonitorReading reference;

            public Cards()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
            }

            // Layout at 96 dpi: card title, 7 rows, padding; steering card with 3 rows.
            private const int CardHeight = 30 + 7 * 15 + 10;
            private const int SteeringHeight = 30 + 3 * 15 + 10;
            private const int Gap = 10;

            /// <summary>The rear row shows only when a rear corner is fully modeled.</summary>
            private bool ShowRears => reading != null && reading.Corners.Any(c => c.Corner == Corner.RL || c.Corner == Corner.RR);

            public void Show(MonitorReading r, MonitorReading baseline)
            {
                reading = r;
                reference = baseline;
                float k = DeviceDpi / 96f;
                int rows = ShowRears ? 2 : 1;
                int h = (int)((rows * (CardHeight + Gap) + SteeringHeight + 1) * k);
                if (Height != h)
                {
                    Height = h;
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (reading == null)
                {
                    return;
                }
                float k = DeviceDpi / 96f;
                int gap = (int)(Gap * k);
                int colW = (Width - 1 - gap) / 2;
                int cardH = (int)(CardHeight * k);
                var spots = new Dictionary<Corner, Rectangle>
                {
                    [Corner.FL] = new Rectangle(0, 0, colW, cardH),
                    [Corner.FR] = new Rectangle(colW + gap, 0, colW, cardH),
                };
                if (ShowRears)
                {
                    spots[Corner.RL] = new Rectangle(0, cardH + gap, colW, cardH);
                    spots[Corner.RR] = new Rectangle(colW + gap, cardH + gap, colW, cardH);
                }
                int steerTop = spots.Values.Max(r => r.Bottom) + gap;
                foreach (KeyValuePair<Corner, Rectangle> spot in spots)
                {
                    CornerGeometry now = reading.Corners.FirstOrDefault(c => c.Corner == spot.Key);
                    CornerGeometry was = reference?.Corners.FirstOrDefault(c => c.Corner == spot.Key);
                    var rows = now == null ? new List<(string, string, string)>() : new List<(string, string, string)>
                    {
                        Row("Camber", now.Camber, was?.Camber, "0.00", " deg"),
                        Row("Toe-in", now.Toe, was?.Toe, "0.00", " deg"),
                        Row("Caster", now.Caster, was?.Caster, "0.00", " deg"),
                        Row("KPI", now.Kpi, was?.Kpi, "0.00", " deg"),
                        Row("Scrub", now.ScrubRadius * 1000, was?.ScrubRadius * 1000, "0.0", " mm"),
                        Row("Trail", now.MechanicalTrail * 1000, was?.MechanicalTrail * 1000, "0.0", " mm"),
                    };
                    if (now != null && was != null)
                    {
                        rows.Add(Row("Travel", (now.WheelCenter.Z - was.WheelCenter.Z) * 1000, null, "0.0", " mm"));
                    }
                    Card(g, spot.Value, spot.Key.ToString(), rows, now == null ? "Not modeled: needs HP_UBJ, HP_LBJ, HP_WC, and HP_SA for this corner." : null, k);
                }

                SteeringGeometry s = reading.Steering;
                var steer = new Rectangle(0, steerTop, 2 * colW + gap, (int)(SteeringHeight * k));
                var steerRows = new List<(string, string, string)>();
                string empty = null;
                if (s == null)
                {
                    empty = "Needs both front corners and a rear wheel center (HP_WC_RL or HP_WC_RR).";
                }
                else if (s.Straight)
                {
                    steerRows.Add(("Wheels", "straight ahead", ""));
                    steerRows.Add(("Wheelbase", F(s.Wheelbase * 1000, "0.0") + " mm", ""));
                    steerRows.Add(("Kingpin track", F(s.KingpinTrack * 1000, "0.0") + " mm", ""));
                }
                else
                {
                    steerRows.Add(("Inner / outer", F(s.InnerSteer, "0.0") + " / " + F(s.OuterSteer, "0.0") + " deg", ""));
                    steerRows.Add(("Ackermann", F(s.AckermannPercent, "0") + " %", ""));
                    steerRows.Add(("Turning radius", F(s.TurningRadius, "0.00") + " m", ""));
                }
                Card(g, steer, "Steering", steerRows, empty, k, twoColumns: true);
            }

            private static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

            private static (string Name, string Value, string Change) Row(string name, double now, double? was, string fmt, string unit)
            {
                string change = "";
                if (was != null)
                {
                    double d = now - was.Value;
                    change = (d >= 0 ? "+" : "") + F(d, fmt);
                }
                return (name, F(now, fmt) + unit, change);
            }

            private void Card(Graphics g, Rectangle r, string title, List<(string Name, string Value, string Change)> rows, string empty, float k, bool twoColumns = false)
            {
                using (GraphicsPath path = HelpText.Rounded(r, 6 * k))
                using (var back = new SolidBrush(Color.FromArgb(0xF7, 0xF8, 0xF9)))
                using (var edge = new Pen(ToolForm.RuleLine))
                {
                    g.FillPath(back, path);
                    g.DrawPath(edge, path);
                }
                Color accent = ToolGroups.Color(ToolGroup.VehicleDesign);
                using (Font head = HelpText.Semibold(10f))
                using (Font label = HelpText.Regular(8.5f))
                using (Font value = HelpText.Semibold(9.5f))
                {
                    int pad = (int)(10 * k);
                    TextRenderer.DrawText(g, title, head, new Point(r.X + pad, r.Y + (int)(6 * k)), accent, HelpText.Line);
                    int top = r.Y + (int)(28 * k);
                    if (empty != null)
                    {
                        TextRenderer.DrawText(g, empty, label, new Rectangle(r.X + pad, top, r.Width - 2 * pad, r.Height - (top - r.Y)),
                            ToolForm.Muted, HelpText.Wrap);
                        return;
                    }
                    int lineH = (int)(15 * k);
                    int perColumn = twoColumns ? 3 : rows.Count;
                    int colW = twoColumns ? (r.Width - 2 * pad) / 2 : r.Width - 2 * pad;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        int x = r.X + pad + (i / perColumn) * colW;
                        int y = top + (i % perColumn) * lineH;
                        TextRenderer.DrawText(g, rows[i].Name, label, new Point(x, y), ToolForm.Muted, HelpText.Line);
                        int vx = x + (int)(colW * 0.42);
                        TextRenderer.DrawText(g, rows[i].Value, value, new Point(vx, y - 1), Brand.Ink, HelpText.Line);
                        if (rows[i].Change.Length > 0)
                        {
                            bool zero = rows[i].Change.TrimStart('+', '-').Trim('0', '.').Length == 0;
                            TextRenderer.DrawText(g, rows[i].Change, label, new Point(x + (int)(colW * 0.78), y), zero ? ToolForm.Muted : accent, HelpText.Line);
                        }
                    }
                }
            }
        }
    }
}
