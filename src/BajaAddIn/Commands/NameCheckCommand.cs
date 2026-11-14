using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.AddIn.Features.Naming;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Checks file names against the team naming convention (issue #6).</summary>
    internal sealed class NameCheckCommand : IAddInCommand
    {
        public string Title => "Name Check";

        public string Hint => "Check file names against the team convention and find the next free part number";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Checks the open document, and every file an assembly uses, against the team naming convention " +
                      NamingConvention.Pattern + ". It says what to fix in each name, shows the next free number for " +
                      "each team, and builds correct names to copy. It never renames anything.",
            Needs = "Open a saved part, assembly, or drawing.",
            Steps = new[]
            {
                "Open the top assembly (or a single part or drawing) and click Name Check.",
                "Read the Results: each file that breaks the convention says what to change.",
                "For a new file, fill in the Name builder and click Copy name, then paste it into Save As.",
            },
            Notes = new[]
            {
                "Example: aDYN_0006_ControlArmLeft_REV01_V02. \"a\" = assembly, \"d\" = drawing, no letter for parts.",
                "Every part in a REV assembly must have the same REV, except REVSTD standard parts (bolts, engine).",
                "Next free numbers come from the assembly and every SolidWorks file in its folder on Box.",
                "Rename files that assemblies use from inside SolidWorks so references follow: turn on Tools > Options > " +
                "FeatureManager > \"Allow component files to be renamed from FeatureManager tree\", then rename the component.",
                "Team codes are in Settings (\"naming\"): POW, CHA, ERG, DYN by default.",
            },
        };

        public ToolGroup Group => ToolGroup.Files;

        public ToolIcon Icon => ToolIcon.NameCheck;

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
            if (string.IsNullOrEmpty(doc.GetPathName()))
            {
                Ui.Show(app, "Save the document first; Name Check reads file names.", swMessageBoxIcon_e.swMbInformation);
                return;
            }

            AddInSettings settings = SettingsStore.Load();
            NameScanner.Result scan = NameScanner.Scan(doc);
            List<NameCheck> checks = NamingConvention.CheckAssembly(scan.Top, scan.Files, settings.Naming.Teams.Keys.ToList());
            Log.Info($"{Title}: {checks.Count} file(s), {checks.Count(c => !c.Ok)} to fix, {scan.Virtual.Count} virtual, " +
                     $"{scan.AllNames.Count} name(s) for numbering");

            using (var dialog = new NameCheckDialog(scan, checks, settings.Naming.Teams))
            {
                dialog.ShowDialog(Ui.MainWindow(app));
            }
        }
    }
}
