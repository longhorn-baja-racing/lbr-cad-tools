using System.Drawing;
using System.Windows.Forms;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.AutoSave
{
    /// <summary>Auto-Save on/off and interval. Built in code (no designer files).</summary>
    internal sealed class AutoSaveDialog : Form
    {
        private readonly CheckBox enabled = new CheckBox();
        private readonly NumericUpDown interval = new NumericUpDown();

        public AutoSaveDialog(AutoSaveSettings current, string lastStatus)
        {
            Text = "Auto-Save";
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

            enabled.Text = "Save modified documents automatically";
            enabled.AutoSize = true;
            enabled.Checked = current.Enabled;
            layout.Controls.Add(enabled);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(18, 3, 3, 3) };
            row.Controls.Add(new Label { Text = "Every", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
            interval.Minimum = AutoSaveSettings.MinIntervalMinutes;
            interval.Maximum = AutoSaveSettings.MaxIntervalMinutes;
            interval.Value = current.IntervalMinutes;
            interval.Width = 60;
            row.Controls.Add(interval);
            row.Controls.Add(new Label { Text = "minutes", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
            layout.Controls.Add(row);
            enabled.CheckedChanged += (s, e) => interval.Enabled = enabled.Checked;
            interval.Enabled = enabled.Checked;

            var notes = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(3, 10, 3, 3),
                ForeColor = SystemColors.GrayText,
                Text =
                    "Saves the real files, not recovery copies, so there is no going back to the last manual save.\n\n" +
                    "Skipped: documents never saved (use Save once first), read-only documents, " +
                    "and any time a command, PropertyManager, or sketch is open (it tries again a minute later).\n\n" +
                    "If SolidWorks backups are on (Tools > Options > Backup/Recover), each auto-save also writes a backup copy.",
            };
            layout.Controls.Add(notes);

            if (!string.IsNullOrEmpty(lastStatus))
            {
                layout.Controls.Add(new Label { Text = "Last run: " + lastStatus, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 10, 3, 3) });
            }

            var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
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

        public bool AutoSaveEnabled => enabled.Checked;

        public int IntervalMinutes => (int)interval.Value;
    }
}
