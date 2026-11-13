using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.BatchExport
{
    internal sealed class BatchExportOptions
    {
        public HashSet<ExportFormat> Formats { get; } = new HashSet<ExportFormat>();

        /// <summary>Also export every unique component of an assembly (all levels).</summary>
        public bool IncludeComponents { get; set; }

        public bool SkipToolbox { get; set; } = true;

        /// <summary>Also export drawings saved next to each model with the same name (.SLDDRW).</summary>
        public bool IncludeDrawings { get; set; }

        public string OutputFolder { get; set; }

        /// <summary>Set by the exporter: the active document, never closed.</summary>
        internal string RootPath { get; set; }
    }

    internal sealed class BatchExportResult
    {
        public List<string> Written { get; } = new List<string>();
        public List<string> Failed { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Exports the active document, and optionally its components and their
    /// drawings, through IModelDocExtension.SaveAs3 (format from the file
    /// extension). Kept apart from the command so the smoke test can drive it.
    ///
    /// SolidWorks quirks handled here:
    /// - STEP, IGES, and STL need the document active and nothing selected
    ///   (with a selection, only the selection is exported).
    /// - A model is exported in the configuration the assembly uses, so the
    ///   active configuration is switched and restored.
    /// - STEP AP214, one DXF per drawing sheet, and one STL per assembly are
    ///   set through user preferences, which are restored afterwards.
    /// </summary>
    internal static class BatchExporter
    {
        private sealed class Job
        {
            public string Path;
            public string Configuration;
            public DocKind Kind;
            public IModelDoc2 Doc;
            public string BaseName;
        }

        public static DocKind? KindOf(string path)
        {
            switch (System.IO.Path.GetExtension(path ?? "").ToUpperInvariant())
            {
                case ".SLDPRT": return DocKind.Part;
                case ".SLDASM": return DocKind.Assembly;
                case ".SLDDRW": return DocKind.Drawing;
                default: return null;
            }
        }

        /// <summary>Unique, unsuppressed, non-virtual components (path + configuration) of an assembly.</summary>
        public static List<(string Path, string Configuration)> UniqueComponents(IModelDoc2 assembly, out int virtualCount)
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            virtualCount = 0;
            object[] components = ((IAssemblyDoc)assembly).GetComponents(false) as object[] ?? new object[0];
            foreach (IComponent2 c in components.Cast<IComponent2>())
            {
                if (c.IsSuppressed() || c.IsEnvelope())
                {
                    continue;
                }
                if (c.IsVirtual)
                {
                    virtualCount++;
                    continue;
                }
                string path = c.GetPathName();
                string config = c.ReferencedConfiguration ?? "";
                if (KindOf(path) != null && seen.Add(path + "|" + config))
                {
                    result.Add((path, config));
                }
            }
            return result;
        }

        public static BatchExportResult Run(ISldWorks app, IModelDoc2 root, BatchExportOptions options)
        {
            var result = new BatchExportResult();
            string rootPath = root.GetPathName();
            DocKind rootKind = KindOf(rootPath) ?? throw new InvalidOperationException("Save the document first.");
            Directory.CreateDirectory(options.OutputFolder);
            options.RootPath = rootPath;

            // ---- Collect the documents ----
            var jobs = new List<Job>
            {
                new Job
                {
                    Path = rootPath,
                    Kind = rootKind,
                    Doc = root,
                    Configuration = rootKind == DocKind.Drawing ? "" : root.ConfigurationManager.ActiveConfiguration.Name,
                },
            };

            if (rootKind == DocKind.Assembly && options.IncludeComponents)
            {
                // Lightweight components have no full model to export.
                ((IAssemblyDoc)root).ResolveAllLightWeightComponents(false);
                List<(string Path, string Configuration)> components = UniqueComponents(root, out int virtualCount);
                foreach ((string path, string config) in components)
                {
                    jobs.Add(new Job { Path = path, Configuration = config, Kind = KindOf(path).Value });
                }
                if (virtualCount > 0)
                {
                    result.Notes.Add($"Skipped {virtualCount} virtual {(virtualCount == 1 ? "component" : "components")} " +
                                     "(saved inside the assembly; save them externally to export them).");
                }
            }

            bool wantDrawings = options.Formats.Contains(ExportFormat.Pdf) || options.Formats.Contains(ExportFormat.Dxf);
            if (options.IncludeDrawings && wantDrawings && rootKind != DocKind.Drawing)
            {
                foreach (string modelPath in jobs.Select(j => j.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                {
                    string drawing = Path.ChangeExtension(modelPath, ".SLDDRW");
                    if (File.Exists(drawing))
                    {
                        jobs.Add(new Job { Path = drawing, Configuration = "", Kind = DocKind.Drawing });
                    }
                }
            }

            // Drop jobs with nothing to export before opening anything.
            jobs = jobs.Where(j => options.Formats.Any(f => ExportFormats.AppliesTo(f, j.Kind))).ToList();

            // ---- Open documents, skip Toolbox, read part numbers ----
            var openedByUs = new List<IModelDoc2>();
            var windowsToClose = new List<IModelDoc2>();
            int toolboxSkipped = 0;
            foreach (Job job in jobs.ToList())
            {
                job.Doc = job.Doc ?? Open(app, job, openedByUs, result);
                if (job.Doc == null)
                {
                    jobs.Remove(job);
                    continue;
                }
                if (options.SkipToolbox && job.Kind == DocKind.Part
                    && job.Doc.Extension.ToolboxPartType != (int)swToolBoxPartType_e.swNotAToolboxPart)
                {
                    toolboxSkipped++;
                    jobs.Remove(job);
                }
            }
            if (toolboxSkipped > 0)
            {
                result.Notes.Add($"Skipped {toolboxSkipped} Toolbox {(toolboxSkipped == 1 ? "part" : "parts")}.");
            }

            IReadOnlyList<string> names = ExportNaming.AssignBaseNames(
                jobs.Select(j => new ExportItem(j.Path, j.Configuration, j.Kind, PartNumber(j.Doc, j.Configuration))).ToList());
            for (int i = 0; i < jobs.Count; i++)
            {
                jobs[i].BaseName = names[i];
            }

            // ---- Export ----
            const int stepApPref = (int)swUserPreferenceIntegerValue_e.swStepAP;
            const int dxfSheetsPref = (int)swUserPreferenceIntegerValue_e.swDxfMultiSheetOption;
            int stepAp = app.GetUserPreferenceIntegerValue(stepApPref);
            int dxfSheets = app.GetUserPreferenceIntegerValue(dxfSheetsPref);
            const int stlOneFilePref = (int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile;
            const int stlInfoPref = (int)swUserPreferenceToggle_e.swSTLShowInfoOnSave;
            bool stlOneFile = app.GetUserPreferenceToggle(stlOneFilePref);
            bool stlInfo = app.GetUserPreferenceToggle(stlInfoPref);
            app.SetUserPreferenceIntegerValue(stepApPref, 214);
            app.SetUserPreferenceIntegerValue(dxfSheetsPref, (int)swDxfMultisheet_e.swDxfSeparateSheets);
            app.SetUserPreferenceToggle(stlOneFilePref, true); // one STL per assembly, not per component
            app.SetUserPreferenceToggle(stlInfoPref, false);   // its dialog would stop the run

            int total = jobs.Sum(j => options.Formats.Count(f => ExportFormats.AppliesTo(f, j.Kind)));
            UserProgressBar progress = null;
            try
            {
                if (app.GetUserProgressBar(out progress) && progress != null)
                {
                    progress.Start(0, Math.Max(total, 1), "Batch Export");
                }

                int done = 0;
                foreach (Job job in jobs)
                {
                    ExportJob(app, job, options, result, windowsToClose, () => progress?.UpdateProgress(++done));
                }
            }
            finally
            {
                progress?.End();
                app.SetUserPreferenceIntegerValue(stepApPref, stepAp);
                app.SetUserPreferenceIntegerValue(dxfSheetsPref, dxfSheets);
                app.SetUserPreferenceToggle(stlOneFilePref, stlOneFile);
                app.SetUserPreferenceToggle(stlInfoPref, stlInfo);

                foreach (IModelDoc2 doc in windowsToClose.Concat(openedByUs).Distinct().Where(d => d != root))
                {
                    try
                    {
                        app.CloseDoc(doc.GetTitle());
                    }
                    catch (Exception ex)
                    {
                        Log.Error("CloseDoc failed", ex);
                    }
                }
                int errors = 0;
                app.ActivateDoc3(rootPath, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            return result;
        }

        private static IModelDoc2 Open(ISldWorks app, Job job, List<IModelDoc2> openedByUs, BatchExportResult result)
        {
            if (app.GetOpenDocumentByName(job.Path) is IModelDoc2 open)
            {
                return open;
            }
            if (!File.Exists(job.Path))
            {
                result.Failed.Add($"{Path.GetFileName(job.Path)}: file not found");
                return null;
            }

            int type = job.Kind == DocKind.Part ? (int)swDocumentTypes_e.swDocPART
                : job.Kind == DocKind.Assembly ? (int)swDocumentTypes_e.swDocASSEMBLY
                : (int)swDocumentTypes_e.swDocDRAWING;
            int errors = 0, warnings = 0;
            IModelDoc2 doc = app.OpenDoc6(job.Path, type,
                (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                job.Configuration, ref errors, ref warnings);
            if (doc == null)
            {
                result.Failed.Add($"{Path.GetFileName(job.Path)}: could not open (error {errors})");
                return null;
            }
            openedByUs.Add(doc);
            return doc;
        }

        private static void ExportJob(ISldWorks app, Job job, BatchExportOptions options, BatchExportResult result,
                                      List<IModelDoc2> windowsToClose, Action step)
        {
            IModelDoc2 doc = job.Doc;
            List<ExportFormat> formats = ExportFormats.All
                .Where(f => options.Formats.Contains(f) && ExportFormats.AppliesTo(f, job.Kind))
                .ToList();

            // Activate the document (STEP/IGES/STL need it). A document that had
            // no window gets one now; close it again afterwards.
            if (!doc.Visible && !windowsToClose.Contains(doc) && job.Path != options.RootPath)
            {
                windowsToClose.Add(doc);
            }
            int activateErrors = 0;
            app.ActivateDoc3(job.Path, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors);

            string previousConfig = null;
            if (job.Kind != DocKind.Drawing && job.Configuration.Length > 0)
            {
                string active = doc.ConfigurationManager.ActiveConfiguration.Name;
                if (!string.Equals(active, job.Configuration, StringComparison.Ordinal))
                {
                    previousConfig = active;
                    doc.ShowConfiguration2(job.Configuration);
                }
            }

            try
            {
                doc.ClearSelection2(true);
                foreach (ExportFormat format in formats)
                {
                    string file = Path.Combine(options.OutputFolder, job.BaseName + ExportFormats.Suffix(format));
                    try
                    {
                        string error = ExportOne(app, doc, job, format, file, result.Written);
                        if (error == null && format != ExportFormat.Dxf)
                        {
                            result.Written.Add(file);
                        }
                        else if (!string.IsNullOrEmpty(error))
                        {
                            result.Failed.Add($"{Path.GetFileName(file)}: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Export {file} failed", ex);
                        result.Failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                    step();
                }
            }
            finally
            {
                if (previousConfig != null)
                {
                    doc.ShowConfiguration2(previousConfig);
                }
            }
        }

        /// <returns>null on success, "" when the format does not apply (no message), else the error.</returns>
        private static string ExportOne(ISldWorks app, IModelDoc2 doc, Job job, ExportFormat format, string file,
                                        List<string> written)
        {
            if (format == ExportFormat.FlatPatternDxf)
            {
                if (!IsSheetMetal(doc))
                {
                    return "";
                }
                DeleteExisting(file);
                bool ok = ((IPartDoc)doc).ExportToDWG2(file, job.Path,
                    (int)swExportToDWG_e.swExportToDWG_ExportSheetMetal, true, null, false, false,
                    1 /* geometry only: no bend lines, so a cutter does not cut them */, null);
                return ok && File.Exists(file) ? null : "flat pattern export failed";
            }

            object exportData = null;
            if (format == ExportFormat.Pdf)
            {
                var pdf = (IExportPdfData)app.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);
                pdf.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
                exportData = pdf;
            }

            DeleteExisting(file);
            DateTime started = DateTime.UtcNow.AddSeconds(-2);
            int errors = 0, warnings = 0;
            bool saved = doc.Extension.SaveAs3(file, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, exportData, null, ref errors, ref warnings);

            bool exists = File.Exists(file);
            if (format == ExportFormat.Dxf)
            {
                // One DXF per sheet, named by SolidWorks: find what was just written.
                List<string> sheets = Directory.EnumerateFiles(Path.GetDirectoryName(file), job.BaseName + "*.dxf")
                    .Where(f => !f.EndsWith(ExportFormats.Suffix(ExportFormat.FlatPatternDxf), StringComparison.OrdinalIgnoreCase)
                                && File.GetLastWriteTimeUtc(f) >= started)
                    .ToList();
                exists = sheets.Count > 0;
                if (saved)
                {
                    written.AddRange(sheets);
                }
            }
            if (saved && exists)
            {
                return null;
            }
            Log.Info($"SaveAs3 {file}: saved {saved}, exists {exists}, errors {errors}, warnings {warnings}");
            return $"save failed (error {errors})";
        }

        private static bool IsSheetMetal(IModelDoc2 doc)
        {
            for (var f = (IFeature)doc.FirstFeature(); f != null; f = (IFeature)f.GetNextFeature())
            {
                if (f.GetTypeName2() == "SheetMetal")
                {
                    return true;
                }
            }
            return false;
        }

        private static void DeleteExisting(string file)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        /// <summary>The first part number property found, configuration-specific first.</summary>
        public static string PartNumber(IModelDoc2 doc, string configuration)
        {
            foreach (string config in new[] { configuration ?? "", "" }.Distinct())
            {
                ICustomPropertyManager props = doc.Extension.get_CustomPropertyManager(config);
                if (props == null)
                {
                    continue;
                }
                string[] names = props.GetNames() as string[] ?? new string[0];
                foreach (string wanted in ExportNaming.PartNumberProperties)
                {
                    string actual = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
                    if (actual == null)
                    {
                        continue;
                    }
                    props.Get6(actual, false, out _, out string resolved, out _, out _);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved.Trim();
                    }
                }
            }
            return null;
        }
    }
}
