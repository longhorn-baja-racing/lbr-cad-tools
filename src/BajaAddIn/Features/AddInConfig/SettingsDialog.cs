using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.AddInConfig
{
    /// <summary>
    /// Shows where settings.json is, what is wrong with it, and which add-ins
    /// exist on this computer, and applies the file without a restart.
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        private readonly AddIn addIn;
        private readonly TextBox problems = new TextBox();

        public SettingsDialog(AddIn addIn)
        {
            this.addIn = addIn;
            Text = "LBR CAD Tools Settings";
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

            // ---- File ----
            var path = new TextBox { Text = SettingsStore.FilePath, ReadOnly = true, Width = 500 };
            var edit = new Button { Text = "Edit in Notepad", AutoSize = true };
            edit.Click += (s, e) => EditFile();
            var folder = new Button { Text = "Open folder", AutoSize = true };
            folder.Click += (s, e) => OpenFolder();
            var fileButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            fileButtons.Controls.Add(edit);
            fileButtons.Controls.Add(folder);
            layout.Controls.Add(Group("Settings file", path, fileButtons, Note(
                "commands.hidden: button names to leave off the tab (takes effect at the next SolidWorks start).\n" +
                "addIns.load / addIns.unload: other add-ins to load or unload when SolidWorks starts, by the titles below " +
                "or by DLL path. In paths, write \\\\ for each \\.\n" +
                "autoSave: also set by the Auto-Save button.")));

            // ---- Problems ----
            problems.Multiline = true;
            problems.ReadOnly = true;
            problems.ScrollBars = ScrollBars.Vertical;
            problems.Width = 500;
            problems.Height = 90;
            layout.Controls.Add(Group("Problems", problems));

            // ---- Add-ins ----
            var list = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 500,
                Height = 110,
                Text = string.Join("\r\n", AddInManager.Installed().Select(a => a.Title)),
            };
            layout.Controls.Add(Group("Add-in titles on this computer (as in Tools > Add-Ins)", list));

            // ---- Buttons ----
            var apply = new Button { Text = "Apply now", AutoSize = true };
            apply.Click += (s, e) => ApplyNow();
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            buttons.Controls.Add(close);
            buttons.Controls.Add(apply);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            CancelButton = close;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowProblems();
        }

        private static Label Note(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 6, 3, 3),
        };

        private static GroupBox Group(string title, params Control[] controls)
        {
            var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            inner.Controls.AddRange(controls);
            var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            group.Controls.Add(inner);
            return group;
        }

        private void ShowProblems()
        {
            List<string> lines = addIn.SettingsProblems;
            problems.Text = lines.Count == 0 ? "None." : string.Join("\r\n", lines);
        }

        private void EditFile()
        {
            try
            {
                if (!File.Exists(SettingsStore.FilePath))
                {
                    // Start from the defaults so every section is there to fill in.
                    SettingsStore.Save(new AddInSettings());
                }
                Process.Start("notepad.exe", "\"" + SettingsStore.FilePath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFolder()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsStore.FilePath));
            Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(SettingsStore.FilePath) + "\"");
        }

        private void ApplyNow()
        {
            addIn.ReloadSettings();
            ShowProblems();
            MessageBox.Show(this,
                addIn.SettingsProblems.Count == 0
                    ? "Settings applied. Hidden buttons change at the next SolidWorks start."
                    : $"Settings applied with {addIn.SettingsProblems.Count} problem(s); see Problems.",
                Text, MessageBoxButtons.OK,
                addIn.SettingsProblems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
