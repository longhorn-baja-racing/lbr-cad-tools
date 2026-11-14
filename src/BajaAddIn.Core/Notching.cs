using System;
using System.Collections.Generic;
using System.Linq;

namespace Lbr.CadTools.Core
{
    /// <summary>One tube body of a weldment frame.</summary>
    public sealed class FrameMember
    {
        public FrameMember(string body, string group)
        {
            Body = body;
            Group = group;
        }

        /// <summary>Solid body name, e.g. "Structural Member3[2]".</summary>
        public string Body { get; }

        /// <summary>Structural member feature the body comes from, e.g. "RRH". Priority is set per group.</summary>
        public string Group { get; }
    }

    /// <summary>Two tube bodies that overlap, and whether the overlap is at each one's end.</summary>
    public sealed class FrameJoint
    {
        public FrameJoint(string a, string b, bool atEndOfA, bool atEndOfB)
        {
            A = a;
            B = b;
            AtEndOfA = atEndOfA;
            AtEndOfB = atEndOfB;
        }

        public string A { get; }
        public string B { get; }
        public bool AtEndOfA { get; }
        public bool AtEndOfB { get; }

        /// <summary>Order-free key for overrides: "A|B" with the names sorted.</summary>
        public string Key => string.CompareOrdinal(A, B) <= 0 ? A + "|" + B : B + "|" + A;
    }

    /// <summary>Why a joint is cut the way it is.</summary>
    public enum NotchReason
    {
        /// <summary>Only the coped tube ends at the joint; the other runs through.</summary>
        EndsOnThroughTube,

        /// <summary>Both end here; the lower-priority group is coped.</summary>
        Priority,

        /// <summary>Both end here; the user swapped the priority decision for this joint.</summary>
        Override,
    }

    /// <summary>One coped cut: <see cref="Trimmed"/> is cut to fit around <see cref="Boundary"/>.</summary>
    public sealed class NotchCut
    {
        public NotchCut(FrameJoint joint, string trimmed, string boundary, NotchReason reason)
        {
            Joint = joint;
            Trimmed = trimmed;
            Boundary = boundary;
            Reason = reason;
        }

        public FrameJoint Joint { get; }
        public string Trimmed { get; }
        public string Boundary { get; }
        public NotchReason Reason { get; }
    }

    /// <summary>The planned cuts, grouped per trimmed body in the order the trim features must be made.</summary>
    public sealed class NotchPlan
    {
        public List<NotchCut> Cuts { get; } = new List<NotchCut>();

        /// <summary>Trimmed body to its boundary bodies, in feature order (a body is trimmed after its boundaries are).</summary>
        public List<KeyValuePair<string, List<string>>> TrimOrder { get; } = new List<KeyValuePair<string, List<string>>>();

        /// <summary>Joints where neither tube ends (tubes cross); nothing is cut there.</summary>
        public List<FrameJoint> Crossings { get; } = new List<FrameJoint>();

        /// <summary>Overrides that could not apply (the joint is not a node where both tubes end).</summary>
        public List<string> IgnoredOverrides { get; } = new List<string>();
    }

    /// <summary>
    /// Ordered tube notching (issue #11): decides, for every joint of a weldment
    /// frame, which tube is coped around which. SolidWorks-free.
    /// </summary>
    public static class Notching
    {
        /// <summary>
        /// Default priority by structural member name, highest first: floor
        /// rails, then the main hoops and the rule-book primary members, then
        /// bracing and secondary members. A group matches the first keyword its
        /// name contains (ignoring case); unmatched groups go last in tree order.
        /// </summary>
        public static IReadOnlyList<string> DefaultKeywords { get; } = new[]
        {
            "Floor", "LFS", "RRH", "RHO", "FBM", "SIM", "FAB", "LDB", "LC", "Brace", "SM", "Secondary",
        };

