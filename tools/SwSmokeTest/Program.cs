using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Features.AddInConfig;
using Lbr.CadTools.AddIn.Features.AutoSave;
using Lbr.CadTools.AddIn.Features.BatchExport;
using Lbr.CadTools.AddIn.Features.FixComponents;
using Lbr.CadTools.AddIn.Features.SketchPoints;
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
                    app.CloseAllDocuments(true);
                    SketchPointsTest(app, dir, keep);
                    app.CloseAllDocuments(true);
                    BatchExportTest(app, dir);
                    app.CloseAllDocuments(true);
                    AutoSaveTest(app, dir);
                    AddInConfigTest(app);
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
            IFeature top = Features(part).Where(f => f.GetTypeName2() == "RefPlane").ElementAt(1);
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
            Check(r.Failed.Count == 0 && r.Written.Count >= expected.Count, $"no failures, {r.Written.Count} written");
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
            IFeature plane = Features(saved).First(f => f.GetTypeName2() == "RefPlane");
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
            IFeature plane = Features(doc).First(f => f.GetTypeName2() == "RefPlane");
            plane.Select2(false, 0);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateLine(0, 0, 0, 0.01, 0.01, 0);
            doc.SketchManager.InsertSketch(true);
            doc.ClearSelection2(true);
        }

        private static string CreateSheetMetalPart(ISldWorks app, string dir, string name)
        {
            IModelDoc2 doc = NewPart(app);
            IFeature plane = Features(doc).First(f => f.GetTypeName2() == "RefPlane");
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
