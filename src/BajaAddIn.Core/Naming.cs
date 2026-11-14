using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lbr.CadTools.Core
{
    /// <summary>What a SolidWorks file is, for the name prefix.</summary>
    public enum CadFileKind
    {
        Part,
        Assembly,
        Drawing,
    }

    /// <summary>
    /// A file name split by the team convention
    /// [a|d][TEAM]_####_[PartName]_REV##_V## (issue #6):
    /// "a" for assemblies, "d" for drawings, nothing for parts; REVSTD marks
    /// standard (off-the-shelf) parts and may leave out the version.
    /// </summary>
    public sealed class CadName
    {
        public CadName(string prefix, string team, int number, string partName, int? revision, int? version)
        {
            Prefix = prefix ?? "";
            Team = team;
            Number = number;
            PartName = partName;
            Revision = revision;
            Version = version;
        }

        /// <summary>"", "a", or "d".</summary>
        public string Prefix { get; }

        /// <summary>Three-letter team code, e.g. "CHA".</summary>
        public string Team { get; }

        /// <summary>The #### number, unique within a team.</summary>
        public int Number { get; }

        public string PartName { get; }

        /// <summary>REV number; null for REVSTD (standard parts).</summary>
        public int? Revision { get; }

        public bool IsStandard => Revision == null;

        /// <summary>V number; null when left out (allowed for REVSTD).</summary>
        public int? Version { get; }

        /// <summary>"CHA_0001": team and number, the key that must be unique.</summary>
        public string Key => Team + "_" + Number.ToString("0000", CultureInfo.InvariantCulture);

        public string RevisionText => Revision == null ? "REVSTD" : "REV" + Revision.Value.ToString("00", CultureInfo.InvariantCulture);

        public override string ToString() => NamingConvention.Format(
            Prefix == "a" ? CadFileKind.Assembly : Prefix == "d" ? CadFileKind.Drawing : CadFileKind.Part,
            Team, Number, PartName, Revision, Version);
    }

    /// <summary>One file's place in a naming check.</summary>
    public sealed class NamedFile
    {
        public NamedFile(string baseName, CadFileKind kind)
        {
            BaseName = baseName;
            Kind = kind;
        }

        /// <summary>File name without folder or extension.</summary>
        public string BaseName { get; }

        public CadFileKind Kind { get; }
    }

    /// <summary>Result of checking one file.</summary>
    public sealed class NameCheck
    {
        public NameCheck(NamedFile file)
        {
            File = file;
        }

        public NamedFile File { get; }

        /// <summary>The parsed name, or null when it does not follow the pattern at all.</summary>
        public CadName Name { get; internal set; }

        /// <summary>Rule breaks, each one sentence with the fix.</summary>
        public List<string> Problems { get; } = new List<string>();

        public bool Ok => Problems.Count == 0;
    }

    public static class NamingConvention
    {
        public const string Pattern = "[a|d][TEAM]_####_[PartName]_REV##_V##";

        /// <summary>Team codes from the convention document; more can be added in settings.json.</summary>
        public static IReadOnlyDictionary<string, string> DefaultTeams { get; } = new Dictionary<string, string>
        {
            ["POW"] = "Powertrain",
            ["CHA"] = "Chassis",
            ["ERG"] = "Ergonomics",
            ["DYN"] = "Dynamics",
        };

        private static readonly Regex Strict = new Regex(
            @"^(?<prefix>[ad]?)(?<team>[A-Z]{3})_(?<num>\d{4})_(?<name>[A-Za-z0-9]+)_REV(?<rev>\d{2}|STD)(?:_V(?<ver>\d{1,2}))?$",
            RegexOptions.CultureInvariant);

        /// <summary>The prefix a kind of file must have.</summary>
        public static string PrefixFor(CadFileKind kind) =>
            kind == CadFileKind.Assembly ? "a" : kind == CadFileKind.Drawing ? "d" : "";

        /// <summary>Builds a name, e.g. aDYN_0006_ControlArm_REV01_V02. A null revision gives REVSTD.</summary>
        public static string Format(CadFileKind kind, string team, int number, string partName, int? revision, int? version)
        {
            string name = PrefixFor(kind) + team + "_" + number.ToString("0000", CultureInfo.InvariantCulture) + "_" + partName + "_" +
                          (revision == null ? "REVSTD" : "REV" + revision.Value.ToString("00", CultureInfo.InvariantCulture));
            if (version != null)
            {
                name += "_V" + version.Value.ToString("00", CultureInfo.InvariantCulture);
            }
            return name;
        }

        /// <summary>Parses a name that follows the pattern; null otherwise. Does not check the prefix against the file kind.</summary>
        public static CadName Parse(string baseName)
        {
            Match m = Strict.Match(baseName ?? "");
            if (!m.Success)
            {
                return null;
            }
            int? rev = m.Groups["rev"].Value == "STD" ? (int?)null : int.Parse(m.Groups["rev"].Value, CultureInfo.InvariantCulture);
            int? ver = m.Groups["ver"].Success ? int.Parse(m.Groups["ver"].Value, CultureInfo.InvariantCulture) : (int?)null;
            return new CadName(m.Groups["prefix"].Value, m.Groups["team"].Value,
                int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture), m.Groups["name"].Value, rev, ver);
        }

        /// <summary>Turns free text into a PartName: words joined in CamelCase, letters and digits only.</summary>
        public static string ToPartName(string text)
        {
            IEnumerable<string> words = Regex.Split(text ?? "", "[^A-Za-z0-9]+").Where(w => w.Length > 0);
            return string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        /// <summary>Checks one file name on its own: pattern, prefix for its kind, team code.</summary>
        public static NameCheck CheckOne(NamedFile file, ICollection<string> teams)
        {
            var check = new NameCheck(file);
            string name = file.BaseName ?? "";
            CadName parsed = Parse(name);
            check.Name = parsed;
            if (parsed == null)
            {
                check.Problems.AddRange(Explain(name, file.Kind, teams));
                return check;
            }

            string want = PrefixFor(file.Kind);
            if (parsed.Prefix != want)
            {
                check.Problems.Add(want.Length == 0
                    ? $"Parts have no letter in front; drop the \"{parsed.Prefix}\"."
                    : $"{KindName(file.Kind)} names start with \"{want}\" (lowercase), e.g. {want}{parsed.Team}_....");
            }
            if (teams != null && teams.Count > 0 && !teams.Contains(parsed.Team))
            {
                check.Problems.Add($"Team code {parsed.Team} is not one of {string.Join(", ", teams)} (add new teams in Settings).");
            }
            if (!parsed.IsStandard && parsed.Version == null)
            {
                check.Problems.Add("Add the version at the end, e.g. _V01. Only REVSTD parts can leave it out.");
            }
            return check;
        }

        /// <summary>Finds the first thing wrong with a name that does not match the pattern.</summary>
        private static IEnumerable<string> Explain(string name, CadFileKind kind, ICollection<string> teams)
        {
            if (name.Contains(" "))
            {
                yield return "No spaces; join words in CamelCase and separate fields with underscores.";
            }
            string[] fields = name.Split('_');
            if (fields.Length < 4)
            {
                yield return $"Does not follow {Pattern} (needs 4 or 5 fields separated by underscores).";
                yield break;
            }
            string want = PrefixFor(kind);
            Match head = Regex.Match(fields[0], "^(?<p>[a-z]?)(?<t>[A-Za-z]{3})$");
            if (!head.Success)
            {
                yield return $"First field should be {(want.Length > 0 ? "\"" + want + "\" + " : "")}the 3-letter team code, e.g. {want}CHA.";
            }
            else if (head.Groups["t"].Value != head.Groups["t"].Value.ToUpperInvariant())
            {
                yield return "Write the team code in capitals, e.g. CHA.";
            }
            if (!Regex.IsMatch(fields[1], @"^\d{4}$"))
            {
                yield return "Second field must be a 4-digit number, e.g. 0012.";
            }
            if (!Regex.IsMatch(fields[2], "^[A-Za-z0-9]+$"))
            {
                yield return "Part name: letters and digits only, words in CamelCase (e.g. ControlArmLeft).";
            }
            if (!Regex.IsMatch(fields[3], @"^REV(\d{2}|STD)$"))
            {
                yield return "Fourth field must be REV## (e.g. REV01) or REVSTD.";
            }
            if (fields.Length == 5 && !Regex.IsMatch(fields[4], @"^V\d{1,2}$"))
            {
                yield return "Last field must be the version, e.g. V01.";
            }
            if (fields.Length > 5)
            {
                yield return "Too many underscores; the part name itself cannot contain one.";
            }
        }

        /// <summary>
        /// Checks the files of one assembly together: each name on its own, the
        /// same team number used for two different things, two versions of one
        /// file, and revisions that do not match the top assembly (REVSTD is
        /// exempt). <paramref name="top"/> is the assembly being checked.
        /// </summary>
        public static List<NameCheck> CheckAssembly(NamedFile top, IReadOnlyList<NamedFile> files, ICollection<string> teams)
        {
            var all = new List<NamedFile> { top };
            all.AddRange(files.Where(f => !string.Equals(f.BaseName, top.BaseName, StringComparison.OrdinalIgnoreCase) || f.Kind != top.Kind));
            List<NameCheck> checks = all.Select(f => CheckOne(f, teams)).ToList();

            CadName topName = checks[0].Name;
            if (topName != null && !topName.IsStandard)
            {
                foreach (NameCheck c in checks.Skip(1))
                {
                    if (c.Name != null && !c.Name.IsStandard && c.Name.Revision != topName.Revision)
                    {
                        c.Problems.Add($"{c.Name.RevisionText} does not match the assembly's {topName.RevisionText}. " +
                                       "Every part in a REV assembly must be the same REV (or REVSTD).");
                    }
                }
            }

            // Drawings share their model's number, so only parts and assemblies are compared.
            foreach (IGrouping<string, NameCheck> group in checks
                         .Where(c => c.Name != null && c.File.Kind != CadFileKind.Drawing)
                         .GroupBy(c => c.Name.Key))
            {
                List<string> names = group.Select(c => c.Name.PartName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count > 1)
                {
                    foreach (NameCheck c in group)
                    {
                        c.Problems.Add($"{group.Key} is also used for {string.Join(", ", names.Where(n => !n.Equals(c.Name.PartName, StringComparison.OrdinalIgnoreCase)))}; " +
                                       "each team number can be used once.");
                    }
                }
                else if (group.Select(c => (c.Name.Revision, c.Name.Version)).Distinct().Count() > 1)
                {
                    foreach (NameCheck c in group)
                    {
                        c.Problems.Add($"More than one revision or version of {group.Key} is in this assembly.");
                    }
                }
            }
            return checks;
        }

        // Team and number of names that are nearly right (any letter case), so a
        // misnamed file still holds its number.
        private static readonly Regex Loose = new Regex(@"^[ad]?(?<team>[A-Za-z]{3})_(?<num>\d{4})(_|$)", RegexOptions.CultureInvariant);

        /// <summary>
        /// The next unused number for a team, one above the highest seen in
        /// <paramref name="baseNames"/>. Names with the team in the wrong case
        /// still count, so the new number cannot collide with them.
        /// </summary>
        public static int NextNumber(string team, IEnumerable<string> baseNames)
        {
            int max = 0;
            foreach (string n in baseNames)
            {
                Match m = Loose.Match(n ?? "");
                if (m.Success && string.Equals(m.Groups["team"].Value, team, StringComparison.OrdinalIgnoreCase))
                {
                    max = Math.Max(max, int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture));
                }
            }
            return max + 1;
        }

        public static string KindName(CadFileKind kind) =>
            kind == CadFileKind.Assembly ? "Assembly" : kind == CadFileKind.Drawing ? "Drawing" : "Part";
    }
}
