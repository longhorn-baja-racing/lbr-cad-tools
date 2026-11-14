using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Lbr.CadTools.AddIn.Features.AutoSave;
using Lbr.CadTools.AddIn.Util;
using Lbr.CadTools.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Opens the Auto-Save settings; the saving itself is <see cref="AutoSaver"/>.</summary>
    internal sealed class AutoSaveCommand : IAddInCommand
    {
        private readonly Func<AutoSaver> saver;

        public AutoSaveCommand(Func<AutoSaver> saver)
        {
            this.saver = saver;
        }

        public string Title => "Auto-Save";

        public string Hint => "Save changed documents automatically every few minutes";

        public ToolHelp Help => new ToolHelp
        {
            Purpose = "Saves your open, changed documents on a timer, so a crash costs minutes instead of hours.",
            Steps = new[]
            {
                "Click Auto-Save.",
                "Turn it on and choose how often it saves.",
                "Click OK. It keeps running in the background until you turn it off.",
            },
            Notes = new[]
            {
                "It saves the real files, not recovery copies, so you cannot go back to your last manual save.",
                "Skips documents that were never saved or are read-only.",
                "Waits while a command, PropertyManager, or sketch is open.",
            },
        };

        public ToolGroup Group => ToolGroup.Files;

        public ToolIcon Icon => ToolIcon.AutoSave;

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            AddInSettings settings = SettingsStore.Load();
            AutoSaver autoSaver = saver();
            using (var dialog = new AutoSaveDialog(settings.AutoSave, autoSaver?.LastStatus))
            {
                if (dialog.ShowDialog(Ui.MainWindow(app)) != DialogResult.OK)
                {
                    return;
                }
                settings.AutoSave.Enabled = dialog.AutoSaveEnabled;
                settings.AutoSave.IntervalMinutes = dialog.IntervalMinutes;
            }

            SettingsStore.Save(settings);
            autoSaver?.Apply(settings.AutoSave);
            if (app.Frame() is IFrame frame)
            {
                frame.SetStatusBarText(settings.AutoSave.Enabled
                    ? $"Auto-save on, every {settings.AutoSave.IntervalMinutes} min"
                    : "Auto-save off");
            }
        }
    }
}
