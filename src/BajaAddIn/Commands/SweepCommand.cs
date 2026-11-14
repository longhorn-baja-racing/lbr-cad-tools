using System.Collections.Generic;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Suspension;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Suspension travel sweep (issue #12).</summary>
    internal sealed class SweepCommand : IAddInCommand
    {
        public string Title => "Suspension Sweep";

        public string Hint => "Step a dimension through its travel and chart camber gain and bump steer from HP_ points";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Moves the suspension through its travel by stepping one dimension (a shock length, an arm angle, " +
                      "a mate distance), and records camber, toe, caster, KPI, scrub, and trail at every step. Shows camber " +
                      "gain and bump steer per inch of wheel travel, charts them, and saves the table as CSV.",
            Needs = "Open the suspension model with HP_ points, then click the dimension that moves it.",
            Steps = new[]
            {
                "Set up HP_ reference points as for Suspension Check (HP_UBJ_FL, HP_LBJ_FL, HP_WC_FL, HP_SA_FL, ...).",
                "Double-click the feature or sketch so its dimensions show, then click the dimension that moves the suspension.",
                "Click Suspension Sweep, set From, To, and the number of steps, then Run sweep.",
                "Read the summary and charts; Save CSV for a spreadsheet.",
            },
            Notes = new[]
            {
                "The model must actually move when that dimension changes: a sketch that solves the linkage, or mates in an assembly.",
                "The dimension is put back to its starting value afterwards, even if a step fails.",
                "Wheel travel is measured from the starting position; bump (wheel up) is positive.",
                "Use a driving dimension, not a reference (driven) one.",
            },
        };

        public ToolGroup Group => ToolGroup.VehicleDesign;

        public ToolIcon Icon => ToolIcon.SuspensionSweep;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            SuspensionSweeper.Driver driver = SuspensionSweeper.Selected(doc);
            if (driver == null)
            {
                Ui.Show(app,
                    "Click the dimension that moves the suspension first (double-click a feature or sketch to show its " +
                    "dimensions), then click Suspension Sweep.",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            List<double> values;
            string unit;
            System.Func<double, double> toDisplay;
            using (var setup = new SweepSetupDialog(driver))
            {
                if (setup.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                values = setup.Values;
                unit = setup.UnitText;
                toDisplay = setup.ToDisplay;
            }

            AddInSettings settings = SettingsStore.Load();
            IFrame frame = app.Frame() as IFrame;
            Cursor.Current = Cursors.WaitCursor;
            SuspensionSweeper.Result result = SuspensionSweeper.Run(app, doc, driver, values, settings.Suspension,
                i => frame?.SetStatusBarText($"{Title}: step {i + 1} of {values.Count}"));
            Cursor.Current = Cursors.Default;
            frame?.SetStatusBarText("");
            Log.Info($"{Title}: {driver.Name}, {result.Steps.Count} of {values.Count} steps, {result.Problems.Count} problem(s)");

            if (result.Steps.Count == 0)
            {
                Ui.Show(app, "No steps could be measured.\n\n" + string.Join("\n", result.Problems), swMessageBoxIcon_e.swMbWarning);
                return;
            }
            using (var form = new SweepResultForm(driver, result, unit, toDisplay))
            {
                form.ShowDialog(Ui.MainWindow(app));
            }
        }
    }
}
