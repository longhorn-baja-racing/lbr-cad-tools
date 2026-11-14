using System.Drawing;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.AutoSave
{
    /// <summary>Auto-Save on/off and interval.</summary>
    internal sealed class AutoSaveDialog : ToolForm
    {
        private readonly CheckBox enabled = new CheckBox();
        private readonly NumericUpDown interval = new NumericUpDown();

        public AutoSaveDialog(AutoSaveSettings current, string lastStatus)
            : base("Auto-Save",
                "Saves your modified documents on a timer, so a crash costs minutes of work instead of hours.",
                ToolIcon.AutoSave, ToolGroup.Files, 420)
        {
            SuspendLayout();

            enabled.Text = "Save modified documents automatically";
            enabled.AutoSize = true;
            enabled.Checked = current.Enabled;
            enabled.Font = new Font(Font, FontStyle.Bold);

            interval.Minimum = AutoSaveSettings.MinIntervalMinutes;
            interval.Maximum = AutoSaveSettings.MaxIntervalMinutes;
            interval.Value = current.IntervalMinutes;
            interval.Width = 60;
            interval.Margin = new Padding(0, 0, 6, 0);
            var every = Row(
                new Label { Text = "Every", AutoSize = true, Margin = new Padding(0, 4, 6, 0) },
                interval,
                new Label { Text = "minutes", AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
            every.Margin = new Padding(20, 6, 0, 0);
            enabled.CheckedChanged += (s, e) => interval.Enabled = enabled.Checked;
            interval.Enabled = enabled.Checked;
            AddSection("Schedule", "Stays on for every SolidWorks session until you turn it off here.", enabled, every);

            AddSection("Good to know", null, Note(
                "- It saves the real files, not recovery copies, so there is no going back to your last manual save.\n" +
                "- Documents never saved (use Save once first) and read-only documents are skipped.\n" +
                "- While a command, PropertyManager, or sketch is open it waits and tries again a minute later.\n" +
                "- If SolidWorks backups are on (Tools > Options > Backup/Recover), each auto-save also writes a backup copy.",
                ContentWidth));

            if (!string.IsNullOrEmpty(lastStatus))
            {
                Label status = StatusBox(ContentWidth);
                SetStatus(status, "Last run: " + lastStatus, Brand.Ink, FooterBack);
                AddSection("Status", null, status);
            }

            Button ok = AddButton("OK", DialogResult.OK, primary: true);
            Button cancel = AddButton("Cancel", DialogResult.Cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ResumeLayout(true);
        }

        public bool AutoSaveEnabled => enabled.Checked;

        public int IntervalMinutes => (int)interval.Value;
    }
}
