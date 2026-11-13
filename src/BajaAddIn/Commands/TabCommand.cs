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

        public string Hint => "Make a single or double shear mounting tab part sized and checked against Baja SAE B.12.2";

        public string IconText => "TB";

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
