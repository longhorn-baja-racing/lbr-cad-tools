using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Tabs
{
    /// <summary>
    /// Mounting tab inputs with live B.12.2 checks and a labeled drawing of the
    /// tab. Rule breaks are warnings; shape errors disable Generate.
    /// </summary>
    internal sealed class TabDialog : ToolForm
    {
        /// <summary>What was picked in the assembly, for the Placement section.</summary>
        internal sealed class PlacementOffer
        {
            public string AssemblyTitle { get; set; }

            /// <summary>Saved assemblies only: the tab part is saved next to it.</summary>
            public bool AssemblySaved { get; set; }

            public string ComponentName { get; set; }

            public bool Round { get; set; }

            /// <summary>Round tube outside diameter in meters (0 for flat faces).</summary>
            public double Diameter { get; set; }
        }

        private static TabInputs last;
        private static LengthUnit lastUnit = LengthUnit.Inch;

        private readonly ComboBox use = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly RadioButton single = new RadioButton { Text = "Single", AutoSize = true };
        private readonly RadioButton dbl = new RadioButton { Text = "Double", AutoSize = true };
        private readonly CheckBox contour = new CheckBox { AutoSize = true };
        private readonly CheckBox gapFromTube = new CheckBox { AutoSize = true };
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly List<Label> unitLabels = new List<Label>();
        private readonly Label rule;
        private readonly Label checks;
        private readonly Button generate;
        private readonly PlacementOffer offer;
        private readonly CheckBox insert = new CheckBox { AutoSize = true, Checked = true };
        private readonly RadioButton along = new RadioButton { Text = "Along the tube", AutoSize = true, Checked = true };
        private readonly RadioButton across = new RadioButton { Text = "Across the tube", AutoSize = true };
        private Label placementNote;
        private LengthUnit shownUnit;
        private bool loading;

        /// <param name="offer">A tube face picked in an assembly, or null to only make the part.</param>
        public TabDialog(PlacementOffer offer = null)
            : base("Mounting Tab",
                "Makes a welded bolt tab part sized from the Baja SAE B.12.2 table and checks the rules as you type. " +
                (offer == null
                    ? "The new part opens unsaved. Tip: select a tube face in an assembly first to insert the tab onto it."
                    : "It is saved next to the assembly and inserted on the tube you selected."),
                ToolIcon.MountingTab, ToolGroup.VehicleDesign, 560)
        {
            this.offer = offer;
            SuspendLayout();

            // ---- What the tab is for ----
            use.Items.AddRange(TabPreset.All.Select(p => (object)p.Name).ToArray());
            unit.Items.AddRange(new object[] { "Inches", "Millimeters" });
            var shear = Row(single, dbl);
            var purpose = new FieldGrid(150, 220);
            purpose.Add("Holds", use);
            purpose.Add("Shear", shear, "double = two tabs, bolt between");
            purpose.Add("Units", unit);
            rule = Note("", ContentWidth);
            rule.Margin = new Padding(0, 6, 0, 0);
            AddSection("What the tab is for",
                "Choosing what it holds fills in the rule minimum sizes below.",
                purpose, rule);

            // ---- Size ----
            var size = new FieldGrid(150, 90);
            AddSize(size, "Bolt", "Bolt diameter  D1");
            AddSize(size, "Hole", "Hole diameter");
            AddSize(size, "EndDiameter", "End diameter  D2");
            AddSize(size, "BaseWidth", "Base width  B");
            AddSize(size, "Length", "Length  L");
            AddSize(size, "Thickness", "Thickness  t");
            Label holeNote = Note("Hole: close fit is D1 + 1/64 in, centered in the rounded end.\nL runs from the base to the hole center.", 290);
            holeNote.Margin = new Padding(0, 8, 0, 0);
            size.AddWide(holeNote);
            var diagram = new TabDiagram(Accent) { Size = new Size(230, 250), Margin = new Padding(16, 0, 0, 0) };
            AddSection("Size", "Letters match Figure B-54 in the rule book.", Row(size, diagram));

            // ---- Tube ----
            var tube = new FieldGrid(150, 90);
            AddSize(tube, "Tube", "Tube OD");
            gapFromTube.Text = "Double shear: space the tabs by the tube OD (one each side of the tube)";
            tube.AddWide(gapFromTube);
            AddSize(tube, "Gap", "Gap between tabs");
            contour.Text = "Cut the base to fit the tube (contour to the tube OD)";
            tube.AddWide(contour);
            AddSection("Tube and spacing", "The tube the tab is welded to.", tube);

            // ---- Placement (only with a picked tube face) ----
            if (offer != null)
            {
                insert.Text = $"Insert into {offer.AssemblyTitle} on {offer.ComponentName ?? "the selected face"}";
                var place = new FieldGrid(150, 220);
                place.AddWide(insert);
                place.Add("Base line runs", Row(along, across));
                placementNote = Note("", ContentWidth);
                placementNote.Margin = new Padding(0, 4, 0, 0);
                AddSection("Placement",
                    (offer.Round ? "Round tube" : "Flat face") + " selected. The tab is centered where you clicked and fixed in place; " +
                    "drag it after unfixing (right-click > Float) to adjust.",
                    place, placementNote);
                insert.CheckedChanged += (s, e) => UpdateChecks();
                along.CheckedChanged += (s, e) => UpdateChecks();
            }

            // ---- Rule check ----
            checks = StatusBox(ContentWidth);
            AddSection("Rule check", null, checks);

            // ---- Buttons ----
            Button reset = AddButton("Reset to rule sizes", left: true);
            reset.Click += (s, e) => Fill(TabInputs.Defaults(SelectedUse), Unit);
            generate = AddButton("Generate", primary: true);
            generate.Click += (s, e) => Accept();
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = generate;
            CancelButton = cancel;

            TabInputs start = last ?? TabInputs.Defaults(TabUse.LapBelt);
            if (offer != null && offer.Round && offer.Diameter > 0)
            {
                start.TubeDiameter = offer.Diameter; // measured from the picked tube
            }
            Fill(start, lastUnit);

            use.SelectedIndexChanged += (s, e) =>
            {
                if (!loading)
                {
                    Fill(TabInputs.Defaults(SelectedUse), Unit);
                }
            };
            unit.SelectedIndexChanged += (s, e) =>
            {
                if (!loading && TryRead(out TabInputs current))
                {
                    Fill(current, Unit);
                }
            };
            single.CheckedChanged += (s, e) => UpdateChecks();
            contour.CheckedChanged += (s, e) => UpdateChecks();
            gapFromTube.CheckedChanged += (s, e) => UpdateChecks();
            foreach (TextBox box in fields.Values)
            {
                box.TextChanged += (s, e) => UpdateChecks();
            }
            ResumeLayout(true);
        }

        public TabInputs Inputs { get; private set; }

        /// <summary>True when the tab should go into the assembly.</summary>
        public bool InsertIntoAssembly => offer != null && offer.AssemblySaved && insert.Checked;

        public TabOrientation Orientation => across.Checked ? TabOrientation.AcrossTube : TabOrientation.AlongTube;

        private TabUse SelectedUse => TabPreset.All[Math.Max(0, use.SelectedIndex)].Use;

        private LengthUnit Unit => unit.SelectedIndex == 1 ? LengthUnit.Millimeter : LengthUnit.Inch;

        private string UnitSymbol => shownUnit == LengthUnit.Millimeter ? "mm" : "in";

        private void AddSize(FieldGrid grid, string key, string label)
        {
            var box = new TextBox { TextAlign = HorizontalAlignment.Right };
            fields[key] = box;
            unitLabels.Add(grid.Add(label, box));
        }

        private void Fill(TabInputs t, LengthUnit u)
        {
            loading = true;
            use.SelectedIndex = TabPreset.All.ToList().FindIndex(p => p.Use == t.Use);
            unit.SelectedIndex = u == LengthUnit.Millimeter ? 1 : 0;
            shownUnit = u;
            foreach (Label l in unitLabels)
            {
                l.Text = UnitSymbol;
            }
            single.Checked = !t.DoubleShear;
            dbl.Checked = t.DoubleShear;
            contour.Checked = t.ContourTubeDiameter > 0;
            gapFromTube.Checked = t.GapFromTube;
            Set("Bolt", t.Bolt);
            Set("Hole", t.Hole);
            Set("EndDiameter", t.EndDiameter);
            Set("BaseWidth", t.BaseWidth);
            Set("Length", t.Length);
            Set("Thickness", t.Thickness);
            Set("Gap", t.Gap);
            Set("Tube", t.TubeDiameter);
            rule.Text = RuleSummary(TabPreset.For(t.Use));
            loading = false;
            UpdateChecks();
        }

        private string RuleSummary(TabPreset p)
        {
            if (p.Use == TabUse.Custom)
            {
                return "Custom: only the shape is checked, not the rules.";
            }
            string Len(double meters) =>
                Units.FromMeters(meters, shownUnit).ToString(shownUnit == LengthUnit.Inch ? "0.####" : "0.#", CultureInfo.InvariantCulture) +
                " " + UnitSymbol;
            var parts = new List<string>
            {
                p.MinBolt.HasValue ? "bolt at least " + Len(p.MinBolt.Value) : "bolt size from its bracket",
                "L at most " + Len(p.MaxLength),
                "t at least " + Len(p.MinThickness),
            };
            if (p.DoubleShear)
            {
                parts.Add("double shear");
            }
            parts.Add("weld " + p.Weld);
            return "B.12.2 for a " + p.Name.ToLowerInvariant() + " tab: " + string.Join(", ", parts) + ".";
        }

        private void Set(string key, double meters)
        {
            double v = Units.FromMeters(meters, shownUnit);
            fields[key].Text = v.ToString(shownUnit == LengthUnit.Inch ? "0.####" : "0.##", CultureInfo.InvariantCulture);
        }

        private bool TryRead(out TabInputs t)
        {
            t = new TabInputs { Use = SelectedUse, DoubleShear = dbl.Checked };
            bool ok = true;
            double Get(string key)
            {
                if (double.TryParse(fields[key].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    return Units.ToMeters(v, shownUnit);
                }
                ok = false;
                return 0;
            }
            t.Bolt = Get("Bolt");
            t.Hole = Get("Hole");
            t.EndDiameter = Get("EndDiameter");
            t.BaseWidth = Get("BaseWidth");
            t.Length = Get("Length");
            t.Thickness = Get("Thickness");
            t.TubeDiameter = Get("Tube");
            t.GapFromTube = gapFromTube.Checked;
            t.Gap = Get("Gap");
            t.ContourTubeDiameter = contour.Checked ? t.TubeDiameter : 0;
            return ok;
        }

        private void UpdateChecks()
        {
            if (loading)
            {
                return;
            }
            gapFromTube.Enabled = dbl.Checked;
            fields["Gap"].Enabled = dbl.Checked && !gapFromTube.Checked;
            UpdatePlacement();

            if (!TryRead(out TabInputs t))
            {
                SetStatus(checks, "Enter numbers with a '.' decimal point.", Bad, BadBack);
                generate.Enabled = false;
                return;
            }
            List<TabIssue> issues = TabRules.Check(t);
            bool errors = issues.Any(i => i.Level == TabIssueLevel.Error);
            generate.Enabled = !errors;
            if (issues.Count == 0)
            {
                SetStatus(checks,
                    t.Use == TabUse.Custom ? "Shape OK. Custom tabs are not checked against the rules." : "Meets B.12.2. Ready to generate.",
                    Good, GoodBack);
            }
            else if (errors)
            {
                SetStatus(checks, "Cannot build this shape:\n" + Bullets(issues.Where(i => i.Level == TabIssueLevel.Error)), Bad, BadBack);
            }
            else
            {
                SetStatus(checks, "Does not meet B.12.2 (you can still generate it):\n" + Bullets(issues), Caution, CautionBack);
            }
        }

        private void UpdatePlacement()
        {
            if (offer == null)
            {
                return;
            }
            if (!offer.AssemblySaved)
            {
                insert.Checked = false;
                insert.Enabled = false;
                placementNote.Text = "Save the assembly first: the tab part is saved in the same folder. For now the tab opens as a separate part.";
                placementNote.ForeColor = Caution;
                along.Enabled = across.Enabled = false;
                return;
            }
            bool on = insert.Checked;
            bool contoured = contour.Checked && offer.Round;
            along.Enabled = on && !contoured;
            across.Enabled = on;
            if (contoured && along.Checked)
            {
                across.Checked = true;
            }
            placementNote.ForeColor = Muted;
            if (!on)
            {
                placementNote.Text = "The tab opens as a separate, unsaved part.";
            }
            else if (contoured)
            {
                placementNote.Text = "A contoured base wraps around the tube, so the tab goes across it.";
            }
            else if (offer.Round && dbl.Checked && gapFromTube.Checked && along.Checked)
            {
                placementNote.Text = "The double shear pair straddles the tube: one plate on each side, base level with the tube center.";
            }
            else
            {
                placementNote.Text = across.Checked
                    ? "The plate stands square to the tube; the base sits on the tube surface."
                    : "The base sits on the tube surface and runs along it.";
            }
        }

        private static string Bullets(IEnumerable<TabIssue> issues) => string.Join("\n", issues.Select(i => "  - " + i.Message));

        private void Accept()
        {
            if (!TryRead(out TabInputs t) || TabRules.Check(t).Any(i => i.Level == TabIssueLevel.Error))
            {
                return;
            }
            Inputs = t;
            last = t;
            lastUnit = shownUnit;
            DialogResult = DialogResult.OK;
        }

        /// <summary>Front view of a tab on its tube with the B.12.2 letters (D1, D2, B, L).</summary>
        private sealed class TabDiagram : Control
        {
            private readonly Color accent;

            public TabDiagram(Color accent)
            {
                this.accent = accent;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Color.FromArgb(0xFA, 0xFA, 0xFB));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                float k = Math.Min(Width / 230f, Height / 250f);
                g.ScaleTransform(k, k);

                using (var border = new Pen(RuleLine))
                {
                    g.DrawRectangle(border, 0, 0, 229, 249);
                }

                // Geometry (diagram units): end circle, base line on the tube.
                var c = new PointF(115, 82);
                const float r = 36, hole = 14, baseY = 186, half = 62;
                var left = new PointF(c.X - half, baseY);
                var right = new PointF(c.X + half, baseY);
                float aLeft = Tangent(left, c, r, leftSide: true);
                float aRight = Tangent(right, c, r, leftSide: false);

                using (var tube = new SolidBrush(Color.FromArgb(0xE3, 0xE6, 0xE8)))
                {
                    g.FillRectangle(tube, 14, baseY, 202, 18);
                }
                using (var path = new GraphicsPath())
                using (var fill = new SolidBrush(Color.FromArgb(0xF6, 0xE2, 0xD1)))
                using (var outline = new Pen(accent, 2f))
                {
                    path.AddLine(left, OnCircle(c, r, aLeft));
                    float sweep = aRight - aLeft;
                    if (sweep < 0)
                    {
                        sweep += 360;
                    }
                    path.AddArc(c.X - r, c.Y - r, 2 * r, 2 * r, aLeft, sweep);
                    path.AddLine(OnCircle(c, r, aRight), right);
                    path.CloseFigure();
                    g.FillPath(fill, path);
                    g.DrawPath(outline, path);
                    g.FillEllipse(Brushes.White, c.X - hole, c.Y - hole, 2 * hole, 2 * hole);
                    g.DrawEllipse(outline, c.X - hole, c.Y - hole, 2 * hole, 2 * hole);
                }

                using (var dim = new Pen(Muted, 1f))
                using (var arrows = new Pen(Muted, 1f) { CustomStartCap = new AdjustableArrowCap(3, 4), CustomEndCap = new AdjustableArrowCap(3, 4) })
                using (var center = new Pen(Muted, 1f) { DashStyle = DashStyle.DashDot })
                using (var font = new Font("Segoe UI Semibold", 9f))
                using (var ink = new SolidBrush(Brand.Ink))
                using (var muted = new SolidBrush(Muted))
                using (var small = new Font("Segoe UI", 8f))
                {
                    // Center lines through the hole.
                    g.DrawLine(center, c.X - r - 8, c.Y, c.X + r + 8, c.Y);
                    g.DrawLine(center, c.X, c.Y - r - 6, c.X, c.Y + r + 8);

                    // D2 across the rounded end, above it.
                    float d2y = c.Y - r - 18;
                    g.DrawLine(dim, c.X - r, c.Y, c.X - r, d2y - 4);
                    g.DrawLine(dim, c.X + r, c.Y, c.X + r, d2y - 4);
                    g.DrawLine(arrows, c.X - r, d2y, c.X + r, d2y);
                    Label(g, "D2", font, ink, c.X, d2y - 9);

                    // D1 hole: leader from the upper left.
                    PointF h = OnCircle(c, hole, 225);
                    g.DrawLine(dim, h.X, h.Y, 46, 40);
                    g.DrawLine(dim, 46, 40, 30, 40);
                    Label(g, "D1", font, ink, 22, 40);

                    // L from the base to the hole center, on the right.
                    float lx = c.X + half + 14;
                    g.DrawLine(dim, c.X + r + 10, c.Y, lx + 4, c.Y);
                    g.DrawLine(dim, right.X + 2, baseY, lx + 4, baseY);
                    g.DrawLine(arrows, lx, c.Y, lx, baseY);
                    Label(g, "L", font, ink, lx + 10, (c.Y + baseY) / 2);

                    // B along the base, under the tube.
                    float by = baseY + 30;
                    g.DrawLine(dim, left.X, baseY, left.X, by + 4);
                    g.DrawLine(dim, right.X, baseY, right.X, by + 4);
                    g.DrawLine(arrows, left.X, by, right.X, by);
                    Label(g, "B", font, ink, c.X, by + 10);

                    g.DrawString("tube", small, muted, 18, baseY + 2);
                    g.DrawString("t = plate thickness", small, muted, 8, 4);
                }
            }

            private static void Label(Graphics g, string text, Font font, Brush brush, float cx, float cy)
            {
                SizeF s = g.MeasureString(text, font);
                using (var back = new SolidBrush(Color.FromArgb(0xFA, 0xFA, 0xFB)))
                {
                    g.FillRectangle(back, cx - s.Width / 2, cy - s.Height / 2, s.Width, s.Height);
                }
                g.DrawString(text, font, brush, cx - s.Width / 2, cy - s.Height / 2);
            }

            private static PointF OnCircle(PointF c, float r, float degrees)
            {
                double a = degrees * Math.PI / 180;
                return new PointF(c.X + r * (float)Math.Cos(a), c.Y + r * (float)Math.Sin(a));
            }

            /// <summary>Angle (degrees, screen coordinates) of the outer tangent point from <paramref name="p"/> to the circle.</summary>
            private static float Tangent(PointF p, PointF c, float r, bool leftSide)
            {
                double dx = p.X - c.X, dy = p.Y - c.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double toP = Math.Atan2(dy, dx);
                double off = Math.Acos(r / d);
                double a = leftSide ? toP + off : toP - off;
                return (float)(a * 180 / Math.PI);
            }
        }
    }
}
