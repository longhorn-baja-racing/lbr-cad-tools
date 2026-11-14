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
    /// Exports every point of a sketch (the open one, or one selected in the
    /// FeatureManager tree) to CSV in model coordinates.
    /// </summary>
    internal sealed class ExportPointsCommand : IAddInCommand
    {
        public string Title => "Export Points";

        public string Hint => "Save every point of a sketch to a CSV file";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Writes the X, Y, Z position of every point in a sketch to a CSV file, in model coordinates. Use it to take hardpoints into Excel, Google Sheets, or another program.",
            Needs = "Open a part or an assembly, then open or select a sketch.",
            Steps = new[]
            {
                "Open a sketch for editing, or click one in the FeatureManager design tree (the feature list on the left).",
                "Click Export Points.",
                "Choose the file and the units, then click Export.",
            },
            Notes = new[]
            {
                "Line and arc end points are included; the sketch origin is not.",
            },
        };

        public ToolGroup Group => ToolGroup.SketchPoints;

        public ToolIcon Icon => ToolIcon.ExportPoints;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            ISketch sketch = SketchPointIo.FindSketch(doc);
            if (sketch == null)
            {
                Ui.Show(app,
                    "Open a sketch for editing, or select one in the FeatureManager design tree, then try again.",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            string sketchName = ((IFeature)sketch).Name;
            List<Point3> points = SketchPointIo.Export(app, sketch);
            if (points.Count == 0)
            {
                Ui.Show(app, $"{sketchName} has no points.", swMessageBoxIcon_e.swMbInformation);
                return;
            }

            string target = $"Exports {points.Count} {(points.Count == 1 ? "point" : "points")} from {sketchName}, " +
                            "including line and arc end points.\nCoordinates are relative to the model origin.";
            string suggested = SafeFileName($"{Path.GetFileNameWithoutExtension(doc.GetTitle())} {sketchName}.csv");

            string path;
            LengthUnit unit;
            using (var dialog = new PointFileDialog(import: false, target, suggested))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                path = dialog.FilePath;
                unit = dialog.Unit;
            }

            File.WriteAllText(path, PointCsv.Write(points, unit), new UTF8Encoding(false));
            Log.Info($"{Title}: {points.Count} points from {sketchName} to {path} ({unit})");
            Ui.Show(app,
                $"Exported {points.Count} {(points.Count == 1 ? "point" : "points")} from {sketchName} to\n{path}",
                swMessageBoxIcon_e.swMbInformation);
        }

        private static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
