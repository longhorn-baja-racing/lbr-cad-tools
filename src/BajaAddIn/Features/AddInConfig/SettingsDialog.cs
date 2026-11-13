using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Features.AddInConfig
{
    /// <summary>
    /// Shows where settings.json is, what is wrong with it, and which add-ins
    /// exist on this computer, and applies the file without a restart.
    /// </summary>
    internal sealed class SettingsDialog : ToolForm
    {
        private readonly AddIn addIn;
        private readonly Label problems;

        public SettingsDialog(AddIn addIn)
            : base("Settings",
                "Hide buttons you do not use, load or unload other add-ins when SolidWorks starts, and fix problems in the settings file.",
                ToolIcon.Settings, ToolGroup.Help, 500)
        {
            this.addIn = addIn;
            SuspendLayout();

            // ---- File ----
            var path = new TextBox { Text = SettingsStore.FilePath, ReadOnly = true, Width = ContentWidth, Margin = new Padding(0, 0, 0, 6) };
            var edit = new Button { Text = "Edit in Notepad", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
            edit.Click += (s, e) => EditFile();
            var folder = new Button { Text = "Open folder", AutoSize = true, Margin = Padding.Empty };
            folder.Click += (s, e) => OpenFolder();
            AddSection("Settings file", "All settings live in one JSON file. Edit it, save it, then click Apply now.",
                path, Row(edit, folder));

            // ---- What can be set ----
            var keys = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            keys.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            keys.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddKey(keys, "commands.hidden", "Button names to leave off the tab. Takes effect at the next SolidWorks start.");
            AddKey(keys, "addIns.load", "Other add-ins to load when SolidWorks starts, by title (listed below) or by DLL path. In paths, write \\\\ for each \\.");
            AddKey(keys, "addIns.unload", "Add-ins to unload when SolidWorks starts, the same way.");
            AddKey(keys, "autoSave", "Also set by the Auto-Save button.");
            AddKey(keys, "suspension", "Model axes and tire radius used by Suspension Check.");
            AddSection("What you can set", null, keys);

            // ---- Problems ----
            problems = StatusBox(ContentWidth);
            AddSection("Problems", "Read from the file when SolidWorks started, or when you last clicked Apply now.", problems);

            // ---- Add-ins ----
            var list = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Width = ContentWidth,
                Height = 110,
                BackColor = FooterBack,
                Text = string.Join("\r\n", AddInManager.Installed().Select(a => a.Title)),
            };
            AddSection("Add-ins on this computer", "Titles as in Tools > Add-Ins, for addIns.load and addIns.unload.", list);

            // ---- Buttons ----
            Button apply = AddButton("Apply now", primary: true);
            apply.Click += (s, e) => ApplyNow();
            Button close = AddButton("Close", DialogResult.Cancel);
            CancelButton = close;
            ShowProblems();
            ResumeLayout(true);
        }

        /// <summary>One row of the key table: the JSON key in a code font, then what it does.</summary>
        private void AddKey(TableLayoutPanel table, string key, string text)
        {
            int row = table.RowCount++;
            table.Controls.Add(new Label
            {
                Text = key,
                AutoSize = true,
                Font = new Font("Consolas", 9f),
                ForeColor = Brand.Ink,
                Margin = new Padding(0, 3, 8, 3),
            }, 0, row);
            Label what = Note(text, ContentWidth - 140);
            what.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(what, 1, row);
        }

        private void ShowProblems()
        {
            List<string> lines = addIn.SettingsProblems;
            if (lines.Count == 0)
            {
                SetStatus(problems, "No problems found.", Good, GoodBack);
            }
            else
            {
                SetStatus(problems, string.Join("\n", lines.Select(l => "- " + l)), Caution, CautionBack);
            }
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
