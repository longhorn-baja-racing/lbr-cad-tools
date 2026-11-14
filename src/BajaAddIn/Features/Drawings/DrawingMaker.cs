using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Features.Drawings
{
    /// <summary>
    /// Auto drawings (issue #13): a new drawing per model from the default
    /// drawing template, with front, top, right (third angle) and isometric
    /// views at a standard scale, title block properties from the file name,
    /// and optionally the model's dimensions. Models are never modified.
    /// </summary>
    internal static class DrawingMaker
    {
        internal sealed class Options
        {
            /// <summary>Import dimensions marked for drawing (parts only).</summary>
            public bool ImportDimensions { get; set; } = true;

            /// <summary>Overwrite drawings that already exist next to the model.</summary>
            public bool Replace { get; set; }
        }

        internal sealed class Result
        {
            public List<string> Made { get; } = new List<string>();
            public List<string> Existing { get; } = new List<string>();
            public List<string> Failed { get; } = new List<string>();
        }

        /// <summary>The drawing path a model gets: next to it, named by <see cref="DrawingLayout.DrawingName"/>.</summary>
        public static string DrawingPath(string modelPath) =>
            Path.Combine(Path.GetDirectoryName(modelPath), DrawingLayout.DrawingName(Path.GetFileNameWithoutExtension(modelPath)) + ".SLDDRW");

        /// <summary>
        /// Parts an assembly uses that get their own drawing: unique files, not
        /// virtual, suppressed, Toolbox, or REVSTD (standard) parts.
        /// </summary>
        public static List<string> PartsOf(IModelDoc2 assembly, out int skipped)
        {
            skipped = 0;
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object[] components = ((IAssemblyDoc)assembly).GetComponents(false) as object[] ?? new object[0];
            foreach (IComponent2 c in components.OfType<IComponent2>())
            {
                string path = c.GetPathName();
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) || !seen.Add(path))
                {
                    continue;
                }
                bool standard = NamingConvention.Parse(Path.GetFileNameWithoutExtension(path))?.IsStandard == true;
                bool toolbox = c.GetModelDoc2() is IModelDoc2 m && m.Extension.ToolboxPartType != (int)swToolBoxPartType_e.swNotAToolboxPart;
                if (c.IsVirtual || c.IsSuppressed() || toolbox || standard)
                {
                    skipped++;
                    continue;
                }
                paths.Add(path);
            }
            return paths;
        }

        /// <summary>Draws each model in turn; drawings are saved and closed unless <paramref name="keepLastOpen"/>.</summary>
        public static Result Make(ISldWorks app, IReadOnlyList<string> modelPaths, Options options, bool keepLastOpen)
        {
            var result = new Result();
            for (int i = 0; i < modelPaths.Count; i++)
            {
                string model = modelPaths[i];
                string drawing = DrawingPath(model);
                if (File.Exists(drawing) && !options.Replace)
                {
                    result.Existing.Add(Path.GetFileName(drawing));
                    continue;
                }

                // SolidWorks cannot save over a drawing that is open. Close it when it has no
                // unsaved edits (it is being replaced anyway); otherwise leave it alone.
                if (app.GetOpenDocumentByName(drawing) is IModelDoc2 open)
                {
                    if (open.GetSaveFlag())
                    {
                        result.Failed.Add($"{Path.GetFileName(drawing)} is open with unsaved changes. Save or close it, then run again.");
                        continue;
                    }
                    app.CloseDoc(drawing);
                }
                try
                {
                    MakeOne(app, model, drawing, options, keepOpen: keepLastOpen && i == modelPaths.Count - 1);
                    result.Made.Add(Path.GetFileName(drawing));
                }
                catch (Exception ex)
                {
                    Log.Error($"Drawing of {model} failed", ex);
                    result.Failed.Add($"{Path.GetFileName(model)}: {ex.Message}");
                }
            }
            return result;
        }

        private static void MakeOne(ISldWorks app, string modelPath, string drawingPath, Options options, bool keepOpen)
        {
            IModelDoc2 model = Open(app, modelPath);
            double[] box = Box(model);
            double sx = box[3] - box[0], sy = box[4] - box[1], sz = box[5] - box[2];
            bool isPart = model.GetType() == (int)swDocumentTypes_e.swDocPART;

            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            var doc = (IModelDoc2)app.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"Could not create a drawing from the template '{template}'.");
            var drawing = (IDrawingDoc)doc;
            try
            {
                var sheet = (ISheet)drawing.GetCurrentSheet();
                double w = 0, h = 0;
                sheet.GetSize(ref w, ref h);
                DrawingPlan plan = DrawingLayout.Plan(sx, sy, sz, w, h);
                sheet.SetScale(plan.Numerator, plan.Denominator, false, false);

                var placed = new List<(IView View, ViewSpot Spot)>();
                foreach (ViewSpot spot in plan.Views)
                {
                    IView view = drawing.CreateDrawViewFromModelView3(modelPath, spot.Orientation, spot.X, spot.Y, 0)
                        ?? throw new InvalidOperationException($"Could not place the {spot.Orientation.TrimStart('*')} view.");
                    view.UseSheetScale = 1;
                    placed.Add((view, spot));
                }

                // SolidWorks rescales the sheet when the first view goes in (automatic view scaling),
                // so set the planned scale again and move the views back to their spots.
                sheet.SetScale(plan.Numerator, plan.Denominator, false, false);
                doc.EditRebuild3();
                foreach ((IView view, ViewSpot spot) in placed)
                {
                    view.Position = new[] { spot.X, spot.Y };
                }

                if (options.ImportDimensions && isPart)
                {
                    drawing.InsertModelAnnotations3(
                        (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                        (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing,
                        true, false, false, false);
                }

                ICustomPropertyManager props = doc.Extension.get_CustomPropertyManager("");
                foreach (KeyValuePair<string, string> p in DrawingLayout.TitleProperties(Path.GetFileNameWithoutExtension(modelPath)))
                {
                    props.Add3(p.Key, (int)swCustomInfoType_e.swCustomInfoText, p.Value,
                        (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                }
                doc.ClearSelection2(true);
                doc.ForceRebuild3(false);

                int errors = 0, warnings = 0;
                if (!doc.Extension.SaveAs3(drawingPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref errors, ref warnings))
                {
                    throw new InvalidOperationException(
                        $"Could not save {Path.GetFileName(drawingPath)}. Check that the file is not open in another program " +
                        $"and that the folder is not read-only (SolidWorks error {errors}).");
                }
                Log.Info($"Drawing {drawingPath} at {plan.ScaleText}");
            }
            catch
            {
                app.CloseDoc(doc.GetTitle());
                throw;
            }
            if (!keepOpen)
            {
                app.CloseDoc(drawingPath);
            }
        }

        private static IModelDoc2 Open(ISldWorks app, string path)
        {
            if (app.GetOpenDocumentByName(path) is IModelDoc2 open)
            {
                return open;
            }
            int type = path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
                ? (int)swDocumentTypes_e.swDocASSEMBLY
                : (int)swDocumentTypes_e.swDocPART;
            int errors = 0, warnings = 0;
            return app.OpenDoc6(path, type, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings)
                ?? throw new InvalidOperationException($"Could not open {Path.GetFileName(path)} (error {errors}).");
        }

        /// <summary>Model bounding box: x0, y0, z0, x1, y1, z1 in meters.</summary>
        private static double[] Box(IModelDoc2 model)
        {
            double[] box = model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY
                ? ((IAssemblyDoc)model).GetBox(0) as double[] // geometry only, no planes or sketches
                : ((IPartDoc)model).GetPartBox(true) as double[];
            if (box == null || box.Length < 6)
            {
                throw new InvalidOperationException("The model has no geometry to draw.");
            }
            return box;
        }
    }
}
