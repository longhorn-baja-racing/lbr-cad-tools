using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.Naming
{
    /// <summary>
    /// Name Check results (issue #6): every file with what to fix, the next
    /// free number per team, and a name builder that copies a correct name.
    /// </summary>
    internal sealed class NameCheckDialog : ToolForm
    {
        private readonly List<NameCheck> checks;
        private readonly NameScanner.Result scan;
        private readonly IReadOnlyDictionary<string, string> teams;
        private readonly ListView list;
        private readonly CheckBox onlyProblems = new CheckBox { Text = "Only show files to fix", AutoSize = true, Checked = true };

        private readonly ComboBox kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox team = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox number = new TextBox();
        private readonly TextBox partName = new TextBox();
        private readonly TextBox revision = new TextBox { Text = "01" };
        private readonly CheckBox standard = new CheckBox { Text = "REVSTD (off-the-shelf part)", AutoSize = true };
        private readonly TextBox version = new TextBox { Text = "01" };
        private readonly Label built;
        private readonly Label detail;
        private readonly Button copyName;

        public NameCheckDialog(NameScanner.Result scan, List<NameCheck> checks, IReadOnlyDictionary<string, string> teams)
            : base("Name Check",
                "Checks file names against the team convention " + NamingConvention.Pattern +
                ", e.g. aDYN_0006_ControlArmLeft_REV01_V02. Nothing is renamed.",
                ToolIcon.NameCheck, ToolGroup.Files, 720)
        {
            this.scan = scan;
            this.checks = checks;
            this.teams = teams;
            SuspendLayout();

            // ---- Results ----
            int bad = checks.Count(c => !c.Ok) + scan.Virtual.Count;
            list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Size = new Size(ContentWidth, 250),
                Margin = new Padding(0, 4, 0, 0),
                ShowItemToolTips = true,
            };
            list.Columns.Add("", 44);
            list.Columns.Add("File", 270);
            list.Columns.Add("Type", 64);
            list.Columns.Add("What to fix", ContentWidth - 44 - 270 - 64 - 24);
            onlyProblems.CheckedChanged += (s, e) => FillList();
            detail = Note("Click a row to see the whole message here.", ContentWidth);
            detail.Margin = new Padding(0, 6, 0, 0);
            detail.MinimumSize = new Size(0, 34);
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is string full)
                {
                    detail.Text = full;
                    detail.ForeColor = Brand.Ink;
                }
            };
            onlyProblems.Margin = new Padding(0, 4, 0, 0);
            var status = StatusBox(ContentWidth);
            if (bad == 0)
            {
                SetStatus(status, $"All {checks.Count} file(s) follow the convention.", Good, GoodBack);
                onlyProblems.Checked = false;
            }
            else
            {
                SetStatus(status, $"{bad} of {checks.Count + scan.Virtual.Count} file(s) need a new name. " +
                                  "Click a row for the full message.", Caution, CautionBack);
            }
            AddSection("Results",
                $"{scan.Top.BaseName} and {scan.Files.Count} file(s) it uses" +
                (scan.Files.Any(f => f.Kind == CadFileKind.Drawing) ? ", plus their drawings in the same folder" : "") + ".",
                status, list, detail, onlyProblems);

            // ---- Next numbers ----
            var next = new StringBuilder();
            foreach (string code in teams.Keys)
            {
                next.Append(code).Append(' ')
                    .Append(NamingConvention.NextNumber(code, scan.AllNames).ToString("0000", CultureInfo.InvariantCulture))
                    .Append("     ");
            }
            var numbers = new Label
            {
                Text = next.ToString().TrimEnd(),
                AutoSize = true,
                Font = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Bold),
                ForeColor = Brand.Ink,
                Margin = new Padding(0, 2, 0, 0),
            };
            AddSection("Next free numbers",
                "One above the highest number found in this assembly and every SolidWorks file in its folder" +
                (scan.FolderTruncated ? " (large folder: only the first 20,000 files were read)" : "") + ".",
                numbers);

            // ---- Name builder ----
            kind.Items.AddRange(new object[] { "Part", "Assembly", "Drawing" });
            foreach (KeyValuePair<string, string> t in teams)
            {
                team.Items.Add(string.IsNullOrEmpty(t.Value) ? t.Key : $"{t.Key}  ({t.Value})");
            }
            var grid = new FieldGrid(130, 290);
            grid.Add("Type", kind);
            grid.Add("Team", team);
            grid.Add("Number", number, "next free number for the team");
            grid.Add("Part name", partName, "2-3 words; spaces become CamelCase");
            grid.Add("Revision", Row(revision, standard), "REV of the assembly it goes in");
            grid.Add("Version", version);
            revision.Width = 50;
            standard.Margin = new Padding(10, 3, 0, 0);
            built = new Label
            {
                AutoSize = true,
                Font = new Font(FontFamily.GenericMonospace, 11f, FontStyle.Bold),
                Margin = new Padding(0, 8, 0, 0),
            };
            copyName = new Button { Text = "Copy name", AutoSize = true, Margin = new Padding(12, 4, 0, 0) };
            copyName.Click += (s, e) => Clipboard.SetText(built.Text);
            AddSection("Name builder",
                "Makes a correct name to copy into Save As. Starts from the open document's team and revision.",
                grid, Row(built, copyName));

            foreach (Control c in new Control[] { kind, team, number, partName, revision, version })
            {
                c.TextChanged += (s, e) => Build();
            }
            kind.SelectedIndexChanged += (s, e) => Build();
            standard.CheckedChanged += (s, e) =>
            {
                revision.Enabled = !standard.Checked;
                Build();
            };
            team.SelectedIndexChanged += (s, e) =>
            {
                if (TeamCode != null)
                {
                    number.Text = NamingConvention.NextNumber(TeamCode, scan.AllNames).ToString("0000", CultureInfo.InvariantCulture);
                }
            };
            StartBuilder();

            Button copyReport = AddButton("Copy report", left: true);
            copyReport.Click += (s, e) => Clipboard.SetText(Report());
            Button close = AddButton("Close", DialogResult.Cancel, primary: true);
            CancelButton = close;
            AcceptButton = close;

            FillList();
            ResumeLayout(true);
        }

        private string TeamCode => team.SelectedIndex < 0 ? null : teams.Keys.ElementAt(team.SelectedIndex);

        private void FillList()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (NameCheck c in checks.Where(c => !onlyProblems.Checked || !c.Ok))
            {
                string fix = c.Ok ? "" : string.Join(" ", c.Problems);
                var item = new ListViewItem(new[] { c.Ok ? "OK" : "Fix", c.File.BaseName, NamingConvention.KindName(c.File.Kind), fix })
                {
                    ForeColor = c.Ok ? Good : Caution,
                    ToolTipText = c.Ok ? c.File.BaseName : c.File.BaseName + "\n- " + string.Join("\n- ", c.Problems),
                    Tag = c.Ok ? c.File.BaseName + ": follows the convention." : c.File.BaseName + ": " + string.Join(" ", c.Problems),
                };
                list.Items.Add(item);
            }
            foreach (string v in scan.Virtual)
            {
                list.Items.Add(new ListViewItem(new[]
                {
                    "Fix", v, "Virtual",
                    "Saved inside the assembly, so it has no file name. Right-click it > Save Part (in External File) and name it.",
                })
                {
                    ForeColor = Caution,
                    Tag = v + ": saved inside the assembly, so it has no file name. Right-click it > Save Part (in External File) and name it.",
                });
            }
            list.EndUpdate();
        }

        /// <summary>Fills the builder from the open document's name.</summary>
        private void StartBuilder()
        {
            CadName top = NamingConvention.Parse(scan.Top.BaseName);
            kind.SelectedIndex = 0; // new parts are the common case
            int teamIndex = top == null ? -1 : teams.Keys.ToList().IndexOf(top.Team);
            team.SelectedIndex = teamIndex >= 0 ? teamIndex : (teams.Count > 0 ? 0 : -1);
            if (top?.Revision != null)
            {
                revision.Text = top.Revision.Value.ToString("00", CultureInfo.InvariantCulture);
            }
            Build();
        }

        private void Build()
        {
            var k = (CadFileKind)Math.Max(0, kind.SelectedIndex);
            string name = NamingConvention.ToPartName(partName.Text);
            bool ok = int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 && n <= 9999;
            int? rev = null;
            if (!standard.Checked)
            {
                ok &= int.TryParse(revision.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int r) && r <= 99;
                rev = r;
            }
            int? ver = null;
            if (version.Text.Trim().Length > 0)
            {
                ok &= int.TryParse(version.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v <= 99;
                ver = v;
            }
            if (TeamCode == null || name.Length == 0 || !ok)
            {
                built.Text = name.Length == 0 ? "Type a part name" : "Check the number, REV, and version";
                built.ForeColor = Muted;
                copyName.Enabled = false;
                return;
            }
            built.Text = NamingConvention.Format(k, TeamCode, n, name, rev, ver);
            NameCheck check = NamingConvention.CheckOne(new NamedFile(built.Text, k), teams.Keys.ToList());
            built.ForeColor = check.Ok ? Good : Caution;
            copyName.Enabled = check.Ok;
        }

        /// <summary>Tab-separated report for a spreadsheet or a message.</summary>
        private string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Status\tFile\tType\tWhat to fix");
            foreach (NameCheck c in checks)
            {
                sb.AppendLine($"{(c.Ok ? "OK" : "Fix")}\t{c.File.BaseName}\t{NamingConvention.KindName(c.File.Kind)}\t{string.Join(" ", c.Problems)}");
            }
            foreach (string v in scan.Virtual)
            {
                sb.AppendLine($"Fix\t{v}\tVirtual\tSave it to its own file and name it.");
            }
            return sb.ToString();
        }
    }
}
