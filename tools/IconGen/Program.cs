using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Lbr.CadTools.AddIn.Util;

namespace Lbr.CadTools.IconGen
{
    /// <summary>
    /// Writes Brand.ProductIcon as a multi-size .ico (PNG frames).
    /// Usage: IconGen.exe &lt;output.ico&gt;
    /// </summary>
    internal static class Program
    {
        private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: IconGen <output.ico>");
                return 2;
            }

            var frames = new List<byte[]>();
            foreach (int size in Sizes)
            {
                using (Bitmap icon = Brand.ProductIcon(size))
                using (var png = new MemoryStream())
                {
                    icon.Save(png, ImageFormat.Png);
                    frames.Add(png.ToArray());
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
            using (var w = new BinaryWriter(File.Create(args[0])))
            {
                // ICONDIR
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)Sizes.Length);
                // ICONDIRENTRY per frame; image data follows all entries.
                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                    w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] frame in frames)
                {
                    w.Write(frame);
                }
            }
            Console.WriteLine($"Wrote {args[0]}");
            return 0;
        }
    }
}
