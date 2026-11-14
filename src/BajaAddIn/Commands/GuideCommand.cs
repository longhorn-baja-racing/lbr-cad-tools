using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Util;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// One window that explains every button: tools by section on the left,
    /// the selected tool's help on the right, and a button to open it.
    /// </summary>
    internal sealed class GuideCommand : IAddInCommand
    {
        private readonly Func<IReadOnlyList<IAddInCommand>> shownCommands;
        private readonly Action<IAddInCommand> run;

        /// <param name="shownCommands">The commands on the tab.</param>
        /// <param name="run">Runs a command the way a button click does.</param>
        public GuideCommand(Func<IReadOnlyList<IAddInCommand>> shownCommands, Action<IAddInCommand> run)
        {
            this.shownCommands = shownCommands;
            this.run = run;
        }

        public string Title => "Tool Guide";

        public string Hint => "What every LBR CAD Tools button does and how to use it";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "This window: every tool, what it needs, and how to use it, step by step.",
        };

        public ToolGroup Group => ToolGroup.Help;

        public ToolIcon Icon => ToolIcon.Guide;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app) => Show(app, null);

        /// <summary>Opens the guide at <paramref name="start"/> (or the first tool), then runs the tool picked with Open.</summary>
        public void Show(ISldWorks app, IAddInCommand start)
        {
            IAddInCommand chosen;
            List<IAddInCommand> tools = shownCommands().Where(c => !(c is GuideCommand)).ToList();
            using (var form = new GuideForm(tools, c => SafeEnabled(c, app)))
            {
                form.Start = start;
                if (form.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                chosen = form.Chosen;
            }
            if (chosen != null)
            {
                run(chosen);
            }
        }

        private static bool SafeEnabled(IAddInCommand c, ISldWorks app)
        {
            try
            {
                return app == null || c.IsEnabled(app);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal sealed class GuideForm : ToolForm
        {
            private readonly Func<IAddInCommand, bool> isEnabled;
            private readonly ToolNav nav;
            private readonly HelpPage page;
            private readonly Panel scroller;
            private readonly Button open;
            private readonly Label reason;

            public GuideForm(IReadOnlyList<IAddInCommand> commands, Func<IAddInCommand, bool> isEnabled)
                : base("Tool Guide",
                    "Every button on the " + Brand.Product + " tab, grouped the same way. " +
                    "Pick a tool on the left to see what it does and how to use it.",
                    ToolIcon.Guide, ToolGroup.Help, 760)
            {
                this.isEnabled = isEnabled;
                SuspendLayout();
                Body.Padding = Padding.Empty;

                var split = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Size = new Size(ContentWidth + 40, 520),
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                };
                split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                nav = new ToolNav(commands) { Dock = DockStyle.Fill, Margin = Padding.Empty, TabIndex = 0 };
                split.Controls.Add(nav, 0, 0);

                scroller = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    Margin = Padding.Empty,
                    Padding = new Padding(28, 22, 24, 12),
                    BackColor = Color.White,
                };
                page = new HelpPage { Location = new Point(28, 22) };
                scroller.Controls.Add(page);
                scroller.Resize += (s, e) => FitPage();
                split.Controls.Add(scroller, 1, 0);
                Body.Controls.Add(split);

                reason = new Label
                {
                    AutoSize = true,
                    ForeColor = Muted,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(4, 7, 0, 0),
                };
                AddFooterLeft(reason);
                Button close = AddButton("Close", DialogResult.Cancel);
                open = AddButton("Open", DialogResult.OK, primary: true);
                CancelButton = close;
                AcceptButton = open;

                nav.SelectionChanged += (s, e) => ShowSelected();
                ResumeLayout(true);
            }

            /// <summary>Tool to show first; null for the first one.</summary>
            public IAddInCommand Start { get; set; }

            /// <summary>Tool picked with the Open button.</summary>
            public IAddInCommand Chosen => nav.Selected;

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                nav.SelectCommand(Start);
                ActiveControl = nav;
            }

            private void FitPage()
            {
                int w = scroller.ClientSize.Width - scroller.Padding.Horizontal;
                if (w > 0 && page.Width != w)
                {
                    page.Width = w;
                }
            }

            private void ShowSelected()
            {
                IAddInCommand c = nav.Selected;
                if (c == null)
                {
                    return;
                }
                scroller.AutoScrollPosition = Point.Empty;
                FitPage();
                page.Command = c;
                bool enabled = isEnabled(c);
                open.Text = "Open " + c.Title;
                open.Enabled = enabled;
                reason.Text = enabled || string.IsNullOrEmpty(c.Help.Needs) ? "" : "Not available yet: " + c.Help.Needs;
            }
        }
    }
}
