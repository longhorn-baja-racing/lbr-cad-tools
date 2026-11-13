using System.Collections.Generic;
using Lbr.CadTools.AddIn.Features.AddInConfig;
using Lbr.CadTools.AddIn.Util;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Opens the settings window. Cannot be hidden through settings.json.</summary>
    internal sealed class SettingsCommand : IAddInCommand
    {
        private readonly AddIn addIn;

        public SettingsCommand(AddIn addIn)
        {
            this.addIn = addIn;
        }

        public string Title => "Settings";

        public string Hint => "Settings file, hidden buttons, and add-ins to load or unload at startup";

        public string IconText => "ST";

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            using (var dialog = new SettingsDialog(addIn))
            {
                dialog.ShowDialog(Ui.MainWindow(app));
            }
        }
    }
}
