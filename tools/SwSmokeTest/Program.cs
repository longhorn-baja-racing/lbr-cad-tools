using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Features.AddInConfig;
using Lbr.CadTools.AddIn.Features.AutoSave;
using Lbr.CadTools.AddIn.Features.BatchExport;
using Lbr.CadTools.AddIn.Features.Drawings;
using Lbr.CadTools.AddIn.Features.FixComponents;
using Lbr.CadTools.AddIn.Features.Naming;
using Lbr.CadTools.AddIn.Features.Notcher;
using Lbr.CadTools.AddIn.Features.SketchPoints;
using Lbr.CadTools.AddIn.Features.Suspension;
using Lbr.CadTools.AddIn.Features.Tabs;
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
        private const int WatchdogMinutes = 8;

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
            // --only=<name>[,<name>] runs just those tests (names as in the Run calls below).
            string only = args.FirstOrDefault(a => a.StartsWith("--only="))?.Substring("--only=".Length);
            bool Want(string name) => only == null || only.Split(',').Contains(name, StringComparer.OrdinalIgnoreCase);
            // --inject=<name>:once|always adds a failure to a test group, to check the retry logic.
            string[] inject = args.FirstOrDefault(a => a.StartsWith("--inject="))?.Substring("--inject=".Length).Split(':');

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
            const int beginAssembly = (int)swUserPreferenceToggle_e.swInsertComponentForNewAssembly;
            bool? beginAssemblySetting = null;
            var flaky = new List<string>();

            ISldWorks Start()
            {
                var started = Stopwatch.StartNew();
                var sw = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                sw.Visible = visible;
                Console.WriteLine($"  SolidWorks {sw.RevisionNumber()}, started in {started.Elapsed.TotalSeconds:F0} s");
                // A visible new assembly opens the Insert Component panel. Turn
                // that off for the run, then restore the user's setting.
                if (beginAssemblySetting == null)
                {
                    beginAssemblySetting = sw.GetUserPreferenceToggle(beginAssembly);
                }
                sw.SetUserPreferenceToggle(beginAssembly, false);
                return sw;
            }

            // Only this test's SolidWorks can be running (checked at startup), so it is safe to end it.
            void Stop(int waitSeconds)
            {
                if (app == null)
                {
                    return;
                }
                try
                {
                    if (beginAssemblySetting != null)
                    {
                        app.SetUserPreferenceToggle(beginAssembly, beginAssemblySetting.Value);
                    }
                    app.CloseAllDocuments(true);
                    app.ExitApp();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ExitApp: " + ex.Message);
                }
                try
                {
                    Marshal.FinalReleaseComObject(app);
                }
                catch (Exception)
                {
                }
                app = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // An automated instance often hangs on exit.
                foreach (Process p in Process.GetProcessesByName("SLDWORKS"))
                {
                    if (!p.WaitForExit(waitSeconds * 1000))
                    {
                        Console.WriteLine($"  SolidWorks (pid {p.Id}) did not exit; killing it");
                        p.Kill();
                        p.WaitForExit(10000);
                    }
                }
            }

            void Attempt(string name, bool retry, Action test)
            {
                try
                {
                    test();
                    if (inject != null && inject[0] == name && (!retry || (inject.Length > 1 && inject[1] == "always")))
                    {
                        Fail("injected failure (--inject)");
                    }
                }
                catch (Exception ex)
                {
                    Fail("Unexpected exception: " + ex);
                }
                try
                {
                    CloseAll(app);
                }
                catch (Exception ex)
                {
                    Fail("Closing documents: " + ex.Message);
                }
            }

            // A long session sometimes faults (RPC_E_SERVERFAULT, issue #37). A test group
            // that fails is retried once in a fresh SolidWorks: a real bug fails again,
            // a flake passes and is listed at the end.
            void Run(string name, Action test)
            {
                if (!Want(name))
                {
                    return;
                }
                watchdog.Change(TimeSpan.FromMinutes(WatchdogMinutes), System.Threading.Timeout.InfiniteTimeSpan);
                int before = failures;
                Attempt(name, false, test);
                if (failures == before)
                {
                    return;
                }
                int firstTry = failures - before;
                Step($"{name}: {firstTry} failure(s); retrying once in a fresh SolidWorks");
                Stop(waitSeconds: 5);
                watchdog.Change(TimeSpan.FromMinutes(WatchdogMinutes), System.Threading.Timeout.InfiniteTimeSpan);
                app = Start();
                failures = before;
                Attempt(name, true, test);
                if (failures == before)
                {
                    flaky.Add(name);
                    Console.WriteLine($"  {name} passed on retry");
                }
            }

            try
            {
                Step(visible ? "Starting SolidWorks (visible; do not click in it)" : "Starting SolidWorks (hidden)");
                app = Start();
                Run("fix", () => FixComponentsTest(app, dir, keep));
                Run("points", () => SketchPointsTest(app, dir, keep));
                Run("export", () => BatchExportTest(app, dir));
                Run("autosave", () =>
                {
                    AutoSaveTest(app, dir);
                    AddInConfigTest(app);
                });
                Run("tab", () => TabTest(app));
                Run("tabinsert", () => TabInsertTest(app, dir));
                Run("suspension", () => SuspensionTest(app, dir));
                Run("sweep", () => SweepTest(app, dir));
                Run("naming", () => NameCheckTest(app, dir));
                Run("notcher", () => NotcherTest(app, dir));
                Run("drawings", () => DrawingsTest(app, dir));
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
                    Stop(waitSeconds: 30);
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
                ? $"PASS ({clock.Elapsed.TotalSeconds:F0} s)" + (flaky.Count > 0 ? $"; passed on retry in a fresh SolidWorks: {string.Join(", ", flaky)} (#37)" : "")
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

            Step("Restore Fix States (back to the mixed start)");
            var restore = ComponentFixer.Restore(top);
            Console.WriteLine("  message: " + (restore == null ? "(null)"
                : FixStateReport.FormatRestore(restore.Value.LastWasFix, restore.Value.Restored, restore.Value.Skipped)));
            Check(restore != null && restore.Value.LastWasFix && restore.Value.Restored == 3 && restore.Value.Skipped == 0,
                "3 restored, 0 skipped");
            bool[] states = TopLevel(top).Select(c => c.IsFixed()).ToArray();
            Check(states.SequenceEqual(new[] { true, false, false, false }), $"start states back (got {Show(states)})");
            Check(ComponentFixer.Restore(top) == null, "second Restore has nothing to do");

            ComponentFixer.Apply(top, true);
            Check(TopLevel(top).All(c => c.IsFixed()), "Fix All again after restore");

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

            Step("Restore after one component was changed by hand");
            SetFixed(top, TopLevel(top)[1], true);
            restore = ComponentFixer.Restore(top);
            Check(restore != null && !restore.Value.LastWasFix && restore.Value.Restored == 3 && restore.Value.Skipped == 1,
                $"3 restored, 1 skipped (got {restore?.Restored}, {restore?.Skipped})");
            Check(TopLevel(top).All(c => c.IsFixed()), "all fixed again");

        }

        // ---- CSV point import / export (issue #4) ----

        private static void SketchPointsTest(ISldWorks app, string dir, bool keep)
        {
            const double tol = 1e-9;
            IModelDoc2 part = NewPart(app);

            Step("Import into a new 3D sketch (through a CSV file)");
            var cloud = new List<Point3>
            {
                new Point3(0.010, 0.020, 0.030),
                new Point3(-0.005, 0, 0.100),
                new Point3(0.0254, -0.0127, 0),
            };
            string csvPath = Path.Combine(dir, "points.csv");
            File.WriteAllText(csvPath, PointCsv.Write(cloud, LengthUnit.Millimeter));
            string csvText = File.ReadAllText(csvPath);
            Check(PointCsv.DetectUnit(csvText) == LengthUnit.Millimeter, "unit detected from written header");
            PointCsvReadResult csv = PointCsv.Read(csvText, LengthUnit.Millimeter);
            Check(csv.Errors.Count == 0 && csv.Points.Count == 3, $"CSV read back: 3 points, 0 errors (got {csv.Points.Count}, {csv.Errors.Count})");

            SketchPointIo.ImportResult r = SketchPointIo.Import(app, part, csv.Points);
            Console.WriteLine($"  sketch {r.SketchName}, new {r.NewSketch}, added {r.Added}, off-plane {r.OffPlane}");
            Check(r.NewSketch && r.Added == 3 && r.OffPlane == 0, "new sketch, 3 added, 0 off-plane");
            Check(part.SketchManager.ActiveSketch == null, "new 3D sketch closed again");

            var sketch3d = (ISketch)((IFeature)part.FeatureByPositionReverse(0)).GetSpecificFeature2();
            List<Point3> exported = SketchPointIo.Export(app, sketch3d);
            Check(SamePoints(exported, cloud, tol), $"3D export matches import (got {Show(exported)})");

            Step("Export finds a sketch selected in the tree");
            ((IFeature)sketch3d).Select2(false, 0);
            ISketch found = SketchPointIo.FindSketch(part);
            Check(found != null && ((IFeature)found).Name == r.SketchName, "selected sketch found");
            part.ClearSelection2(true);
            Check(SketchPointIo.FindSketch(part) == null, "nothing found with no selection and no open sketch");

            Step("Import into an open 2D sketch on the Top plane");
            // Template order is Front, Top, Right; found by type so language does not matter.
            IFeature top = Planes(part).ElementAt(1);
            top.Select2(false, 0);
            part.SketchManager.InsertSketch(true);
            ISketch sketch2d = part.SketchManager.ActiveSketch;
            Check(sketch2d != null && !sketch2d.Is3D(), "2D sketch open");

            var onPlane = new List<Point3>
            {
                new Point3(0.010, 0, 0.020),
                new Point3(0.030, 0, -0.010),
                new Point3(0.015, 0.005, 0.000), // 5 mm above the Top plane
            };
            r = SketchPointIo.Import(app, part, onPlane);
            Console.WriteLine($"  sketch {r.SketchName}, new {r.NewSketch}, added {r.Added}, off-plane {r.OffPlane}");
            Check(!r.NewSketch && r.Added == 3 && r.OffPlane == 1, "existing sketch, 3 added, 1 off-plane");

            // Sketch space on Top is (X, -Z). Model (10, 0, 20) mm must be stored
            // as |y| = 20 mm; a model-space store would give y = 0.
            List<ISketchPoint> raw = (sketch2d.GetSketchPoints2() as object[] ?? new object[0]).Cast<ISketchPoint>().ToList();
            Console.WriteLine("  raw sketch points: " + string.Join(" ", raw.Select(p => $"({p.X:0.####}, {p.Y:0.####}, {p.Z:0.####})")));
            Check(raw.Any(p => Math.Abs(p.X - 0.010) < tol && Math.Abs(Math.Abs(p.Y) - 0.020) < tol),
                "points stored in sketch space");

            exported = SketchPointIo.Export(app, sketch2d);
            var expected = new List<Point3> { onPlane[0], onPlane[1], new Point3(0.015, 0, 0) };
            Check(SamePoints(exported, expected, tol), $"2D export in model space, off-plane point projected (got {Show(exported)})");

            part.SketchManager.CreateLine(0, 0, 0, 0.05, 0.05, 0);
            int withLine = SketchPointIo.Export(app, sketch2d).Count;
            Check(withLine == 5, $"line end points are exported too: 5 points (got {withLine})");
            part.SketchManager.InsertSketch(true);

            if (keep)
            {
                Save(part, Path.Combine(dir, "Points.SLDPRT"));
                Console.WriteLine($"  Sample CSV for manual import: {csvPath}");
            }
        }

        // ---- Batch Export (issue #5) ----

        private static void BatchExportTest(ISldWorks app, string dir)
        {
            string sub = Path.Combine(dir, "export");
            Directory.CreateDirectory(sub);

            Step("Building parts, a sheet metal plate, an assembly, and a drawing");
            string boxA = CreateBoxPart(app, sub, "ExBoxA", 0.040);
            string boxB = CreateBoxPart(app, sub, "ExBoxB", 0.030);
            var boxBDoc = (IModelDoc2)app.GetOpenDocumentByName(boxB);
            boxBDoc.Extension.get_CustomPropertyManager("").Add3("PartNo",
                (int)swCustomInfoType_e.swCustomInfoText, "LBR-1001",
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            Save(boxBDoc, boxB);
            string plate = CreateSheetMetalPart(app, sub, "ExPlate");
            Check(plate != null, "sheet metal base flange created");

            IModelDoc2 top = NewAssembly(app);
            AddComponent(top, boxA, 0.0);
            AddComponent(top, boxA, 0.1);
            AddComponent(top, boxB, 0.2);
            if (plate != null)
            {
                AddComponent(top, plate, 0.3);
            }
            string topPath = Save(top, Path.Combine(sub, "ExTop.SLDASM"));

            string drawingPath = CreateDrawing(app, boxA, Path.Combine(sub, "ExBoxA.SLDDRW"));
            Check(drawingPath != null, "drawing created and closed");

            int errors = 0;
            app.ActivateDoc3(topPath, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            string topConfig = top.ConfigurationManager.ActiveConfiguration.Name;

            const int stepApPref = (int)swUserPreferenceIntegerValue_e.swStepAP;
            app.SetUserPreferenceIntegerValue(stepApPref, 203);

            Step("Batch Export: assembly + components + drawings, all formats");
            var options = new BatchExportOptions
            {
                IncludeComponents = true,
                IncludeDrawings = true,
                OutputFolder = Path.Combine(sub, "Exports"),
            };
            options.Formats.UnionWith(ExportFormats.All);
            var clock = Stopwatch.StartNew();
            BatchExportResult r = BatchExporter.Run(app, top, options);
            Console.WriteLine($"  {clock.Elapsed.TotalSeconds:F0} s; message: " +
                BatchExportReport.Format(options.OutputFolder, r.Written.Count, r.Failed, r.Notes).Replace("\n", "\n           "));
            Console.WriteLine("  written: " + string.Join(" ", r.Written.Select(Path.GetFileName)));

            Check(r.Failed.Count == 0, $"no failures (got {r.Failed.Count})");
            var expected = new List<string>();
            foreach (string name in new[] { "ExTop", "ExBoxA", "LBR-1001" }.Concat(plate != null ? new[] { "ExPlate" } : new string[0]))
            {
                expected.AddRange(new[] { ".step", ".igs", ".x_t", ".stl" }.Select(ext => name + ext));
            }
            if (plate != null)
            {
                expected.Add("ExPlate-flat.dxf");
            }
            expected.Add("ExBoxA.pdf");
            foreach (string file in expected)
            {
                string full = Path.Combine(options.OutputFolder, file);
                Check(File.Exists(full) && new FileInfo(full).Length > 0, $"{file} written");
            }
            Check(Directory.GetFiles(options.OutputFolder, "ExBoxA*.dxf").Length >= 1, "drawing DXF written");
            Check(!File.Exists(Path.Combine(options.OutputFolder, "ExBoxB.step")), "ExBoxB named by its PartNo, not file name");
            Check(!File.Exists(Path.Combine(options.OutputFolder, "ExBoxB-flat.dxf")), "no flat pattern for a non-sheet-metal part");

            string stepHeader = File.ReadAllText(Path.Combine(options.OutputFolder, "ExBoxA.step"));
            Check(stepHeader.IndexOf("AUTOMOTIVE_DESIGN", StringComparison.OrdinalIgnoreCase) >= 0, "STEP is AP214 (AUTOMOTIVE_DESIGN schema)");
            Check(app.GetUserPreferenceIntegerValue(stepApPref) == 203, "user's STEP AP preference restored");

            var active = app.ActiveDoc as IModelDoc2;
            Check(active != null && string.Equals(active.GetPathName(), topPath, StringComparison.OrdinalIgnoreCase),
                $"assembly active again (got {active?.GetTitle()})");
            Check(top.ConfigurationManager.ActiveConfiguration.Name == topConfig, "assembly configuration unchanged");
            Check(app.GetOpenDocumentByName(drawingPath) == null, "drawing opened by the export was closed");

            Step("Batch Export again (replaces files)");
            r = BatchExporter.Run(app, top, options);
            Check(r.Failed.Count == 0 && r.Written.Count >= expected.Count,
                $"no failures, {r.Written.Count} written" + (r.Failed.Count > 0 ? ": " + string.Join("; ", r.Failed) : ""));
        }

        // ---- Auto-Saver (issue #8) ----

        private static void AutoSaveTest(ISldWorks app, string dir)
        {
            Step("Auto-save: idle detection");
            app.GetRunningCommandInfo(out int commandId, out string title, out bool uiActive);
            Console.WriteLine($"  GetRunningCommandInfo idle: id {commandId}, title '{title}', uiActive {uiActive}");
            Check(!AutoSaver.IsBusy(app, out string busy), $"not busy when idle (got busy with {busy})");

            Step("Auto-save: saves modified saved documents only");
            string sub = Path.Combine(dir, "autosave");
            Directory.CreateDirectory(sub);
            string savedPath = CreateBoxPart(app, sub, "AutoA", 0.020);
            var saved = (IModelDoc2)app.GetOpenDocumentByName(savedPath);
            Console.WriteLine($"  Visible: saved part {saved.Visible}");
            DateTime before = File.GetLastWriteTimeUtc(savedPath);
            AddSketchLine(saved);
            Check(saved.GetSaveFlag(), "saved part is modified");

            IModelDoc2 fresh = NewPart(app);
            AddSketchLine(fresh);
            Console.WriteLine($"  Visible: new part {fresh.Visible}; modified {fresh.GetSaveFlag()}");

            System.Threading.Thread.Sleep(1100); // file times have coarse resolution
            AutoSaver.Result r = AutoSaver.SaveNow(app);
            Console.WriteLine("  status: " + AutoSavePolicy.Status(DateTime.Now, r.Saved, r.Failed, r.NeverSaved));
            Check(r.Saved == 1 && r.Failed.Count == 0, $"1 saved, 0 failed (got {r.Saved}, {r.Failed.Count})");
            Check(r.NeverSaved == 1, $"1 never-saved document reported (got {r.NeverSaved})");
            Check(!saved.GetSaveFlag() && File.GetLastWriteTimeUtc(savedPath) > before, "saved part written to disk");

            r = AutoSaver.SaveNow(app);
            Check(r.Saved == 0, $"second run saves nothing (got {r.Saved})");

            Step("Auto-save: busy detection");
            int err = 0;
            app.ActivateDoc3(savedPath, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
            IFeature plane = Planes(saved).First();
            plane.Select2(false, 0);
            saved.SketchManager.InsertSketch(true);
            Check(AutoSaver.IsBusy(app, out busy), $"busy while editing a sketch ({busy})");
            saved.SketchManager.InsertSketch(true);
            saved.ClearSelection2(true);
            bool ran = app.RunCommand(9 /* swCommands_Fillet */, "");
            Check(ran && AutoSaver.IsBusy(app, out busy), $"busy while the Fillet PropertyManager is open ({busy})");
            app.RunCommand(-1 /* swCommands_PmCancel */, "");
            Check(!AutoSaver.IsBusy(app, out busy), $"idle again after cancel (got {busy})");
        }

        // ---- Suspension check (issue #12) ----

        private static void SuspensionTest(ISldWorks app, string dir)
        {
            const double camber = -1.5, toe = 0.3, caster = 7, kpi = 9;
            const double radius = 11.5 * Units.MetersPerInch; // default tire radius setting
            Step("Suspension: hardpoints part with named reference points");

            // Vehicle coordinates (X forward, Y left, Z up) -> SolidWorks default model axes (X left, Y up, Z forward).
            Vec3 Model(Vec3 v) => new Vec3(v.Y, v.Z, v.X);
            var points = new Dictionary<string, Vec3>();
            foreach (Corner corner in new[] { Corner.FL, Corner.FR })
            {
                int side = corner == Corner.FL ? 1 : -1;
                double c = camber * Math.PI / 180, t = toe * Math.PI / 180;
                var spin = new Vec3(Math.Sin(t) * Math.Cos(c), side * Math.Cos(t) * Math.Cos(c), -Math.Sin(c));
                var wc = new Vec3(1.5, side * 0.65, radius);
                var steer = new Vec3(-Math.Tan(caster * Math.PI / 180), -side * Math.Tan(kpi * Math.PI / 180), 1).Unit();
                var lbj = new Vec3(1.5, side * 0.58, 0.15);
                points[Hardpoints.Name(Hardpoints.Wc, corner)] = wc;
                points[Hardpoints.Name(Hardpoints.Sa, corner)] = wc - spin * 0.08;
                points[Hardpoints.Name(Hardpoints.Lbj, corner)] = lbj;
                points[Hardpoints.Name(Hardpoints.Ubj, corner)] = lbj + steer * 0.22;
            }
            points[Hardpoints.Name(Hardpoints.Wc, Corner.RL)] = new Vec3(0, 0.6, radius);

            IModelDoc2 part = NewPart(app);
            part.SketchManager.Insert3DSketch(true);
            part.SketchManager.AddToDB = true;
            foreach (Vec3 v in points.Values)
            {
                Vec3 m = Model(v);
                part.SketchManager.CreatePoint(m.X, m.Y, m.Z);
            }
            part.SketchManager.AddToDB = false;
            part.SketchManager.Insert3DSketch(true);
            var sketch = (ISketch)((IFeature)part.FeatureByPositionReverse(0)).GetSpecificFeature2();

            int named = 0;
            foreach (ISketchPoint sp in ((sketch.GetSketchPoints2() as object[]) ?? new object[0]).Cast<ISketchPoint>())
            {
                var at = new Vec3(sp.X, sp.Y, sp.Z);
                string name = points.First(p => (Model(p.Value) - at).Length < 1e-9).Key;
                part.ClearSelection2(true);
                sp.Select4(false, null);
                object created = part.FeatureManager.InsertReferencePoint(
                    (int)swRefPointType_e.swRefPointSketchPoint, 0, 0, 1);
                // Renaming through the returned object faults out of process; use a fresh handle.
                if (created != null && part.FeatureByPositionReverse(0) is IFeature f && f.GetTypeName2() == "RefPoint")
                {
                    f.Name = name;
                    named++;
                }
            }
            part.ClearSelection2(true);
            Check(named == points.Count, $"{points.Count} reference points created and named (got {named})");
            string sub = Path.Combine(dir, "suspension");
            Directory.CreateDirectory(sub);
            string partPath = Save(part, Path.Combine(sub, "Hardpoints.SLDPRT"));

            Step("Suspension: read through an assembly (component moved off origin)");
            IModelDoc2 asm = NewAssembly(app);
            IComponent2 component = AddComponent(asm, partPath, 0.25);
            // AddComponent5 places the part by its bounding box center, so read the real offset.
            var xform = (double[])component.Transform2.ArrayData;
            var offset = new Vec3(xform[9], xform[10], xform[11]);
            Console.WriteLine($"  component offset {offset}");
            HardpointReader.Result read = HardpointReader.Read(app, asm);
            Check(read.Points.Count == points.Count && read.Problems.Count == 0,
                $"{points.Count} points read, no problems (got {read.Points.Count}, {string.Join("; ", read.Problems)})");
            Vec3 ubj = read.Points[Hardpoints.Name(Hardpoints.Ubj, Corner.FL)];
            Vec3 expectedUbj = Model(points[Hardpoints.Name(Hardpoints.Ubj, Corner.FL)]) + offset;
            Check((ubj - expectedUbj).Length < 1e-9, $"point moved by the component transform (got {ubj}, expected {expectedUbj})");

            var problems = new List<string>();
            var settings = new SuspensionSettings();
            // The offset moves every point (sideways too, which decides left and right),
            // so take it back out: the hardpoints are meant to be in car coordinates.
            foreach (string key in read.Points.Keys.ToList())
            {
                read.Points[key] = read.Points[key] - offset;
            }
            (List<CornerGeometry> corners, SteeringGeometry steering) = HardpointReader.Compute(read, settings, problems);
            Console.WriteLine("  " + SuspensionMath.Report(corners, steering).Replace("\n", "\n  "));
            Check(problems.Count == 0, $"no problems (got {string.Join("; ", problems)})");
            Check(corners.Count == 2, $"2 front corners (got {corners.Count})");
            Check(corners.All(g => Math.Abs(g.Camber - camber) < 1e-6 && Math.Abs(g.Toe - toe) < 1e-6
                                   && Math.Abs(g.Caster - caster) < 1e-6 && Math.Abs(g.Kpi - kpi) < 1e-6),
                "camber, toe, caster, and KPI match what the points were built with");
            Check(steering != null && steering.Straight && Math.Abs(steering.Wheelbase - 1.5) < 1e-3,
                $"straight ahead, wheelbase 1.5 m (got {steering?.Wheelbase:0.####})");
        }

        // ---- Mounting tab generator (issue #7) ----

        private static void TabTest(ISldWorks app)
        {
            const double inch = Units.MetersPerInch;

            Step("Tab: lap belt, double shear, base contoured to a 1.5 in tube");
            TabInputs lap = TabInputs.Defaults(TabUse.LapBelt);
            lap.TubeDiameter = 1.5 * inch; // also the gap between the tabs
            lap.ContourTubeDiameter = lap.TubeDiameter;
            CheckTab(app, lap, bodies: 2);

            Step("Tab: seat, single shear, straight base");
            CheckTab(app, TabInputs.Defaults(TabUse.Seat), bodies: 1);
        }

        private static void CheckTab(ISldWorks app, TabInputs t, int bodies)
        {
            IModelDoc2 doc = TabBuilder.Build(app, t);
            object[] solids = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
            Check(solids.Length == bodies, $"{bodies} solid bodies (got {solids.Length})");

            double volume = doc.Extension.CreateMassProperty().Volume;
            double expected = bodies * TabGeometry.Area(t) * t.Thickness;
            Console.WriteLine($"  volume {volume * 1e9:F1} mm3, expected {expected * 1e9:F1} mm3");
            Check(Math.Abs(volume - expected) / expected < 1e-4, "volume matches the computed outline");

            var box = (double[])((IPartDoc)doc).GetPartBox(true);
            double width = box[3] - box[0], height = box[4] - box[1], depth = box[5] - box[2];
            double expectDepth = bodies == 2 ? 2 * t.Thickness + t.TabGap : t.Thickness;
            Console.WriteLine($"  box {width * 1000:F2} x {height * 1000:F2} x {depth * 1000:F2} mm");
            Check(Math.Abs(width - t.BaseWidth) < 1e-6, "width is B");
            Check(Math.Abs(height - (t.Length + t.EndDiameter / 2)) < 1e-6, "height is L + D2/2");
            Check(Math.Abs(depth - expectDepth) < 1e-6, bodies == 2 ? "depth is 2t + gap (tube OD)" : "depth is t");

            doc.Extension.get_CustomPropertyManager("").Get6("Description", false, out _, out string description, out _, out _);
            Check(description == TabRules.Describe(t), $"Description property set ({description})");
        }

        // ---- Name Check (issue #6) ----

        private static void NameCheckTest(ISldWorks app, string dir)
        {
            string folder = Path.Combine(dir, "Naming");
            Directory.CreateDirectory(Path.Combine(folder, "Old"));

            Step("Name Check: assembly with good, wrong-REV, and badly named files");
            string hoop = CreateBoxPart(app, folder, "CHA_0002_FrontHoop_REV01_V01", 0.02);
            string rear = CreateBoxPart(app, folder, "CHA_0003_RearHoop_REV02_V01", 0.02);
            string bad = CreateBoxPart(app, folder, "Part7", 0.02);
            string pan = CreateBoxPart(app, folder, "ERG_0005_SeatPan_REV01_V01", 0.02);

            IModelDoc2 sub = NewAssembly(app);
            AddComponent(sub, pan, 0);
            string subPath = Save(sub, Path.Combine(folder, "aERG_0004_Seat_REV01_V01.SLDASM"));

            // Names only matter for these: a drawing of the hoop (same REV), an old-REV drawing, and an old part.
            File.WriteAllText(Path.Combine(folder, "dCHA_0002_FrontHoop_REV01_V01.SLDDRW"), "");
            File.WriteAllText(Path.Combine(folder, "dCHA_0002_FrontHoop_REV00_V03.SLDDRW"), "");
            File.WriteAllText(Path.Combine(folder, "Old", "CHA_0040_OldFrame_REV00_V09.SLDPRT"), "");

            IModelDoc2 top = NewAssembly(app);
            AddComponent(top, hoop, 0);
            AddComponent(top, rear, 0.05);
            AddComponent(top, bad, 0.10);
            AddComponent(top, subPath, 0.15);
            Save(top, Path.Combine(folder, "aCHA_0001_Frame_REV01_V01.SLDASM"));

            NameScanner.Result scan = NameScanner.Scan(top);
            Console.WriteLine($"  files: {string.Join(", ", scan.Files.Select(f => f.BaseName))}");
            Check(scan.Top.BaseName == "aCHA_0001_Frame_REV01_V01" && scan.Top.Kind == CadFileKind.Assembly, "top assembly read");
            Check(scan.Files.Count == 6, $"4 parts, 1 subassembly, 1 drawing (got {scan.Files.Count})");
            Check(scan.Files.Any(f => f.BaseName == "ERG_0005_SeatPan_REV01_V01"), "part inside the subassembly found");
            Check(scan.Files.Any(f => f.BaseName == "dCHA_0002_FrontHoop_REV01_V01" && f.Kind == CadFileKind.Drawing), "same-REV drawing joins the check");
            Check(!scan.Files.Any(f => f.BaseName.Contains("REV00")), "old-REV drawing left out");

            List<NameCheck> checks = NamingConvention.CheckAssembly(scan.Top, scan.Files, NamingConvention.DefaultTeams.Keys.ToList());
            foreach (NameCheck c in checks.Where(c => !c.Ok))
            {
                Console.WriteLine($"  fix {c.File.BaseName}: {string.Join(" ", c.Problems)}");
            }
            Check(checks.Single(c => c.File.BaseName.StartsWith("CHA_0003")).Problems.Any(p => p.Contains("REV02 does not match")), "wrong REV flagged");
            Check(!checks.Single(c => c.File.BaseName == "Part7").Ok, "Part7 flagged");
            Check(checks.Count(c => !c.Ok) == 2, "only those two need fixing");
            Check(NamingConvention.NextNumber("CHA", scan.AllNames) == 41, "next CHA number counts the Old folder (0041)");
            Check(NamingConvention.NextNumber("ERG", scan.AllNames) == 6, "next ERG number is 0006");
        }

        // ---- Mounting tab inserted on a tube (issue #35) ----

        private static void TabInsertTest(ISldWorks app, string dir)
        {
            const double inch = Units.MetersPerInch;
            const double r = 0.5 * inch;

            // Round tube: built along part Z, then turned so its axis runs along assembly X,
            // centered on (0, y0, z0). The offset and the rotation check the transforms.
            const double y0 = 0.10, z0 = -0.05, length = 0.30;
            string tubePath = CreateTubePart(app, dir, "Tube", r, length);
            IModelDoc2 asm = NewAssembly(app);
            IComponent2 tube = AddComponent(asm, tubePath, 0);
            var mu = (IMathUtility)app.GetMathUtility();
            // Part Z -> assembly X, part X -> assembly Y, part Y -> assembly Z.
            tube.Transform2 = (MathTransform)mu.CreateTransform(new double[]
            {
                0, 1, 0,  0, 0, 1,  1, 0, 0,
                -length / 2, y0, z0,
                1, 0, 0, 0,
            });
            Save(asm, Path.Combine(dir, "Tab Insert.SLDASM"));

            Step("Tab insert: round tube, single seat tab along the tube");
            const double pickX = 0.04;
            TabMount m = PickFace(app, asm, pickX, y0 + r, z0);
            Check(m != null && m.IsRound, "picked face read as a round tube");
            if (m == null)
            {
                return;
            }
            Check(Math.Abs(m.Radius - r) < 1e-6, $"radius {m.Radius / inch:0.####} in");
            Check(Math.Abs(Math.Abs(m.Axis.X) - 1) < 1e-6, $"axis along assembly X ({m.Axis.X:F3}, {m.Axis.Y:F3}, {m.Axis.Z:F3})");
            Check(Math.Abs(m.PickDistanceFromAxis() - r) < 1e-5, $"pick lies on the tube surface ({m.PickDistanceFromAxis():F5})");

            TabInputs seat = TabInputs.Defaults(TabUse.Seat);
            seat.DoubleShear = false;
            seat.TubeDiameter = 2 * r;
            IComponent2 tab = InsertTab(app, asm, seat, m, TabOrientation.AlongTube);
            // GetBox is padded, so check exact points through the component transform. The
            // recorded pick is a little off the requested point, like a real click.
            Vec3 axisFoot = new Vec3(m.Pick.X, y0, z0);
            Vec3 up = (m.Pick - axisFoot).Unit();
            Vec3 baseMid = axisFoot + up * r;
            Check(Maps(app, tab, new Vec3(0, 0, seat.Thickness / 2), baseMid), "base middle on the tube surface under the click");
            Check(Maps(app, tab, new Vec3(seat.BaseWidth / 2, 0, seat.Thickness / 2), baseMid + m.Axis * (seat.BaseWidth / 2)), "base runs along the tube axis");
            Check(Maps(app, tab, new Vec3(0, seat.Length, seat.Thickness / 2), baseMid + up * seat.Length), "hole straight out from the tube");
            Check(tab.IsFixed(), "tab is fixed");
            Check(File.Exists(Path.Combine(dir, "Tab-Seat-1.SLDPRT")), "Tab-Seat-1.SLDPRT saved next to the assembly");

            Step("Tab insert: contoured seat tab, across the tube (side facing +Z)");
            m = PickFace(app, asm, -0.08, y0, z0 + r); // side of the tube facing +Z
            if (m != null)
            {
                TabInputs fit = TabInputs.Defaults(TabUse.Seat); // B = 0.75 in fits the 1 in tube
                fit.TubeDiameter = 2 * r;
                fit.ContourTubeDiameter = 2 * r;
                Check(!TabRules.Check(fit).Any(i => i.Level == TabIssueLevel.Error), "contoured seat tab is buildable");
                tab = InsertTab(app, asm, fit, m, TabOrientation.AlongTube); // contour forces across
                double c = Math.Sqrt(r * r - fit.BaseWidth * fit.BaseWidth / 4);
                double depth = fit.DoubleShear ? 2 * fit.Thickness + fit.TabGap : fit.Thickness;
                Vec3 foot = new Vec3(m.Pick.X, y0, z0);
                Vec3 outward = (m.Pick - foot).Unit();
                Check(outward.Z > 0.99, "picked the side facing +Z");
                Check(Maps(app, tab, new Vec3(0, -c, depth / 2), foot), "base arc center on the tube axis");
                Check(Maps(app, tab, new Vec3(0, fit.Length, depth / 2), foot + outward * (c + fit.Length)), "hole points away from the tube");
                Vec3 corner = Map(app, tab, new Vec3(fit.BaseWidth / 2, 0, 0));
                double cornerRadius = Math.Sqrt((corner.Y - y0) * (corner.Y - y0) + (corner.Z - z0) * (corner.Z - z0));
                Check(Math.Abs(cornerRadius - r) < 1e-6, $"base corners on the tube surface ({cornerRadius:F5})");

                TabInputs lap = TabInputs.Defaults(TabUse.LapBelt);
                lap.TubeDiameter = 2 * r;
                lap.ContourTubeDiameter = 2 * r;
                Check(TabRules.Check(lap).Any(i => i.Level == TabIssueLevel.Error), "lap belt base wider than the tube cannot be contoured (blocked in the dialog)");
            }

            Step("Tab insert: square tube, flat face");
            string boxPath = CreateBoxPart(app, dir, "Square", 0.025);
            int errs = 0;
            app.ActivateDoc3(asm.GetTitle(), false, 0, ref errs);
            IComponent2 square = AddComponent(asm, boxPath, 0.5); // box 25 x 20 x 10 mm
            double[] squareBox = (double[])square.GetBox(false, false);
            ShowBox(squareBox);
            double topZ = squareBox[5];
            m = PickFace(app, asm, (squareBox[0] + squareBox[3]) / 2, (squareBox[1] + squareBox[4]) / 2, topZ);
            Check(m != null && !m.IsRound, "picked face read as flat");
            if (m != null)
            {
                Check(Math.Abs(m.Normal.Z - 1) < 1e-6, $"normal +Z ({m.Normal.X:F3}, {m.Normal.Y:F3}, {m.Normal.Z:F3})");
                Check(Math.Abs(Math.Abs(m.Axis.X) - 1) < 1e-6, "length along the 25 mm edge (X)");
                tab = InsertTab(app, asm, seat, m, TabOrientation.AlongTube);
                Vec3 pick = m.Pick;
                Check(Maps(app, tab, new Vec3(0, seat.Length, seat.Thickness / 2), new Vec3(pick.X, pick.Y, pick.Z + seat.Length)), "tab stands on the top face, hole straight up");
            }
            asm.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errs, ref errs);

            Step("Tab insert: convention-named assembly names the tab the same way");
            string pedalDir = Path.Combine(dir, "PedalBox");
            Directory.CreateDirectory(pedalDir);
            IModelDoc2 pedal = NewAssembly(app);
            AddComponent(pedal, tubePath, 0);
            Save(pedal, Path.Combine(pedalDir, "aERG_0001_PedalBox_REV02_V03.SLDASM"));
            File.WriteAllText(Path.Combine(pedalDir, "ERG_0007_Pedal_REV02_V01.SLDPRT"), ""); // a number already in use
            m = PickFace(app, pedal, 0, r, 0.1); // top of the tube (axis along Z here)
            if (m != null)
            {
                tab = InsertTab(app, pedal, seat, m, TabOrientation.AlongTube);
                string tabName = Path.GetFileNameWithoutExtension(tab.GetPathName());
                Console.WriteLine($"  tab file: {tabName}");
                Check(tabName == "ERG_0008_SeatTab_REV02_V01", "tab named ERG_0008_SeatTab_REV02_V01 (team and REV of the assembly, next free number)");
            }
        }

        private static IComponent2 InsertTab(ISldWorks app, IModelDoc2 asm, TabInputs t, TabMount m, TabOrientation o)
        {
            IModelDoc2 part = TabBuilder.Build(app, t);
            return TabInserter.Insert(app, asm, part, t.Use, TabPlacement.Place(t, m, o));
        }

        private static TabMount PickFace(ISldWorks app, IModelDoc2 asm, double x, double y, double z)
        {
            int errs = 0;
            app.ActivateDoc3(asm.GetTitle(), false, 0, ref errs);
            asm.ClearSelection2(true);
            bool selected = asm.Extension.SelectByID2("", "FACE", x, y, z, false, 0, null, 0);
            Check(selected, $"face selected at ({x:F4}, {y:F4}, {z:F4})");
            return selected ? TabInserter.ReadSelection(app, out _) : null;
        }

        private static void ShowBox(double[] b) =>
            Console.WriteLine($"  box x {b[0]:F4}..{b[3]:F4}, y {b[1]:F4}..{b[4]:F4}, z {b[2]:F4}..{b[5]:F4}");

        /// <summary>A point in the component's part coordinates, in assembly coordinates.</summary>
        private static Vec3 Map(ISldWorks app, IComponent2 component, Vec3 part)
        {
            var mu = (IMathUtility)app.GetMathUtility();
            var pt = (IMathPoint)mu.CreatePoint(new[] { part.X, part.Y, part.Z });
            var r = (double[])((IMathPoint)pt.MultiplyTransform(component.Transform2)).ArrayData;
            return new Vec3(r[0], r[1], r[2]);
        }

        private static bool Maps(ISldWorks app, IComponent2 component, Vec3 part, Vec3 expected)
        {
            Vec3 got = Map(app, component, part);
            bool ok = (got - expected).Length < 1e-6;
            if (!ok)
            {
                Console.WriteLine($"  expected ({expected.X:F5}, {expected.Y:F5}, {expected.Z:F5}) got ({got.X:F5}, {got.Y:F5}, {got.Z:F5})");
            }
            return ok;
        }

        private static string CreateTubePart(ISldWorks app, string dir, string name, double radius, double length)
        {
            IModelDoc2 doc = NewPart(app);
            Planes(doc).First().Select2(false, 0); // Front plane: extrude along part Z
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCircleByRadius(0, 0, 0, radius);
            doc.SketchManager.CreateCircleByRadius(0, 0, 0, radius - 0.065 * Units.MetersPerInch);
            doc.SketchManager.InsertSketch(true);
            ((IFeature)doc.FeatureByPositionReverse(0)).Select2(false, 0);
            IFeature extrude = doc.FeatureManager.FeatureExtrusion2(
                true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, length, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            if (extrude == null)
            {
                throw new InvalidOperationException("Tube extrusion failed");
            }
            return Save(doc, Path.Combine(dir, name + ".SLDPRT"));
        }

        // ---- Tube notcher (issue #11) ----

        private static void NotcherTest(ISldWorks app, string dir)
        {
            Step("Notcher: weldment frame (LFS floor, RRH hoop on its corners, SM diagonal)");
            IModelDoc2 doc = NewPart(app);
            string profile = FindPipeProfile(app, out string config);
            Console.WriteLine($"  profile {profile} [{config}]");

            // Floor rectangle 1.0 x 0.6 m, hoop 0.8 m tall on the x = 0 edge, diagonal mid-floor to mid-hoop.
            IFeature lfs = AddMembers(doc, profile, config, "LFS", new[]
            {
                (new Vec3(0, 0, 0), new Vec3(1, 0, 0)), (new Vec3(1, 0, 0), new Vec3(1, 0, 0.6)),
                (new Vec3(1, 0, 0.6), new Vec3(0, 0, 0.6)), (new Vec3(0, 0, 0.6), new Vec3(0, 0, 0)),
            });
            IFeature rrh = AddMembers(doc, profile, config, "RRH", new[]
            {
                (new Vec3(0, 0, 0), new Vec3(0, 0.8, 0)), (new Vec3(0, 0.8, 0), new Vec3(0, 0.8, 0.6)),
                (new Vec3(0, 0.8, 0.6), new Vec3(0, 0, 0.6)),
            });
            IFeature sm = AddMembers(doc, profile, config, "SM", new[] { (new Vec3(0.5, 0, 0), new Vec3(0, 0.8, 0.3)) });
            Check(lfs != null && rrh != null && sm != null, "three structural member features made");
            if (lfs == null || rrh == null || sm == null)
            {
                return;
            }
            Console.WriteLine($"  feature types: {lfs.GetTypeName2()}");
            Check(FrameNotcher.IsWeldmentPart(doc), "part read as a weldment");

            FrameNotcher.Frame frame = FrameNotcher.Read(doc);
            Console.WriteLine($"  groups {string.Join(", ", frame.Groups)}; members {frame.Members.Count}; skipped {frame.Skipped.Count}");
            foreach (FrameJoint j in frame.Joints)
            {
                Console.WriteLine($"  joint {j.A} (end {j.AtEndOfA}) / {j.B} (end {j.AtEndOfB})");
            }
            Check(frame.Members.Count == 8, $"8 tube bodies (got {frame.Members.Count})");
            Check(frame.Groups.SequenceEqual(new[] { "LFS", "RRH", "SM" }), "groups in tree order");

            List<string> order = Notching.DefaultOrder(frame.Groups);
            Check(order.SequenceEqual(new[] { "LFS", "RRH", "SM" }), "default order LFS > RRH > SM");
            NotchPlan plan = Notching.Plan(frame.Members, frame.Joints, order);
            foreach (NotchCut c in plan.Cuts)
            {
                Console.WriteLine($"  cut {c.Trimmed} around {c.Boundary} ({c.Reason})");
            }
            string Group(string body) => frame.Members.Single(m => m.Body == body).Group;
            Check(plan.Cuts.Any(c => Group(c.Trimmed) == "SM" && Group(c.Boundary) == "LFS" && c.Reason == NotchReason.EndsOnThroughTube),
                "SM coped around the LFS it ends on");
            Check(plan.Cuts.Any(c => Group(c.Trimmed) == "SM" && Group(c.Boundary) == "RRH" && c.Reason == NotchReason.EndsOnThroughTube),
                "SM coped around the RRH top it ends on");
            Check(plan.Cuts.Where(c => c.Reason == NotchReason.Priority).All(c => Group(c.Trimmed) == "RRH" && Group(c.Boundary) == "LFS")
                  && plan.Cuts.Any(c => c.Reason == NotchReason.Priority),
                "at the corner nodes RRH is coped around LFS (priority)");
            Check(plan.Crossings.Count == 0, "no crossings");

            // An un-notched copy to try the button on by hand (--keep keeps it in Documents).
            Save(doc, Path.Combine(dir, "Notch Demo Frame.SLDPRT"));

            double before = Volume(doc);
            (int made, List<string> problems) = FrameNotcher.Apply(doc, plan);
            problems.ForEach(p => Console.WriteLine("  problem: " + p));
            double after = Volume(doc);
            Console.WriteLine($"  {made} trim feature(s); volume {before * 1e6:F1} -> {after * 1e6:F1} cm3");
            Check(made == plan.TrimOrder.Count && problems.Count == 0, $"one trim feature per coped tube ({plan.TrimOrder.Count})");
            Check(after < before - 1e-7, "material removed");

            FrameNotcher.Frame again = FrameNotcher.Read(doc); // deletes the notches, re-measures
            Check(again.OldNotches == made, $"re-run removes the {made} earlier notch feature(s)");
            Check(Math.Abs(Volume(doc) - before) < 1e-9, "frame back to untrimmed");
            (made, problems) = FrameNotcher.Apply(doc, Notching.Plan(again.Members, again.Joints, order));
            Check(Math.Abs(Volume(doc) - after) < 1e-9 && problems.Count == 0, "re-run gives the same result");

            // Coped tubes no longer overlap what they were cut around.
            FrameNotcher.Frame trimmed = FrameNotcher.ReadWithoutDeleting(doc);
            Check(trimmed.Joints.Count == 0, $"no overlaps left between groups (got {trimmed.Joints.Count})");
            Save(doc, Path.Combine(dir, "Notch Frame.SLDPRT"));
        }

        private static double Volume(IModelDoc2 doc) => doc.Extension.CreateMassProperty().Volume;

        private static string FindPipeProfile(ISldWorks app, out string config)
        {
            string root = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsWeldmentProfiles);
            string path = (root ?? "").Split(';')
                .Select(r => Path.Combine(r.Trim(), "ansi inch", "pipe.sldlfp"))
                .FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("ansi inch\\pipe.sldlfp not found under " + root);
            string[] configs = (app.GetConfigurationNames(path) as object[] ?? new object[0]).OfType<string>().ToArray();
            config = configs.FirstOrDefault(c => c.StartsWith("1.5", StringComparison.Ordinal)) ?? configs.FirstOrDefault() ?? "";
            return path;
        }

        /// <summary>A 3D sketch of lines and one structural member feature on them, renamed to <paramref name="name"/>.</summary>
        private static IFeature AddMembers(IModelDoc2 doc, string profile, string config, string name, (Vec3 A, Vec3 B)[] lines)
        {
            doc.ClearSelection2(true);
            ISketchManager sm = doc.SketchManager;
            sm.Insert3DSketch(true);
            bool addToDb = sm.AddToDB;
            sm.AddToDB = true;
            var segments = new List<SketchSegment>();
            foreach ((Vec3 a, Vec3 b) in lines)
            {
                segments.Add((SketchSegment)sm.CreateLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z));
            }
            sm.AddToDB = addToDb;
            sm.Insert3DSketch(true);

            IFeatureManager fm = doc.FeatureManager;
            StructuralMemberGroup group = fm.CreateStructuralMemberGroup();
            group.Segments = segments.ToArray();
            IFeature f = fm.InsertStructuralWeldment5(profile,
                (int)swConnectedSegmentsOption_e.swConnectedSegments_SimpleCut, false,
                new[] { group }, config);
            if (f != null)
            {
                f.Name = name;
            }
            doc.ClearSelection2(true);
            return f;
        }

        // ---- Auto drawings (issue #13) ----

        private static void DrawingsTest(ISldWorks app, string dir)
        {
            string folder = Path.Combine(dir, "Drawings");
            Directory.CreateDirectory(folder);

            Step("Drawings: one part, then an assembly and its parts");
            string bracket = CreateBoxPart(app, folder, "CHA_0002_SeatBracket_REV01_V01", 0.06);
            string plate = CreateBoxPart(app, folder, "CHA_0003_BasePlate_REV01_V01", 0.40);
            IModelDoc2 asm = NewAssembly(app);
            AddComponent(asm, bracket, 0);
            AddComponent(asm, plate, 0.1);
            string asmPath = Save(asm, Path.Combine(folder, "aCHA_0001_Seat_REV01_V01.SLDASM"));

            var options = new DrawingMaker.Options { ImportDimensions = true };
            DrawingMaker.Result r = DrawingMaker.Make(app, new[] { bracket }, options, keepLastOpen: false);
            r.Failed.ForEach(f => Console.WriteLine("  failed: " + f));
            string bracketDrawing = Path.Combine(folder, "dCHA_0002_SeatBracket_REV01_V01.SLDDRW");
            Check(r.Made.Count == 1 && File.Exists(bracketDrawing), "dCHA_0002_SeatBracket_REV01_V01.SLDDRW made");
            CheckDrawing(app, bracketDrawing, "CHA_0002", "Seat Bracket", s => s == 1); // 60 mm part plus its iso fits a letter sheet at 1:1

            int errs = 0;
            app.ActivateDoc3(asm.GetTitle(), false, 0, ref errs);
            List<string> parts = DrawingMaker.PartsOf(asm, out int skipped);
            Check(parts.Count == 2 && skipped == 0, $"assembly uses 2 parts to draw (got {parts.Count}, skipped {skipped})");
            var all = new List<string> { asmPath };
            all.AddRange(parts);
            r = DrawingMaker.Make(app, all, options, keepLastOpen: false);
            r.Failed.ForEach(f => Console.WriteLine("  failed: " + f));
            Check(r.Made.Count == 2 && r.Existing.SequenceEqual(new[] { "dCHA_0002_SeatBracket_REV01_V01.SLDDRW" }),
                $"assembly and plate drawn, existing bracket drawing kept (made {r.Made.Count}, existing {r.Existing.Count})");
            CheckDrawing(app, Path.Combine(folder, "dCHA_0001_Seat_REV01_V01.SLDDRW"), "CHA_0001", "Seat", s => s < 1);

            r = DrawingMaker.Make(app, new[] { bracket }, new DrawingMaker.Options { Replace = true }, keepLastOpen: false);
            Check(r.Made.Count == 1 && r.Failed.Count == 0, "Replace remakes an existing drawing");

            Step("Drawings: replacing a drawing that is still open (user report)");
            string plateDrawing = Path.Combine(folder, "dCHA_0003_BasePlate_REV01_V01.SLDDRW");
            r = DrawingMaker.Make(app, new[] { plate }, new DrawingMaker.Options { Replace = true }, keepLastOpen: true);
            Check(r.Made.Count == 1 && app.GetOpenDocumentByName(plateDrawing) != null, "single drawing left open");
            r = DrawingMaker.Make(app, all, new DrawingMaker.Options { Replace = true }, keepLastOpen: false);
            r.Failed.ForEach(f => Console.WriteLine("  failed: " + f));
            Check(r.Made.Count == 3 && r.Failed.Count == 0, "batch Replace closes the open, unchanged drawing and remakes it");

            r = DrawingMaker.Make(app, new[] { plate }, new DrawingMaker.Options { Replace = true }, keepLastOpen: true);
            var openDrawing = (IModelDoc2)app.GetOpenDocumentByName(plateDrawing);
            openDrawing.SetSaveFlag(); // pretend the user edited it
            r = DrawingMaker.Make(app, all, new DrawingMaker.Options { Replace = true }, keepLastOpen: false);
            r.Failed.ForEach(f => Console.WriteLine("  failed: " + f));
            Check(r.Made.Count == 2 && r.Failed.Count == 1 && r.Failed[0].Contains("unsaved changes"),
                "an open drawing with unsaved edits is left alone, with a clear message");
            Check(app.GetOpenDocumentByName(plateDrawing) != null, "and it is still open");
        }

        private static void CheckDrawing(ISldWorks app, string path, string partNo, string description, Func<double, bool> scaleOk)
        {
            int errors = 0, warnings = 0;
            var doc = app.OpenDoc6(path, (int)swDocumentTypes_e.swDocDRAWING, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            if (doc == null)
            {
                Fail($"could not open {path} (error {errors})");
                return;
            }
            var drawing = (IDrawingDoc)doc;
            var sheet = (ISheet)drawing.GetCurrentSheet();
            var props = (double[])sheet.GetProperties2();
            double w = 0, h = 0;
            sheet.GetSize(ref w, ref h);
            var views = new List<IView>();
            for (var v = (IView)((IView)drawing.GetFirstView()).GetNextView(); v != null; v = (IView)v.GetNextView())
            {
                views.Add(v);
            }
            Console.WriteLine($"  {Path.GetFileName(path)}: sheet {w * 1000:F0} x {h * 1000:F0} mm, scale {props[2]}:{props[3]}, " +
                              $"views {string.Join(", ", views.Select(v => v.GetOrientationName()))}");
            Check(views.Count == 4, "four views");
            Check(views.Select(v => v.GetOrientationName()).OrderBy(n => n).SequenceEqual(new[] { "*Front", "*Isometric", "*Right", "*Top" }),
                "front, top, right, isometric");
            Check(scaleOk(props[2] / props[3]), "scale suits the model size");
            bool onSheet = views.All(v =>
            {
                var o = (double[])v.GetOutline();
                bool inside = o[0] >= -1e-6 && o[1] >= -1e-6 && o[2] <= w + 1e-6 && o[3] <= h + 1e-6;
                if (!inside)
                {
                    Console.WriteLine($"  {v.GetOrientationName()} outline {o[0] * 1000:F0},{o[1] * 1000:F0} .. {o[2] * 1000:F0},{o[3] * 1000:F0} mm");
                }
                return inside;
            });
            Check(onSheet, "every view inside the sheet");
            doc.Extension.get_CustomPropertyManager("").Get6("PartNo", false, out _, out string no, out _, out _);
            doc.Extension.get_CustomPropertyManager("").Get6("Description", false, out _, out string desc, out _, out _);
            Check(no == partNo && desc == description, $"title properties {no} / {desc}");

            // A PDF next to the drawing, to look at the layout (kept with --keep).
            string pdf = Path.ChangeExtension(path, ".pdf");
            doc.Extension.SaveAs3(pdf, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null, null, ref errors, ref warnings);
            Check(File.Exists(pdf), "PDF of the drawing written");
            app.CloseDoc(path);
        }

        // ---- Suspension travel sweep (issue #12) ----

        private static void SweepTest(ISldWorks app, string dir)
        {
            Step("Sweep: upright swung by a sketch angle, HP_ points on the sketch");
            const double deg = Math.PI / 180;
            const int inputDim = (int)swUserPreferenceToggle_e.swInputDimValOnCreate;
            bool inputDimSetting = app.GetUserPreferenceToggle(inputDim);
            app.SetUserPreferenceToggle(inputDim, false); // no value prompt while dimensioning
            try
            {
                IModelDoc2 part = NewPart(app);
                Planes(part).First().Select2(false, 0); // Front plane: sketch X = model X (left), Y = model Y (up)
                ISketchManager sm = part.SketchManager;
                sm.InsertSketch(true);
                sm.AddToDB = true;
                var ground = (ISketchSegment)sm.CreateLine(0, 0, 0, 0.1, 0, 0);
                var upright = (ISketchSegment)sm.CreateLine(0, 0, 0, -0.04, 0.22, 0);
                var spindle = (ISketchSegment)sm.CreateLine(-0.02, 0.11, 0, 0.078, 0.128, 0);
                sm.AddToDB = false;

                void Relate(string relation, params object[] picks)
                {
                    part.ClearSelection2(true);
                    foreach (object o in picks)
                    {
                        if (o is ISketchSegment s)
                        {
                            s.Select4(true, null);
                        }
                        else
                        {
                            ((ISketchPoint)o).Select4(true, null);
                        }
                    }
                    part.SketchAddConstraints(relation);
                }
                IDimension Dimension(double x, double y, double value, params ISketchSegment[] picks)
                {
                    part.ClearSelection2(true);
                    foreach (ISketchSegment s in picks)
                    {
                        s.Select4(true, null);
                    }
                    var dd = (IDisplayDimension)part.AddDimension2(x, y, 0);
                    IDimension d = dd?.GetDimension2(0);
                    d?.SetSystemValue3(value, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    return d;
                }
                ISketchPoint Start(ISketchSegment s) => (ISketchPoint)((ISketchLine)s).GetStartPoint2();
                ISketchPoint End(ISketchSegment s) => (ISketchPoint)((ISketchLine)s).GetEndPoint2();

                Relate("sgFIXED", ground);
                Relate("sgMERGEPOINTS", Start(ground), Start(upright));
                Relate("sgATMIDDLE", Start(spindle), upright);
                Relate("sgPERPENDICULAR", spindle, upright);
                Dimension(-0.08, 0.1, 0.22, upright);
                Dimension(0.05, 0.2, 0.10, spindle);
                IDimension angle = Dimension(0.03, 0.05, 100 * deg, ground, upright);
                part.ClearSelection2(true);
                sm.InsertSketch(true);
                Check(angle != null, "angle dimension made");
                if (angle == null)
                {
                    return;
                }

                // Reference points on the sketch points, named for the front left corner.
                var sketch = (ISketch)((IFeature)part.FeatureByPositionReverse(0)).GetSpecificFeature2();
                int named = 0;
                foreach ((ISketchPoint pt, string name) in new[]
                {
                    (Start(upright), "HP_LBJ_FL"), (End(upright), "HP_UBJ_FL"), (Start(spindle), "HP_SA_FL"), (End(spindle), "HP_WC_FL"),
                })
                {
                    part.ClearSelection2(true);
                    pt.Select4(false, null);
                    object created = part.FeatureManager.InsertReferencePoint((int)swRefPointType_e.swRefPointSketchPoint, 0, 0, 1);
                    if (created != null && part.FeatureByPositionReverse(0) is IFeature f && f.GetTypeName2() == "RefPoint")
                    {
                        f.Name = name;
                        named++;
                    }
                }
                part.ClearSelection2(true);
                Check(named == 4, $"4 hardpoints named (got {named})");
                Save(part, Path.Combine(dir, "Sweep Upright.SLDPRT"));

                SuspensionSweeper.Driver driver = SuspensionSweeper.FromDimension(angle);
                Check(driver != null && driver.Angular && Math.Abs(driver.Start - 100 * deg) < 1e-9, $"driver read: {driver?.Name}, angular");
                List<double> values = SuspensionSweep.Values(90 * deg, 110 * deg, 5);
                var settings = new SuspensionSettings();
                SuspensionSweeper.Result r = SuspensionSweeper.Run(app, part, driver, values, settings);
                r.Problems.ForEach(p => Console.WriteLine("  problem: " + p));
                Check(r.Steps.Count == 5 && r.Reference.Count == 1, $"5 steps of 1 corner (got {r.Steps.Count})");
                if (r.Steps.Count != 5)
                {
                    return;
                }
                CornerGeometry start = r.Reference[0];
                foreach (SweepStep s in r.Steps)
                {
                    CornerGeometry g = s.Corners[0];
                    Console.WriteLine($"  angle {s.Driver / deg:F1}: camber {g.Camber:F3}, KPI {g.Kpi:F3}, toe {g.Toe:F3}, " +
                                      $"travel {SuspensionSweep.Travel(g, r.Reference) * 1000:F2} mm");
                }
                bool exact = r.Steps.All(s =>
                {
                    double turn = (s.Driver - driver.Start) / deg;
                    CornerGeometry g = s.Corners[0];
                    return Math.Abs(g.Camber - (start.Camber - turn)) < 1e-6 && Math.Abs(g.Kpi - (start.Kpi + turn)) < 1e-6 && Math.Abs(g.Toe) < 1e-6;
                });
                Check(exact, "camber changes by minus the turn, KPI by plus the turn, toe stays 0");
                var now = (double[])angle.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                Check(Math.Abs(now[0] - 100 * deg) < 1e-9, "angle back to 100 deg after the sweep");
                SweepSummary sum = SuspensionSweep.Summarize(r.Steps, r.Reference).Single();
                Console.WriteLine($"  camber gain {sum.CamberGainPerInch:F3} deg/in, bump steer {sum.BumpSteerPerInch:F4} deg/in, " +
                                  $"travel {sum.TravelMin * 1000:F1} to {sum.TravelMax * 1000:F1} mm");
                Check(Math.Abs(sum.BumpSteerPerInch) < 1e-6 && sum.TravelMax > sum.TravelMin, "summary: travel range, no bump steer");
                string csv = SuspensionSweep.Csv(r.Steps, r.Reference, driver.Name, "deg", v => v / deg);
                Check(csv.Split('\n').Count(l => l.Trim().Length > 0) == 6, "CSV has a header and 5 rows");
            }
            finally
            {
                app.SetUserPreferenceToggle(inputDim, inputDimSetting);
            }
        }

        // ---- Add-in config (issue #9) ----

        private static void AddInConfigTest(ISldWorks app)
        {
            Step("Add-in config: registry lookup");
            List<AddInManager.InstalledAddIn> installed = AddInManager.Installed();
            foreach (AddInManager.InstalledAddIn a in installed)
            {
                Console.WriteLine($"  {a.Title} -> {a.Path ?? "(no path)"}");
            }
            Check(installed.Any(a => a.Title == "LBR CAD Tools" && a.Path != null && File.Exists(a.Path)),
                "LBR CAD Tools found with its DLL path");
            Check(AddInManager.Resolve("No Such Add-In", installed, out string error) == null && error.Contains("no add-in"),
                "unknown title reported");

            // A SolidWorks add-in that ships with every install and is off by default here.
            const string probe = "Presentation Manager";
            string path = AddInManager.Resolve(probe, installed, out error);
            if (path == null)
            {
                Console.WriteLine($"  {probe} not installed; skipping load/unload ({error})");
                return;
            }

            Step($"Add-in config: load and unload {probe}");
            int load1 = app.LoadAddIn(path);
            int load2 = app.LoadAddIn(path);
            int unload1 = app.UnloadAddIn(path);
            int unload2 = app.UnloadAddIn(path);
            Console.WriteLine($"  LoadAddIn {load1}, again {load2}; UnloadAddIn {unload1}, again {unload2}");
            Check(load1 == 0 && load2 == 2 && unload1 == 0 && unload2 == 0, "return codes: load 0 then 2, unload 0 then 0");
            Check(new[] { load1, load2, unload1, unload2 }.All(c => AddInManager.Describe(probe, true, c) == null),
                "none of them reported as a problem");

            Step("Add-in config: Apply from settings");
            string ownClsid = "{" + typeof(Lbr.CadTools.AddIn.AddIn).GUID + "}";
            var settings = new AddInSettings();
            settings.LoadAddIns.Add(probe);
            settings.LoadAddIns.Add("No Such Add-In");
            List<string> problems = AddInManager.Apply(app, settings, ownClsid);
            Console.WriteLine("  problems: " + string.Join(" | ", problems));
            Check(problems.Count == 1 && problems[0].Contains("No Such Add-In"), "load: only the unknown title is a problem");

            settings = new AddInSettings();
            settings.UnloadAddIns.Add(probe);
            settings.UnloadAddIns.Add("LBR CAD Tools");
            problems = AddInManager.Apply(app, settings, ownClsid);
            Console.WriteLine("  problems: " + string.Join(" | ", problems));
            Check(problems.Count == 1 && problems[0].Contains("cannot unload itself"), "unload: refuses to unload LBR CAD Tools");
        }

        private static void AddSketchLine(IModelDoc2 doc)
        {
            IFeature plane = Planes(doc).First();
            plane.Select2(false, 0);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateLine(0, 0, 0, 0.01, 0.01, 0);
            doc.SketchManager.InsertSketch(true);
            doc.ClearSelection2(true);
        }

        private static string CreateSheetMetalPart(ISldWorks app, string dir, string name)
        {
            IModelDoc2 doc = NewPart(app);
            IFeature plane = Planes(doc).First();
            plane.Select2(false, 0);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(0, 0, 0, 0.060, 0.040, 0);
            doc.SketchManager.InsertSketch(true);
            ((IFeature)doc.FeatureByPositionReverse(0)).Select2(false, 0);
            CustomBendAllowance bend = doc.FeatureManager.CreateCustomBendAllowance();
            IFeature flange = doc.FeatureManager.InsertSheetMetalBaseFlange2(
                0.003, false, 0.003, 0, 0, false, 0, 0, 0, bend,
                false, 1, 0.0005, 0.0005, 0.5, true, false, true, true);
            if (flange == null)
            {
                return null;
            }
            return Save(doc, Path.Combine(dir, name + ".SLDPRT"));
        }

        /// <summary>Drawing with one front view of the model, saved and closed.</summary>
        private static string CreateDrawing(ISldWorks app, string modelPath, string path)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            var doc = (IModelDoc2)app.NewDocument(template, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (doc == null)
            {
                Console.WriteLine($"  NewDocument failed for drawing template '{template}'");
                return null;
            }
            IView view = ((IDrawingDoc)doc).CreateDrawViewFromModelView3(modelPath, "*Front", 0.15, 0.15, 0);
            if (view == null)
            {
                Console.WriteLine("  CreateDrawViewFromModelView3 failed");
            }
            Save(doc, path);
            app.CloseDoc(doc.GetTitle());
            return view == null ? null : path;
        }

        private static bool SamePoints(List<Point3> actual, List<Point3> expected, double tol) =>
            actual.Count == expected.Count
            && expected.All(e => actual.Any(a =>
                Math.Abs(a.X - e.X) < tol && Math.Abs(a.Y - e.Y) < tol && Math.Abs(a.Z - e.Z) < tol));

        private static string Show(IEnumerable<Point3> points) =>
            string.Join(" ", points.Select(p => $"({p.X * 1000:0.###}, {p.Y * 1000:0.###}, {p.Z * 1000:0.###}) mm"));

        private static bool[] SubStates(IModelDoc2 sub) => TopLevel(sub).Select(c => c.IsFixed()).ToArray();

        private static string Show(bool[] states) => "[" + string.Join(", ", states) + "]";

        // ---- Model building helpers ----

        /// <summary>
        /// Closes everything and gives SolidWorks a moment to finish. Creating
        /// a part right after closing the Batch Export documents sometimes gave
        /// a document whose features all threw RPC_E_SERVERFAULT.
        /// </summary>
        private static void CloseAll(ISldWorks app)
        {
            app.CloseAllDocuments(true);
            System.Threading.Thread.Sleep(2000);
        }

        private static IModelDoc2 NewPart(ISldWorks app)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            return (IModelDoc2)app.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"NewDocument failed for part template '{template}'");
        }

        private static string CreateBoxPart(ISldWorks app, string dir, string name, double width)
        {
            IModelDoc2 doc = NewPart(app);

            // First reference plane, found by type so template language does not matter.
            IFeature plane = Planes(doc).FirstOrDefault()
                ?? throw new InvalidOperationException("No reference plane in part template: " + Diagnose(doc));
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

        /// <summary>
        /// Reference planes in tree order (Front, Top, Right). Right after many
        /// documents close, GetTypeName2 on a new document's features can throw
        /// RPC_E_SERVERFAULT out of process; such a feature is skipped.
        /// </summary>
        private static IEnumerable<IFeature> Planes(IModelDoc2 doc) =>
            Features(doc).Where(f =>
            {
                try
                {
                    return f.GetTypeName2() == "RefPlane";
                }
                catch (COMException)
                {
                    return false;
                }
            });

        private static string Diagnose(IModelDoc2 doc)
        {
            var parts = new List<string> { $"type {doc.GetType()}, title {doc.GetTitle()}" };
            foreach (IFeature f in Features(doc).Take(8))
            {
                string name, type;
                try { name = f.Name; } catch (Exception ex) { name = "!" + ex.GetType().Name; }
                try { type = f.GetTypeName2(); } catch (Exception ex) { type = "!" + ex.GetType().Name; }
                try { if (type.StartsWith("!")) type += "/" + f.GetTypeName(); } catch (Exception ex) { type += "/!" + ex.GetType().Name; }
                parts.Add($"{name}:{type}");
            }
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// All features from IFeatureManager.GetFeatures rather than walking
        /// FirstFeature/GetNextFeature: out of process the walk sometimes hits
        /// an entry (right after Design Binder) whose calls all throw
        /// RPC_E_SERVERFAULT, so the planes after it are never reached.
        /// </summary>
        private static IEnumerable<IFeature> Features(IModelDoc2 doc) =>
            ((doc.FeatureManager.GetFeatures(false) as object[]) ?? new object[0]).OfType<IFeature>();

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
