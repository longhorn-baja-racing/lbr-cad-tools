using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Util;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Version, credits, and links. Also confirms the add-in loaded.</summary>
    internal sealed class AboutCommand : IAddInCommand
    {
        public string Title => "About";

        public string Hint => "Version, credits, log folder, and where to report problems";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Shows the add-in and SolidWorks versions, links to the project on GitHub, and opens the log folder to attach to a bug report.",
        };

        public ToolGroup Group => ToolGroup.Help;

        public ToolIcon Icon => ToolIcon.About;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            using (var form = new AboutForm(typeof(AboutCommand).Assembly.GetName().Version.ToString(3), app.RevisionNumber()))
            {
                form.ShowDialog(Ui.MainWindow(app));
            }
        }

        private sealed class AboutForm : Form
        {
            public AboutForm(string version, string solidWorksRevision)
            {
                Text = "About " + Brand.Product;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoScaleMode = AutoScaleMode.Dpi;
                Font = SystemFonts.MessageBoxFont;
                BackColor = Color.White;
                ClientSize = new Size(480, 300);

                var header = new Panel { Dock = DockStyle.Top, Height = Brand.HeaderHeight };
                header.Paint += (s, e) => Brand.PaintHeader(e.Graphics, header.ClientRectangle, Brand.Product, "Version " + version);

                var body = new Label
                {
                    AutoSize = false,
                    Location = new Point(18, Brand.HeaderHeight + 14),
                    Size = new Size(444, 96),
                    ForeColor = Brand.Ink,
                    Text = "SolidWorks tools for the " + Brand.Organization + " team: fix states, CSV points, " +
                           "batch export, auto-save, mounting tabs, and suspension checks.\n\n" +
                           "Running in SolidWorks " + solidWorksRevision + ".\n" +
                           "Requests and bug reports: GitHub issues (attach the log).",
                };

                var github = new Button { Text = "GitHub", AutoSize = true };
                github.Click += (s, e) => Process.Start(Brand.RepositoryUrl);
                var logs = new Button { Text = "Open log folder", AutoSize = true };
                logs.Click += (s, e) =>
                {
                    Directory.CreateDirectory(Log.DataDirectory);
                    Process.Start("explorer.exe", "\"" + Log.DataDirectory + "\"");
                };
                var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Location = new Point(12, Brand.HeaderHeight + 116),
                    Size = new Size(456, 36),
                };
                buttons.Controls.Add(close);
                buttons.Controls.Add(logs);
                buttons.Controls.Add(github);

                var footer = new Panel { Dock = DockStyle.Bottom, Height = 34 };
                footer.Paint += (s, e) =>
                {
                    using (var line = new Pen(Color.FromArgb(0xE3, 0xE6, 0xE8)))
                    {
                        e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
                    }
                    Brand.PaintSignature(e.Graphics, new Point(16, 8), BackColor);
                };

                Controls.Add(body);
                Controls.Add(buttons);
                Controls.Add(footer);
                Controls.Add(header);
                CancelButton = close;
            }
        }
    }
}
