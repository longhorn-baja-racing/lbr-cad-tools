using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.SketchPoints
{
    /// <summary>
    /// Asks for a CSV file and its units.
    /// For import, picking a file whose header names a unit selects that unit.
    /// </summary>
    internal sealed class PointFileDialog : ToolForm
    {
        private static readonly LengthUnit[] UnitChoices = { LengthUnit.Millimeter, LengthUnit.Inch, LengthUnit.Meter };
        private static readonly string[] UnitNames = { "Millimeters (mm)", "Inches (in)", "Meters (m)" };

        // Remembered for the SolidWorks session.
        private static LengthUnit lastUnit = LengthUnit.Millimeter;
        private static string lastDirectory;

        private readonly bool import;
        private readonly TextBox pathBox = new TextBox();
        private readonly ComboBox unitBox = new ComboBox();
        private readonly Label unitNote;

        public PointFileDialog(bool import, string targetDescription, string suggestedFileName = null)
            : base(import ? "Import Points" : "Export Points",
                import
                    ? "Reads X, Y, Z rows from a CSV file and adds each row as a sketch point."
                    : "Writes the points of a sketch to a CSV file, one X, Y, Z row per point.",
                import ? ToolIcon.ImportPoints : ToolIcon.ExportPoints, ToolGroup.SketchPoints, 480)
        {
            this.import = import;
            SuspendLayout();

            Label target = Note(targetDescription, ContentWidth);
            target.ForeColor = Brand.Ink;
            AddSection(import ? "Where the points go" : "What gets exported", null, target);

            pathBox.Width = ContentWidth - 100;
            pathBox.Margin = new Padding(0, 1, 6, 0);
            pathBox.TextChanged += (s, e) => OnPathChanged();
            var browse = new Button { Text = "Browse...", AutoSize = true, Margin = Padding.Empty };
            browse.Click += (s, e) => Browse();
            AddSection("CSV file",
                import
                    ? "Comma separated X, Y, Z. A header row and extra columns are fine."
                    : "You are asked before an existing file is replaced.",
                Row(pathBox, browse));

            unitBox.DropDownStyle = ComboBoxStyle.DropDownList;
            unitBox.Items.AddRange(UnitNames);
            unitBox.SelectedIndex = Array.IndexOf(UnitChoices, lastUnit);
            unitBox.Width = 180;
            unitNote = Note("", ContentWidth);
            unitNote.Margin = new Padding(0, 6, 0, 0);
            AddSection("Units",
                import
                    ? "The units of the numbers in the file."
                    : "The units to write the numbers in. The header row names them, so Import Points picks them up.",
                unitBox, unitNote);

            Button ok = AddButton(import ? "Import" : "Export", primary: true);
            ok.Click += (s, e) => Accept();
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (!import && suggestedFileName != null)
            {
                pathBox.Text = Path.Combine(lastDirectory ?? DefaultDirectory(), suggestedFileName);
            }
            OnPathChanged();
            ResumeLayout(true);
        }

        public string FilePath => pathBox.Text.Trim();

        public LengthUnit Unit => UnitChoices[unitBox.SelectedIndex];

        private static string DefaultDirectory() =>
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

        private void Browse()
        {
            FileDialog dialog = import
                ? (FileDialog)new OpenFileDialog { CheckFileExists = true }
                : new SaveFileDialog { OverwritePrompt = false, DefaultExt = "csv" }; // Accept() asks
            using (dialog)
            {
                dialog.Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                string current = FilePath;
                dialog.InitialDirectory = current.Length > 0 && Directory.Exists(Path.GetDirectoryName(current))
                    ? Path.GetDirectoryName(current)
                    : lastDirectory ?? DefaultDirectory();
                if (current.Length > 0)
                {
                    dialog.FileName = Path.GetFileName(current);
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    pathBox.Text = dialog.FileName;
                }
            }
        }

        private void OnPathChanged()
        {
            unitNote.Text = "";
            if (!import || !File.Exists(FilePath))
            {
                return;
            }
            try
            {
                LengthUnit? detected = PointCsv.DetectUnit(File.ReadAllText(FilePath));
                if (detected.HasValue)
                {
                    unitBox.SelectedIndex = Array.IndexOf(UnitChoices, detected.Value);
                    unitNote.Text = "Units taken from the file header.";
                }
                else
                {
                    unitNote.Text = "The file does not say its units. Check this is right.";
                }
            }
            catch (IOException ex)
            {
                unitNote.Text = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                unitNote.Text = ex.Message;
            }
        }

        private void Accept()
        {
            string path = FilePath;
            string error = null;
            if (path.Length == 0)
            {
                error = "Choose a file.";
            }
            else if (import && !File.Exists(path))
            {
                error = "That file does not exist.";
            }
            else if (!import && !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ""))
            {
                error = "That folder does not exist.";
            }
            else if (!import && File.Exists(path) && MessageBox.Show(this,
                         $"{Path.GetFileName(path)} already exists. Replace it?", Text,
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            lastUnit = Unit;
            lastDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            DialogResult = DialogResult.OK;
        }
    }
}
