using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lbr.CadTools.Core
{
    /// <summary>A point in meters (SolidWorks model space).</summary>
    public readonly struct Point3
    {
        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }

    /// <summary>Result of <see cref="PointCsv.Read"/>.</summary>
    public sealed class PointCsvReadResult
    {
        /// <summary>Points converted to meters, in file order.</summary>
        public List<Point3> Points { get; } = new List<Point3>();

        /// <summary>One message per bad line, with its 1-based line number.</summary>
        public List<string> Errors { get; } = new List<string>();

        public bool HasHeader { get; internal set; }
    }

    /// <summary>
    /// Reads and writes point lists as CSV.
    ///
    /// Reading:
    /// - Separator is tab, semicolon, or comma (first one found on the first
    ///   data line), else whitespace. Numbers always use '.' as the decimal point.
    /// - Blank lines and lines starting with '#' are skipped.
    /// - The first line is a header when any of its fields is not a number.
    ///   Header columns named X, Y, Z (optionally with a unit, like "X (mm)")
    ///   pick the columns, so extra columns such as a point name are allowed.
    ///   Otherwise each row must have 2 or 3 values; Z defaults to 0.
    /// </summary>
    public static class PointCsv
    {
        private static readonly Regex UnitInHeader = new Regex(
            @"[\(\[]\s*(mm|millimeters?|in|inch|inches|m|meters?)\s*[\)\]]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string UnitSymbol(LengthUnit unit)
        {
            switch (unit)
            {
                case LengthUnit.Meter: return "m";
                case LengthUnit.Millimeter: return "mm";
                case LengthUnit.Inch: return "in";
                default: throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        /// <summary>
        /// The unit named in the header (for example "X (mm)"), or null when
        /// there is no header, no unit, or the columns disagree.
        /// </summary>
        public static LengthUnit? DetectUnit(string text)
        {
            List<string> header = DataLines(text ?? "").Select(l => l.Fields).FirstOrDefault();
            if (header == null || !IsHeader(header))
            {
                return null;
            }

            var units = new HashSet<LengthUnit>();
            foreach (string field in header)
            {
                Match m = UnitInHeader.Match(field);
                if (m.Success)
                {
                    units.Add(ParseUnit(m.Groups[1].Value));
                }
            }
            return units.Count == 1 ? units.First() : (LengthUnit?)null;
        }

        /// <summary>Parses <paramref name="text"/>; values are in <paramref name="unit"/>.</summary>
        public static PointCsvReadResult Read(string text, LengthUnit unit)
        {
            var result = new PointCsvReadResult();
            int[] columns = null; // X, Y, and optionally Z column indexes from the header
            bool first = true;

            foreach ((int lineNumber, List<string> fields) in DataLines(text ?? ""))
            {
                if (first)
                {
                    first = false;
                    if (IsHeader(fields))
                    {
                        result.HasHeader = true;
                        columns = HeaderColumns(fields);
                        continue;
                    }
                }

                List<string> values;
                if (columns != null)
                {
                    if (fields.Count <= columns.Max())
                    {
                        result.Errors.Add($"Line {lineNumber}: expected at least {columns.Max() + 1} values, found {fields.Count}.");
                        continue;
                    }
                    values = columns.Select(i => fields[i]).ToList();
                }
                else
                {
                    if (fields.Count < 2 || fields.Count > 3)
                    {
                        result.Errors.Add($"Line {lineNumber}: expected 2 or 3 values (X, Y, Z), found {fields.Count}.");
                        continue;
                    }
                    values = fields;
                }

                var numbers = new double[3];
                string bad = null;
                for (int i = 0; i < values.Count; i++)
                {
                    if (!TryParseNumber(values[i], out numbers[i]))
                    {
                        bad = values[i];
                        break;
                    }
                }
                if (bad != null)
                {
                    result.Errors.Add($"Line {lineNumber}: '{bad}' is not a number.");
                    continue;
                }

                result.Points.Add(new Point3(
                    Units.ToMeters(numbers[0], unit),
                    Units.ToMeters(numbers[1], unit),
                    Units.ToMeters(numbers[2], unit)));
            }
            return result;
        }

        /// <summary>
        /// Writes points (in meters) as CSV in <paramref name="unit"/>, with a
        /// header such as "X (mm),Y (mm),Z (mm)" and CRLF line endings.
        /// </summary>
        public static string Write(IEnumerable<Point3> points, LengthUnit unit)
        {
            string symbol = UnitSymbol(unit);
            var sb = new StringBuilder();
            sb.Append($"X ({symbol}),Y ({symbol}),Z ({symbol})\r\n");
            foreach (Point3 p in points)
            {
                sb.Append(FormatNumber(Units.FromMeters(p.X, unit))).Append(',')
                  .Append(FormatNumber(Units.FromMeters(p.Y, unit))).Append(',')
                  .Append(FormatNumber(Units.FromMeters(p.Z, unit))).Append("\r\n");
            }
            return sb.ToString();
        }

        // ---- Helpers ----

        /// <summary>Non-blank, non-comment lines split into trimmed fields.</summary>
        private static IEnumerable<(int LineNumber, List<string> Fields)> DataLines(string text)
        {
            string[] lines = text.Split('\n');
            char? separator = null;
            bool separatorKnown = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!separatorKnown)
                {
                    separator = line.IndexOf('\t') >= 0 ? '\t'
                        : line.IndexOf(';') >= 0 ? ';'
                        : line.IndexOf(',') >= 0 ? ','
                        : (char?)null;
                    separatorKnown = true;
                }

                IEnumerable<string> parts = separator.HasValue
                    ? line.Split(separator.Value)
                    : trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                yield return (i + 1, parts.Select(Unquote).ToList());
            }
        }

        private static string Unquote(string field)
        {
            string f = field.Trim();
            if (f.Length >= 2 && f[0] == '"' && f[f.Length - 1] == '"')
            {
                f = f.Substring(1, f.Length - 2).Trim();
            }
            return f;
        }

        private static bool IsHeader(List<string> fields) => fields.Any(f => !TryParseNumber(f, out _));

        /// <summary>Indexes of the X, Y (and Z) columns, or null to use column order.</summary>
        private static int[] HeaderColumns(List<string> header)
        {
            int Find(string axis) => header.FindIndex(f =>
                string.Equals(UnitInHeader.Replace(f, "").Trim(), axis, StringComparison.OrdinalIgnoreCase));

            int x = Find("x"), y = Find("y"), z = Find("z");
            if (x < 0 || y < 0)
            {
                return null;
            }
            return z < 0 ? new[] { x, y } : new[] { x, y, z };
        }

        private static LengthUnit ParseUnit(string symbol)
        {
            string s = symbol.ToLowerInvariant();
            if (s == "mm" || s.StartsWith("millimeter", StringComparison.Ordinal)) return LengthUnit.Millimeter;
            if (s == "in" || s.StartsWith("inch", StringComparison.Ordinal)) return LengthUnit.Inch;
            return LengthUnit.Meter;
        }

        private static bool TryParseNumber(string s, out double value) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value) && !double.IsInfinity(value);

        private static string FormatNumber(double value)
        {
            string s = value.ToString("0.#########", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }
    }
}
