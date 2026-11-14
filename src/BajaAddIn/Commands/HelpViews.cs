using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Util;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Shared text helpers for the painted help views.</summary>
    internal static class HelpText
    {
        public const TextFormatFlags Wrap =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

        public const TextFormatFlags Line =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        /// <summary>Flags for measuring one line. Never measure with EndEllipsis: it clips to the proposed size.</summary>
        public const TextFormatFlags Measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public static Font Regular(float size) => new Font("Segoe UI", size);

        public static Font Semibold(float size) => new Font("Segoe UI Semibold", size);

        /// <summary>Draws (when <paramref name="paint"/>) wrapped text and returns its height.</summary>
        public static int Block(Graphics g, string text, Font font, Color color, int x, int y, int width, bool paint)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                return 0;
            }
            Size size = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), Wrap);
            if (paint)
            {
                TextRenderer.DrawText(g, text, font, new Rectangle(x, y, width, size.Height), color, Wrap);
            }
            return size.Height;
        }

        /// <summary>"Parts, assemblies" style list of where a command appears.</summary>
        public static IEnumerable<string> DocumentNames(IEnumerable<swDocumentTypes_e> types)
        {
            foreach (swDocumentTypes_e t in types)
            {
                switch (t)
                {
                    case swDocumentTypes_e.swDocPART: yield return "Part"; break;
                    case swDocumentTypes_e.swDocASSEMBLY: yield return "Assembly"; break;
                    case swDocumentTypes_e.swDocDRAWING: yield return "Drawing"; break;
                }
            }
        }

        /// <summary>Lighter version of a color, for tinted backgrounds.</summary>
        public static Color Tint(Color c, float amount) => Color.FromArgb(
            (int)(c.R + (255 - c.R) * amount),
            (int)(c.G + (255 - c.G) * amount),
            (int)(c.B + (255 - c.B) * amount));

        public static GraphicsPath Rounded(RectangleF r, float radius) =>
            ToolIcons.RoundedRect(r.X, r.Y, r.Width, r.Height, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
    }

    /// <summary>
    /// One command's full help: icon and title, then labeled blocks (What it
    /// does, Before you start, Steps, Good to know). Sizes its own height to
    /// the width it is given.
    /// </summary>
    internal sealed class HelpPage : Control
    {
        private IAddInCommand command;

        public HelpPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public IAddInCommand Command
        {
            get => command;
            set
            {
                command = value;
                UpdateHeight();
                Invalidate();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateHeight();
        }

        private void UpdateHeight()
        {
            if (command == null || Width <= 0)
            {
                return;
            }
            using (Graphics g = CreateGraphics())
            {
                int h = Arrange(g, paint: false);
                if (Height != h)
                {
                    Height = h;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (command != null)
            {
                Arrange(e.Graphics, paint: true);
            }
        }

        private int Arrange(Graphics g, bool paint)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = DeviceDpi / 96f;
            int S(float v) => (int)Math.Round(v * k);

            Color accent = ToolGroups.Color(command.Group);
            ToolHelp help = command.Help;
            int width = Width;
            int y = 0;

            using (Font title = HelpText.Semibold(15f))
            using (Font chip = HelpText.Semibold(8f))
            using (Font body = HelpText.Regular(9.5f))
            using (Font label = HelpText.Semibold(8.5f))
            using (Font number = HelpText.Semibold(8.5f))
            {
                // ---- Title row: icon, name, section chip, hint ----
                int icon = S(52);
                if (paint)
                {
                    ToolIcons.Draw(g, new RectangleF(0, 0, icon, icon), command.Icon, accent);
                }
                int tx = icon + S(14);
                int tw = width - tx;
                int ty = S(1);
                ty += HelpText.Block(g, command.Title, title, Brand.Ink, tx, ty, tw, paint);
                ty += S(2);
                ty += Chips(g, chip, tx, ty, accent, paint, k, ToolGroups.Name(command.Group));
                ty += S(6);
                ty += HelpText.Block(g, command.Hint + ".", body, ToolForm.Muted, tx, ty, tw, paint);
                y = Math.Max(icon, ty) + S(16);

                // ---- Blocks ----
                y = Heading(g, label, "WHAT IT DOES", y, width, accent, paint, k);
                y += HelpText.Block(g, help.Purpose, body, Brand.Ink, 0, y, width, paint) + S(16);

                y = Heading(g, label, "WORKS IN", y, width, accent, paint, k);
                y += Chips(g, chip, 0, y, Color.FromArgb(0x5F, 0x6B, 0x73), paint, k,
                           new List<string>(HelpText.DocumentNames(command.DocumentTypes)).ToArray());
                y += S(16);

                if (!string.IsNullOrEmpty(help.Needs))
                {
                    y = Heading(g, label, "BEFORE YOU START", y, width, accent, paint, k);
                    y += Callout(g, body, help.Needs, y, width, paint, k) + S(16);
                }

                if (help.Steps.Count > 0)
                {
                    y = Heading(g, label, "STEPS", y, width, accent, paint, k);
                    int d = S(20);
                    for (int i = 0; i < help.Steps.Count; i++)
                    {
                        int textX = d + S(10);
                        int h = HelpText.Block(g, help.Steps[i], body, Brand.Ink, textX, y + S(1), width - textX, paint);
                        if (paint)
                        {
                            using (var fill = new SolidBrush(accent))
                            {
                                g.FillEllipse(fill, 0, y, d, d);
                            }
                            TextRenderer.DrawText(g, (i + 1).ToString(), number, new Rectangle(0, y, d, d), Color.White,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        y += Math.Max(d, h + S(2)) + S(8);
                    }
                    y += S(8);
                }

                if (help.Notes.Count > 0)
                {
                    y = Heading(g, label, "GOOD TO KNOW", y, width, accent, paint, k);
                    foreach (string note in help.Notes)
                    {
                        int textX = S(16);
                        int h = HelpText.Block(g, note, body, Brand.Ink, textX, y, width - textX, paint);
                        if (paint)
                        {
                            using (var dot = new SolidBrush(accent))
                            {
                                float r = 2.6f * k;
                                float cy = y + body.Height / 2f;
                                g.FillEllipse(dot, S(4) - r, cy - r, 2 * r, 2 * r);
                            }
                        }
                        y += h + S(7);
                    }
                    y += S(8);
                }
            }
            return y;
        }

        /// <summary>Small capitals label with a short colored rule under it.</summary>
        private static int Heading(Graphics g, Font font, string text, int y, int width, Color accent, bool paint, float k)
        {
            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, HelpText.Measure);
            if (paint)
            {
                TextRenderer.DrawText(g, text, font, new Point(0, y), accent, HelpText.Line);
                using (var pen = new Pen(ToolForm.RuleLine))
                {
                    int from = size.Width + (int)(10 * k);
                    int mid = y + size.Height / 2;
                    g.DrawLine(pen, from, mid, width, mid);
                }
            }
            return y + size.Height + (int)(6 * k);
        }

        /// <summary>Pill-shaped labels in a row; returns their height.</summary>
        internal static int Chips(Graphics g, Font font, int x, int y, Color color, bool paint, float k, params string[] texts)
        {
            int h = 0;
            foreach (string t in texts)
            {
                Size size = TextRenderer.MeasureText(g, t, font, Size.Empty, HelpText.Measure);
                var r = new Rectangle(x, y, size.Width + (int)(16 * k), size.Height + (int)(6 * k));
                if (paint)
                {
                    using (GraphicsPath path = HelpText.Rounded(r, r.Height / 2f))
                    using (var back = new SolidBrush(HelpText.Tint(color, 0.86f)))
                    {
                        g.FillPath(back, path);
                    }
                    TextRenderer.DrawText(g, t, font, r, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                x = r.Right + (int)(6 * k);
                h = r.Height;
            }
            return h;
        }

        /// <summary>Text in a pale amber box with a left bar.</summary>
        private static int Callout(Graphics g, Font font, string text, int y, int width, bool paint, float k)
        {
            int pad = (int)(10 * k);
            int bar = (int)(3 * k);
            Size size = TextRenderer.MeasureText(g, text, font, new Size(width - 2 * pad - bar, int.MaxValue), HelpText.Wrap);
            int h = size.Height + 2 * pad - (int)(2 * k);
            if (paint)
            {
                using (var back = new SolidBrush(ToolForm.CautionBack))
                using (var edge = new SolidBrush(Color.FromArgb(0xE0, 0xA8, 0x2E)))
                {
                    g.FillRectangle(back, 0, y, width, h);
                    g.FillRectangle(edge, 0, y, bar, h);
                }
                TextRenderer.DrawText(g, text, font, new Rectangle(bar + pad, y + pad - (int)k, width - 2 * pad - bar, size.Height),
                    Color.FromArgb(0x5C, 0x3D, 0x00), HelpText.Wrap);
            }
            return h;
        }
    }

    /// <summary>
    /// Left-hand list of the Tool Guide: section names with the tools under
    /// them. Click or use the arrow keys to select.
    /// </summary>
    internal sealed class ToolNav : Control
    {
        private readonly List<(ToolGroup Group, IAddInCommand Command)> rows = new List<(ToolGroup, IAddInCommand)>();
        private readonly List<Rectangle> bounds = new List<Rectangle>();
        private int selected = -1;
        private int hover = -1;

        public ToolNav(IReadOnlyList<IAddInCommand> commands)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = ToolForm.FooterBack;
            foreach (ToolGroup group in ToolGroups.Order)
            {
                bool any = false;
                foreach (IAddInCommand c in commands)
                {
                    if (c.Group != group)
                    {
                        continue;
                    }
                    if (!any)
                    {
                        rows.Add((group, null));
                        any = true;
                    }
                    rows.Add((group, c));
                }
            }
            Cursor = Cursors.Hand;
        }

        public event EventHandler SelectionChanged;

        public IAddInCommand Selected => selected >= 0 ? rows[selected].Command : null;

        public void SelectCommand(IAddInCommand command)
        {
            int i = command == null ? -1 : rows.FindIndex(r => r.Command == command);
            if (i < 0)
            {
                i = rows.FindIndex(r => r.Command != null);
            }
            SetSelected(i);
        }

        private void SetSelected(int i)
        {
            if (i < 0 || i >= rows.Count || rows[i].Command == null || i == selected)
            {
                return;
            }
            selected = i;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Measure()
        {
            bounds.Clear();
            float k = DeviceDpi / 96f;
            int y = (int)(10 * k);
            for (int i = 0; i < rows.Count; i++)
            {
                bool header = rows[i].Command == null;
                if (header && i > 0)
                {
                    y += (int)(10 * k);
                }
                int h = (int)((header ? 26 : 30) * k);
                bounds.Add(new Rectangle(0, y, Width, h));
                y += h;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Measure();
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = DeviceDpi / 96f;
            using (Font head = HelpText.Semibold(8f))
            using (Font item = HelpText.Regular(9.5f))
            using (Font itemBold = HelpText.Semibold(9.5f))
            using (var border = new Pen(ToolForm.RuleLine))
            {
                g.DrawLine(border, Width - 1, 0, Width - 1, Height);
                for (int i = 0; i < rows.Count; i++)
                {
                    Rectangle r = bounds[i];
                    (ToolGroup group, IAddInCommand c) = rows[i];
                    Color accent = ToolGroups.Color(group);
                    if (c == null)
                    {
                        TextRenderer.DrawText(g, ToolGroups.Name(group).ToUpperInvariant(), head,
                            new Rectangle((int)(16 * k), r.Y, r.Width - (int)(20 * k), r.Height), accent,
                            HelpText.Line | TextFormatFlags.Bottom);
                        continue;
                    }
                    bool isSelected = i == selected;
                    if (isSelected || i == hover)
                    {
                        using (var back = new SolidBrush(isSelected ? Color.White : Color.FromArgb(0xEA, 0xEC, 0xEE)))
                        {
                            g.FillRectangle(back, r.X, r.Y, r.Width - 1, r.Height);
                        }
                    }
                    if (isSelected)
                    {
                        using (var bar = new SolidBrush(accent))
                        {
                            g.FillRectangle(bar, 0, r.Y, (int)(3 * k), r.Height);
                        }
                    }
                    int icon = (int)(20 * k);
                    ToolIcons.Draw(g, new RectangleF(16 * k, r.Y + (r.Height - icon) / 2f, icon, icon), c.Icon, accent);
                    int tx = (int)(16 * k) + icon + (int)(9 * k);
                    TextRenderer.DrawText(g, c.Title, isSelected ? itemBold : item,
                        new Rectangle(tx, r.Y, r.Width - tx - (int)(6 * k), r.Height), Brand.Ink,
                        HelpText.Line | TextFormatFlags.VerticalCenter);
                }
                if (Focused && selected >= 0)
                {
                    Rectangle f = bounds[selected];
                    ControlPaint.DrawFocusRectangle(g, new Rectangle(f.X + (int)(4 * k), f.Y + 1, f.Width - (int)(9 * k), f.Height - 2));
                }
            }
        }

        private int HitTest(Point p)
        {
            Measure();
            return bounds.FindIndex(b => b.Contains(p));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i >= 0 && rows[i].Command == null)
            {
                i = -1;
            }
            if (i != hover)
            {
                hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            SetSelected(HitTest(e.Location));
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0;
            if (step == 0)
            {
                return;
            }
            for (int i = selected + step; i >= 0 && i < rows.Count; i += step)
            {
                if (rows[i].Command != null)
                {
                    SetSelected(i);
                    break;
                }
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}
