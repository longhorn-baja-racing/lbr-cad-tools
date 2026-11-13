using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lbr.CadTools.Core
{
    /// <summary>
    /// Builds the result message for Fix All / Float All.
    /// </summary>
    public static class FixStateReport
    {
        /// <summary>At most this many failed component names are listed.</summary>
        public const int MaxListedFailures = 10;

        /// <param name="fix">True for Fix All, false for Float All.</param>
        /// <param name="changed">Components whose state was changed.</param>
        /// <param name="unchanged">Components that were already in the target state.</param>
        /// <param name="failed">Names of components that could not be changed.</param>
        public static string Format(bool fix, int changed, int unchanged, IReadOnlyList<string> failed)
        {
            if (changed < 0) throw new ArgumentOutOfRangeException(nameof(changed));
            if (unchanged < 0) throw new ArgumentOutOfRangeException(nameof(unchanged));
            failed = failed ?? Array.Empty<string>();

            string state = fix ? "fixed" : "floating";
            string verb = fix ? "Fixed" : "Floated";

            if (changed + unchanged + failed.Count == 0)
            {
                return "The assembly has no top-level components.";
            }

            var sb = new StringBuilder();
            if (changed == 0 && failed.Count == 0)
            {
                sb.Append($"All {Count(unchanged)} top-level {Plural(unchanged, "component is", "components are")} already {state}.");
            }
            else
            {
                sb.Append($"{verb} {Count(changed)} top-level {Plural(changed, "component", "components")}.");
                if (unchanged > 0)
                {
                    sb.Append($" {Count(unchanged)} {Plural(unchanged, "was", "were")} already {state}.");
                }
            }

            if (failed.Count > 0)
            {
                sb.Append($"\n\nCould not change {Count(failed.Count)} {Plural(failed.Count, "component", "components")}:");
                foreach (string name in failed.Take(MaxListedFailures))
                {
                    sb.Append($"\n  {name}");
                }
                if (failed.Count > MaxListedFailures)
                {
                    sb.Append($"\n  ...and {failed.Count - MaxListedFailures} more");
                }
            }

            if (changed > 0)
            {
                sb.Append(fix
                    ? "\n\nCtrl+Z cannot undo this. Use Float All to reverse it."
                    : "\n\nCtrl+Z cannot undo this. Use Fix All to reverse it.");
            }
            if (fix && changed > 0)
            {
                sb.Append("\nFixing applies to all configurations.");
            }
            return sb.ToString();
        }

        private static string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Plural(int n, string one, string many) => n == 1 ? one : many;
    }
}
