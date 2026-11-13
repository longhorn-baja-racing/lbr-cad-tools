using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Lbr.CadTools.Setup
{
    /// <summary>
    /// Install and uninstall steps. No UI here; progress goes to the callback.
    /// Runs elevated (app.manifest requires administrator).
    /// </summary>
    internal static class Installer
    {
        private const string PayloadPrefix = "payload/";
        private const string AddInDll = "BajaAddIn.dll";
        private const string SetupExeName = "LBR-CAD-Tools-Setup.exe";
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LbrCadTools";

        public static string InstallDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LBR CAD Tools");

        /// <summary>Version this setup installs (the setup's own version; both come from Directory.Build.props).</summary>
        public static Version PayloadVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// Installed version: from the Windows uninstall entry, or "0.0" for an
        /// older install made by the scripts (files present, no entry). Null when
        /// not installed.
        /// </summary>
        public static Version InstalledVersion()
        {
            using (RegistryKey key = LocalMachine().OpenSubKey(UninstallKeyPath))
            {
                if (key?.GetValue("DisplayVersion") is string v && Version.TryParse(v, out Version parsed))
                {
                    return parsed;
                }
            }
            return File.Exists(Path.Combine(InstallDir, AddInDll)) ? new Version(0, 0) : null;
        }

        public static bool SolidWorksRunning() => Process.GetProcessesByName("SLDWORKS").Length > 0;

        /// <summary>True when this exe is the copy kept in the install folder (started from Windows Apps).</summary>
        public static bool RunningFromInstallDir() =>
            string.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).TrimEnd('\\'),
                InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        public static void Install(Action<string> progress)
        {
            if (SolidWorksRunning())
            {
                throw new InvalidOperationException("SolidWorks is open. Save your work, close SolidWorks, and try again.");
            }

            string dll = Path.Combine(InstallDir, AddInDll);
            if (File.Exists(dll))
            {
                progress("Removing the previous version");
                RegAsm("/unregister", dll); // best effort; files are replaced next
                foreach (string f in Directory.GetFiles(InstallDir))
                {
                    if (!string.Equals(f, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(f);
                    }
                }
            }

            progress($"Copying files to {InstallDir}");
            Directory.CreateDirectory(InstallDir);
            Assembly self = Assembly.GetExecutingAssembly();
            foreach (string name in self.GetManifestResourceNames().Where(n => n.StartsWith(PayloadPrefix, StringComparison.Ordinal)))
            {
                using (Stream source = self.GetManifestResourceStream(name))
                using (FileStream target = File.Create(Path.Combine(InstallDir, name.Substring(PayloadPrefix.Length))))
                {
                    source.CopyTo(target);
                }
            }
            string setupCopy = Path.Combine(InstallDir, SetupExeName);
            if (!RunningFromInstallDir())
            {
                File.Copy(self.Location, setupCopy, true);
            }

            progress("Registering with SolidWorks");
            (int code, string output) = RegAsm("/codebase", dll);
            if (code != 0)
            {
                throw new InvalidOperationException($"Registering the add-in failed (RegAsm exit code {code}).\n{output}");
            }

            progress("Adding LBR CAD Tools to Windows Apps");
            using (RegistryKey key = LocalMachine().CreateSubKey(UninstallKeyPath))
            {
                key.SetValue("DisplayName", "LBR CAD Tools");
                key.SetValue("DisplayVersion", PayloadVersion.ToString(3));
                key.SetValue("Publisher", "Longhorn Baja Racing");
                key.SetValue("Comments", "SolidWorks add-in for Longhorn Baja Racing, The University of Texas at Austin");
                key.SetValue("URLInfoAbout", "https://github.com/longhorn-baja-racing/lbr-cad-tools");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("DisplayIcon", setupCopy + ",0");
                key.SetValue("UninstallString", $"\"{setupCopy}\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long bytes = Directory.GetFiles(InstallDir).Sum(f => new FileInfo(f).Length);
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        public static void Uninstall(Action<string> progress)
        {
            if (SolidWorksRunning())
            {
                throw new InvalidOperationException("SolidWorks is open. Close SolidWorks and try again.");
            }

            string dll = Path.Combine(InstallDir, AddInDll);
            if (File.Exists(dll))
            {
                progress("Unregistering from SolidWorks");
                (int code, string output) = RegAsm("/unregister", dll);
                if (code != 0)
                {
                    throw new InvalidOperationException($"Unregistering the add-in failed (RegAsm exit code {code}).\n{output}");
                }
            }

            progress("Removing it from Windows Apps");
            LocalMachine().DeleteSubKeyTree(UninstallKeyPath, false);

            progress($"Deleting {InstallDir}");
            if (RunningFromInstallDir())
            {
                // This exe lives in the folder; delete everything else now and the
                // folder once this process has exited.
                foreach (string f in Directory.GetFiles(InstallDir).Where(f =>
                             !string.Equals(f, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(f);
                }
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            else if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, true);
            }
        }

        /// <summary>
        /// Newest installed SolidWorks year from the registry, or null. Used to
        /// warn when it is older than the one this build targets.
        /// </summary>
        public static int? NewestSolidWorksYear()
        {
            using (RegistryKey sw = LocalMachine().OpenSubKey(@"SOFTWARE\SolidWorks"))
            {
                List<int> years = (sw?.GetSubKeyNames() ?? new string[0])
                    .Where(n => n.StartsWith("SOLIDWORKS ", StringComparison.OrdinalIgnoreCase))
                    .Select(n => int.TryParse(n.Substring(11), out int y) ? y : 0)
                    .Where(y => y > 0)
                    .ToList();
                return years.Count == 0 ? (int?)null : years.Max();
            }
        }

        /// <summary>SolidWorks year of the embedded interop assemblies (major 34 = 2026).</summary>
        public static int BuiltForYear()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadPrefix + "SolidWorks.Interop.sldworks.dll"))
            {
                string temp = Path.GetTempFileName();
                try
                {
                    using (FileStream f = File.Create(temp))
                    {
                        s.CopyTo(f);
                    }
                    return 1992 + AssemblyName.GetAssemblyName(temp).Version.Major;
                }
                finally
                {
                    File.Delete(temp);
                }
            }
        }

        private static RegistryKey LocalMachine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        /// <summary>Runs 64-bit RegAsm; its RA0000 "unsigned assembly" warning is normal and ignored.</summary>
        private static (int Code, string Output) RegAsm(string action, string dll)
        {
            string regasm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe");
            if (!File.Exists(regasm))
            {
                throw new InvalidOperationException("64-bit RegAsm was not found. Install .NET Framework 4.8 (Windows Update) and try again.");
            }
            var psi = new ProcessStartInfo(regasm, $"{action} /nologo \"{dll}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                string filtered = string.Join("\n", output.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("RA0000")));
                return (p.ExitCode, filtered.Trim());
            }
        }
    }
}
