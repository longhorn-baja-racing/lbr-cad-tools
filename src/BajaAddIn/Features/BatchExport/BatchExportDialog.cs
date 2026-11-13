using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.BatchExport
{
    /// <summary>
    /// Batch Export options: scope, formats, and output folder. Choices are
    /// remembered for the SolidWorks session.
    /// </summary>
    internal sealed class BatchExportDialog : ToolForm
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
            : base("Batch Export",
                $"Exports {documentName} to every format you tick, in one go. Files are named by part number.",
                ToolIcon.BatchExport, ToolGroup.Files, 500)
        {
            this.kind = kind;
            SuspendLayout();

            // ---- Scope ----
            bool assembly = kind == DocKind.Assembly;
            Setup(includeComponents, assembly
                ? $"All {componentCount} unique components (every level of the assembly)"
                : "Components (assemblies only)", assembly && lastIncludeComponents, assembly);
            Setup(skipToolbox, "Skip Toolbox parts (bolts, nuts, washers)", lastSkipToolbox, assembly);
            skipToolbox.Margin = new Padding(20, 3, 0, 3);
            Setup(includeDrawings, "Drawings with the same name as each model (.SLDDRW)",
                lastIncludeDrawings, kind != DocKind.Drawing);
            includeComponents.CheckedChanged += (s, e) => skipToolbox.Enabled = includeComponents.Checked;
            skipToolbox.Enabled = includeComponents.Checked;
            AddSection("What to export",
                $"Always {documentName}. Add more:",
                includeComponents, skipToolbox, includeDrawings);

            // ---- Formats ----
            var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var models = Column("3D models");
            var sheets = Column("Drawings and sheet metal");
            foreach (ExportFormat format in ExportFormats.All)
            {
                var box = new CheckBox();
                Setup(box, ExportFormats.Label(format), lastFormats.Contains(format), true);
                formatBoxes[format] = box;
                bool drawing = format == ExportFormat.Pdf || format == ExportFormat.Dxf || format == ExportFormat.FlatPatternDxf;
                (drawing ? sheets : models).Controls.Add(box);
            }
            columns.Controls.Add(models, 0, 0);
            columns.Controls.Add(sheets, 1, 0);
            UpdateFormatAvailability();
            includeDrawings.CheckedChanged += (s, e) => UpdateFormatAvailability();
            includeComponents.CheckedChanged += (s, e) => UpdateFormatAvailability();
            AddSection("Formats",
                "Grayed-out formats do not apply to anything chosen above (for example PDF needs a drawing).",
                columns);

            // ---- Output ----
            folderBox.Width = ContentWidth - 100;
            folderBox.Margin = new Padding(0, 1, 6, 0);
            folderBox.Text = defaultFolder;
            var browse = new Button { Text = "Browse...", AutoSize = true, Margin = Padding.Empty };
            browse.Click += (s, e) => Browse();
            Setup(openFolder, "Open the folder when done", lastOpenFolder, true);
            openFolder.Margin = new Padding(0, 8, 0, 0);
            AddSection("Output folder",
                "Files with the same name already in this folder are replaced.",
                Row(folderBox, browse), openFolder);

            // ---- Buttons ----
            Button ok = AddButton("Export", primary: true);
            ok.Click += (s, e) => Accept();
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ResumeLayout(true);
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

        private static FlowLayoutPanel Column(string title)
        {
            var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 12, 0) };
            column.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
            return column;
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
