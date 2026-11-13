using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lbr.CadTools.Core
{
    public enum DocKind
    {
        Part,
        Assembly,
        Drawing,
    }

    public enum ExportFormat
    {
        Step,
        Iges,
        Parasolid,
        Stl,
        Pdf,
        Dxf,
        /// <summary>Sheet metal flat pattern; parts only.</summary>
        FlatPatternDxf,
    }

    public static class ExportFormats
    {
        public static IReadOnlyList<ExportFormat> All { get; } =
            (ExportFormat[])Enum.GetValues(typeof(ExportFormat));

        /// <summary>File name suffix including the extension, e.g. ".step" or "-flat.dxf".</summary>
        public static string Suffix(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Step: return ".step";
                case ExportFormat.Iges: return ".igs";
                case ExportFormat.Parasolid: return ".x_t";
                case ExportFormat.Stl: return ".stl";
                case ExportFormat.Pdf: return ".pdf";
                case ExportFormat.Dxf: return ".dxf";
                case ExportFormat.FlatPatternDxf: return "-flat.dxf";
                default: throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }

        public static string Label(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Step: return "STEP AP214 (.step)";
                case ExportFormat.Iges: return "IGES (.igs)";
                case ExportFormat.Parasolid: return "Parasolid (.x_t)";
                case ExportFormat.Stl: return "STL (.stl)";
                case ExportFormat.Pdf: return "PDF of drawings, all sheets (.pdf)";
                case ExportFormat.Dxf: return "DXF of drawings, one file per sheet (.dxf)";
                case ExportFormat.FlatPatternDxf: return "Sheet metal flat patterns (-flat.dxf)";
                default: throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }

        /// <summary>
        /// Whether the format can be made from this kind of document. Flat
        /// patterns also need the part to be sheet metal, checked at export time.
        /// </summary>
        public static bool AppliesTo(ExportFormat format, DocKind kind)
        {
            switch (format)
            {
                case ExportFormat.Step:
                case ExportFormat.Iges:
                case ExportFormat.Parasolid:
                case ExportFormat.Stl:
                    return kind == DocKind.Part || kind == DocKind.Assembly;
                case ExportFormat.Pdf:
                case ExportFormat.Dxf:
                    return kind == DocKind.Drawing;
                case ExportFormat.FlatPatternDxf:
                    return kind == DocKind.Part;
                default:
                    return false;
            }
        }
    }

    /// <summary>One document (and configuration) to export.</summary>
    public sealed class ExportItem
    {
        public ExportItem(string path, string configuration, DocKind kind, string partNumber)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Configuration = configuration ?? "";
            Kind = kind;
            PartNumber = partNumber;
        }

        public string Path { get; }

        /// <summary>Configuration to export; empty for drawings.</summary>
        public string Configuration { get; }

        public DocKind Kind { get; }

        /// <summary>Part number property value, or null/empty when there is none.</summary>
        public string PartNumber { get; }
    }

    /// <summary>
    /// Output file names. A file is named by its part number property when it
    /// has one, else by its file name. Models and drawings are named
    /// separately, so a part and its drawing share a name (PN.step, PN.pdf).
    /// </summary>
    public static class ExportNaming
    {
        /// <summary>
        /// Custom properties read as the part number, first match wins
        /// (configuration-specific before file-level). Matching ignores case.
        /// The team scheme is still open (issue #16).
        /// </summary>
        public static IReadOnlyList<string> PartNumberProperties { get; } =
            new[] { "PartNo", "Part Number", "PartNumber", "Part No", "Number" };

        // Windows rules, written out so results do not depend on the OS.
        private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();

        /// <summary>Base names (no extension) for each item, in input order.</summary>
        public static IReadOnlyList<string> AssignBaseNames(IReadOnlyList<ExportItem> items)
        {
            var names = new string[items.Count];

            // Files exported in more than one configuration get the configuration name added.
            var multiConfig = new HashSet<string>(
                items.Where(i => i.Kind != DocKind.Drawing)
                     .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(i => i.Configuration).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                     .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            var used = new Dictionary<bool, HashSet<string>>
            {
                [false] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                [true] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };

            for (int i = 0; i < items.Count; i++)
            {
                ExportItem item = items[i];
                string name = string.IsNullOrWhiteSpace(item.PartNumber)
                    ? FileStem(item.Path)
                    : item.PartNumber.Trim();
                if (item.Kind != DocKind.Drawing && multiConfig.Contains(item.Path) && item.Configuration.Length > 0)
                {
                    name += "-" + item.Configuration;
                }
                name = Sanitize(name);

                HashSet<string> taken = used[item.Kind == DocKind.Drawing];
                string unique = name;
                for (int n = 2; taken.Contains(unique); n++)
                {
                    unique = $"{name}-{n}";
                }
                taken.Add(unique);
                names[i] = unique;
            }
            return names;
        }

        /// <summary>Replaces characters Windows does not allow in file names.</summary>
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(c < 32 || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
            }
            string s = sb.ToString().Trim().TrimEnd('.', ' ');
            return s.Length == 0 ? "_" : s;
        }

        /// <summary>File name without folder or extension, for either slash style.</summary>
        public static string FileStem(string path)
        {
            string file = path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1);
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }
    }

    /// <summary>Builds the Batch Export result message.</summary>
    public static class BatchExportReport
    {
        public const int MaxListed = 10;

        /// <param name="folder">Output folder.</param>
        /// <param name="written">Files written.</param>
        /// <param name="failed">"file: reason" for each export that failed.</param>
        /// <param name="notes">Other things the user should know (skipped components and so on).</param>
        public static string Format(string folder, int written, IReadOnlyList<string> failed, IReadOnlyList<string> notes)
        {
            failed = failed ?? Array.Empty<string>();
            notes = notes ?? Array.Empty<string>();

            var sb = new StringBuilder();
            if (written == 0 && failed.Count == 0)
            {
                sb.Append("Nothing to export with the chosen formats.");
            }
            else
            {
                sb.Append($"Exported {written} {(written == 1 ? "file" : "files")} to\n{folder}");
            }

            if (failed.Count > 0)
            {
                sb.Append($"\n\n{failed.Count} {(failed.Count == 1 ? "export" : "exports")} failed:");
                foreach (string f in failed.Take(MaxListed))
                {
                    sb.Append("\n  ").Append(f);
                }
                if (failed.Count > MaxListed)
                {
                    sb.Append($"\n  ...and {failed.Count - MaxListed} more (see the log)");
                }
            }

            if (notes.Count > 0)
            {
                sb.Append("\n");
                foreach (string n in notes)
                {
                    sb.Append("\n").Append(n);
                }
            }
            return sb.ToString();
        }
    }
}
