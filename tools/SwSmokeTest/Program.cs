using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Features.FixComponents;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.SmokeTest
{
    /// <summary>
    /// Starts SolidWorks (hidden by default), builds throwaway models, runs the
    /// add-in's feature code against them, and checks the results.
    ///
    /// Anything that needs a person in the SolidWorks UI is checked by hand;
    /// --keep leaves the models for that.
    ///
    /// Options:
    ///   --visible  show the SolidWorks window
    ///   --keep     save the models to Documents\LBR CAD Tools Test (not deleted)
    /// Exit code: 0 pass, 1 check failed, 2 could not run.
    /// </summary>
    internal static class Program
    {
        private const double MinFreeCommitGb = 6.0;
        private const int WatchdogMinutes = 4;

        private static int failures;

        [StructLayout(LayoutKind.Sequential)]
        private sealed class MemoryStatusEx
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        /// <summary>Free commit (RAM + page file) in GB; AvailPageFile is the commit headroom.</summary>
        private static double FreeCommitGb()
        {
            var status = new MemoryStatusEx();
            return GlobalMemoryStatusEx(status) ? status.AvailPageFile / (1024.0 * 1024 * 1024) : double.NaN;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            bool visible = args.Contains("--visible");
            bool keep = args.Contains("--keep");

            if (Process.GetProcessesByName("SLDWORKS").Length > 0)
            {
                Console.Error.WriteLine("SolidWorks is running. Close it first so the test does not touch your session.");
                return 2;
            }

            double freeCommitGb = FreeCommitGb();
            Console.WriteLine($"Free commit memory: {freeCommitGb:F1} GB");
            if (freeCommitGb < MinFreeCommitGb)
            {
                Console.WriteLine($"  Warning: below {MinFreeCommitGb} GB. SolidWorks may stop with a low-memory dialog.");
                Console.WriteLine("  Close browsers, Discord, Steam, etc. and retry if the run times out.");
            }

            // Any modal dialog blocks the API call forever, so time the run out.
            var watchdog = new System.Threading.Timer(_ =>
            {
                Console.WriteLine();
                Console.WriteLine($"TIMEOUT after {WatchdogMinutes} min. SolidWorks is probably showing a dialog");
                Console.WriteLine("(most often 'critically low on committed memory'). Closing it.");
                foreach (Process p in Process.GetProcessesByName("SLDWORKS"))
                {
                    p.Kill();
                }
                System.Environment.Exit(2);
            }, null, TimeSpan.FromMinutes(WatchdogMinutes), System.Threading.Timeout.InfiniteTimeSpan);

            string dir = keep
                ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "LBR CAD Tools Test")
                : Path.Combine(Path.GetTempPath(), "lbr-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (keep && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);

            ISldWorks app = null;
            var clock = Stopwatch.StartNew();
            try
            {
                Step(visible ? "Starting SolidWorks (visible; do not click in it)" : "Starting SolidWorks (hidden)");
                app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                app.Visible = visible;
                Console.WriteLine($"  SolidWorks {app.RevisionNumber()}, started in {clock.Elapsed.TotalSeconds:F0} s");

                // A visible new assembly opens the Insert Component panel. Turn
                // that off for the run, then restore the user's setting.
                const int beginAssembly = (int)swUserPreferenceToggle_e.swInsertComponentForNewAssembly;
                bool beginAssemblySetting = app.GetUserPreferenceToggle(beginAssembly);
                app.SetUserPreferenceToggle(beginAssembly, false);
                try
                {
                    FixComponentsTest(app, dir, keep);
                }
                finally
                {
                    app.SetUserPreferenceToggle(beginAssembly, beginAssemblySetting);
                }
            }
            catch (Exception ex)
            {
                Fail("Unexpected exception: " + ex);
            }
            finally
            {
                if (app != null)
                {
                    Step("Closing SolidWorks");
                    try
                    {
                        app.CloseAllDocuments(true);
                        app.ExitApp();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  ExitApp: " + ex.Message);
                    }
                    Marshal.FinalReleaseComObject(app);
                    app = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // An automated instance often hangs on exit. Only this test's
                    // instance can be running here (checked at startup).
                    foreach (Process p in Process.GetProcessesByName("SLDWORKS"))
                    {
                        if (!p.WaitForExit(30000))
                        {
                            Console.WriteLine($"  SolidWorks (pid {p.Id}) did not exit; killing it");
                            p.Kill();
                            p.WaitForExit(10000);
                        }
                    }
                }
                if (keep)
                {
                    Console.WriteLine($"  Models kept in {dir}");
                }
                else
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"  Could not delete {dir}");
                    }
                }
            }

            watchdog.Dispose();
            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? $"PASS ({clock.Elapsed.TotalSeconds:F0} s)"
                : $"FAIL: {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }

        // ---- Fix All / Float All (issue #3) ----

        private static void FixComponentsTest(ISldWorks app, string dir, bool keep)
        {
            Step("Building 3 box parts");
            string p1 = CreateBoxPart(app, dir, "BoxA", 0.040);
            string p2 = CreateBoxPart(app, dir, "BoxB", 0.030);
            string p3 = CreateBoxPart(app, dir, "BoxC", 0.020);

            Step("Building subassembly (one child fixed, one floating)");
            IModelDoc2 sub = NewAssembly(app);
            AddComponent(sub, p1, 0.0);
            AddComponent(sub, p2, 0.1);
            ComponentFixer.Apply(sub, false);
            SetFixed(sub, TopLevel(sub)[0], true);
            string subPath = Save(sub, Path.Combine(dir, "Sub.SLDASM"));

            Step("Building top assembly (3 parts + subassembly)");
            IModelDoc2 top = NewAssembly(app);
            AddComponent(top, p1, 0.0);
            AddComponent(top, p2, 0.1);
            AddComponent(top, p3, 0.2);
            AddComponent(top, subPath, 0.3);

            // Known start: all floating except one, so Fix All sees mixed input.
            ComponentFixer.Apply(top, false);
            List<IComponent2> comps = TopLevel(top);
            Check(comps.Count == 4, $"top assembly has 4 top-level components (got {comps.Count})");
            Check(comps.All(c => !c.IsFixed()), "setup: all top-level components floating");
            SetFixed(top, comps[0], true);
            Check(comps[0].IsFixed(), "setup: first component fixed by hand");

            // Ground truth for "subassembly untouched" is the subassembly document
            // itself. Children read through the top assembly report fixed
            // whenever the subassembly is rigid, so they prove nothing.
            bool[] subOwnStates = SubStates(sub);
            Check(subOwnStates.SequenceEqual(new[] { true, false }),
                $"setup: subassembly own states [True, False] (got {Show(subOwnStates)})");

            if (keep)
            {
                // Saved in the mixed start state for the manual undo check.
                Save(top, Path.Combine(dir, "Top.SLDASM"));
            }

            Step("Fix All");
            ComponentFixer.Result r = ComponentFixer.Apply(top, true);
            Report(true, r);
            Check(r.TopLevel == 4 && r.Changed == 3 && r.Unchanged == 1 && r.Failed.Count == 0,
                $"counts: 4 top-level, 3 changed, 1 unchanged, 0 failed " +
                $"(got {r.TopLevel}, {r.Changed}, {r.Unchanged}, {r.Failed.Count})");
            Check(TopLevel(top).All(c => c.IsFixed()), "all top-level components fixed");
            Check(SubStates(sub).SequenceEqual(subOwnStates) && !sub.GetSaveFlag(),
                "subassembly file untouched and not modified");

            Step("Fix All again (nothing to do)");
            r = ComponentFixer.Apply(top, true);
            Report(true, r);
            Check(r.Changed == 0 && r.Unchanged == 4, $"0 changed, 4 unchanged (got {r.Changed}, {r.Unchanged})");

            Step("Float All");
            r = ComponentFixer.Apply(top, false);
            Report(false, r);
            Check(r.Changed == 4 && r.Failed.Count == 0, $"4 changed, 0 failed (got {r.Changed}, {r.Failed.Count})");
            Check(TopLevel(top).All(c => !c.IsFixed()), "all top-level components floating");
            Check(SubStates(sub).SequenceEqual(subOwnStates) && !sub.GetSaveFlag(),
                "subassembly file untouched and not modified");

        }

        private static bool[] SubStates(IModelDoc2 sub) => TopLevel(sub).Select(c => c.IsFixed()).ToArray();

        private static string Show(bool[] states) => "[" + string.Join(", ", states) + "]";

        // ---- Model building helpers ----

        private static string CreateBoxPart(ISldWorks app, string dir, string name, double width)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            var doc = (IModelDoc2)app.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"NewDocument failed for part template '{template}'");

            // First reference plane, found by type so template language does not matter.
            IFeature plane = Features(doc).FirstOrDefault(f => f.GetTypeName2() == "RefPlane")
                ?? throw new InvalidOperationException("No reference plane in part template");
            plane.Select2(false, 0);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(0, 0, 0, width, 0.020, 0);
            doc.SketchManager.InsertSketch(true);

            var sketch = (IFeature)doc.FeatureByPositionReverse(0);
            sketch.Select2(false, 0);
            IFeature extrude = doc.FeatureManager.FeatureExtrusion2(
                true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.010, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            if (extrude == null)
            {
                throw new InvalidOperationException($"Extrusion failed for {name}");
            }
            return Save(doc, Path.Combine(dir, name + ".SLDPRT"));
        }

        private static IModelDoc2 NewAssembly(ISldWorks app)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            return (IModelDoc2)app.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"NewDocument failed for assembly template '{template}'");
        }

        private static IComponent2 AddComponent(IModelDoc2 assembly, string path, double x)
        {
            // AddComponent5 needs the file open in this session; the helpers keep it open.
            var component = ((IAssemblyDoc)assembly).AddComponent5(
                path, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "", false, "", x, 0, 0);
            return component ?? throw new InvalidOperationException($"AddComponent5 failed for {path}");
        }

        private static string Save(IModelDoc2 doc, string path)
        {
            int errors = 0, warnings = 0;
            bool ok = doc.Extension.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref errors, ref warnings);
            if (!ok)
            {
                throw new InvalidOperationException($"SaveAs3 failed for {path} (errors {errors}, warnings {warnings})");
            }
            return path;
        }

        private static void SetFixed(IModelDoc2 assembly, IComponent2 component, bool fix)
        {
            assembly.ClearSelection2(true);
            component.Select4(false, null, false);
            if (fix)
            {
                ((IAssemblyDoc)assembly).FixComponent();
            }
            else
            {
                ((IAssemblyDoc)assembly).UnfixComponent();
            }
            assembly.ClearSelection2(true);
        }

        private static List<IComponent2> TopLevel(IModelDoc2 assembly) =>
            (((IAssemblyDoc)assembly).GetComponents(true) as object[] ?? new object[0]).Cast<IComponent2>().ToList();

        private static IEnumerable<IFeature> Features(IModelDoc2 doc)
        {
            for (var f = (IFeature)doc.FirstFeature(); f != null; f = (IFeature)f.GetNextFeature())
            {
                yield return f;
            }
        }

        // ---- Output ----

        private static void Step(string title) => Console.WriteLine($"\n== {title}");

        private static void Report(bool fix, ComponentFixer.Result r) =>
            Console.WriteLine("  message: " + FixStateReport.Format(fix, r.Changed, r.Unchanged, r.Failed).Replace("\n", "\n           "));

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
            if (!ok)
            {
                failures++;
            }
        }

        private static void Fail(string message)
        {
            Console.WriteLine("  [FAIL] " + message);
            failures++;
        }
    }
}
