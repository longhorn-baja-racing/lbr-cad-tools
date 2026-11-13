using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Tabs
{
    /// <summary>
    /// Mounting tab inputs with live B.12.2 checks. Built in code (no designer
    /// files). Rule breaks are warnings; shape errors disable Generate.
    /// </summary>
    internal sealed class TabDialog : Form
    {
        private static TabInputs last;
        private static LengthUnit lastUnit = LengthUnit.Inch;

        private readonly ComboBox use = new ComboBox();
        private readonly ComboBox unit = new ComboBox();
        private readonly RadioButton single = new RadioButton();
        private readonly RadioButton dbl = new RadioButton();
        private readonly CheckBox contour = new CheckBox();
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly Label checks = new Label();
        private readonly Button generate = new Button();
        private LengthUnit shownUnit;
        private bool loading;

        private static readonly (string Key, string Label)[] Sizes =
        {
            ("Bolt", "Bolt diameter D1"),
            ("Hole", "Hole diameter (close fit)"),
            ("EndDiameter", "End diameter D2"),
            ("BaseWidth", "Base width B"),
            ("Length", "Length L (base to hole center)"),
            ("Thickness", "Thickness t"),
            ("Gap", "Gap between tabs (double shear)"),
            ("Tube", "Tube OD (contoured base)"),
        };

        public TabDialog()
        {
            Text = "Mounting Tab (B.12.2)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            int row = 0;
            void Add(string label, Control control)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                control.Dock = DockStyle.Fill;
                grid.Controls.Add(control, 1, row);
                row++;
            }

            use.DropDownStyle = ComboBoxStyle.DropDownList;
            use.Items.AddRange(TabPreset.All.Select(p => (object)p.Name).ToArray());
            Add("Use", use);

            unit.DropDownStyle = ComboBoxStyle.DropDownList;
            unit.Items.AddRange(new object[] { "Inches", "Millimeters" });
            Add("Units", unit);

            single.Text = "Single";
            dbl.Text = "Double";
            single.AutoSize = dbl.AutoSize = true;
            var shear = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            shear.Controls.Add(single);
            shear.Controls.Add(dbl);
            Add("Shear", shear);

            foreach ((string key, string label) in Sizes)
            {
                if (key == "Tube")
                {
                    contour.Text = "Contour the base to a tube";
                    contour.AutoSize = true;
                    grid.Controls.Add(contour, 0, row);
                    grid.SetColumnSpan(contour, 2);
                    row++;
                }
                var box = new TextBox();
                fields[key] = box;
                Add(label, box);
            }

            var reset = new Button { Text = "Use rule sizes", AutoSize = true };
            reset.Click += (s, e) => Fill(TabInputs.Defaults(SelectedUse), Unit);
            grid.Controls.Add(reset, 1, row++);

            checks.AutoSize = true;
            checks.MaximumSize = new Size(440, 0);
            checks.Margin = new Padding(3, 10, 3, 3);
            grid.Controls.Add(checks, 0, row);
            grid.SetColumnSpan(checks, 2);
            row++;

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                ForeColor = SystemColors.GrayText,
                Text = "Creates a new part (not saved). Double shear makes two bodies spaced by the gap. " +
                       "Weld along the whole base line; no cutouts or lightening holes (B.12.2.2).",
            };
            grid.Controls.Add(note, 0, row);
            grid.SetColumnSpan(note, 2);
            row++;

            generate.Text = "Generate";
            generate.AutoSize = true;
            generate.Click += (s, e) => Accept();
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(generate);
            grid.Controls.Add(buttons, 0, row);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
            AcceptButton = generate;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Fill(last ?? TabInputs.Defaults(TabUse.LapBelt), lastUnit);

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
            foreach (TextBox box in fields.Values)
            {
                box.TextChanged += (s, e) => UpdateChecks();
            }
        }

        public TabInputs Inputs { get; private set; }

        private TabUse SelectedUse => TabPreset.All[Math.Max(0, use.SelectedIndex)].Use;

        private LengthUnit Unit => unit.SelectedIndex == 1 ? LengthUnit.Millimeter : LengthUnit.Inch;

        private void Fill(TabInputs t, LengthUnit u)
        {
            loading = true;
            use.SelectedIndex = TabPreset.All.ToList().FindIndex(p => p.Use == t.Use);
            unit.SelectedIndex = u == LengthUnit.Millimeter ? 1 : 0;
            shownUnit = u;
            single.Checked = !t.DoubleShear;
            dbl.Checked = t.DoubleShear;
            contour.Checked = t.ContourTubeDiameter > 0;
            Set("Bolt", t.Bolt);
            Set("Hole", t.Hole);
            Set("EndDiameter", t.EndDiameter);
            Set("BaseWidth", t.BaseWidth);
            Set("Length", t.Length);
            Set("Thickness", t.Thickness);
            Set("Gap", t.Gap);
            Set("Tube", t.ContourTubeDiameter > 0 ? t.ContourTubeDiameter : 1.25 * Units.MetersPerInch);
            loading = false;
            UpdateChecks();
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
            t.Gap = dbl.Checked ? Get("Gap") : 0;
            t.ContourTubeDiameter = contour.Checked ? Get("Tube") : 0;
            return ok;
        }

        private void UpdateChecks()
        {
            if (loading)
            {
                return;
            }
            fields["Gap"].Enabled = dbl.Checked;
            fields["Tube"].Enabled = contour.Checked;

            if (!TryRead(out TabInputs t))
            {
                checks.ForeColor = Color.Firebrick;
                checks.Text = "Enter numbers with a '.' decimal point.";
                generate.Enabled = false;
                return;
            }
            List<TabIssue> issues = TabRules.Check(t);
            bool errors = issues.Any(i => i.Level == TabIssueLevel.Error);
            generate.Enabled = !errors;
            if (issues.Count == 0)
            {
                checks.ForeColor = Color.DarkGreen;
                checks.Text = t.Use == TabUse.Custom ? "Shape OK (custom: no rule checks)." : "Meets B.12.2.";
            }
            else
            {
                checks.ForeColor = errors ? Color.Firebrick : Color.DarkGoldenrod;
                checks.Text = string.Join("\n", issues.Select(i => (i.Level == TabIssueLevel.Error ? "Cannot build: " : "Rule: ") + i.Message));
            }
        }

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
    }
}
