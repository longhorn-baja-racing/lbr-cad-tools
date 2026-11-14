using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lbr.CadTools.Core
{
    /// <summary>A drawing view position (center) on the sheet, in meters.</summary>
    public readonly struct ViewSpot
    {
        public ViewSpot(string orientation, double x, double y)
        {
            Orientation = orientation;
            X = x;
            Y = y;
        }

        /// <summary>SolidWorks named view: "*Front", "*Top", "*Right", "*Isometric".</summary>
        public string Orientation { get; }

        public double X { get; }
        public double Y { get; }
    }

    /// <summary>Sheet scale and where the four views go.</summary>
    public sealed class DrawingPlan
    {
        public DrawingPlan(double numerator, double denominator, IReadOnlyList<ViewSpot> views)
        {
            Numerator = numerator;
            Denominator = denominator;
            Views = views;
        }

        /// <summary>Scale as SolidWorks sets it: Numerator:Denominator, e.g. 1:2.</summary>
        public double Numerator { get; }

        public double Denominator { get; }

        public double Scale => Numerator / Denominator;

        public string ScaleText => Numerator.ToString(CultureInfo.InvariantCulture) + ":" + Denominator.ToString(CultureInfo.InvariantCulture);

        public IReadOnlyList<ViewSpot> Views { get; }
    }

    /// <summary>
    /// Auto drawing layout (issue #13): third angle front, top above it, right
    /// beside it, isometric in the top right corner, at the largest standard
    /// scale that fits the sheet above the title block.
    /// </summary>
    public static class DrawingLayout
    {
        /// <summary>Standard scales, largest first (numerator, denominator).</summary>
        public static IReadOnlyList<(double Num, double Den)> StandardScales { get; } = new[]
        {
            (10.0, 1.0), (5.0, 1.0), (4.0, 1.0), (2.0, 1.0), (1.0, 1.0), (1.0, 2.0), (1.0, 4.0), (1.0, 5.0),
            (1.0, 8.0), (1.0, 10.0), (1.0, 16.0), (1.0, 20.0), (1.0, 50.0), (1.0, 100.0),
        };

        // Fractions of the sheet. The title block sits along the bottom of most templates.
        private const double Margin = 0.06;
        private const double TitleBlock = 0.22;
        private const double Gap = 0.05;

        /// <summary>
        /// Plans the views. <paramref name="sizeX"/>, <paramref name="sizeY"/>,
        /// <paramref name="sizeZ"/> are the model's bounding box (meters, model
        /// axes: Front looks at X by Y, Top at X by Z, Right at Z by Y).
        /// </summary>
        public static DrawingPlan Plan(double sizeX, double sizeY, double sizeZ, double sheetWidth, double sheetHeight)
        {
            double usableW = sheetWidth * (1 - 2 * Margin);
            double usableH = sheetHeight * (1 - Margin - TitleBlock);
            double gap = sheetWidth * Gap;

            // Front + right + isometric across; front + top up. The iso is about as wide as the largest size.
            double iso = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
            (double num, double den) = StandardScales[StandardScales.Count - 1];
            foreach ((double n, double d) in StandardScales)
            {
                double s = n / d;
                double across = s * (sizeX + sizeZ + iso) + 2 * gap;
                double up = s * (sizeY + sizeZ) + gap;
                if (across <= usableW && up <= usableH)
                {
                    num = n;
                    den = d;
                    break;
                }
            }

            double k = num / den;
            // Center the group of views in the space above the title block.
            double groupW = k * (sizeX + sizeZ + iso) + 2 * gap;
            double groupH = k * (sizeY + sizeZ) + gap;
            double left = sheetWidth * Margin + Math.Max(0, (usableW - groupW) / 2);
            double bottom = sheetHeight * TitleBlock + Math.Max(0, (usableH - groupH) / 2);
            double frontX = left + k * sizeX / 2;
            double frontY = bottom + k * sizeY / 2;
            double topY = bottom + k * sizeY + gap + k * sizeZ / 2;
            double rightX = left + k * sizeX + gap + k * sizeZ / 2;
            double isoX = Math.Min(sheetWidth * (1 - Margin) - k * iso / 2, rightX + k * sizeZ / 2 + gap + k * iso / 2);
            double isoY = topY;
            return new DrawingPlan(num, den, new[]
            {
                new ViewSpot("*Front", frontX, frontY),
                new ViewSpot("*Top", frontX, topY),
                new ViewSpot("*Right", rightX, frontY),
                new ViewSpot("*Isometric", isoX, isoY),
            });
        }

        /// <summary>
        /// Drawing file name for a model: "d" plus the model name when it follows
        /// the team convention (CHA_0002_Bracket_REV01_V01 -> dCHA_0002_Bracket_REV01_V01,
        /// assemblies drop their "a"); otherwise the model name.
        /// </summary>
        public static string DrawingName(string modelBaseName)
        {
            CadName n = NamingConvention.Parse(modelBaseName);
            return n == null
                ? modelBaseName
                : NamingConvention.Format(CadFileKind.Drawing, n.Team, n.Number, n.PartName, n.Revision, n.Version);
        }

        /// <summary>
        /// Title block properties for the drawing file, from the model name:
        /// PartNo (CHA_0002), Description (Front Hoop Left), Revision (REV01 or REVSTD), Version (V01).
        /// </summary>
        public static Dictionary<string, string> TitleProperties(string modelBaseName)
        {
            CadName n = NamingConvention.Parse(modelBaseName);
            if (n == null)
            {
                return new Dictionary<string, string> { ["PartNo"] = modelBaseName, ["Description"] = SplitWords(modelBaseName) };
            }
            var props = new Dictionary<string, string>
            {
                ["PartNo"] = n.Key,
                ["Description"] = SplitWords(n.PartName),
                ["Revision"] = n.RevisionText,
            };
            if (n.Version != null)
            {
                props["Version"] = "V" + n.Version.Value.ToString("00", CultureInfo.InvariantCulture);
            }
            return props;
        }

        /// <summary>"FrontHoopLeft" -> "Front Hoop Left"; "CVTEngine" -> "CVT Engine".</summary>
        public static string SplitWords(string camel) =>
            Regex.Replace(Regex.Replace(camel ?? "", "([a-z0-9])([A-Z])", "$1 $2"), "([A-Z]+)([A-Z][a-z])", "$1 $2").Replace('_', ' ').Trim();
    }
}
