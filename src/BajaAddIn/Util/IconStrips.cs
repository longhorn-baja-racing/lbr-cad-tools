using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Draws command icon strips at runtime so the repo needs no binary image
    /// files. SolidWorks wants one horizontal strip per size, one square cell
    /// per command, in the order the commands are added.
    /// </summary>
    internal static class IconStrips
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        // Burnt orange, #BF5700.
        private static readonly Color Fill = Color.FromArgb(0xBF, 0x57, 0x00);

        /// <summary>Writes one strip per size and returns their paths, smallest first.</summary>
        public static string[] Create(string directory, string prefix, IReadOnlyList<string> glyphs)
        {
            Directory.CreateDirectory(directory);
            var paths = new string[Sizes.Length];
            for (int s = 0; s < Sizes.Length; s++)
            {
                int size = Sizes[s];
                string path = Path.Combine(directory, $"{prefix}_{size}.png");
                using (var strip = new Bitmap(size * glyphs.Count, size, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < glyphs.Count; i++)
                    {
                        DrawCell(g, new Rectangle(i * size, 0, size, size), glyphs[i]);
                    }
                    strip.Save(path, ImageFormat.Png);
                }
                paths[s] = path;
            }
            return paths;
        }

        private static void DrawCell(Graphics g, Rectangle cell, string glyph)
        {
            int pad = cell.Width / 10;
            var box = new Rectangle(cell.X + pad, cell.Y + pad, cell.Width - 2 * pad, cell.Height - 2 * pad);
            using (var brush = new SolidBrush(Fill))
            {
                g.FillRectangle(brush, box);
            }

            float emSize = box.Height * (glyph.Length > 1 ? 0.42f : 0.62f);
            using (var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(glyph, font, Brushes.White, box, format);
            }
        }
    }
}
