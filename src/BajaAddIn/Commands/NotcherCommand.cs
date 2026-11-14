using System.Collections.Generic;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.Notcher;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Ordered tube notcher for weldment frames (issue #11).</summary>
    internal sealed class NotcherCommand : IAddInCommand
    {
        public string Title => "Tube Notcher";

        public string Hint => "Cope every frame tube to fit the tubes it meets, in priority order";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Makes the fish-mouth (coped) cuts on a weldment frame so tubes fit where they meet. You set which " +
                      "member groups win (for example LFS over RRH over SM); the tool works out every joint and adds " +
                      "the Trim/Extend features in the right order.",
            Needs = "Open the frame part, built from weldment structural members.",
            Steps = new[]
            {
                "Open the frame part. Name each structural member feature after its group (LFS, RRH, SM, ...); the default order uses these names.",
                "Click Tube Notcher. It reads every tube and joint (a large frame can take a few seconds).",
                "Put the groups in priority order, most important on top. Check the Cuts list.",
                "Tick Swap on a node you want the other way round, then click Notch.",
            },
            Notes = new[]
            {
                "A tube that ends on the side of another tube is always the one coped; priority decides only where both end.",
                "The cuts are Trim/Extend features named \"LBR Notch - ...\". Run the tool again after changing the frame; " +
                "it deletes and remakes them.",
                "The order and swaps are saved in the part (custom properties), so the next run and your teammates start from them.",
                "Tubes in the same structural member feature are joined by that feature's own corner treatment, not by this tool.",
                "Two tubes that cross with neither ending there are reported, not cut.",
            },
        };

        public ToolGroup Group => ToolGroup.VehicleDesign;

        public ToolIcon Icon => ToolIcon.Notcher;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
        };

        // Cheap on purpose: SolidWorks and the task pane call this often. Execute checks for a weldment.
        public bool IsEnabled(ISldWorks app) =>
            app.ActiveDoc is IModelDoc2 doc && doc.GetType() == (int)swDocumentTypes_e.swDocPART;

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            if (!FrameNotcher.IsWeldmentPart(doc))
            {
                Ui.Show(app, "This part has no structural members. Open the frame part (a weldment built with Structural Member).",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            List<string> savedOrder = Notching.SplitOrder(FrameNotcher.ReadProperty(doc, Notching.OrderProperty));
            HashSet<string> savedSwaps = Notching.SplitSwaps(FrameNotcher.ReadProperty(doc, Notching.SwapProperty));

            FrameNotcher.Frame frame;
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                frame = FrameNotcher.Read(doc); // removes the last run's notches to measure the bare tubes
            }
            finally
            {
                Cursor.Current = previous;
            }
            List<string> order = Notching.MergeOrder(savedOrder, frame.Groups);
            Log.Info($"{Title}: {frame.Members.Count} tubes, {frame.Groups.Count} groups, {frame.Joints.Count} joints, " +
                     $"{frame.OldNotches} old notches removed");

            NotchPlan plan;
            using (var dialog = new NotcherDialog(frame, order, savedSwaps))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    if (frame.OldNotches > 0)
                    {
                        // Put the previous cuts back as they were.
                        FrameNotcher.Apply(doc, Notching.Plan(frame.Members, frame.Joints, order, savedSwaps));
                    }
                    return;
                }
                plan = dialog.Plan;
                FrameNotcher.WriteProperty(doc, Notching.OrderProperty, Notching.JoinOrder(dialog.Order));
                FrameNotcher.WriteProperty(doc, Notching.SwapProperty, Notching.JoinSwaps(dialog.Swaps));
            }

            Cursor.Current = Cursors.WaitCursor;
            (int made, List<string> problems) = FrameNotcher.Apply(doc, plan);
            Cursor.Current = previous;
            Log.Info($"{Title}: {made} notch feature(s), {problems.Count} problem(s)");
            Ui.Show(app,
                $"Made {made} notch feature(s) (\"{Notching.FeaturePrefix} - ...\" at the bottom of the tree)." +
                (problems.Count > 0 ? "\n\nProblems:\n  " + string.Join("\n  ", problems) : "") +
                "\n\nSave the part to keep them.",
                problems.Count > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
        }
    }
}
