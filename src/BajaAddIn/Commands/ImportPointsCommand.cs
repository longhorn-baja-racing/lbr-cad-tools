using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.SketchPoints;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Imports X, Y, Z rows from a CSV file as sketch points: into the sketch
    /// being edited, or into a new 3D sketch.
    /// </summary>
    internal sealed class ImportPointsCommand : IAddInCommand
    {
        private const int MaxListedErrors = 10;

        public string Title => "Import Points";

        public string Hint => "Place sketch points from a CSV file of X, Y, Z coordinates";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Reads a CSV file of X, Y, Z coordinates (hardpoints, measured or test data) and places a sketch point at each one, instead of typing coordinates in by hand.",
            Needs = "Open a part or an assembly.",
            Steps = new[]
            {
                "Optional: open a 2D or 3D sketch for editing. Otherwise a new 3D sketch is made.",
                "Click Import Points and choose the CSV file.",
                "Check the units. They are read from the header row when the file states them.",
                "Click Import.",
            },
            Notes = new[]
            {
                "Each row is X, Y, Z measured from the model origin. A header row and extra columns are fine.",
                "In a 2D sketch, points off the sketch plane are projected onto it.",
            },
        };

        public ToolGroup Group => ToolGroup.SketchPoints;

        public ToolIcon Icon => ToolIcon.ImportPoints;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            ISketch active = doc.SketchManager.ActiveSketch;
            string target = active == null
                ? "Points go into a new 3D sketch. To use a 2D sketch instead, open it for editing first."
                : $"Points go into the open sketch, {((IFeature)active).Name}." +
                  (active.Is3D() ? "" : " Points off its plane are projected onto it.");
            target += "\nCoordinates are relative to the model origin.";

            string path;
            LengthUnit unit;
            using (var dialog = new PointFileDialog(import: true, target))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                path = dialog.FilePath;
                unit = dialog.Unit;
            }

            PointCsvReadResult csv = PointCsv.Read(File.ReadAllText(path), unit);
            if (csv.Errors.Count > 0)
            {
                var sb = new StringBuilder($"{Path.GetFileName(path)} has {csv.Errors.Count} bad " +
                                           (csv.Errors.Count == 1 ? "line" : "lines") + ":");
                foreach (string error in csv.Errors.Take(MaxListedErrors))
                {
                    sb.Append("\n  ").Append(error);
                }
                if (csv.Errors.Count > MaxListedErrors)
                {
                    sb.Append($"\n  ...and {csv.Errors.Count - MaxListedErrors} more");
                }
                sb.Append("\n\nNothing was imported. Fix the file and try again.");
                Ui.Show(app, sb.ToString(), swMessageBoxIcon_e.swMbWarning);
                return;
            }
            if (csv.Points.Count == 0)
            {
                Ui.Show(app, $"{Path.GetFileName(path)} has no points.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            SketchPointIo.ImportResult r = SketchPointIo.Import(app, doc, csv.Points);
            Log.Info($"{Title}: {path} ({unit}), {csv.Points.Count} rows, added {r.Added} " +
                     $"to {r.SketchName} (new {r.NewSketch}), off-plane {r.OffPlane}");

            var message = new StringBuilder(
                $"Added {r.Added} {(r.Added == 1 ? "point" : "points")} to " +
                (r.NewSketch ? $"a new 3D sketch, {r.SketchName}." : $"{r.SketchName}."));
            if (r.Added < csv.Points.Count)
            {
                message.Append($"\n{csv.Points.Count - r.Added} could not be added.");
            }
            if (r.OffPlane > 0)
            {
                string was = r.OffPlane == 1 ? "was" : "were";
                message.Append($"\n{r.OffPlane} {was} not on the sketch plane and {was} projected onto it.");
            }
            Ui.Show(app, message.ToString(),
                r.Added < csv.Points.Count || r.OffPlane > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
        }
    }

    /// <summary>Shared bits of the point import and export commands.</summary>
    internal static class PointCommands
    {
        public static bool IsPartOrAssembly(ISldWorks app)
        {
            if (!(app.ActiveDoc is IModelDoc2 doc))
            {
                return false;
            }
            int type = doc.GetType();
            return type == (int)swDocumentTypes_e.swDocPART || type == (int)swDocumentTypes_e.swDocASSEMBLY;
        }

    }
}
