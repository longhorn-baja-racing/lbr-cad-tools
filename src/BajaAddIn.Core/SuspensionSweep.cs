using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Lbr.CadTools.Core
{
    /// <summary>The suspension at one value of the driving dimension.</summary>
    public sealed class SweepStep
    {
        public SweepStep(double driver, IReadOnlyList<CornerGeometry> corners)
        {
            Driver = driver;
            Corners = corners;
        }

        /// <summary>Driving dimension value in SolidWorks units (meters or radians).</summary>
        public double Driver { get; }

        public IReadOnlyList<CornerGeometry> Corners { get; }
    }

    /// <summary>How one corner changes over the sweep.</summary>
    public sealed class SweepSummary
    {
        public Corner Corner { get; set; }

        /// <summary>Wheel center travel range, meters (bump positive).</summary>
        public double TravelMin { get; set; }

        public double TravelMax { get; set; }

        public double CamberMin { get; set; }

        public double CamberMax { get; set; }

        /// <summary>Camber change per inch of bump, degrees (least squares).</summary>
        public double CamberGainPerInch { get; set; }

        public double ToeMin { get; set; }

        public double ToeMax { get; set; }

        /// <summary>Toe change per inch of bump, degrees (bump steer, positive = toe-in).</summary>
        public double BumpSteerPerInch { get; set; }
    }

    /// <summary>
    /// Suspension travel sweep (issue #12): step a driving dimension, record
    /// the alignment at each step, and sum it up as camber gain and bump steer.
    /// </summary>
    public static class SuspensionSweep
    {
        /// <summary><paramref name="count"/> evenly spaced values from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
        public static List<double> Values(double from, double to, int count)
        {
            if (count < 2)
            {
                throw new ArgumentException("Use at least 2 steps.");
            }
            return Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1)).ToList();
        }

        /// <summary>
        /// Wheel travel of each corner at each step: wheel center height minus
        /// its height in <paramref name="reference"/> (the model as it was before the sweep).
        /// </summary>
        public static double Travel(CornerGeometry at, IReadOnlyList<CornerGeometry> reference)
        {
            CornerGeometry r = reference.FirstOrDefault(c => c.Corner == at.Corner);
            return r == null ? 0 : at.WheelCenter.Z - r.WheelCenter.Z;
        }

        public static List<SweepSummary> Summarize(IReadOnlyList<SweepStep> steps, IReadOnlyList<CornerGeometry> reference)
        {
            var result = new List<SweepSummary>();
            IEnumerable<Corner> corners = steps.SelectMany(s => s.Corners).Select(c => c.Corner).Distinct().OrderBy(c => c);
            foreach (Corner corner in corners)
            {
                List<CornerGeometry> series = steps.Select(s => s.Corners.FirstOrDefault(c => c.Corner == corner)).Where(c => c != null).ToList();
                List<double> travel = series.Select(c => Travel(c, reference)).ToList();
                result.Add(new SweepSummary
                {
                    Corner = corner,
                    TravelMin = travel.Min(),
                    TravelMax = travel.Max(),
                    CamberMin = series.Min(c => c.Camber),
                    CamberMax = series.Max(c => c.Camber),
                    CamberGainPerInch = Slope(travel, series.Select(c => c.Camber).ToList()) * Units.MetersPerInch,
                    ToeMin = series.Min(c => c.Toe),
                    ToeMax = series.Max(c => c.Toe),
                    BumpSteerPerInch = Slope(travel, series.Select(c => c.Toe).ToList()) * Units.MetersPerInch,
                });
            }
            return result;
        }

        /// <summary>Least squares slope of y over x; 0 when x does not change.</summary>
        public static double Slope(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            int n = Math.Min(x.Count, y.Count);
            if (n < 2)
            {
                return 0;
            }
            double mx = x.Take(n).Average(), my = y.Take(n).Average();
            double sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sxx += (x[i] - mx) * (x[i] - mx);
                sxy += (x[i] - mx) * (y[i] - my);
            }
            return sxx < 1e-18 ? 0 : sxy / sxx;
        }

        /// <summary>
        /// CSV with one row per step: the driving value (in <paramref name="driverUnit"/>
        /// via <paramref name="toDisplay"/>), then per corner travel (mm), camber,
        /// toe, caster, KPI (degrees), scrub and trail (mm).
        /// </summary>
        public static string Csv(IReadOnlyList<SweepStep> steps, IReadOnlyList<CornerGeometry> reference,
                                 string driverName, string driverUnit, Func<double, double> toDisplay)
        {
            string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            List<Corner> corners = steps.SelectMany(s => s.Corners).Select(c => c.Corner).Distinct().OrderBy(c => c).ToList();
            var sb = new StringBuilder();
            var header = new List<string> { $"{driverName} ({driverUnit})" };
            foreach (Corner c in corners)
            {
                header.AddRange(new[]
                {
                    $"{c} travel (mm)", $"{c} camber (deg)", $"{c} toe-in (deg)", $"{c} caster (deg)", $"{c} KPI (deg)",
                    $"{c} scrub (mm)", $"{c} trail (mm)",
                });
            }
            sb.AppendLine(string.Join(",", header.Select(Quote)));
            foreach (SweepStep s in steps)
            {
                var row = new List<string> { F(toDisplay(s.Driver)) };
                foreach (Corner c in corners)
                {
                    CornerGeometry g = s.Corners.FirstOrDefault(x => x.Corner == c);
                    if (g == null)
                    {
                        row.AddRange(Enumerable.Repeat("", 7));
                        continue;
                    }
                    row.AddRange(new[]
                    {
                        F(Travel(g, reference) * 1000), F(g.Camber), F(g.Toe), F(g.Caster), F(g.Kpi),
                        F(g.ScrubRadius * 1000), F(g.MechanicalTrail * 1000),
                    });
                }
                sb.AppendLine(string.Join(",", row));
            }
            return sb.ToString();
        }

        private static string Quote(string s) => s.Contains(",") || s.Contains("\"") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
