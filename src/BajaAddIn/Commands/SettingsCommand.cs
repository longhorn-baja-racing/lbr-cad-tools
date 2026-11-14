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

        public string Hint => "Hide buttons, auto-load other add-ins, and check settings.json";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Shows the settings file, what each setting means, and any problems found in it. Settings can hide buttons you do not use and load or unload other add-ins when SolidWorks starts.",
            Steps = new[]
            {
                "Click Settings.",
                "Click Edit in Notepad, change the file, and save it.",
                "Click Apply now to re-read it.",
            },
            Notes = new[]
            {
                "Hidden buttons change at the next SolidWorks start.",
                "The window lists the add-in titles on this computer, for the load and unload lists.",
            },
        };

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
