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
    /// <summary>One window that explains every button, section by section.</summary>
    internal sealed class GuideCommand : IAddInCommand
    {
        private readonly Func<IReadOnlyList<IAddInCommand>> allCommands;

        public GuideCommand(Func<IReadOnlyList<IAddInCommand>> allCommands)
        {
            this.allCommands = allCommands;
        }

        public string Title => "Tool Guide";

        public string Hint => "What every LBR CAD Tools button does, how to use it, and what it needs";

        public string Usage => "This window.";

        public ToolGroup Group => ToolGroup.Help;

        public ToolIcon Icon => ToolIcon.Guide;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            using (var form = new GuideForm(allCommands().Where(c => !(c is GuideCommand)).ToList()))
            {
                form.ShowDialog(Ui.MainWindow(app));
            }
        }

        internal sealed class GuideForm : ToolForm
        {
            public GuideForm(IReadOnlyList<IAddInCommand> commands)
                : base("Tool Guide",
                    "Every button on the " + Brand.Product + " tab, section by section. " +
                    "In SolidWorks, hover over a button for a one-line reminder.",
                    ToolIcon.Guide, ToolGroup.Help, 600)
            {
                SuspendLayout();
                var list = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true,
                    Margin = Padding.Empty,
                    Padding = new Padding(0, 0, 0, 8),
                };
                foreach (ToolGroup group in ToolGroups.Order)
                {
                    List<IAddInCommand> members = commands.Where(c => c.Group == group).ToList();
                    if (members.Count == 0)
                    {
                        continue;
                    }
                    var heading = new SectionHeading(ToolGroups.Name(group), ToolGroups.Color(group))
                    {
                        Size = new Size(ContentWidth - 24, 26),
                        Margin = new Padding(0, list.Controls.Count == 0 ? 4 : 18, 0, 0),
                    };
                    list.Controls.Add(heading);
                    Label subtitle = Note(ToolGroups.Subtitle(group), ContentWidth - 24);
                    subtitle.Margin = new Padding(0, 0, 0, 4);
                    list.Controls.Add(subtitle);
                    foreach (IAddInCommand c in members)
                    {
                        list.Controls.Add(Entry(c));
                    }
                }

                var scroller = new Panel
                {
                    AutoScroll = true,
                    Size = new Size(ContentWidth, 500),
                    Margin = new Padding(0, 8, 0, 0),
                };
                scroller.Controls.Add(list);
                Body.Controls.Add(scroller);

                Button close = AddButton("Close", DialogResult.Cancel, primary: true);
                CancelButton = close;
                AcceptButton = close;
                ResumeLayout(true);
            }

            private Control Entry(IAddInCommand c)
            {
                int textWidth = ContentWidth - 24 - 52;
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
                row.Controls.Add(new IconView(c.Icon, ToolGroups.Color(c.Group)) { Size = new Size(36, 36), Margin = new Padding(0, 2, 14, 0) }, 0, 0);

                var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
                text.Controls.Add(new Label
                {
                    Text = c.Title,
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 10f),
                    ForeColor = Brand.Ink,
                    Margin = Padding.Empty,
                });
                text.Controls.Add(new Label
                {
                    Text = c.Hint + ".",
                    AutoSize = true,
                    MaximumSize = new Size(textWidth, 0),
                    ForeColor = Brand.Ink,
                    Margin = new Padding(0, 1, 0, 3),
                });
                text.Controls.Add(Note(c.Usage, textWidth));
                Label where = Note("Works in: " + DocumentTypeNames(c.DocumentTypes), textWidth);
                where.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f, FontStyle.Italic);
                where.Margin = new Padding(0, 3, 0, 0);
                text.Controls.Add(where);
                row.Controls.Add(text, 1, 0);
                return row;
            }

            private static string DocumentTypeNames(IEnumerable<swDocumentTypes_e> types)
            {
                var names = new List<string>();
                foreach (swDocumentTypes_e t in types)
                {
                    switch (t)
                    {
                        case swDocumentTypes_e.swDocPART: names.Add("parts"); break;
                        case swDocumentTypes_e.swDocASSEMBLY: names.Add("assemblies"); break;
                        case swDocumentTypes_e.swDocDRAWING: names.Add("drawings"); break;
                    }
                }
                return string.Join(", ", names);
            }
        }
    }
}
