using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Base for the add-in's dialogs, so they all share one layout: a thin
    /// accent strip in the tool's section color, a header with the tool's icon,
    /// name, and a one-line explanation, titled sections in the body, and a
    /// footer bar with the buttons. Built in code (no designer files).
    /// </summary>
    internal class ToolForm : Form
    {
        public static readonly Color Muted = Color.FromArgb(0x5F, 0x6B, 0x73);
        public static readonly Color RuleLine = Color.FromArgb(0xE1, 0xE4, 0xE7);
        public static readonly Color FooterBack = Color.FromArgb(0xF4, 0xF5, 0xF6);
        public static readonly Color Good = Color.FromArgb(0x1E, 0x6B, 0x34);
        public static readonly Color GoodBack = Color.FromArgb(0xE8, 0xF4, 0xEA);
        public static readonly Color Caution = Color.FromArgb(0x8A, 0x5A, 0x00);
        public static readonly Color CautionBack = Color.FromArgb(0xFD, 0xF3, 0xDC);
        public static readonly Color Bad = Color.FromArgb(0xA4, 0x1E, 0x1E);
        public static readonly Color BadBack = Color.FromArgb(0xFB, 0xE9, 0xE9);

        private readonly FlowLayoutPanel leftButtons;
        private readonly FlowLayoutPanel rightButtons;

        /// <param name="title">Window title and header.</param>
        /// <param name="summary">One or two sentences: what this does.</param>
        /// <param name="contentWidth">Width of the body content at 96 dpi.</param>
        public ToolForm(string title, string summary, ToolIcon icon, ToolGroup group, int contentWidth = 460)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.White;
            ForeColor = Brand.Ink;
            Accent = ToolGroups.Color(group);
            ContentWidth = contentWidth;

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Location = Point.Empty,
            };

            root.Controls.Add(Strip(4, Accent));

            // ---- Header ----
            var header = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(20, 16, 20, 14),
            };
            header.Controls.Add(new IconView(icon, Accent) { Size = new Size(44, 44), Margin = new Padding(0, 2, 14, 0) }, 0, 0);
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            text.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Brand.Ink,
                Margin = new Padding(0, 0, 0, 2),
            });
            text.Controls.Add(Note(summary, contentWidth - 58));
            header.Controls.Add(text, 1, 0);
            root.Controls.Add(header);
            root.Controls.Add(Strip(1, RuleLine));

            // ---- Body ----
            Body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = Padding.Empty,
                Padding = new Padding(20, 4, 20, 16),
                MinimumSize = new Size(contentWidth + 40, 0),
            };
            root.Controls.Add(Body);

            // ---- Footer ----
            var footer = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(16, 10, 16, 10),
                BackColor = FooterBack,
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.Paint += (s, e) =>
            {
                using (var pen = new Pen(RuleLine))
                {
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                }
            };
            leftButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            rightButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Right,
            };
            footer.Controls.Add(leftButtons, 0, 0);
            footer.Controls.Add(rightButtons, 1, 0);
            root.Controls.Add(footer);

            Controls.Add(root);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ResumeLayout(false);
        }

        /// <summary>Section color of this tool.</summary>
        protected Color Accent { get; }

        /// <summary>Width of body content at 96 dpi.</summary>
        protected int ContentWidth { get; }

        /// <summary>Sections and other content, top to bottom.</summary>
        protected FlowLayoutPanel Body { get; }

        /// <summary>
        /// Adds a titled section to the body: a heading with a rule, an optional
        /// gray explanation, then the content.
        /// </summary>
        protected FlowLayoutPanel AddSection(string title, string subtitle, params Control[] content)
        {
            var section = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0),
            };
            section.Controls.Add(new SectionHeading(title, Accent) { Size = new Size(ContentWidth, 24) });
            if (!string.IsNullOrEmpty(subtitle))
            {
                Label note = Note(subtitle, ContentWidth);
                note.Margin = new Padding(0, 0, 0, 6);
                section.Controls.Add(note);
            }
            foreach (Control c in content)
            {
                section.Controls.Add(c);
            }
            Body.Controls.Add(section);
            return section;
        }

        /// <summary>
        /// Adds a footer button. Primary buttons are filled in burnt orange; at
        /// most one per dialog. Left buttons sit at the footer's left end.
        /// </summary>
        protected Button AddButton(string text, DialogResult result = DialogResult.None, bool primary = false, bool left = false)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(left ? 0 : 92, 30),
                Padding = new Padding(8, 0, 8, 0),
                Margin = left ? new Padding(0, 0, 8, 0) : new Padding(8, 0, 0, 0),
                DialogResult = result,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                button.Font = new Font(Font, FontStyle.Bold);
                void Paint()
                {
                    button.BackColor = button.Enabled ? Brand.BurntOrange : Color.FromArgb(0xD5, 0xD9, 0xDC);
                    button.FlatAppearance.BorderColor = button.BackColor;
                    button.ForeColor = Color.White;
                }
                Paint();
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xA6, 0x4B, 0x00);
                button.EnabledChanged += (s, e) => Paint();
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Brand.Ink;
                button.FlatAppearance.BorderColor = Color.FromArgb(0xC4, 0xCA, 0xCF);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEC, 0xEE, 0xF0);
            }
            (left ? leftButtons : rightButtons).Controls.Add(button);
            return button;
        }

        /// <summary>Gray explanatory text that wraps at <paramref name="width"/>.</summary>
        public static Label Note(string text, int width) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = Muted,
            Margin = new Padding(0, 0, 0, 0),
        };

        /// <summary>Text in a tinted box, for live status (rule checks, results).</summary>
        public static Label StatusBox(int width) => new Label
        {
            AutoSize = true,
            MinimumSize = new Size(width, 0),
            MaximumSize = new Size(width, 0),
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 4, 0, 0),
        };

        public static void SetStatus(Label box, string text, Color fore, Color back)
        {
            box.Text = text;
            box.ForeColor = fore;
            box.BackColor = back;
        }

        /// <summary>Controls side by side.</summary>
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }

        private static Panel Strip(int height, Color color) => new Panel
        {
            Size = new Size(1, height),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = color,
        };

        /// <summary>
        /// Label / field / trailing text grid. Labels share one column so the
        /// fields line up.
        /// </summary>
        internal sealed class FieldGrid : TableLayoutPanel
        {
            private readonly int fieldWidth;

            public FieldGrid(int labelWidth, int fieldWidth)
            {
                this.fieldWidth = fieldWidth;
                ColumnCount = 3;
                AutoSize = true;
                Margin = Padding.Empty;
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fieldWidth + 6));
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }

            /// <summary>Adds a row; returns the trailing label (units or a hint) so it can change later.</summary>
            public Label Add(string label, Control field, string trailing = null)
            {
                int row = RowCount++;
                Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 4, 8, 4),
                }, 0, row);
                field.Margin = new Padding(0, 3, 6, 3);
                if (!(field is FlowLayoutPanel) && !(field is CheckBox))
                {
                    field.Width = fieldWidth;
                }
                Controls.Add(field, 1, row);
                var tail = new Label
                {
                    Text = trailing ?? "",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    ForeColor = Muted,
                    Margin = new Padding(0, 4, 0, 4),
                };
                Controls.Add(tail, 2, row);
                return tail;
            }

            /// <summary>A control spanning the whole row (check boxes, notes).</summary>
            public void AddWide(Control control, int indent = 0)
            {
                int row = RowCount++;
                control.Margin = new Padding(indent, 3, 0, 3);
                Controls.Add(control, 0, row);
                SetColumnSpan(control, 3);
            }
        }

        /// <summary>Section heading: color marker, title, and a hairline to the right edge.</summary>
        internal sealed class SectionHeading : Control
        {
            private readonly Color accent;

            public SectionHeading(string title, Color accent)
            {
                Text = title;
                this.accent = accent;
                Margin = Padding.Empty;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Font = new Font("Segoe UI Semibold", 10f);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Color.White);
                float k = DeviceDpiScale(g);
                int mid = Height / 2;
                using (var marker = new SolidBrush(accent))
                {
                    g.FillRectangle(marker, 0, mid - (int)(7 * k), (int)(3 * k), (int)(14 * k));
                }
                int x = (int)(10 * k);
                Size size = TextRenderer.MeasureText(g, Text, Font);
                TextRenderer.DrawText(g, Text, Font, new Point(x, mid - size.Height / 2), Brand.Ink, TextFormatFlags.NoPadding);
                using (var pen = new Pen(RuleLine))
                {
                    int from = x + size.Width + (int)(8 * k);
                    if (from < Width)
                    {
                        g.DrawLine(pen, from, mid, Width, mid);
                    }
                }
            }
        }

        /// <summary>A tool icon drawn as vector shapes at whatever size the control has.</summary>
        internal sealed class IconView : Control
        {
            private readonly ToolIcon icon;
            private readonly Color tile;

            public IconView(ToolIcon icon, Color tile)
            {
                this.icon = icon;
                this.tile = tile;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                ToolIcons.Draw(e.Graphics, new RectangleF(0, 0, Width, Height), icon, tile);
            }
        }

        private static float DeviceDpiScale(Graphics g) => g.DpiY / 96f;
    }
}
