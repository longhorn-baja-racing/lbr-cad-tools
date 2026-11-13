using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace Lbr.CadTools.AddIn.Features.AddInConfig
{
    /// <summary>
    /// Loads and unloads other SolidWorks add-ins listed in settings.json.
    /// Entries are titles as shown in Tools > Add-Ins, or DLL paths. Titles
    /// are looked up in HKLM\SOFTWARE\SolidWorks\AddIns and the add-in's COM
    /// registration (InprocServer32, or its CodeBase for .NET add-ins).
    /// </summary>
    internal static class AddInManager
    {
        internal sealed class InstalledAddIn
        {
            public string Clsid { get; set; }
            public string Title { get; set; }

            /// <summary>DLL path, or null when the COM registration has none.</summary>
            public string Path { get; set; }
        }

        /// <summary>Add-ins registered with SolidWorks on this computer, by title.</summary>
        public static List<InstalledAddIn> Installed()
        {
            var result = new List<InstalledAddIn>();
            using (RegistryKey addIns = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SolidWorks\AddIns"))
            {
                if (addIns == null)
                {
                    return result;
                }
                foreach (string clsid in addIns.GetSubKeyNames())
                {
                    using (RegistryKey key = addIns.OpenSubKey(clsid))
                    {
                        string title = key?.GetValue("Title") as string;
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            continue;
                        }
                        result.Add(new InstalledAddIn { Clsid = clsid, Title = title.Trim(), Path = DllPath(clsid) });
                    }
                }
            }
            return result.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string DllPath(string clsid)
        {
            using (RegistryKey server = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32"))
            {
                if (server == null)
                {
                    return null;
                }
                if (server.GetValue("CodeBase") is string codeBase && Uri.TryCreate(codeBase, UriKind.Absolute, out Uri uri))
                {
                    return uri.LocalPath;
                }
                string path = server.GetValue(null) as string;
                return string.IsNullOrWhiteSpace(path) || path.EndsWith("mscoree.dll", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : System.Environment.ExpandEnvironmentVariables(path);
            }
        }

        /// <summary>The DLL path for a settings entry, or null with <paramref name="error"/> set.</summary>
        public static string Resolve(string entry, IReadOnlyList<InstalledAddIn> installed, out string error)
        {
            error = null;
            if (entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(entry))
                {
                    return entry;
                }
                error = $"\"{entry}\": file not found.";
                return null;
            }

            InstalledAddIn match = installed.FirstOrDefault(a => string.Equals(a.Title, entry, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                error = $"\"{entry}\": no add-in with that title on this computer (see the list in Settings).";
                return null;
            }
            if (match.Path == null)
            {
                error = $"\"{entry}\": its DLL could not be found in the registry; use the DLL path instead.";
                return null;
            }
            return match.Path;
        }

        /// <summary>
        /// Applies the load and unload lists. This add-in is never unloaded by
        /// its own settings. Returns one line per problem.
        /// </summary>
        public static List<string> Apply(ISldWorks app, AddInSettings settings, string ownClsid)
        {
            var problems = new List<string>();
            if (settings.LoadAddIns.Count == 0 && settings.UnloadAddIns.Count == 0)
            {
                return problems;
            }
            List<InstalledAddIn> installed = Installed();
            string ownPath = installed.FirstOrDefault(a => string.Equals(a.Clsid, ownClsid, StringComparison.OrdinalIgnoreCase))?.Path;

            foreach ((string entry, bool load) in settings.LoadAddIns.Select(e => (e, true))
                         .Concat(settings.UnloadAddIns.Select(e => (e, false))))
            {
                string path = Resolve(entry, installed, out string error);
                if (path == null)
                {
                    problems.Add(error);
                    continue;
                }
                if (ownPath != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(ownPath), StringComparison.OrdinalIgnoreCase))
                {
                    if (!load)
                    {
                        problems.Add($"\"{entry}\" is LBR CAD Tools itself; it cannot unload itself. Use Tools > Add-Ins instead.");
                    }
                    continue;
                }

                int code = load ? app.LoadAddIn(path) : app.UnloadAddIn(path);
                Log.Info($"{(load ? "LoadAddIn" : "UnloadAddIn")} {path} -> {code}");
                string problem = Describe(entry, load, code);
                if (problem != null)
                {
                    problems.Add(problem);
                }
            }
            return problems;
        }

        /// <summary>
        /// Return codes of LoadAddIn / UnloadAddIn (checked on SW 2026 SP5 with
        /// the smoke test): 0 = done, 2 = already in that state, anything else
        /// is an error.
        /// </summary>
        internal static string Describe(string entry, bool load, int code)
        {
            switch (code)
            {
                case 0:
                case 2:
                    return null;
                default:
                    return $"\"{entry}\": SolidWorks could not {(load ? "load" : "unload")} it (code {code}).";
            }
        }
    }
}
