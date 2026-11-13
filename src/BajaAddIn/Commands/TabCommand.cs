using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Tabs;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Generates a B.12.2 mounting tab part (issue #7).</summary>
    internal sealed class TabCommand : IAddInCommand
    {
        public string Title => "Mounting Tab";

        public string Hint => "Create a welded bolt tab part sized from the Baja SAE B.12.2 table, with live rule checks";

        public string Usage =>
            "1. Pick what the tab holds (lap belt, anti-sub belt, seat, fire extinguisher, fuel tank). The rule sizes fill in.\n" +
            "2. Adjust sizes as needed. The rule check updates as you type.\n" +
            "3. Generate makes a new, unsaved part. Any rule warnings are also saved in its \"Tab Rule Check\" property.\n" +
            "Weld the full base line; no cutouts or lightening holes (B.12.2.2).";

        public ToolGroup Group => ToolGroup.VehicleDesign;

        public ToolIcon Icon => ToolIcon.MountingTab;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            TabInputs inputs;
            using (var dialog = new TabDialog())
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                inputs = dialog.Inputs;
            }

            IModelDoc2 doc = TabBuilder.Build(app, inputs);
            List<TabIssue> issues = TabRules.Check(inputs);
            Log.Info($"{Title}: {TabRules.Describe(inputs)}, {issues.Count} rule warning(s)");
            if (issues.Count > 0)
            {
                Ui.Show(app,
                    "Tab created, but it does not meet B.12.2:\n  " + string.Join("\n  ", issues.Select(i => i.Message)) +
                    "\n\nThe same list is in the part's \"Tab Rule Check\" property.",
                    swMessageBoxIcon_e.swMbWarning);
            }
        }
    }
}
