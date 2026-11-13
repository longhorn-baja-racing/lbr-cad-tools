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

        public string Hint => "Hide buttons, load or unload other add-ins at startup, and check settings.json for problems";

        public string Usage =>
            "Opens settings.json in Notepad, lists problems in it, and shows the add-in titles on this computer.\n" +
            "Apply now re-reads the file. Hidden buttons change at the next SolidWorks start.";

        public ToolGroup Group => ToolGroup.Help;

        public ToolIcon Icon => ToolIcon.Settings;

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
