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

        public string Hint => "Save modified documents automatically every few minutes; click to turn it on or off and set the interval";

        public string Usage =>
            "Saves the real files, not recovery copies, so there is no going back to the last manual save.\n" +
            "Skips documents that were never saved or are read-only, and waits while a command, PropertyManager, or sketch is open.";

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
