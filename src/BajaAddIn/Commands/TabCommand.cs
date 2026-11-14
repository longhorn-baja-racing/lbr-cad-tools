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

        public string Hint => "Make a welded bolt tab part sized to Baja SAE rule B.12.2";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Builds a new part for a tab that is welded to a frame tube and holds a bolt, for a seat belt, seat, fire extinguisher, or fuel tank. Sizes start at the rule book minimums for what it holds, and a rule check updates as you type.",
            Steps = new[]
            {
                "Optional: in an assembly, click the tube face the tab is welded to. The tab is then inserted there.",
                "Click Mounting Tab.",
                "Under Holds, pick what the tab is for. The rule sizes fill in.",
                "Adjust the sizes if needed and watch the Rule check box.",
                "Click Generate. Without a tube selected, a new, unsaved part opens.",
            },
            Notes = new[]
            {
                "Rule warnings are also saved in the part's \"Tab Rule Check\" custom property.",
                "With a tube selected, Tube OD is measured from it, and you choose whether the base runs along or across the tube.",
                "Inserted tabs are saved next to the assembly as Tab-<use>-<n>.SLDPRT and fixed in place.",
                "B.12.2.2: weld the full base line, and no cutouts or lightening holes.",
            },
        };

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
            // A tube face picked in an assembly turns on the Placement section.
            var assembly = app.ActiveDoc as IModelDoc2;
            TabMount mount = TabInserter.ReadSelection(app, out string componentName);
            TabDialog.PlacementOffer offer = mount == null ? null : new TabDialog.PlacementOffer
            {
                AssemblyTitle = assembly.GetTitle(),
                AssemblySaved = !string.IsNullOrEmpty(assembly.GetPathName()),
                ComponentName = componentName,
                Round = mount.IsRound,
                Diameter = mount.IsRound ? 2 * mount.Radius : 0,
            };

            TabInputs inputs;
            bool insert;
            TabOrientation orientation;
            using (var dialog = new TabDialog(offer))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                inputs = dialog.Inputs;
                insert = dialog.InsertIntoAssembly;
                orientation = dialog.Orientation;
            }

            IModelDoc2 doc = TabBuilder.Build(app, inputs);
            List<TabIssue> issues = TabRules.Check(inputs);
            Log.Info($"{Title}: {TabRules.Describe(inputs)}, {issues.Count} rule warning(s), insert={insert}");

            string placed = "";
            if (insert)
            {
                TabFrame frame = TabPlacement.Place(inputs, mount, orientation);
                IComponent2 component = TabInserter.Insert(app, assembly, doc, inputs.Use, frame);
                placed = $"Inserted {component.Name2} on {componentName ?? "the selected face"} and fixed it in place.\n" +
                         "To move it: right-click it > Float, drag, then Fix again.";
                Log.Info($"{Title}: inserted {component.Name2} ({TabPlacement.Effective(inputs, mount, orientation)})");
            }

            if (issues.Count > 0)
            {
                Ui.Show(app,
                    (placed.Length > 0 ? placed + "\n\n" : "") +
                    "The tab does not meet B.12.2:\n  " + string.Join("\n  ", issues.Select(i => i.Message)) +
                    "\n\nThe same list is in the part's \"Tab Rule Check\" property.",
                    swMessageBoxIcon_e.swMbWarning);
            }
            else if (placed.Length > 0)
            {
                Ui.Show(app, placed, swMessageBoxIcon_e.swMbInformation);
            }
        }
    }
}
