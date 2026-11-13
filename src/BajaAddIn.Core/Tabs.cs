using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lbr.CadTools.Core
{
    public enum TabUse
    {
        LapBelt,
        AntiSubBelt,
        Seat,
        FireExtinguisher,
        FuelTank,
        Custom,
    }

    /// <summary>
    /// One row of the Baja SAE B.12.2 attachment tab table (Figure B-55).
    /// Lengths in meters. MinBolt is null where the rules say "check bracket".
    /// </summary>
    public sealed class TabPreset
    {
        private const double In = Units.MetersPerInch;

        private TabPreset(TabUse use, string name, double? minBolt, double maxLength, double minThickness, bool doubleShear, string weld)
        {
            Use = use;
            Name = name;
            MinBolt = minBolt;
            MaxLength = maxLength;
            MinThickness = minThickness;
            DoubleShear = doubleShear;
            Weld = weld;
        }

        public TabUse Use { get; }
        public string Name { get; }
        public double? MinBolt { get; }
        public double MaxLength { get; }
        public double MinThickness { get; }
        public bool DoubleShear { get; }
        public string Weld { get; }

        /// <summary>Table values from docs/rules-reference.md (2027 Rev A, B.12.2).</summary>
        public static IReadOnlyList<TabPreset> All { get; } = new[]
        {
            new TabPreset(TabUse.LapBelt, "Lap belt", 7.0 / 16 * In, 2.0 * In, 0.090 * In, true, "one side"),
            new TabPreset(TabUse.AntiSubBelt, "Anti-sub belt", 7.0 / 16 * In, 2.0 * In, 0.090 * In, true, "one side"),
            new TabPreset(TabUse.Seat, "Seat", 0.25 * In, 2.0 * In, 0.125 * In, false, "both sides"),
            new TabPreset(TabUse.FireExtinguisher, "Fire extinguisher", null, 3.0 * In, 0.125 * In, false, "both sides"),
            new TabPreset(TabUse.FuelTank, "Fuel tank", null, 2.0 * In, 0.125 * In, false, "both sides"),
            new TabPreset(TabUse.Custom, "Custom (no rule checks)", null, double.PositiveInfinity, 0, false, ""),
        };

        public static TabPreset For(TabUse use)
        {
            foreach (TabPreset p in All)
            {
                if (p.Use == use)
                {
                    return p;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(use));
        }
    }

    /// <summary>Tab inputs in meters (Figure B-54 names).</summary>
    public sealed class TabInputs
    {
        public TabUse Use { get; set; } = TabUse.LapBelt;

        /// <summary>D1: nominal bolt diameter.</summary>
        public double Bolt { get; set; }

        /// <summary>Hole diameter; close fit by default (bolt + 1/64 in).</summary>
        public double Hole { get; set; }

        /// <summary>D2: diameter of the rounded end, concentric with the hole.</summary>
        public double EndDiameter { get; set; }

        /// <summary>B: base width along the weld line.</summary>
        public double BaseWidth { get; set; }

        /// <summary>L: base line midpoint to hole center.</summary>
        public double Length { get; set; }

        /// <summary>t: plate thickness.</summary>
        public double Thickness { get; set; }

        public bool DoubleShear { get; set; }

        /// <summary>Clear gap between the two tabs of a double shear pair.</summary>
        public double Gap { get; set; }

        /// <summary>When above 0, the base is cut to fit a tube of this outside diameter.</summary>
        public double ContourTubeDiameter { get; set; }

        public const double CloseFitClearance = Units.MetersPerInch / 64;

        /// <summary>
        /// Rule-minimum geometry for a preset: D2 = 2.2 D1, B = 3 D1, t at the
        /// minimum, L at half the maximum, close-fit hole. Custom and "check
        /// bracket" uses start from a 1/4 in bolt.
        /// </summary>
        public static TabInputs Defaults(TabUse use)
        {
            TabPreset p = TabPreset.For(use);
            double bolt = p.MinBolt ?? 0.25 * Units.MetersPerInch;
            double maxLength = double.IsInfinity(p.MaxLength) ? 2.0 * Units.MetersPerInch : p.MaxLength;
            return new TabInputs
            {
                Use = use,
                Bolt = bolt,
                Hole = bolt + CloseFitClearance,
                EndDiameter = 2.2 * bolt,
                BaseWidth = 3 * bolt,
                Length = maxLength / 2,
                Thickness = p.MinThickness > 0 ? p.MinThickness : 0.125 * Units.MetersPerInch,
                DoubleShear = p.DoubleShear,
                Gap = 0.5 * Units.MetersPerInch,
            };
        }
    }

    public enum TabIssueLevel
    {
        /// <summary>Breaks a B.12.2 rule; the tab can still be made.</summary>
        Warning,

        /// <summary>The shape cannot be built.</summary>
        Error,
    }

    public readonly struct TabIssue
    {
        public TabIssue(TabIssueLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public TabIssueLevel Level { get; }
        public string Message { get; }

        public override string ToString() => $"{Level}: {Message}";
    }

    /// <summary>Rule and geometry checks for a tab.</summary>
    public static class TabRules
    {
        // Rounding slack so values typed from the table (e.g. 0.090 in) pass.
        private const double Slack = 1e-7;

        public static List<TabIssue> Check(TabInputs t)
        {
            var issues = new List<TabIssue>();
            void Error(string m) => issues.Add(new TabIssue(TabIssueLevel.Error, m));
            void Warn(string m) => issues.Add(new TabIssue(TabIssueLevel.Warning, m));

            if (t.Bolt <= 0 || t.Hole <= 0 || t.EndDiameter <= 0 || t.BaseWidth <= 0 || t.Length <= 0 || t.Thickness <= 0)
            {
                Error("All sizes must be greater than zero.");
                return issues;
            }
            if (t.DoubleShear && t.Gap <= 0)
            {
                Error("The gap between double shear tabs must be greater than zero.");
            }
            if (t.Hole >= t.EndDiameter)
            {
                Error("The hole must be smaller than the end diameter D2.");
            }
            // L > D2/2 also keeps the base corners outside the D2 circle, so
            // the side lines always have a tangent point.
            if (t.Length <= t.EndDiameter / 2)
            {
                Error("Length L must be more than half of D2, or the rounded end runs into the base.");
            }
            if (t.ContourTubeDiameter > 0)
            {
                double r = t.ContourTubeDiameter / 2;
                if (r < t.BaseWidth / 2)
                {
                    Error("The tube is narrower than the base width B, so the base cannot be contoured to it.");
                }
                else if (TabGeometry.ContourDepth(t) >= t.Length - t.Hole / 2)
                {
                    Error("The base contour reaches the hole; make L longer.");
                }
            }
            if (issues.Count > 0 || t.Use == TabUse.Custom)
            {
                return issues;
            }

            TabPreset p = TabPreset.For(t.Use);
            if (p.MinBolt.HasValue && t.Bolt < p.MinBolt.Value - Slack)
            {
                Warn($"B.12.2: {p.Name} bolts must be at least {Inches(p.MinBolt.Value)} (D1).");
            }
            if (!p.MinBolt.HasValue)
            {
                Warn($"B.12.2: the {p.Name.ToLowerInvariant()} bolt size comes from its bracket; check it.");
            }
            if (t.Hole < t.Bolt - Slack)
            {
                Warn("The hole is smaller than the bolt.");
            }
            else if (t.Hole > t.Bolt + 2 * TabInputs.CloseFitClearance + Slack)
            {
                Warn("B.12.2.1: the hole must match the bolt's nominal size; use a close fit (bolt + 1/64 in).");
            }
            if (t.EndDiameter < 2.2 * t.Bolt - Slack)
            {
                Warn($"B.12.2: D2 must be at least 2.2 x D1 = {Inches(2.2 * t.Bolt)}.");
            }
            if (t.BaseWidth < 3 * t.Bolt - Slack)
            {
                Warn($"B.12.2: B must be at least 3 x D1 = {Inches(3 * t.Bolt)}.");
            }
            if (t.Length > p.MaxLength + Slack)
            {
                Warn($"B.12.2: {p.Name} tabs may be at most {Inches(p.MaxLength)} long (L).");
            }
            if (t.Thickness < p.MinThickness - Slack)
            {
                Warn($"B.12.2: {p.Name} tabs must be at least {Inches(p.MinThickness)} thick (t).");
            }
            if (p.DoubleShear && !t.DoubleShear)
            {
                Warn($"B.12.2: {p.Name} tabs must be in double shear.");
            }
            return issues;
        }

        public static string Inches(double meters) =>
            Units.MetersToInches(meters).ToString("0.###", CultureInfo.InvariantCulture) + " in";

        /// <summary>One-line description for the part's Description property.</summary>
        public static string Describe(TabInputs t)
        {
            TabPreset p = TabPreset.For(t.Use);
            string use = t.Use == TabUse.Custom ? "Custom" : p.Name;
            return $"{use} tab, {(t.DoubleShear ? "double" : "single")} shear, " +
                   $"{Inches(t.Bolt)} bolt, {Inches(t.Thickness)} thick";
        }
    }

    /// <summary>
    /// Tab outline in the sketch plane: base line on y = 0 from -B/2 to B/2,
    /// hole center at (0, L), straight sides tangent to the D2 circle.
    /// </summary>
    public static class TabGeometry
    {

        /// <summary>
        /// Where the right side line touches the D2 circle. The left side is
        /// the mirror image (negate X).
        /// </summary>
        public static (double X, double Y) RightTangentPoint(TabInputs t)
        {
            double b = t.BaseWidth / 2, r = t.EndDiameter / 2, l = t.Length;
            // From the circle center C = (0, l) to the corner P = (b, 0).
            double dx = b, dy = -l;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double toP = Math.Atan2(dy, dx);
            double spread = Math.Acos(r / d);
            // Two tangent points at toP +/- spread; the outer one has the larger X.
            double a1 = toP + spread, a2 = toP - spread;
            double x1 = r * Math.Cos(a1), x2 = r * Math.Cos(a2);
            double a = x1 >= x2 ? a1 : a2;
            return (r * Math.Cos(a), l + r * Math.Sin(a));
        }

        /// <summary>How far the base contour cuts up into the tab at its middle (0 when straight).</summary>
        public static double ContourDepth(TabInputs t)
        {
            if (t.ContourTubeDiameter <= 0)
            {
                return 0;
            }
            double r = t.ContourTubeDiameter / 2, b = t.BaseWidth / 2;
            return r - Math.Sqrt(Math.Max(0, r * r - b * b));
        }

        /// <summary>Face area of one tab (outline minus hole and base contour).</summary>
        public static double Area(TabInputs t)
        {
            double b = t.BaseWidth / 2, r = t.EndDiameter / 2, l = t.Length;
            (double tx, double ty) = RightTangentPoint(t);

            // Trapezoid from the base corners to the two tangent points.
            double area = (2 * b + 2 * tx) / 2 * ty;
            // Circular segment above the chord between the tangent points.
            double theta = 2 * Math.Atan2(tx, ty - l); // arc angle over the top
            area += r * r / 2 * (theta - Math.Sin(theta));
            area -= Math.PI * t.Hole * t.Hole / 4;

            if (t.ContourTubeDiameter > 0)
            {
                double rt = t.ContourTubeDiameter / 2;
                double phi = 2 * Math.Asin(b / rt);
                area -= rt * rt / 2 * (phi - Math.Sin(phi));
            }
            return area;
        }
    }
}
