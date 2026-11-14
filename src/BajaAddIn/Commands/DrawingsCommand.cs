using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Drawings;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Starts drawings for a part or an assembly and its parts (issue #13).</summary>
    internal sealed class DrawingsCommand : IAddInCommand
    {
        public string Title => "Make Drawings";

        public string Hint => "Start a drawing with front, top, right, and isometric views for this part, or every part of an assembly";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Creates the drawing you would start by hand: front, top, and right views (third angle) plus an " +
                      "isometric, at a standard scale that fits the sheet, with the part number, description, and " +
                      "revision filled in from the file name. Add the remaining dimensions and notes yourself.",
            Needs = "Open a saved part or assembly.",
            Steps = new[]
            {
                "Open the part (or the assembly) and click Make Drawings.",
                "For an assembly, choose whether to also draw every part in it.",
                "Click Make. Drawings are saved next to each model, named d + the model name (dCHA_0002_SeatBracket_REV01_V01).",
            },
            Notes = new[]
            {
                "Uses your default drawing template (Tools > Options > Default Templates) and its sheet size and title block.",
                "Title block properties written to the drawing: PartNo, Description, Revision, Version.",
                "Dimensions marked for drawing in the part (most sketch and feature dimensions) are imported when the box is ticked. " +
                "Expect to tidy them.",
                "Existing drawings are left alone unless you tick Replace. Models are never changed.",
                "Toolbox, REVSTD, virtual, and suppressed parts are skipped when drawing a whole assembly.",
            },
        };

        public ToolGroup Group => ToolGroup.Files;

        public ToolIcon Icon => ToolIcon.Drawings;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            string path = doc.GetPathName();
            if (string.IsNullOrEmpty(path))
            {
                Ui.Show(app, "Save the model first; the drawing is saved next to it.", swMessageBoxIcon_e.swMbInformation);
                return;
            }
            bool assembly = doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            int skipped = 0;
            List<string> parts = assembly ? DrawingMaker.PartsOf(doc, out skipped) : new List<string>();
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);

            var targets = new List<string> { path };
            var options = new DrawingMaker.Options();
            using (var dialog = new Dialog(Path.GetFileNameWithoutExtension(path), assembly, parts.Count, skipped, template))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                if (dialog.IncludeParts)
                {
                    targets.AddRange(parts);
                }
                options.ImportDimensions = dialog.ImportDimensions;
                options.Replace = dialog.Replace;
            }

            Cursor.Current = Cursors.WaitCursor;
            // Open the drawing at the end only when there is one; a batch is left closed on disk.
            DrawingMaker.Result r = DrawingMaker.Make(app, targets, options, keepLastOpen: targets.Count == 1);
            Cursor.Current = Cursors.Default;
            Log.Info($"{Title}: made {r.Made.Count}, existing {r.Existing.Count}, failed {r.Failed.Count}");

            var lines = new List<string>();
            if (r.Made.Count > 0)
            {
                lines.Add($"Made {r.Made.Count} drawing(s) next to the models:\n  " + string.Join("\n  ", r.Made.Take(15)) +
                          (r.Made.Count > 15 ? $"\n  ... and {r.Made.Count - 15} more" : ""));
            }
            if (r.Existing.Count > 0)
            {
                lines.Add($"Left alone, already exist ({r.Existing.Count}): " + string.Join(", ", r.Existing.Take(10)) +
                          (r.Existing.Count > 10 ? ", ..." : "") + ". Tick Replace to remake them.");
            }
            if (r.Failed.Count > 0)
            {
                lines.Add("Failed:\n  " + string.Join("\n  ", r.Failed));
            }
            Ui.Show(app, lines.Count == 0 ? "Nothing to do." : string.Join("\n\n", lines),
                r.Failed.Count > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
        }

        private sealed class Dialog : ToolForm
        {
            private readonly CheckBox includeParts = new CheckBox { AutoSize = true, Checked = true };
            private readonly CheckBox dimensions = new CheckBox { Text = "Import the part's dimensions (those marked for drawing)", AutoSize = true, Checked = true };
            private readonly CheckBox replace = new CheckBox { Text = "Replace drawings that already exist", AutoSize = true };

            public Dialog(string model, bool assembly, int parts, int skipped, string template)
                : base("Make Drawings",
                    "Front, top, right, and isometric views at a standard scale, with the title block filled in from the file name.",
                    ToolIcon.Drawings, ToolGroup.Files, 520)
            {
                SuspendLayout();
                var what = new List<Control>
                {
                    new Label { Text = model + (assembly ? " (assembly)" : ""), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 2, 0, 4) },
                };
                if (assembly)
                {
                    includeParts.Text = $"Also draw every part it uses ({parts} part(s))";
                    includeParts.Enabled = parts > 0;
                    includeParts.Checked = parts > 0;
                    what.Add(includeParts);
                    if (skipped > 0)
                    {
                        what.Add(Note($"{skipped} Toolbox, REVSTD, virtual, or suppressed part(s) are skipped.", ContentWidth));
                    }
                }
                AddSection("What to draw", null, what.ToArray());

                Label templateNote = Note("Template: " + (string.IsNullOrEmpty(template) ? "(none set)" : Path.GetFileName(template)) +
                                          ". Change it in Tools > Options > Default Templates.", ContentWidth);
                templateNote.Margin = new Padding(0, 6, 0, 0);
                AddSection("Options", null, dimensions, replace, templateNote);

                Button make = AddButton("Make", DialogResult.OK, primary: true);
                Button cancel = AddButton("Cancel", DialogResult.Cancel);
                AcceptButton = make;
                CancelButton = cancel;
                ResumeLayout(true);
            }

            public bool IncludeParts => includeParts.Enabled && includeParts.Checked;

            public bool ImportDimensions => dimensions.Checked;

            public bool Replace => replace.Checked;
        }
    }
}
