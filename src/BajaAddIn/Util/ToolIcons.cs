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
        NameCheck,
        Notcher,
        Drawings,
        SuspensionSweep,
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
                        case ToolIcon.FloatAll: Move(p); break;
                        case ToolIcon.RestoreFixStates: Restore(p); break;
                        case ToolIcon.ImportPoints: ImportTray(p); break;
                        case ToolIcon.ExportPoints: ExportSheet(p); break;
                        case ToolIcon.BatchExport: Sheets(p); break;
                        case ToolIcon.MountingTab: Tab(p); break;
                        case ToolIcon.SuspensionCheck: Camber(p); break;
                        case ToolIcon.AutoSave: Disk(p); break;
                        case ToolIcon.Settings: Gear(p); break;
                        case ToolIcon.About: Info(p); break;
                        case ToolIcon.Guide: Book(p); break;
                        case ToolIcon.NameCheck: NameTag(p); break;
                        case ToolIcon.Notcher: Cope(p); break;
                        case ToolIcon.Drawings: DrawingSheet(p); break;
                        case ToolIcon.SuspensionSweep: Curve(p); break;
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

        /// <summary>Four-way move arrows around a free part: the SolidWorks "drag me" cross.</summary>
        private static void Move(Painter p)
        {
            using (Pen shaft = p.InkPen(9))
            {
                shaft.StartCap = shaft.EndCap = LineCap.Flat;
                p.G.DrawLine(shaft, 50, 24, 50, 76);
                p.G.DrawLine(shaft, 24, 50, 76, 50);
            }
            const float tip = 38, back = 22, half = 13;
            for (int i = 0; i < 4; i++)
            {
                double a = i * Math.PI / 2;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                p.G.FillPolygon(p.Ink, new[]
                {
                    new PointF(50 + dx * tip, 50 + dy * tip),
                    new PointF(50 + dx * back - dy * half, 50 + dy * back + dx * half),
                    new PointF(50 + dx * back + dy * half, 50 + dy * back - dx * half),
                });
            }
            p.Circle(p.Cut, 50, 50, 15);
            p.Circle(p.Ink, 50, 50, 10.5f);
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

        /// <summary>Arrow dropping into an open tray that holds three sketch points.</summary>
        private static void ImportTray(Painter p)
        {
            using (Pen tray = p.InkPen(8))
            {
                p.G.DrawLines(tray, new[] { new PointF(15, 52), new PointF(15, 84), new PointF(85, 84), new PointF(85, 52) });
            }
            foreach (PointF pt in new[] { new PointF(31, 70), new PointF(69, 70) })
            {
                p.Circle(p.Ink, pt.X, pt.Y, 7);
            }
            p.Circle(p.Ink, 50, 72, 7);

            p.G.FillRectangle(p.Ink, 43, 10, 14, 26);
            p.G.FillPolygon(p.Ink, new[] { new PointF(28, 33), new PointF(72, 33), new PointF(50, 58) });
        }

        /// <summary>Sheet of points with an arrow leaving it to the upper right.</summary>
        private static void ExportSheet(Painter p)
        {
            using (GraphicsPath sheet = Sheet(12, 26, 54, 62, 14))
            {
                p.G.FillPath(p.Ink, sheet);
            }
            p.Circle(p.Cut, 25, 74, 5.5f);
            p.Circle(p.Cut, 39, 60, 5.5f);
            p.Circle(p.Cut, 53, 74, 5.5f);

            // Arrow with a tile-colored outline so it reads where it crosses the sheet.
            var tip = new PointF(88, 12);
            var shaftStart = new PointF(46, 54);
            using (Pen halo = p.CutPen(17))
            {
                p.G.DrawLine(halo, shaftStart, new PointF(76, 24));
            }
            var head = new[] { tip, new PointF(87, 42), new PointF(58, 13) };
            using (Pen halo = p.CutPen(6))
            {
                halo.LineJoin = LineJoin.Round;
                p.G.DrawPolygon(halo, head);
            }
            using (Pen shaft = p.InkPen(9))
            {
                p.G.DrawLine(shaft, shaftStart, new PointF(74, 26));
            }
            p.G.FillPolygon(p.Ink, head);
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

        /// <summary>Front view of a tire leaning in (camber) on the ground, with a vertical reference line and the angle between them.</summary>
        private static void Camber(Painter p)
        {
            const float lean = -16;           // degrees; negative leans the top inward (left)
            var contact = new PointF(60, 84); // bottom center of the tire

            GraphicsState s = p.G.Save();
            p.G.TranslateTransform(contact.X, contact.Y);
            p.G.RotateTransform(lean);
            using (GraphicsPath tire = RoundedRect(-15, -70, 30, 70, 10))
            {
                p.G.FillPath(p.Ink, tire);
            }
            using (GraphicsPath rim = RoundedRect(-4.5f, -55, 9, 40, 3))
            {
                p.G.FillPath(p.Cut, rim);
            }
            p.G.Restore(s);

            // Vertical reference through the contact patch, and the camber arc.
            using (Pen halo = p.CutPen(9))
            using (Pen line = p.InkPen(4.5f))
            {
                halo.DashStyle = DashStyle.Solid;
                p.G.DrawLine(halo, contact.X, 78, contact.X, 12);
                p.G.DrawLine(line, contact.X, 78, contact.X, 12);
                const float r = 54;
                using (Pen arc = p.InkPen(5))
                {
                    p.G.DrawArc(arc, contact.X - r, contact.Y - r, 2 * r, 2 * r, -90, lean);
                }
            }
            using (Pen ground = p.InkPen(7))
            {
                p.G.DrawLine(ground, 14, 89, 86, 89);
            }
        }

        /// <summary>
        /// A tube coped onto a cross tube, side on: a horizontal tube whose right
        /// end has a curved bite (the fish mouth) hugging a cross tube seen end-on.
        /// </summary>
        private static void Cope(Painter p)
        {
            const float cx = 76, cy = 50, bite = 23;
            using (var mouth = new GraphicsPath())
            using (var region = new Region(new RectangleF(8, 33, 70, 34)))
            {
                mouth.AddEllipse(cx - bite, cy - bite, 2 * bite, 2 * bite);
                region.Exclude(mouth);
                p.G.FillRegion(p.Ink, region);
            }
            using (Pen seam = p.CutPen(4))
            {
                p.G.DrawLine(seam, 16, 42, 46, 42); // highlight line so it reads as a tube
            }
            // Cross tube with its bore, a small gap inside the bite.
            p.Circle(p.Ink, cx, cy, bite - 5);
            p.Circle(p.Cut, cx, cy, bite - 13);
        }

        /// <summary>Chart axes with a camber curve through three data points.</summary>
        private static void Curve(Painter p)
        {
            using (Pen axes = p.InkPen(7))
            {
                p.G.DrawLines(axes, new[] { new PointF(18, 16), new PointF(18, 82), new PointF(86, 82) });
            }
            var pts = new[] { new PointF(28, 30), new PointF(50, 46), new PointF(76, 70) };
            using (Pen curve = p.InkPen(6))
            {
                p.G.DrawCurve(curve, new[] { new PointF(26, 26), pts[0], pts[1], pts[2], new PointF(80, 74) }, 0.5f);
            }
            foreach (PointF pt in pts)
            {
                p.Circle(p.Cut, pt.X, pt.Y, 8);
                p.Circle(p.Ink, pt.X, pt.Y, 5.5f);
            }
        }

        /// <summary>Landscape drawing sheet: two views and a title block in the corner.</summary>
        private static void DrawingSheet(Painter p)
        {
            using (GraphicsPath sheet = RoundedRect(10, 20, 80, 60, 4))
            {
                p.G.FillPath(p.Ink, sheet);
            }
            using (Pen line = p.CutPen(4))
            {
                line.StartCap = line.EndCap = LineCap.Flat;
                p.G.DrawRectangle(line, 19, 42, 24, 18);  // front view
                p.G.DrawRectangle(line, 19, 29, 24, 7);   // top view
                p.G.DrawRectangle(line, 49, 42, 9, 18);   // right view
            }
            p.G.FillRectangle(p.Cut, 62, 64, 22, 10);     // title block
        }

        /// <summary>Name tag (label with a string hole) carrying a check mark.</summary>
        private static void NameTag(Painter p)
        {
            using (var tag = new GraphicsPath())
            {
                tag.AddPolygon(new[]
                {
                    new PointF(10, 50), new PointF(34, 22), new PointF(88, 22), new PointF(88, 78), new PointF(34, 78),
                });
                p.G.FillPath(p.Ink, tag);
            }
            p.Circle(p.Cut, 30, 50, 6.5f);
            using (Pen check = p.CutPen(10))
            {
                p.G.DrawLines(check, new[] { new PointF(46, 51), new PointF(58, 63), new PointF(78, 37) });
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
