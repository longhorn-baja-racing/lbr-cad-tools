using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Names, colors, and the drawn marks shared by the add-in and the
    /// installer (which links this file). Everything is drawn in code, so the
    /// repository holds no image files.
    /// </summary>
    internal static class Brand
    {
        public const string Product = "LBR CAD Tools";
        public const string Organization = "Longhorn Baja Racing";
        public const string University = "The University of Texas at Austin";
        public const string Developer = "Cyrus";
        public const string RepositoryUrl = "https://github.com/longhorn-baja-racing/lbr-cad-tools";

        /// <summary>Burnt orange, #BF5700.</summary>
        public static readonly Color BurntOrange = Color.FromArgb(0xBF, 0x57, 0x00);

        public static readonly Color Ink = Color.FromArgb(0x33, 0x3F, 0x48);

        /// <summary>
        /// The developer's mark: the Monstera leaf from docs/assets/monstera.svg
        /// in its own greens, on a transparent background, centered in a square.
        /// </summary>
        public static Bitmap Leaf(int size) => DrawLeaf(size, null);

        /// <summary>The leaf's outline (with its holes) in one color, for dark or colored backgrounds.</summary>
        public static Bitmap LeafSilhouette(int size, Color color) => DrawLeaf(size, color);

        private static Bitmap DrawLeaf(int size, Color? single)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (GraphicsPath outline = SvgPath(BrandLeaf.Outline))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                RectangleF b = outline.GetBounds();
                float k = size / Math.Max(b.Width, b.Height);
                g.TranslateTransform((size - b.Width * k) / 2, (size - b.Height * k) / 2);
                g.ScaleTransform(k, k);
                g.TranslateTransform(-b.X, -b.Y);
                if (single != null)
                {
                    using (var brush = new SolidBrush(single.Value))
                    {
                        g.FillPath(brush, outline);
                    }
                    return bmp;
                }
                using (GraphicsPath light = SvgPath(BrandLeaf.Light))
                using (GraphicsPath muted = SvgPath(BrandLeaf.Muted))
                using (var deep = new SolidBrush(ColorTranslator.FromHtml(BrandLeaf.OutlineColor)))
                using (var lightBrush = new SolidBrush(ColorTranslator.FromHtml(BrandLeaf.LightColor)))
                using (var mutedBrush = new SolidBrush(ColorTranslator.FromHtml(BrandLeaf.MutedColor)))
                {
                    g.FillPath(deep, outline);
                    g.SetClip(outline);
                    g.FillPath(lightBrush, light);
                    g.FillPath(mutedBrush, muted);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Reads SVG path data with absolute M, L, C, and Z commands (all the
        /// leaf artwork uses) into an even-odd path.
        /// </summary>
        private static GraphicsPath SvgPath(string data)
        {
            var path = new GraphicsPath(FillMode.Alternate);
            var tokens = System.Text.RegularExpressions.Regex.Matches(data, @"[MLCZ]|-?\d+(?:\.\d+)?");
            int i = 0;
            char command = 'M';
            var current = new PointF();
            var start = new PointF();
            float Next() => float.Parse(tokens[i++].Value, System.Globalization.CultureInfo.InvariantCulture);
            PointF NextPoint() => new PointF(Next(), Next());
            while (i < tokens.Count)
            {
                string token = tokens[i].Value;
                if (char.IsLetter(token[0]))
                {
                    command = token[0];
                    i++;
                    if (command == 'Z')
                    {
                        path.CloseFigure();
                        current = start;
                    }
                    continue;
                }
                switch (command)
                {
                    case 'M':
                        path.StartFigure();
                        current = start = NextPoint();
                        command = 'L'; // further pairs after M are lines
                        break;
                    case 'L':
                        PointF to = NextPoint();
                        path.AddLine(current, to);
                        current = to;
                        break;
                    case 'C':
                        PointF c1 = NextPoint(), c2 = NextPoint(), end = NextPoint();
                        path.AddBezier(current, c1, c2, end);
                        current = end;
                        break;
                    default:
                        i++; // unknown: skip the number
                        break;
                }
            }
            return path;
        }

        /// <summary>
        /// Paints the burnt orange header band used by the add-in's windows and
        /// the installer: product name, a subtitle, and the organization line.
        /// </summary>
        public static void PaintHeader(Graphics g, Rectangle bounds, string title, string subtitle)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var band = new SolidBrush(BurntOrange))
            {
                g.FillRectangle(band, bounds);
            }
            float scale = g.DpiY / 96f;
            using (var titleFont = new Font("Segoe UI Semibold", 17f))
            using (var subFont = new Font("Segoe UI", 9f))
            using (var faint = new SolidBrush(Color.FromArgb(235, Color.White)))
            {
                float x = bounds.X + 18 * scale;
                g.DrawString(title, titleFont, Brushes.White, x, bounds.Y + 12 * scale);
                g.DrawString(subtitle, subFont, faint, x, bounds.Y + 46 * scale);
                g.DrawString($"{Organization}  |  {University}", subFont, faint, x, bounds.Y + 64 * scale);
            }

            // The developer's leaf, small and white, at the right end of the band.
            int leafSize = (int)Math.Round(52 * scale);
            using (Bitmap leaf = LeafSilhouette(leafSize, Color.FromArgb(70, Color.White)))
            {
                g.DrawImage(leaf, bounds.Right - leafSize - 16 * scale, bounds.Y + (bounds.Height - leafSize) / 2f);
            }
        }

        /// <summary>Height of <see cref="PaintHeader"/> at 96 dpi.</summary>
        public const int HeaderHeight = 92;

        /// <summary>The product icon: "LBR" in white on a rounded burnt orange square.</summary>
        public static Bitmap ProductIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(BurntOrange))
            using (var font = new Font("Segoe UI", size * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float pad = size * 0.06f, r = size * 0.22f, w = size - 2 * pad;
                path.AddArc(pad, pad, r, r, 180, 90);
                path.AddArc(pad + w - r, pad, r, r, 270, 90);
                path.AddArc(pad + w - r, pad + w - r, r, r, 0, 90);
                path.AddArc(pad, pad + w - r, r, r, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
                g.DrawString("LBR", font, Brushes.White, new RectangleF(0, size * 0.02f, size, size), format);
            }
            return bmp;
        }

        /// <summary>Small "made by" line with the leaf, for window footers.</summary>
        public static void PaintSignature(Graphics g, Point at, Color background)
        {
            float scale = g.DpiY / 96f;
            int leafSize = (int)Math.Round(18 * scale);
            using (Bitmap leaf = Leaf(leafSize))
            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(0x6B, 0x75, 0x7D)))
            {
                g.DrawImage(leaf, at.X, at.Y);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.DrawString($"made by {Developer}", font, brush, at.X + leafSize + 4 * scale, at.Y + 1 * scale);
            }
        }
    }
}
