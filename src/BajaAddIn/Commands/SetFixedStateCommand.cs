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
            ? "Fix every top-level component of this assembly in place, in all configurations. Undo with Restore Fix States"
            : "Float every top-level component of this assembly so it can move, in all configurations. Undo with Restore Fix States";

        public string Usage => (fix
            ? "Open an assembly and click Fix All. Every top-level component is fixed in all configurations.\n"
            : "Open an assembly and click Float All. Every top-level component floats in all configurations.\n") +
            "Parts inside subassemblies, and the subassembly files, are not changed. A summary lists anything that could not be changed.";

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
