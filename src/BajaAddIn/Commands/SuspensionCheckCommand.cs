using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Suspension;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Static suspension numbers (camber, toe, caster, KPI, scrub, trail,
    /// Ackermann, turning radius) from HP_* reference points (issue #12).
    /// Move or steer the assembly and run it again; a live Task Pane is planned.
    /// </summary>
    internal sealed class SuspensionCheckCommand : IAddInCommand
    {
        public string Title => "Suspension Check";

        public string Hint => "Measure camber, toe, caster, KPI, scrub, trail, and Ackermann from HP_ points";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Measures the static suspension and steering geometry from named reference points: camber, toe, caster, kingpin inclination (KPI), scrub radius, trail, Ackermann, and turning radius.",
            Needs = "Open a part or an assembly with HP_ reference points.",
            Steps = new[]
            {
                "Add a reference point at each hardpoint and name it HP_<point>_<corner>, for example HP_UBJ_FL.",
                "Click Suspension Check to see the report.",
                "Move or steer the assembly and click it again to compare.",
            },
            Notes = new[]
            {
                "Points: UBJ upper ball joint, LBJ lower ball joint, WC wheel center, SA any point on the spin axis inboard of WC.",
                "Corners: FL, FR, RL, RR. A rear corner may have only WC (it locates the rear axle).",
                "Model axes and tire radius are set under \"suspension\" in Settings.",
            },
        };

        public ToolGroup Group => ToolGroup.VehicleDesign;

        public ToolIcon Icon => ToolIcon.SuspensionCheck;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) => PointCommands.IsPartOrAssembly(app);

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            AddInSettings settings = SettingsStore.Load();
            HardpointReader.Result read = HardpointReader.Read(app, doc);
            var problems = new List<string>(read.Problems);
            (List<CornerGeometry> corners, SteeringGeometry steering) = HardpointReader.Compute(read, settings.Suspension, problems);

            string text;
            if (corners.Count == 0)
            {
                text = "No complete corner found.\n\n" +
                       "Add reference points named HP_<point>_<corner>, for example HP_UBJ_FL:\n" +
                       "  UBJ = upper ball joint, LBJ = lower ball joint, WC = wheel center,\n" +
                       "  SA = any point on the spin axis, inboard of the wheel center.\n" +
                       "  Corners: FL, FR, RL, RR. A rear corner may have only WC (locates the rear axle).\n";
            }
            else
            {
                text = SuspensionMath.Report(corners, steering);
            }
            text += $"\n\nAxes: forward {settings.Suspension.ForwardAxis}, up {settings.Suspension.UpAxis}; " +
                    $"tire radius {settings.Suspension.TireRadiusInches:0.##} in (change under \"suspension\" in Settings). " +
                    $"Found {read.Points.Count} HP_ point(s).";
            if (problems.Count > 0)
            {
                text += "\n\nProblems:\n  " + string.Join("\n  ", problems);
            }
            Log.Info($"{Title}: {corners.Count} corner(s), {problems.Count} problem(s)");

            using (var form = new ReportForm(Title, text))
            {
                form.ShowDialog(Ui.MainWindow(app));
            }
        }

        /// <summary>Read-only monospaced report with Copy and Close.</summary>
        private sealed class ReportForm : Form
        {
            public ReportForm(string title, string text)
            {
                Text = title;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MinimizeBox = false;
                Font = SystemFonts.MessageBoxFont;
                Size = new Size(760, 420);

                var box = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Dock = DockStyle.Fill,
                    Font = new Font(FontFamily.GenericMonospace, 9.5f),
                    Text = text.Replace("\n", "\r\n"),
                };
                var copy = new Button { Text = "Copy", AutoSize = true };
                copy.Click += (s, e) => Clipboard.SetText(box.Text);
                var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
                buttons.Controls.Add(close);
                buttons.Controls.Add(copy);
                Controls.Add(box);
                Controls.Add(buttons);
                CancelButton = close;
                box.Select(0, 0);
            }
        }
    }
}
