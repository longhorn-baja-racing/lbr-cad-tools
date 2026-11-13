using System.Collections.Generic;
using Lbr.CadTools.AddIn.Features.FixComponents;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Undo for Fix All / Float All, which Ctrl+Z cannot undo. Enabled only
    /// after one of them changed something in the active assembly.
    /// </summary>
    internal sealed class RestoreFixStatesCommand : IAddInCommand
    {
        public string Title => "Restore Fix States";

        public string Hint => "Undo the last Fix All or Float All in the active assembly";

        public string IconText => "RS";

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocASSEMBLY,
        };

        public bool IsEnabled(ISldWorks app) =>
            app.ActiveDoc is IModelDoc2 doc
            && doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY
            && ComponentFixer.History.ContainsKey(ComponentFixer.Key(doc));

        public void Execute(ISldWorks app)
        {
            var doc = (IModelDoc2)app.ActiveDoc;
            var r = ComponentFixer.Restore(doc);
            if (r == null)
            {
                Ui.Show(app, "Nothing to restore in this assembly.", swMessageBoxIcon_e.swMbInformation);
                return;
            }
            (bool lastWasFix, int restored, int skipped) = r.Value;
            Log.Info($"{Title}: last was {(lastWasFix ? "Fix All" : "Float All")}, restored {restored}, skipped {skipped}");
            Ui.Show(app, FixStateReport.FormatRestore(lastWasFix, restored, skipped),
                skipped > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
        }
    }
}
