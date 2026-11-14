using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Writes command icon strips at runtime so the repo needs no binary image
    /// files. SolidWorks wants one horizontal strip per size, one square cell
    /// per command, in the order the commands are added.
    /// </summary>
    internal static class IconStrips
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        /// <summary>Writes one strip per size and returns their paths, smallest first.</summary>
        /// <param name="cells">One painter per cell; each draws into the square it is given.</param>
        public static string[] Create(string directory, string prefix, IReadOnlyList<Action<Graphics, Rectangle>> cells)
        {
            Directory.CreateDirectory(directory);
            var paths = new string[Sizes.Length];
            for (int s = 0; s < Sizes.Length; s++)
            {
                int size = Sizes[s];
                string path = Path.Combine(directory, $"{prefix}_{size}.png");
                using (var strip = new Bitmap(size * cells.Count, size, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        cells[i](g, new Rectangle(i * size, 0, size, size));
                    }
                    strip.Save(path, ImageFormat.Png);
                }
                paths[s] = path;
            }
            return paths;
        }
    }
}
