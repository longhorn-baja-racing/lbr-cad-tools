using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Suspension;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Live suspension numbers while the model moves (issue #12).</summary>
    internal sealed class MonitorCommand : IAddInCommand
    {
        private static MonitorForm open;

        public string Title => "Suspension Monitor";

        public string Hint => "Live camber, toe, caster, KPI, scrub, trail, and steering while you drag or steer the suspension";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Keeps a small window open next to the model with the suspension numbers from the HP_ points, " +
                      "updating as you drag the wheel through travel, steer, or change a dimension. Set a baseline to " +
                      "read changes directly, for example camber change and bump steer for a given travel.",
            Needs = "Open the suspension part or assembly with HP_ reference points.",
            Steps = new[]
            {
                "Set up HP_ reference points as for Suspension Check (HP_UBJ_FL, HP_LBJ_FL, HP_WC_FL, HP_SA_FL, ...).",
                "Click Suspension Monitor. The window stays open while you work.",
                "Click Set baseline at ride height, then drag the wheel or steer: each number shows its change.",
                "After adding or renaming points, click Find points again.",
            },
            Notes = new[]
            {
                "It follows the document that was active when it opened; open it again for another model.",
                "Updates up to about ten times a second while the view redraws, and once a second otherwise.",
                "For whole-travel curves and CSV, use Suspension Sweep.",
            },
        };

        public ToolGroup Group => ToolGroup.VehicleDesign;

        public ToolIcon Icon => ToolIcon.SuspensionMonitor;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            if (open != null && !open.IsDisposed)
            {
                open.Close(); // one monitor at a time; reopen on the active document
            }

            var monitor = new SuspensionMonitor(doc, SettingsStore.Load().Suspension);
            open = new MonitorForm(monitor);
            open.FormClosed += (s, e) => open = null;

            // Top right of the SolidWorks window, where the task pane usually is.
            IWin32Window owner = Ui.MainWindow(app);
            Rectangle area = Screen.FromHandle(owner.Handle).WorkingArea;
            open.Show(owner);
            open.Location = new Point(area.Right - open.Width - 360, area.Top + 140);
            monitor.Start();
            Log.Info($"{Title}: watching {monitor.Title}");
        }
    }
}