        /// <summary>Groups in default priority order (highest first).</summary>
        public static List<string> DefaultOrder(IEnumerable<string> groupsInTreeOrder, IReadOnlyList<string> keywords = null)
        {
            keywords = keywords ?? DefaultKeywords;
            List<string> groups = groupsInTreeOrder.Distinct(StringComparer.Ordinal).ToList();
            int Rank(string g)
            {
                for (int i = 0; i < keywords.Count; i++)
                {
                    if (g.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
                return keywords.Count;
            }
            // OrderBy is stable: equal ranks keep tree order.
            return groups.OrderBy(Rank).ToList();
        }

        /// <summary>
        /// Merges a saved order with the groups that exist now: saved groups that
        /// still exist keep their place, new groups are slotted in by default rank.
        /// </summary>
        public static List<string> MergeOrder(IEnumerable<string> saved, IEnumerable<string> groupsInTreeOrder)
        {
            List<string> now = groupsInTreeOrder.Distinct(StringComparer.Ordinal).ToList();
            List<string> kept = (saved ?? new string[0]).Where(now.Contains).Distinct(StringComparer.Ordinal).ToList();
            foreach (string g in DefaultOrder(now))
            {
                if (!kept.Contains(g))
                {
                    kept.Add(g);
                }
            }
            return kept;
        }

        /// <summary>
        /// Plans the cuts. <paramref name="order"/> lists groups highest priority
        /// first. <paramref name="swapped"/> holds <see cref="FrameJoint.Key"/>s
        /// whose priority decision is reversed. Joints inside one group are left to
        /// the structural member's own corner treatment.
        /// </summary>
        public static NotchPlan Plan(IReadOnlyList<FrameMember> members, IReadOnlyList<FrameJoint> joints,
                                     IReadOnlyList<string> order, ICollection<string> swapped = null)
        {
            var plan = new NotchPlan();
            Dictionary<string, FrameMember> byBody = members.ToDictionary(m => m.Body, StringComparer.Ordinal);
            int Rank(string body)
            {
                int i = order.ToList().IndexOf(byBody[body].Group);
                return i < 0 ? int.MaxValue : i;
            }
            var usedOverrides = new HashSet<string>(StringComparer.Ordinal);

            foreach (FrameJoint j in joints)
            {
                if (!byBody.ContainsKey(j.A) || !byBody.ContainsKey(j.B) || byBody[j.A].Group == byBody[j.B].Group)
                {
                    continue;
                }
                if (j.AtEndOfA && !j.AtEndOfB)
                {
                    plan.Cuts.Add(new NotchCut(j, j.A, j.B, NotchReason.EndsOnThroughTube));
                }
                else if (j.AtEndOfB && !j.AtEndOfA)
                {
                    plan.Cuts.Add(new NotchCut(j, j.B, j.A, NotchReason.EndsOnThroughTube));
                }
                else if (j.AtEndOfA && j.AtEndOfB)
                {
                    // Lower priority = larger rank is coped; ties go to the later name so the result is stable.
                    bool aIsLower = Rank(j.A) != Rank(j.B) ? Rank(j.A) > Rank(j.B) : string.CompareOrdinal(j.A, j.B) > 0;
                    bool swap = swapped != null && swapped.Contains(j.Key);
                    if (swap)
                    {
                        usedOverrides.Add(j.Key);
                        aIsLower = !aIsLower;
                    }
                    plan.Cuts.Add(aIsLower
                        ? new NotchCut(j, j.A, j.B, swap ? NotchReason.Override : NotchReason.Priority)
                        : new NotchCut(j, j.B, j.A, swap ? NotchReason.Override : NotchReason.Priority));
                }
                else
                {
                    plan.Crossings.Add(j);
                }
            }

            if (swapped != null)
            {
                plan.IgnoredOverrides.AddRange(swapped.Where(k => !usedOverrides.Contains(k)));
            }

            // Feature order: trim a body only after the bodies it is cut against are final.
            // Bodies with no pending boundaries go first, highest priority first; cycles are
            // broken by priority.
            Dictionary<string, List<string>> boundaries = plan.Cuts
                .GroupBy(c => c.Trimmed, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Boundary).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            var pending = new HashSet<string>(boundaries.Keys, StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                List<string> ready = pending.Where(b => boundaries[b].All(x => !pending.Contains(x) || x == b)).ToList();
                if (ready.Count == 0)
                {
                    ready = new List<string> { pending.OrderBy(Rank).ThenBy(b => b, StringComparer.Ordinal).First() };
                }
                foreach (string b in ready.OrderBy(Rank).ThenBy(b => b, StringComparer.Ordinal))
                {
                    plan.TrimOrder.Add(new KeyValuePair<string, List<string>>(b, boundaries[b]));
                    pending.Remove(b);
                }
            }
            return plan;
        }

        /// <summary>Part property holding the group order, highest first, separated by '|'.</summary>
        public const string OrderProperty = "LBR Notch Order";

        /// <summary>Part property holding swapped joint keys, separated by ';'.</summary>
        public const string SwapProperty = "LBR Notch Swaps";

        /// <summary>Name prefix of the trim features this tool makes; re-running replaces them.</summary>
        public const string FeaturePrefix = "LBR Notch";

        public static string JoinOrder(IEnumerable<string> order) => string.Join("|", order);

        public static List<string> SplitOrder(string text) =>
            (text ?? "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static string JoinSwaps(IEnumerable<string> keys) => string.Join(";", keys.OrderBy(k => k, StringComparer.Ordinal));

        public static HashSet<string> SplitSwaps(string text) =>
            new HashSet<string>((text ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Contains("|")), StringComparer.Ordinal);

        /// <summary>
        /// True when an overlap reaches a tube's end: the overlap's farthest
        /// point along the tube axis is within <paramref name="tolerance"/> of
        /// the tube's extreme point in that direction (either direction).
        /// </summary>
        /// <param name="tubeMin">Tube extent along its axis (lowest projection).</param>
        /// <param name="tubeMax">Tube extent along its axis (highest projection).</param>
        /// <param name="overlapMin">Overlap extent along the same axis.</param>
        /// <param name="overlapMax">Overlap extent along the same axis.</param>
        public static bool ReachesEnd(double tubeMin, double tubeMax, double overlapMin, double overlapMax, double tolerance) =>
            overlapMin - tubeMin <= tolerance || tubeMax - overlapMax <= tolerance;
    }
}
