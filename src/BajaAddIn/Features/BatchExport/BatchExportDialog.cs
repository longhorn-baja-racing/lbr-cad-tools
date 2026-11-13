using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.BatchExport
{
    /// <summary>
    /// Batch Export options: formats, scope, and output folder. Built in code
    /// (no designer files). Choices are remembered for the SolidWorks session.
    /// </summary>
    internal sealed class BatchExportDialog : Form
    {
        private static readonly HashSet<ExportFormat> lastFormats = new HashSet<ExportFormat> { ExportFormat.Step, ExportFormat.Pdf };
        private static bool lastIncludeComponents = true;
        private static bool lastSkipToolbox = true;
        private static bool lastIncludeDrawings = true;
        private static bool lastOpenFolder = true;

        private readonly Dictionary<ExportFormat, CheckBox> formatBoxes = new Dictionary<ExportFormat, CheckBox>();
        private readonly CheckBox includeComponents = new CheckBox();
        private readonly CheckBox skipToolbox = new CheckBox();
        private readonly CheckBox includeDrawings = new CheckBox();
        private readonly CheckBox openFolder = new CheckBox();
        private readonly TextBox folderBox = new TextBox();
        private readonly DocKind kind;

        /// <param name="documentName">Shown at the top.</param>
        /// <param name="kind">Kind of the active document.</param>
        /// <param name="componentCount">Unique components (assemblies only).</param>
        /// <param name="defaultFolder">Initial output folder.</param>
        public BatchExportDialog(string documentName, DocKind kind, int componentCount, string defaultFolder)
        {
            this.kind = kind;
            Text = "Batch Export";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(10),
                Dock = DockStyle.Fill,
            };

            layout.Controls.Add(new Label { Text = $"Export {documentName}", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });

            // ---- Scope ----
            bool assembly = kind == DocKind.Assembly;
            Setup(includeComponents, assembly
                ? $"Include all {componentCount} unique components (every level)"
                : "Include components (assemblies only)", assembly && lastIncludeComponents, assembly);
            Setup(skipToolbox, "Skip Toolbox parts (bolts, nuts, washers)", lastSkipToolbox, assembly);
            Setup(includeDrawings, "Include drawings with the same name as each model (.SLDDRW)",
                lastIncludeDrawings, kind != DocKind.Drawing);
            includeComponents.CheckedChanged += (s, e) => skipToolbox.Enabled = includeComponents.Checked;
            skipToolbox.Enabled = includeComponents.Checked;
            layout.Controls.Add(Group("Scope", includeComponents, skipToolbox, includeDrawings));

            // ---- Formats ----
            var boxes = new List<Control>();
            foreach (ExportFormat format in ExportFormats.All)
            {
                var box = new CheckBox();
                Setup(box, ExportFormats.Label(format), lastFormats.Contains(format), true);
                formatBoxes[format] = box;
                boxes.Add(box);
            }
            UpdateFormatAvailability();
            includeDrawings.CheckedChanged += (s, e) => UpdateFormatAvailability();
            includeComponents.CheckedChanged += (s, e) => UpdateFormatAvailability();
            layout.Controls.Add(Group("Formats", boxes.ToArray()));

            // ---- Output ----
            var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            folderBox.Width = 360;
            folderBox.Text = defaultFolder;
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) => Browse();
            folderRow.Controls.Add(folderBox);
            folderRow.Controls.Add(browse);
            Setup(openFolder, "Open the folder when done", lastOpenFolder, true);
            var overwriteNote = new Label
            {
                Text = "Files with the same name in this folder are replaced.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            };
            layout.Controls.Add(Group("Output folder", folderRow, overwriteNote, openFolder));

            // ---- Buttons ----
            var ok = new Button { Text = "Export", AutoSize = true };
            ok.Click += (s, e) => Accept();
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        public BatchExportOptions Options { get; private set; }

        public bool OpenFolderWhenDone => openFolder.Checked;

        private static void Setup(CheckBox box, string text, bool isChecked, bool enabled)
        {
            box.Text = text;
            box.AutoSize = true;
            box.Checked = isChecked && enabled;
            box.Enabled = enabled;
        }

        private static GroupBox Group(string title, params Control[] controls)
        {
            var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            inner.Controls.AddRange(controls);
            var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(470, 0), Padding = new Padding(8) };
            group.Controls.Add(inner);
            return group;
        }

        /// <summary>Formats that cannot apply to anything in the current scope are greyed out.</summary>
        private void UpdateFormatAvailability()
        {
            var kinds = new HashSet<DocKind> { kind };
            if (kind == DocKind.Assembly && includeComponents.Checked)
            {
                kinds.Add(DocKind.Part);
            }
            if (kind != DocKind.Drawing && includeDrawings.Checked)
            {
                kinds.Add(DocKind.Drawing);
            }
            foreach (KeyValuePair<ExportFormat, CheckBox> pair in formatBoxes)
            {
                pair.Value.Enabled = kinds.Any(k => ExportFormats.AppliesTo(pair.Key, k));
            }
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the output folder", ShowNewFolderButton = true })
            {
                string current = folderBox.Text.Trim();
                if (Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Accept()
        {
            List<ExportFormat> formats = formatBoxes.Where(p => p.Value.Enabled && p.Value.Checked).Select(p => p.Key).ToList();
            string folder = folderBox.Text.Trim();
            string error = null;
            if (formats.Count == 0)
            {
                error = "Choose at least one format.";
            }
            else if (folder.Length == 0 || !Path.IsPathRooted(folder))
            {
                error = "Choose an output folder (a full path).";
            }
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var options = new BatchExportOptions
            {
                IncludeComponents = includeComponents.Enabled && includeComponents.Checked,
                SkipToolbox = skipToolbox.Checked,
                IncludeDrawings = includeDrawings.Enabled && includeDrawings.Checked,
                OutputFolder = folder,
            };
            options.Formats.UnionWith(formats);
            Options = options;

            lastFormats.Clear();
            lastFormats.UnionWith(formatBoxes.Where(p => p.Value.Checked).Select(p => p.Key));
            if (includeComponents.Enabled)
            {
                lastIncludeComponents = includeComponents.Checked;
            }
            lastSkipToolbox = skipToolbox.Checked;
            if (includeDrawings.Enabled)
            {
                lastIncludeDrawings = includeDrawings.Checked;
            }
            lastOpenFolder = openFolder.Checked;
            DialogResult = DialogResult.OK;
        }
    }
}
