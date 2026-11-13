using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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

        /// <summary>Monstera green for the developer mark.</summary>
        public static readonly Color LeafGreen = Color.FromArgb(0x2F, 0x6B, 0x3A);

        public static readonly Color Ink = Color.FromArgb(0x33, 0x3F, 0x48);

        /// <summary>
        /// The developer's mark: a minimalist Monstera leaf with a few
        /// slit fenestrations, on a transparent background. Drawn in a 100 x 100
        /// unit box (tip at the top, stem at the bottom) and scaled.
        /// </summary>
        public static Bitmap Monstera(int size, Color color)
        {
            float k = size / 100f;
            var leaf = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(leaf))
            using (GraphicsPath outline = LeafOutline())
            using (var brush = new SolidBrush(color))
            using (var stem = new Pen(color, 4.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(k, k);
                g.FillPath(brush, outline);
                g.DrawLine(stem, 50f, 72f, 50f, 97f);
            }

            // Cut the fenestrations out with an antialiased mask, so the edges
            // stay smooth on any background.
            using (var cuts = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(cuts))
                using (GraphicsPath holes = Fenestrations())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(k, k);
                    g.FillPath(Brushes.White, holes);
                }
                ApplyMask(leaf, cuts);
            }
            return leaf;
        }

        /// <summary>Heart-shaped leaf: pointed tip at (50, 2), basal lobes either side of the stem.</summary>
        private static GraphicsPath LeafOutline()
        {
            var p = new GraphicsPath(FillMode.Winding);
            p.AddBezier(50, 2, 62, 10, 92, 18, 92, 46);   // right shoulder (pointed tip)
            p.AddBezier(92, 46, 92, 72, 75, 91, 58, 86);  // right lobe
            p.AddBezier(58, 86, 53, 85, 51, 79, 50, 72);  // into the sinus
            p.AddBezier(50, 72, 49, 79, 47, 85, 42, 86);  // out of the sinus
            p.AddBezier(42, 86, 25, 91, 8, 72, 8, 46);    // left lobe
            p.AddBezier(8, 46, 8, 18, 38, 10, 50, 2);     // left shoulder
            p.CloseFigure();
            return p;
        }

        /// <summary>Three narrow slits per side, rising toward the edge like veins and open at it.</summary>
        private static GraphicsPath Fenestrations()
        {
            var p = new GraphicsPath(FillMode.Winding);
            // Inner tip near the midrib, then two points just past the leaf edge (mirrored for the left).
            float[][] slits =
            {
                new float[] { 55, 27, 80, 5, 85, 9 },
                new float[] { 56, 47, 99, 24, 100, 30 },
                new float[] { 57, 66, 100, 50, 99, 56 },
            };
            foreach (float[] s in slits)
            {
                p.AddPolygon(new[] { new PointF(s[0], s[1]), new PointF(s[2], s[3]), new PointF(s[4], s[5]) });
                p.AddPolygon(new[] { new PointF(100 - s[0], s[1]), new PointF(100 - s[2], s[3]), new PointF(100 - s[4], s[5]) });
            }
            return p;
        }
        /// <summary>Multiplies the image's alpha by (1 - mask alpha).</summary>
        private static void ApplyMask(Bitmap image, Bitmap mask)
        {
            var rect = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData id = image.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = image.Width * image.Height;
                var pixels = new int[n];
                var maskPixels = new int[n];
                Marshal.Copy(id.Scan0, pixels, 0, n);
                Marshal.Copy(md.Scan0, maskPixels, 0, n);
                for (int i = 0; i < n; i++)
                {
                    int cut = (maskPixels[i] >> 24) & 0xFF;
                    if (cut == 0)
                    {
                        continue;
                    }
                    int alpha = (pixels[i] >> 24) & 0xFF;
                    alpha = alpha * (255 - cut) / 255;
                    pixels[i] = (alpha << 24) | (pixels[i] & 0x00FFFFFF);
                }
                Marshal.Copy(pixels, 0, id.Scan0, n);
            }
            finally
            {
                image.UnlockBits(id);
                mask.UnlockBits(md);
            }
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
            using (Bitmap leaf = Monstera(leafSize, Color.FromArgb(70, Color.White)))
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
            using (Bitmap leaf = Monstera(leafSize, LeafGreen))
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
