using System.Collections.Generic;
using Lbr.CadTools.AddIn.Features.FixComponents;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Fix All / Float All buttons. Logic lives in <see cref="ComponentFixer"/>.</summary>
    internal sealed class SetFixedStateCommand : IAddInCommand
    {
        private readonly bool fix;

        public SetFixedStateCommand(bool fix)
        {
            this.fix = fix;
        }

        public string Title => fix ? "Fix All" : "Float All";

        public string Hint => fix
            ? "Lock every top-level component in place, in all configurations"
            : "Free every top-level component so it can be dragged, in all configurations";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = fix
                ? "Fixes every top-level component of the open assembly, in every configuration, so nothing moves while you mate, measure, or take screenshots."
                : "Floats every top-level component of the open assembly, in every configuration, so you can drag parts around to check motion and clearance.",
            Needs = "Open an assembly.",
            Steps = new[]
            {
                "Open the assembly (.SLDASM).",
                fix ? "Click Fix All." : "Click Float All.",
                "Read the summary. It counts what changed and lists anything that could not be changed.",
            },
            Notes = new[]
            {
                "Ctrl+Z cannot undo this. Use Restore Fix States to put every component back.",
                "Only top-level components change. Parts inside subassemblies, and the subassembly files, are left alone.",
            },
        };

        public ToolGroup Group => ToolGroup.Assembly;

        public ToolIcon Icon => fix ? ToolIcon.FixAll : ToolIcon.FloatAll;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) =>
            app.ActiveDoc is IModelDoc2 doc && doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;

        public void Execute(ISldWorks app)
        {
            ComponentFixer.Result r = ComponentFixer.Apply((IModelDoc2)app.ActiveDoc, fix);
            Log.Info($"{Title}: {r.TopLevel} top-level, changed {r.Changed}, " +
                     $"unchanged {r.Unchanged}, failed {r.Failed.Count}");

            app.SendMsgToUser2(
                FixStateReport.Format(fix, r.Changed, r.Unchanged, r.Failed),
                (int)(r.Failed.Count > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation),
                (int)swMessageBoxBtn_e.swMbOk);
        }
    }
}
