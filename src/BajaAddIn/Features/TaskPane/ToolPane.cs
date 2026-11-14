using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Util;

namespace Lbr.CadTools.AddIn.Features.TaskPane
{
    /// <summary>
    /// Contents of the LBR CAD Tools task pane: every button on the tab, by
    /// section, with a heading and a sub-label per section and a short
    /// description per tool. Click a tool to run it; the "?" opens its page in
    /// the Tool Guide. Tools that do not apply to the open document are grayed
    /// out and say what they need.
    /// </summary>
    internal sealed class ToolPane : UserControl
    {
        private readonly Canvas canvas;
        private readonly Timer refresh;

        /// <param name="commands">Commands on the tab, in tab order.</param>
        /// <param name="isEnabled">Same answer as the button's enable state.</param>
        /// <param name="run">Runs a command the way a button click does.</param>
        /// <param name="explain">Opens the Tool Guide at a command.</param>
        public ToolPane(IReadOnlyList<IAddInCommand> commands, Func<IAddInCommand, bool> isEnabled,
                        Action<IAddInCommand> run, Action<IAddInCommand> explain)
        {
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            BackColor = Color.White;
            Font = SystemFonts.MessageBoxFont;
            canvas = new Canvas(commands, isEnabled, run, explain) { Location = Point.Empty };
            Controls.Add(canvas);

            // Enable states follow the open document; poll only while the pane is on screen.
            refresh = new Timer { Interval = 800 };
            refresh.Tick += (s, e) =>
            {
                if (IsHandleCreated && IsWindowVisible(Handle))
                {
                    canvas.RefreshStates();
                }
            };
            refresh.Start();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w = ClientSize.Width;
            if (canvas != null && w > 0 && canvas.Width != w)
            {
                canvas.Width = w;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refresh.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>Painted list; sizes its height to its width.</summary>
        internal sealed class Canvas : Control
        {
            private readonly List<IAddInCommand> commands;
            private readonly Func<IAddInCommand, bool> isEnabled;
            private readonly Action<IAddInCommand> run;
            private readonly Action<IAddInCommand> explain;
            private readonly Dictionary<IAddInCommand, bool> enabled = new Dictionary<IAddInCommand, bool>();
            private readonly List<(Rectangle Card, Rectangle Help, IAddInCommand Command)> hits =
                new List<(Rectangle, Rectangle, IAddInCommand)>();
            private readonly ToolTip tip = new ToolTip { InitialDelay = 600, AutoPopDelay = 8000 };
            private IAddInCommand hover;
            private bool hoverHelp;

            public Canvas(IReadOnlyList<IAddInCommand> commands, Func<IAddInCommand, bool> isEnabled,
                          Action<IAddInCommand> run, Action<IAddInCommand> explain)
            {
                this.commands = commands.ToList();
                this.isEnabled = isEnabled;
                this.run = run;
                this.explain = explain;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
                foreach (IAddInCommand c in this.commands)
                {
                    enabled[c] = true;
                }
            }

            public void RefreshStates()
            {
                bool changed = false;
                foreach (IAddInCommand c in commands)
                {
                    bool now = isEnabled(c);
                    if (enabled[c] != now)
                    {
                        enabled[c] = now;
                        changed = true;
                    }
                }
                if (changed)
                {
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
                if (Width <= 0 || !IsHandleCreated)
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

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                RefreshStates();
                UpdateHeight();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                Arrange(e.Graphics, paint: true);
            }

            private int Arrange(Graphics g, bool paint)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = DeviceDpi / 96f;
                int S(float v) => (int)Math.Round(v * k);
                int width = Width;
                int pad = S(12);
                hits.Clear();

                using (Font brand = HelpText.Semibold(11f))
                using (Font small = HelpText.Regular(8.5f))
                using (Font section = HelpText.Semibold(8.5f))
                using (Font title = HelpText.Semibold(9.5f))
                using (Font help = HelpText.Semibold(8f))
                {
                    // ---- Header band ----
                    int band = S(58);
                    if (paint)
                    {
                        using (var fill = new SolidBrush(Brand.BurntOrange))
                        {
                            g.FillRectangle(fill, 0, 0, width, band);
                        }
                        int icon = S(34);
                        // White plate so the orange logo stands out on the orange band.
                        using (GraphicsPath plate = HelpText.Rounded(new RectangleF(pad - S(3), (band - icon) / 2f - S(3), icon + S(6), icon + S(6)), S(9)))
                        {
                            g.FillPath(Brushes.White, plate);
                        }
                        using (Bitmap logo = Brand.ProductIcon(icon))
                        {
                            g.DrawImage(logo, pad, (band - icon) / 2);
                        }
                        int tx = pad + icon + S(10);
                        TextRenderer.DrawText(g, Brand.Product, brand, new Rectangle(tx, S(10), width - tx - pad, S(22)),
                            Color.White, HelpText.Line);
                        TextRenderer.DrawText(g, Brand.Organization, small, new Rectangle(tx, S(31), width - tx - pad, S(18)),
                            Color.FromArgb(0xFF, 0xE6, 0xD2), HelpText.Line);
                    }
                    int y = band + S(10);
                    y += HelpText.Block(g, "Click a tool to run it. Click ? to learn how it works.", small,
                        ToolForm.Muted, pad, y, width - 2 * pad, paint) + S(4);

                    // ---- Sections ----
                    foreach (ToolGroup group in ToolGroups.Order)
                    {
                        List<IAddInCommand> members = commands.Where(c => c.Group == group).ToList();
                        if (members.Count == 0)
                        {
                            continue;
                        }
                        Color accent = ToolGroups.Color(group);
                        y += S(14);
                        int headH = TextRenderer.MeasureText(g, "A", section, Size.Empty, HelpText.Measure).Height;
                        if (paint)
                        {
                            using (var bar = new SolidBrush(accent))
                            {
                                g.FillRectangle(bar, pad, y + S(1), S(3), headH - S(2));
                            }
                            string name = ToolGroups.Name(group).ToUpperInvariant();
                            int nx = pad + S(9);
                            TextRenderer.DrawText(g, name, section, new Point(nx, y), accent, HelpText.Line);
                            int after = nx + TextRenderer.MeasureText(g, name, section, Size.Empty, HelpText.Measure).Width + S(8);
                            using (var rule = new Pen(ToolForm.RuleLine))
                            {
                                g.DrawLine(rule, after, y + headH / 2, width - pad, y + headH / 2);
                            }
                        }
                        y += headH + S(2);
                        y += HelpText.Block(g, ToolGroups.Subtitle(group), small, ToolForm.Muted, pad, y, width - 2 * pad, paint);
                        y += S(6);

                        bool compact = ToolGroups.SmallButtons(group);
                        foreach (IAddInCommand c in members)
                        {
                            y += Card(g, c, accent, compact, pad, y, width - 2 * pad, paint, k, title, small, help) + S(4);
                        }
                    }
                    y += S(10);

                    // ---- Footer: made by ----
                    if (paint)
                    {
                        using (var rule = new Pen(ToolForm.RuleLine))
                        {
                            g.DrawLine(rule, pad, y, width - pad, y);
                        }
                        Brand.PaintSignature(g, new Point(pad, y + S(8)), BackColor);
                    }
                    y += S(36);
                    return y;
                }
            }

            /// <summary>One tool: icon, name, and a line of text. Returns its height.</summary>
            private int Card(Graphics g, IAddInCommand c, Color accent, bool compact, int x, int y, int w,
                             bool paint, float k, Font title, Font small, Font helpFont)
            {
                int S(float v) => (int)Math.Round(v * k);
                bool on = enabled[c];
                int inner = S(8);
                int icon = S(compact ? 22 : 32);
                int tx = x + inner + icon + S(10);
                int helpSize = S(18);
                int tw = w - (tx - x) - inner - helpSize - S(6);

                string detail = on || string.IsNullOrEmpty(c.Help.Needs) ? c.Hint : "Needs: " + c.Help.Needs;
                int titleH = TextRenderer.MeasureText(g, c.Title, title, Size.Empty, HelpText.Measure).Height;
                int detailH = compact ? 0 : TextRenderer.MeasureText(g, detail, small, new Size(Math.Max(tw, 1), int.MaxValue), HelpText.Wrap).Height;
                int textH = titleH + (compact ? 0 : S(1) + detailH);
                int h = Math.Max(icon, textH) + 2 * inner;
                var card = new Rectangle(x, y, w, h);
                var helpBox = new Rectangle(card.Right - inner - helpSize, y + (compact ? (h - helpSize) / 2 : inner), helpSize, helpSize);
                hits.Add((card, helpBox, c));

                if (!paint)
                {
                    return h;
                }

                bool hot = hover == c;
                if (hot)
                {
                    using (GraphicsPath path = HelpText.Rounded(card, S(6)))
                    using (var back = new SolidBrush(on ? HelpText.Tint(accent, 0.92f) : Color.FromArgb(0xF4, 0xF5, 0xF6)))
                    using (var edge = new Pen(on ? HelpText.Tint(accent, 0.6f) : ToolForm.RuleLine))
                    {
                        g.FillPath(back, path);
                        g.DrawPath(edge, path);
                    }
                }

                Color tile = on ? accent : Color.FromArgb(0xB4, 0xBB, 0xC1);
                ToolIcons.Draw(g, new RectangleF(x + inner, y + (compact ? (h - icon) / 2f : inner), icon, icon), c.Icon, tile);

                int ty = y + (compact ? (h - titleH) / 2 : inner + Math.Max(0, (icon - textH) / 2));
                TextRenderer.DrawText(g, c.Title, title, new Rectangle(tx, ty, tw, titleH),
                    on ? Brand.Ink : Color.FromArgb(0x8A, 0x94, 0x9C), HelpText.Line);
                if (!compact)
                {
                    Color detailColor = on ? ToolForm.Muted : ToolForm.Caution;
                    TextRenderer.DrawText(g, detail, small, new Rectangle(tx, ty + titleH + S(1), tw, detailH), detailColor, HelpText.Wrap);
                }

                // "?" button: always faintly visible, stronger on hover.
                Color ring = hot && hoverHelp ? accent : hot ? Color.FromArgb(0x9A, 0xA3, 0xAB) : Color.FromArgb(0xC9, 0xCF, 0xD4);
                using (var pen = new Pen(ring, Math.Max(1f, 1.2f * k)))
                {
                    if (hot && hoverHelp)
                    {
                        using (var fill = new SolidBrush(accent))
                        {
                            g.FillEllipse(fill, helpBox);
                        }
                    }
                    g.DrawEllipse(pen, helpBox);
                }
                TextRenderer.DrawText(g, "?", helpFont, helpBox, hot && hoverHelp ? Color.White : ring,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return h;
            }

            private (IAddInCommand Command, bool Help) HitTest(Point p)
            {
                foreach ((Rectangle card, Rectangle help, IAddInCommand c) in hits)
                {
                    if (card.Contains(p))
                    {
                        Rectangle h = help;
                        h.Inflate(3, 3);
                        return (c, h.Contains(p));
                    }
                }
                return (null, false);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                (IAddInCommand c, bool help) = HitTest(e.Location);
                if (c != hover || help != hoverHelp)
                {
                    hover = c;
                    hoverHelp = help;
                    Cursor = c == null || (!help && !enabled[c]) ? Cursors.Default : Cursors.Hand;
                    tip.SetToolTip(this, c == null ? null : help ? $"How to use {c.Title}" : c.Hint);
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hover = null;
                hoverHelp = false;
                Invalidate();
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }
                (IAddInCommand c, bool help) = HitTest(e.Location);
                if (c == null)
                {
                    return;
                }
                if (help)
                {
                    explain(c);
                }
                else if (isEnabled(c))
                {
                    run(c);
                    RefreshStates();
                }
                else
                {
                    explain(c);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    tip.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
