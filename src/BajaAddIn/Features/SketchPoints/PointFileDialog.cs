using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.SketchPoints
{
    /// <summary>
    /// Asks for a CSV file and its units. Built in code (no designer files).
    /// For import, picking a file whose header names a unit selects that unit.
    /// </summary>
    internal sealed class PointFileDialog : Form
    {
        private static readonly LengthUnit[] UnitChoices = { LengthUnit.Millimeter, LengthUnit.Inch, LengthUnit.Meter };
        private static readonly string[] UnitNames = { "Millimeters (mm)", "Inches (in)", "Meters (m)" };

        // Remembered for the SolidWorks session.
        private static LengthUnit lastUnit = LengthUnit.Millimeter;
        private static string lastDirectory;

        private readonly bool import;
        private readonly TextBox pathBox = new TextBox();
        private readonly ComboBox unitBox = new ComboBox();
        private readonly Label unitNote = new Label();
        private readonly Button okButton = new Button();

        public PointFileDialog(bool import, string targetDescription, string suggestedFileName = null)
        {
            this.import = import;
            Text = import ? "Import Points from CSV" : "Export Sketch Points to CSV";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                AutoSize = true,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var target = new Label { Text = targetDescription, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 3, 3, 10) };
            layout.Controls.Add(target, 0, 0);
            layout.SetColumnSpan(target, 3);

            layout.Controls.Add(new Label { Text = "File:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            pathBox.Dock = DockStyle.Fill;
            pathBox.TextChanged += (s, e) => OnPathChanged();
            layout.Controls.Add(pathBox, 1, 1);
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) => Browse();
            layout.Controls.Add(browse, 2, 1);

            layout.Controls.Add(new Label { Text = "Units:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            unitBox.DropDownStyle = ComboBoxStyle.DropDownList;
            unitBox.Items.AddRange(UnitNames);
            unitBox.SelectedIndex = Array.IndexOf(UnitChoices, lastUnit);
            unitBox.Width = 160;
            layout.Controls.Add(unitBox, 1, 2);

            unitNote.AutoSize = true;
            unitNote.ForeColor = SystemColors.GrayText;
            layout.Controls.Add(unitNote, 1, 3);
            layout.SetColumnSpan(unitNote, 2);

            okButton.Text = import ? "Import" : "Export";
            okButton.AutoSize = true;
            okButton.Click += (s, e) => Accept();
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 3);

            Controls.Add(layout);
            AcceptButton = okButton;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            if (!import && suggestedFileName != null)
            {
                pathBox.Text = Path.Combine(lastDirectory ?? DefaultDirectory(), suggestedFileName);
            }
            OnPathChanged();
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

    /// <summary>Lets a SolidWorks frame handle own a WinForms dialog.</summary>
    internal sealed class WindowHandle : IWin32Window
    {
        public WindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        /// <summary>The SolidWorks main window, or null when it cannot be found.</summary>
        public static WindowHandle Of(ISldWorks app) =>
            app.Frame() is IFrame frame ? new WindowHandle(new IntPtr(frame.GetHWndx64())) : null;
    }
}
