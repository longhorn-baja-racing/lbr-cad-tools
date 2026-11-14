using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Suspension
{
    /// <summary>Sweep range for the selected driving dimension.</summary>
    internal sealed class SweepSetupDialog : ToolForm
    {
        private readonly SuspensionSweeper.Driver driver;
        private readonly ComboBox unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox from = new TextBox { TextAlign = HorizontalAlignment.Right };
        private readonly TextBox to = new TextBox { TextAlign = HorizontalAlignment.Right };
        private readonly NumericUpDown steps = new NumericUpDown { Minimum = 2, Maximum = 101, Value = 11 };
        private readonly Label check;
        private readonly Button run;
        private LengthUnit shown = LengthUnit.Inch;

        public SweepSetupDialog(SuspensionSweeper.Driver driver)
            : base("Suspension Sweep",
                "Steps the selected dimension through a range and records camber, toe, caster, KPI, scrub, and trail " +
                "at each step. The dimension is put back afterwards.",
                ToolIcon.SuspensionSweep, ToolGroup.VehicleDesign, 480)
        {
            this.driver = driver;
            SuspendLayout();
            var grid = new FieldGrid(140, 110);
            grid.AddWide(new Label { Text = driver.Name, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            var now = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            grid.Add("Now", now);
            if (!driver.Angular)
            {
                unit.Items.AddRange(new object[] { "Inches", "Millimeters" });
                unit.SelectedIndex = 0;
                grid.Add("Units", unit);
            }
            now.Text = Show(driver.Start) + " " + UnitText;
            string u = UnitText;
            grid.Add("From", from, u);
            grid.Add("To", to, u);
            grid.Add("Steps", steps, "values, both ends included");
            double span = driver.Angular ? 5 * Math.PI / 180 : 1 * Units.MetersPerInch;
            from.Text = Show(driver.Start - span);
            to.Text = Show(driver.Start + span);
            AddSection("Driving dimension", "The dimension you selected. Pick a range the mechanism can reach.", grid);

            check = StatusBox(ContentWidth);
            AddSection("Check", null, check);

            unit.SelectedIndexChanged += (s, e) =>
            {
                bool okFrom = TryRead(from.Text, out double f), okTo = TryRead(to.Text, out double t);
                shown = unit.SelectedIndex == 1 ? LengthUnit.Millimeter : LengthUnit.Inch;
                if (okFrom && okTo)
                {
                    from.Text = Show(f);
                    to.Text = Show(t);
                }
                foreach (Label l in grid.Controls.OfType<Label>().Where(l => l.Text == "in" || l.Text == "mm"))
                {
                    l.Text = UnitText;
                }
                now.Text = Show(driver.Start) + " " + UnitText;
                Validate2();
            };
            from.TextChanged += (s, e) => Validate2();
            to.TextChanged += (s, e) => Validate2();
            steps.ValueChanged += (s, e) => Validate2();

            run = AddButton("Run sweep", DialogResult.OK, primary: true);
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = run;
            CancelButton = cancel;
            Validate2();
            ResumeLayout(true);
        }

        /// <summary>Values to set, in SolidWorks units.</summary>
        public List<double> Values { get; private set; }

        public string UnitText => driver.Angular ? "deg" : shown == LengthUnit.Millimeter ? "mm" : "in";

        /// <summary>SolidWorks units to the shown unit.</summary>
        public double ToDisplay(double v) => driver.Angular ? v * 180 / Math.PI : Units.FromMeters(v, shown);

        private string Show(double v) =>
            ToDisplay(v).ToString(driver.Angular || shown == LengthUnit.Millimeter ? "0.###" : "0.####", CultureInfo.InvariantCulture);

        private bool TryRead(string text, out double value)
        {
            value = 0;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return false;
            }
            value = driver.Angular ? v * Math.PI / 180 : Units.ToMeters(v, shown);
            return true;
        }

        private void Validate2()
        {
            if (!TryRead(from.Text, out double f) || !TryRead(to.Text, out double t))
            {
                SetStatus(check, "Enter numbers with a '.' decimal point.", Bad, BadBack);
                run.Enabled = false;
                return;
            }
            if (Math.Abs(t - f) < 1e-12)
            {
                SetStatus(check, "From and To are the same.", Bad, BadBack);
                run.Enabled = false;
                return;
            }
            Values = SuspensionSweep.Values(f, t, (int)steps.Value);
            double step = (t - f) / ((int)steps.Value - 1);
            SetStatus(check, $"{Values.Count} rebuilds, every {Math.Abs(ToDisplay(f + step) - ToDisplay(f)):0.####} {UnitText}. " +
                             "Large assemblies take a moment per step.", Good, GoodBack);
            run.Enabled = true;
        }
    }

    /// <summary>Sweep results: summary per corner, camber and toe charts over wheel travel, the table, CSV.</summary>
    internal sealed class SweepResultForm : ToolForm
    {
        private readonly string csv;

        public SweepResultForm(SuspensionSweeper.Driver driver, SuspensionSweeper.Result result, string unit, Func<double, double> toDisplay)
            : base("Suspension Sweep",
                $"{result.Steps.Count} steps of {driver.Name}. Wheel travel is measured from where the model was before the sweep; bump is positive.",
                ToolIcon.SuspensionSweep, ToolGroup.VehicleDesign, 760)
        {
            SuspendLayout();
            List<SweepSummary> summary = SuspensionSweep.Summarize(result.Steps, result.Reference);
            csv = SuspensionSweep.Csv(result.Steps, result.Reference, driver.Name, unit, toDisplay);

            // ---- Summary ----
            var table = new StringBuilder();
            table.AppendLine("Corner   Travel (mm)        Camber (deg)      Camber gain   Toe-in (deg)      Bump steer");
            foreach (SweepSummary s in summary)
            {
                table.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-6} {1,7:0.0} to {2,6:0.0}   {3,6:0.00} to {4,6:0.00}   {5,6:0.00}/in    {6,6:0.000} to {7,6:0.000}  {8,6:0.000}/in",
                    s.Corner, s.TravelMin * 1000, s.TravelMax * 1000, s.CamberMin, s.CamberMax, s.CamberGainPerInch,
                    s.ToeMin, s.ToeMax, s.BumpSteerPerInch));
            }
            var summaryLabel = new Label
            {
                Text = table.ToString().TrimEnd(),
                AutoSize = true,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                ForeColor = Brand.Ink,
                Margin = new Padding(0, 4, 0, 0),
            };
            AddSection("Summary", "Gain and bump steer are least-squares slopes per inch of wheel travel (camber - = top in, toe + = in).", summaryLabel);

            // ---- Charts ----
            var camber = new SweepChart("Camber (deg)", result, g => g.Camber) { Size = new Size(ContentWidth / 2 - 8, 220), Margin = new Padding(0, 4, 16, 0) };
            var toe = new SweepChart("Toe-in (deg)", result, g => g.Toe) { Size = new Size(ContentWidth / 2 - 8, 220), Margin = new Padding(0, 4, 0, 0) };
            AddSection("Over wheel travel", "One line per corner; x is wheel center travel in mm.", Row(camber, toe));

            if (result.Problems.Count > 0)
            {
                Label problems = StatusBox(ContentWidth);
                SetStatus(problems, "Problems:\n  " + string.Join("\n  ", result.Problems.Take(12)) +
                                    (result.Problems.Count > 12 ? $"\n  ... and {result.Problems.Count - 12} more" : ""), Caution, CautionBack);
                AddSection("Check", null, problems);
            }

            Button save = AddButton("Save CSV...", left: true);
            save.Click += (s, e) => SaveCsv(driver.Name);
            Button copy = AddButton("Copy table", left: true);
            copy.Click += (s, e) => Clipboard.SetText(csv.Replace(',', '\t'));
            Button close = AddButton("Close", DialogResult.Cancel, primary: true);
            CancelButton = close;
            AcceptButton = close;
            ResumeLayout(true);
        }

        private void SaveCsv(string driverName)
        {
            string safe = new string(driverName.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '@' ? '_' : c).ToArray());
            using (var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Sweep " + safe + ".csv" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dialog.FileName, csv);
                }
            }
        }

        /// <summary>Line chart of one value against wheel travel, one line per corner.</summary>
        private sealed class SweepChart : Control
        {
            private static readonly Color[] Palette =
            {
                Color.FromArgb(0xBF, 0x57, 0x00), Color.FromArgb(0x00, 0x5F, 0x86), Color.FromArgb(0x3D, 0x7A, 0x34), Color.FromArgb(0x6B, 0x4C, 0x9A),
            };

            private readonly string title;
            private readonly List<(Corner Corner, List<PointF> Points)> series = new List<(Corner, List<PointF>)>();

            public SweepChart(string title, SuspensionSweeper.Result result, Func<CornerGeometry, double> value)
            {
                this.title = title;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                foreach (Corner c in result.Steps.SelectMany(s => s.Corners).Select(g => g.Corner).Distinct().OrderBy(c => c))
                {
                    var pts = result.Steps
                        .Select(s => s.Corners.FirstOrDefault(g => g.Corner == c))
                        .Where(g => g != null)
                        .Select(g => new PointF((float)(SuspensionSweep.Travel(g, result.Reference) * 1000), (float)value(g)))
                        .OrderBy(p => p.X)
                        .ToList();
                    series.Add((c, pts));
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = DeviceDpi / 96f;
                using (Font font = HelpText.Regular(8f))
                using (Font head = HelpText.Semibold(9f))
                using (var axis = new Pen(ToolForm.RuleLine))
                {
                    TextRenderer.DrawText(g, title, head, new Point(0, 0), Brand.Ink, HelpText.Line);
                    var plot = new RectangleF(44 * k, 22 * k, Width - 52 * k, Height - 44 * k);
                    List<PointF> all = series.SelectMany(s => s.Points).ToList();
                    if (all.Count == 0)
                    {
                        return;
                    }
                    float x0 = all.Min(p => p.X), x1 = all.Max(p => p.X), y0 = all.Min(p => p.Y), y1 = all.Max(p => p.Y);
                    if (x1 - x0 < 1e-6) { x0 -= 1; x1 += 1; }
                    if (y1 - y0 < 1e-3) { y0 -= 0.5f; y1 += 0.5f; }
                    float pad = (y1 - y0) * 0.08f;
                    y0 -= pad;
                    y1 += pad;
                    PointF Map(PointF p) => new PointF(plot.Left + (p.X - x0) / (x1 - x0) * plot.Width, plot.Bottom - (p.Y - y0) / (y1 - y0) * plot.Height);

                    g.DrawRectangle(axis, plot.X, plot.Y, plot.Width, plot.Height);
                    string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
                    TextRenderer.DrawText(g, F(y1), font, new Rectangle(0, (int)plot.Top - 6, (int)(40 * k), 14), ToolForm.Muted, TextFormatFlags.Right);
                    TextRenderer.DrawText(g, F(y0), font, new Rectangle(0, (int)plot.Bottom - 8, (int)(40 * k), 14), ToolForm.Muted, TextFormatFlags.Right);
                    TextRenderer.DrawText(g, F(x0) + " mm", font, new Point((int)plot.Left, (int)(plot.Bottom + 3)), ToolForm.Muted, TextFormatFlags.NoPadding);
                    string right = F(x1) + " mm";
                    Size rs = TextRenderer.MeasureText(right, font);
                    TextRenderer.DrawText(g, right, font, new Point((int)(plot.Right - rs.Width), (int)(plot.Bottom + 3)), ToolForm.Muted, TextFormatFlags.NoPadding);
                    if (x0 < 0 && x1 > 0)
                    {
                        using (var zero = new Pen(ToolForm.RuleLine) { DashStyle = DashStyle.Dash })
                        {
                            float zx = Map(new PointF(0, y0)).X;
                            g.DrawLine(zero, zx, plot.Top, zx, plot.Bottom);
                        }
                    }

                    int legendX = (int)(Width - 4 * k);
                    for (int i = series.Count - 1; i >= 0; i--)
                    {
                        Color c = Palette[i % Palette.Length];
                        using (var pen = new Pen(c, 2f * k))
                        using (var dot = new SolidBrush(c))
                        {
                            PointF[] pts = series[i].Points.Select(Map).ToArray();
                            if (pts.Length > 1)
                            {
                                g.DrawLines(pen, pts);
                            }
                            foreach (PointF p in pts)
                            {
                                g.FillEllipse(dot, p.X - 2.5f * k, p.Y - 2.5f * k, 5 * k, 5 * k);
                            }
                            string name = series[i].Corner.ToString();
                            Size s = TextRenderer.MeasureText(name, head);
                            legendX -= s.Width;
                            TextRenderer.DrawText(g, name, head, new Point(legendX, 0), c, HelpText.Line);
                            legendX -= (int)(8 * k);
                        }
                    }
                }
            }
        }
    }
}
