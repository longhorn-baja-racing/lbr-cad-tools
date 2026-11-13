using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>Pictograms for the command buttons, dialogs, and the Tool Guide.</summary>
    internal enum ToolIcon
    {
        FixAll,
        FloatAll,
        RestoreFixStates,
        ImportPoints,
        ExportPoints,
        BatchExport,
        MountingTab,
        SuspensionCheck,
        AutoSave,
        Settings,
        About,
        Guide,
    }

    /// <summary>
    /// Draws each <see cref="ToolIcon"/> as a white pictogram on a rounded
    /// tile in its section color. Shapes are laid out in a 100 x 100 box and
    /// scaled, and kept bold enough to read at the 20 px toolbar size.
    /// </summary>
    internal static class ToolIcons
    {
        public static Bitmap Render(ToolIcon icon, Color tile, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Draw(g, new RectangleF(0, 0, size, size), icon, tile);
            }
            return bmp;
        }

        public static void Draw(Graphics g, RectangleF cell, ToolIcon icon, Color tile)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(cell.X, cell.Y);
                g.ScaleTransform(cell.Width / 100f, cell.Height / 100f);

                using (GraphicsPath back = RoundedRect(4, 4, 92, 92, 20))
                using (var fill = new SolidBrush(tile))
                {
                    g.FillPath(fill, back);
                }

                using (var ink = new SolidBrush(Color.White))
                using (var cut = new SolidBrush(tile))
                {
                    var p = new Painter(g, ink, cut, tile);
                    switch (icon)
                    {
                        case ToolIcon.FixAll: Lock(p, closed: true); break;
                        case ToolIcon.FloatAll: Lock(p, closed: false); break;
                        case ToolIcon.RestoreFixStates: Restore(p); break;
                        case ToolIcon.ImportPoints: Points(p, import: true); break;
                        case ToolIcon.ExportPoints: Points(p, import: false); break;
                        case ToolIcon.BatchExport: Sheets(p); break;
                        case ToolIcon.MountingTab: Tab(p); break;
                        case ToolIcon.SuspensionCheck: Wheel(p); break;
                        case ToolIcon.AutoSave: Disk(p); break;
                        case ToolIcon.Settings: Gear(p); break;
                        case ToolIcon.About: Info(p); break;
                        case ToolIcon.Guide: Book(p); break;
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private sealed class Painter
        {
            public Painter(Graphics g, Brush ink, Brush cut, Color tile)
            {
                G = g;
                Ink = ink;
                Cut = cut;
                Tile = tile;
            }

            public Graphics G { get; }
            public Brush Ink { get; }
            public Brush Cut { get; }
            public Color Tile { get; }

            public Pen InkPen(float width) =>
                new Pen(Color.White, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            public Pen CutPen(float width) =>
                new Pen(Tile, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            public void Circle(Brush b, float cx, float cy, float r) => G.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
        }

        // ---- Assembly ----

        /// <summary>Padlock; open has the shackle lifted out of the body on the left.</summary>
        private static void Lock(Painter p, bool closed)
        {
            float lift = closed ? 0 : 12;
            using (Pen shackle = p.InkPen(10))
            {
                shackle.StartCap = shackle.EndCap = LineCap.Flat;
                p.G.DrawArc(shackle, 33, 16 - lift, 34, 34, 180, 180);
                p.G.DrawLine(shackle, 67, 33 - lift, 67, 50);
                p.G.DrawLine(shackle, 33, 33 - lift, 33, closed ? 50 : 36 - lift);
            }
            using (GraphicsPath body = RoundedRect(22, 44, 56, 40, 8))
            {
                p.G.FillPath(p.Ink, body);
            }
            p.Circle(p.Cut, 50, 60, 6.5f);
            p.G.FillRectangle(p.Cut, 47, 60, 6, 13);
        }

        /// <summary>Undo-style arrow (over the top, down on the left) around a small padlock.</summary>
        private static void Restore(Painter p)
        {
            const float cx = 52, cy = 54, r = 30, start = 50, sweep = -230;
            using (Pen arc = p.InkPen(10))
            {
                arc.EndCap = LineCap.Flat;
                p.G.DrawArc(arc, cx - r, cy - r, 2 * r, 2 * r, start, sweep);
            }
            // Arrowhead pointing along the arc's direction of travel at its end.
            float end = start + sweep;
            PointF at = OnCircle(cx, cy, r, end);
            double a = end * Math.PI / 180;
            var dir = new PointF((float)Math.Sin(a), (float)-Math.Cos(a));
            var side = new PointF(-dir.Y, dir.X);
            p.G.FillPolygon(p.Ink, new[]
            {
                new PointF(at.X + dir.X * 14, at.Y + dir.Y * 14),
                new PointF(at.X + side.X * 13 - dir.X * 3, at.Y + side.Y * 13 - dir.Y * 3),
                new PointF(at.X - side.X * 13 - dir.X * 3, at.Y - side.Y * 13 - dir.Y * 3),
            });
            using (GraphicsPath body = RoundedRect(41, 51, 22, 17, 3))
            {
                p.G.FillPath(p.Ink, body);
            }
            using (Pen shackle = p.InkPen(5))
            {
                p.G.DrawArc(shackle, 45, 41, 14, 16, 180, 180);
            }
        }

        // ---- Sketch points ----

        /// <summary>Three sketch points with an arrow coming in (import) or going out (export).</summary>
        private static void Points(Painter p, bool import)
        {
            using (Pen link = p.InkPen(4) )
            {
                link.DashStyle = DashStyle.Dot;
                p.G.DrawLines(link, new[] { new PointF(22, 78), new PointF(50, 66), new PointF(78, 78) });
            }
            foreach (PointF pt in new[] { new PointF(22, 78), new PointF(50, 66), new PointF(78, 78) })
            {
                p.Circle(p.Ink, pt.X, pt.Y, 8);
                p.Circle(p.Cut, pt.X, pt.Y, 3);
            }

            // Vertical arrow above the middle point.
            float top = 14, bottom = 52;
            if (import)
            {
                p.G.FillRectangle(p.Ink, 44, top, 12, 22);
                p.G.FillPolygon(p.Ink, new[] { new PointF(30, 34), new PointF(70, 34), new PointF(50, bottom) });
            }
            else
            {
                p.G.FillRectangle(p.Ink, 44, top + 16, 12, 22);
                p.G.FillPolygon(p.Ink, new[] { new PointF(30, 32), new PointF(70, 32), new PointF(50, top - 2) });
            }
        }

        // ---- Files ----

        /// <summary>Stack of three sheets; the front one carries an arrow.</summary>
        private static void Sheets(Painter p)
        {
            using (Pen gap = p.CutPen(5))
            {
                foreach (float o in new[] { 16f, 8f, 0f })
                {
                    using (GraphicsPath sheet = Sheet(22 + o, 30 - o, 44, 54, 12))
                    {
                        p.G.FillPath(p.Ink, sheet);
                        p.G.DrawPath(gap, sheet);
                    }
                }
            }
            using (Pen arrow = p.CutPen(7))
            {
                p.G.DrawLine(arrow, 32, 62, 50, 62);
            }
            p.G.FillPolygon(p.Cut, new[] { new PointF(47, 52), new PointF(59, 62), new PointF(47, 72) });
        }

        /// <summary>Floppy disk with a clock badge.</summary>
        private static void Disk(Painter p)
        {
            using (GraphicsPath disk = Sheet(14, 14, 56, 56, 12, cornerLeft: false))
            {
                p.G.FillPath(p.Ink, disk);
            }
            p.G.FillRectangle(p.Cut, 26, 14, 28, 16);
            p.G.FillRectangle(p.Ink, 43, 17, 7, 10);
            using (GraphicsPath label = RoundedRect(23, 42, 38, 28, 3))
            {
                p.G.FillPath(p.Cut, label);
            }

            p.Circle(p.Cut, 68, 68, 26);
            p.Circle(p.Ink, 68, 68, 20);
            using (Pen hands = p.CutPen(5))
            {
                p.G.DrawLine(hands, 68, 68, 68, 55);
                p.G.DrawLine(hands, 68, 68, 77, 73);
            }
        }

        // ---- Vehicle design ----

        /// <summary>Welded tab rising from a tube seen end-on: tapered body, rounded end, bolt hole.</summary>
        private static void Tab(Painter p)
        {
            using (var body = new GraphicsPath())
            {
                body.AddLine(28, 70, 34, 32);
                body.AddArc(34, 16, 32, 32, 180, 180);
                body.AddLine(66, 32, 72, 70);
                body.CloseFigure();
                p.G.FillPath(p.Ink, body);
            }
            p.Circle(p.Cut, 50, 32, 8);

            // Tube cross section, separated from the tab by a thin gap (the weld line).
            p.Circle(p.Cut, 50, 76, 19);
            p.Circle(p.Ink, 50, 76, 15);
            p.Circle(p.Cut, 50, 76, 8);
        }

        /// <summary>Double wishbone corner: two arms from inboard pivots to a cambered wheel, on the ground.</summary>
        private static void Wheel(Painter p)
        {
            using (Pen arm = p.InkPen(7))
            {
                p.G.DrawLine(arm, 18, 34, 58, 30);
                p.G.DrawLine(arm, 18, 64, 56, 66);
            }
            p.Circle(p.Ink, 18, 34, 7);
            p.Circle(p.Ink, 18, 64, 7);
            p.Circle(p.Cut, 18, 34, 2.5f);
            p.Circle(p.Cut, 18, 64, 2.5f);

            GraphicsState s = p.G.Save();
            p.G.TranslateTransform(68, 48);
            p.G.RotateTransform(-10);
            using (GraphicsPath tire = RoundedRect(-13, -36, 26, 72, 9))
            using (Pen outline = p.CutPen(5))
            {
                p.G.DrawPath(outline, tire);
                p.G.FillPath(p.Ink, tire);
            }
            using (GraphicsPath rim = RoundedRect(-5, -22, 10, 44, 3))
            {
                p.G.FillPath(p.Cut, rim);
            }
            p.G.Restore(s);
            using (Pen ground = p.InkPen(6))
            {
                p.G.DrawLine(ground, 14, 89, 86, 89);
            }
        }

        // ---- Help ----

        private static void Gear(Painter p)
        {
            const int teeth = 8;
            var pts = new PointF[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                float a = i * 360f / teeth;
                pts[4 * i] = OnCircle(50, 50, 28, a - 15);
                pts[4 * i + 1] = OnCircle(50, 50, 38, a - 8);
                pts[4 * i + 2] = OnCircle(50, 50, 38, a + 8);
                pts[4 * i + 3] = OnCircle(50, 50, 28, a + 15);
            }
            p.G.FillPolygon(p.Ink, pts);
            p.Circle(p.Ink, 50, 50, 29);
            p.Circle(p.Cut, 50, 50, 11);
        }

        private static void Info(Painter p)
        {
            p.Circle(p.Ink, 50, 50, 34);
            p.Circle(p.Cut, 50, 32, 6.5f);
            using (GraphicsPath stem = RoundedRect(44, 43, 12, 31, 4))
            {
                p.G.FillPath(p.Cut, stem);
            }
        }

        /// <summary>Open book with two text lines on each page.</summary>
        private static void Book(Painter p)
        {
            p.G.FillPolygon(p.Ink, new[]
            {
                new PointF(47, 30), new PointF(33, 24), new PointF(14, 26),
                new PointF(14, 74), new PointF(33, 72), new PointF(47, 79),
            });
            p.G.FillPolygon(p.Ink, new[]
            {
                new PointF(53, 30), new PointF(67, 24), new PointF(86, 26),
                new PointF(86, 74), new PointF(67, 72), new PointF(53, 79),
            });
            using (Pen line = p.CutPen(4))
            {
                p.G.DrawLine(line, 21, 40, 40, 43);
                p.G.DrawLine(line, 21, 52, 40, 55);
                p.G.DrawLine(line, 60, 43, 79, 40);
                p.G.DrawLine(line, 60, 55, 79, 52);
            }
        }

        // ---- Shapes ----

        private static PointF OnCircle(float cx, float cy, float r, float degrees)
        {
            double a = degrees * Math.PI / 180;
            return new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
        }

        public static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            float d = 2 * r;
            var path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Rectangle with one top corner cut off (a page fold, or the floppy's chamfer).</summary>
        private static GraphicsPath Sheet(float x, float y, float w, float h, float fold, bool cornerLeft = false)
        {
            var path = new GraphicsPath();
            if (cornerLeft)
            {
                path.AddPolygon(new[]
                {
                    new PointF(x + fold, y), new PointF(x + w, y), new PointF(x + w, y + h),
                    new PointF(x, y + h), new PointF(x, y + fold),
                });
            }
            else
            {
                path.AddPolygon(new[]
                {
                    new PointF(x, y), new PointF(x + w - fold, y), new PointF(x + w, y + fold),
                    new PointF(x + w, y + h), new PointF(x, y + h),
                });
            }
            return path;
        }
    }
}
