using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.BatchExport;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Exports the active document, and optionally its components and their
    /// drawings, to STEP/IGES/Parasolid/STL/PDF/DXF in one go.
    /// </summary>
    internal sealed class BatchExportCommand : IAddInCommand
    {
        public string Title => "Batch Export";

        public string Hint => "Export this document and its parts to STEP, STL, PDF, DXF, and more in one go";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Saves copies of the open document, and optionally every part in it and their drawings, in the formats you tick: STEP, IGES, Parasolid, STL, PDF, or DXF. Use it to send files to a vendor, a 3D printer, or a teammate without SolidWorks.",
            Needs = "Open a saved part, assembly, or drawing.",
            Steps = new[]
            {
                "Save the document.",
                "Click Batch Export.",
                "Choose what to include: every unique component, Toolbox parts, and drawings with the same name.",
                "Tick the formats and pick the output folder (default: an Exports folder next to the file).",
                "Click Export.",
            },
            Notes = new[]
            {
                "Files are named by part number.",
                "Files with the same name in the output folder are replaced.",
            },
        };

        public ToolGroup Group => ToolGroup.Files;

        public ToolIcon Icon => ToolIcon.BatchExport;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => app.ActiveDoc is IModelDoc2;

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            string path = doc.GetPathName();
            DocKind? kind = BatchExporter.KindOf(path);
            if (kind == null)
            {
                Ui.Show(app, "Save the document first, then try again.", swMessageBoxIcon_e.swMbInformation);
                return;
            }

            int componentCount = kind == DocKind.Assembly ? BatchExporter.UniqueComponents(doc, out _).Count : 0;
            string defaultFolder = Path.Combine(Path.GetDirectoryName(path), "Exports");

            BatchExportOptions options;
            bool openFolder;
            using (var dialog = new BatchExportDialog(Path.GetFileName(path), kind.Value, componentCount, defaultFolder))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                options = dialog.Options;
                openFolder = dialog.OpenFolderWhenDone;
            }

            Log.Info($"{Title}: {path} -> {options.OutputFolder}, formats [{string.Join(", ", options.Formats)}], " +
                     $"components {options.IncludeComponents}, drawings {options.IncludeDrawings}, skip Toolbox {options.SkipToolbox}");
            BatchExportResult r = BatchExporter.Run(app, doc, options);
            Log.Info($"{Title}: wrote {r.Written.Count}, failed {r.Failed.Count}");
            foreach (string failure in r.Failed)
            {
                Log.Info("  failed: " + failure);
            }

            if (openFolder && r.Written.Count > 0)
            {
                Process.Start("explorer.exe", "\"" + options.OutputFolder + "\"");
            }
            Ui.Show(app,
                BatchExportReport.Format(options.OutputFolder, r.Written.Count, r.Failed, r.Notes),
                r.Failed.Count > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
        }
    }
}
