using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Lbr.CadTools.ApiCheck
{
    /// <summary>
    /// Fails when the add-in calls a SolidWorks API member that is newer than
    /// the oldest SolidWorks the team runs (issue #14). Reads every SolidWorks
    /// member the compiled DLL references and looks up its "Availability" in
    /// the local SolidWorks API help (sldworksapi.chm).
    ///
    /// Usage: ApiCheck [--max-year 2025] [--chm path] [--list] add-in.dll...
    /// Exit codes: 0 ok, 1 too-new members found, 2 setup problem.
    /// </summary>
    internal static class Program
    {
        private const int YearOffset = 1992; // SolidWorks major revision 33 = 2025

        private static int Main(string[] args)
        {
            int maxYear = 2025;
            bool list = false;
            string chm = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\sldworksapi.chm";
            var dlls = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--max-year": maxYear = int.Parse(args[++i]); break;
                    case "--chm": chm = args[++i]; break;
                    case "--list": list = true; break;
                    default: dlls.Add(args[i]); break;
                }
            }
            if (dlls.Count == 0 || !File.Exists(chm) || dlls.Any(d => !File.Exists(d)))
            {
                Console.Error.WriteLine("Usage: ApiCheck [--max-year 2025] [--chm sldworksapi.chm] [--list] add-in.dll...");
                Console.Error.WriteLine(File.Exists(chm) ? "A DLL was not found." : $"API help not found: {chm}");
                return 2;
            }

            string work = Path.Combine(Path.GetTempPath(), "lbr-apicheck-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Dictionary<string, int> added = ReadAvailability(chm, work);
                if (added.Count < 1000)
                {
                    Console.Error.WriteLine($"Only {added.Count} entries read from {chm}; the help could not be decompiled.");
                    return 2;
                }

                int maxMajor = maxYear - YearOffset;
                var tooNew = new List<string>();
                foreach (string member in ReferencedMembers(dlls))
                {
                    int? major = Lookup(added, member);
                    string when = major == null ? "before versioned help" : $"SolidWorks {major + YearOffset}";
                    if (major > maxMajor)
                    {
                        tooNew.Add($"{member} (added in {when})");
                    }
                    if (list)
                    {
                        Console.WriteLine($"{member,-60} {when}");
                    }
                }

                if (tooNew.Count > 0)
                {
                    Console.Error.WriteLine($"These SolidWorks API members are newer than SolidWorks {maxYear}, the oldest version the team runs:");
                    tooNew.ForEach(t => Console.Error.WriteLine("  " + t));
                    Console.Error.WriteLine("Use an older equivalent, or raise the minimum version (issue #14).");
                    return 1;
                }
                Console.WriteLine($"API check: every SolidWorks call is available in SolidWorks {maxYear}.");
                return 0;
            }
            finally
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Interface.Member to the SolidWorks major revision that added it.</summary>
        private static Dictionary<string, int> ReadAvailability(string chm, string work)
        {
            // hh.exe -decompile fails on paths with spaces, so work in a temp folder.
            Directory.CreateDirectory(work);
            string copy = Path.Combine(work, "api.chm");
            File.Copy(chm, copy);
            string html = Path.Combine(work, "html");
            Directory.CreateDirectory(html);
            using (Process p = Process.Start(new ProcessStartInfo(
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "hh.exe"),
                       $"-decompile {html} {copy}") { UseShellExecute = false }))
            {
                p.WaitForExit();
            }

            var page = new Regex(@"~SolidWorks\.Interop\.\w+\.(\w+)~(\w+)$");
            var availability = new Regex(@"Availability\s.{0,120}?Revision Number\s+(\d+)", RegexOptions.Singleline);
            var added = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(html, "*.htm*", SearchOption.AllDirectories))
            {
                Match m = page.Match(Path.GetFileNameWithoutExtension(file));
                if (!m.Success)
                {
                    continue;
                }
                string text = Regex.Replace(File.ReadAllText(file), "<[^>]*>", " ");
                Match a = availability.Match(text);
                // Pages without an Availability line describe the original API, older than any supported release.
                added[m.Groups[1].Value + "." + m.Groups[2].Value] = a.Success ? int.Parse(a.Groups[1].Value) : 0;
            }
            return added;
        }

        private static int? Lookup(Dictionary<string, int> added, string member)
        {
            string[] parts = member.Split('.');
            string type = parts[0], name = parts[1];
            foreach (string key in new[] { member, "I" + type + "." + name, type.TrimStart('I') + "." + name })
            {
                if (added.TryGetValue(key, out int major))
                {
                    return major == 0 ? (int?)null : major;
                }
            }
            return null;
        }

        /// <summary>Type.Member for every SolidWorks interop member the DLLs reference.</summary>
        private static SortedSet<string> ReferencedMembers(IEnumerable<string> dlls)
        {
            var used = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string dll in dlls)
            {
                using (var pe = new PEReader(File.OpenRead(dll)))
                {
                    MetadataReader md = pe.GetMetadataReader();
                    foreach (MemberReferenceHandle h in md.MemberReferences)
                    {
                        MemberReference mr = md.GetMemberReference(h);
                        if (mr.Parent.Kind != HandleKind.TypeReference)
                        {
                            continue;
                        }
                        TypeReference tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                        if (!md.GetString(tr.Namespace).StartsWith("SolidWorks.Interop", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string name = Regex.Replace(md.GetString(mr.Name), "^(get_|set_|add_|remove_)", "");
                        if (name != ".ctor")
                        {
                            used.Add(md.GetString(tr.Name) + "." + name);
                        }
                    }
                }
            }
            return used;
        }
    }
}
