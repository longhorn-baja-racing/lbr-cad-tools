using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Commands;
using Lbr.CadTools.AddIn.Features.AddInConfig;
using Lbr.CadTools.AddIn.Features.AutoSave;
using Lbr.CadTools.AddIn.Features.TaskPane;
using Lbr.CadTools.Core;
using Lbr.CadTools.AddIn.Util;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace Lbr.CadTools.AddIn
{
    /// <summary>
    /// SolidWorks entry point. Registered with RegAsm (see scripts/register.ps1),
    /// loaded by SolidWorks, and builds the LBR CAD Tools command tab.
    /// </summary>
    [ComVisible(true)]
    [Guid("1fd92743-7a43-4c1a-8e05-bcd0ef590911")]
    public sealed class AddIn : ISwAddin
    {
        public const string Title = "LBR CAD Tools";
        private const string Description = "Longhorn Baja Racing CAD tools";
        private const int MainCommandGroupId = 1;

        private static readonly swDocumentTypes_e[] TabDocumentTypes =
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        private ISldWorks app;
        private ICommandManager commandManager;
        private int cookie;
        private List<IAddInCommand> commands = new List<IAddInCommand>();
        private int[] commandUserIds = new int[0];
        private AutoSaver autoSaver;
        private System.Windows.Forms.Timer startupTimer;
        private TaskpaneView taskPaneView;
        private ToolPane toolPane;

        /// <summary>Registry form of the class GUID, e.g. "{1fd92743-...}".</summary>
        internal static string Clsid => "{" + typeof(AddIn).GUID + "}";

        /// <summary>Problems from the last settings load and add-in config run, for the Settings window.</summary>
        internal List<string> SettingsProblems { get; } = new List<string>();

        internal AutoSaver AutoSaver => autoSaver;

        // Each command's index here is its stable user ID: append new commands,
        // do not reorder. Buttons are shown by section (ToolGroups.Order), then
        // in this order. Changing the shown list rebuilds the command group on
        // the next load (see AddCommandGroup).
        internal List<IAddInCommand> AllCommands() => new List<IAddInCommand>
        {
            new SetFixedStateCommand(fix: true),
            new SetFixedStateCommand(fix: false),
            new RestoreFixStatesCommand(),
            new ImportPointsCommand(),
            new ExportPointsCommand(),
            new BatchExportCommand(),
            new TabCommand(),
            new SuspensionCheckCommand(),
            new AutoSaveCommand(() => autoSaver),
            new SettingsCommand(this),
            new AboutCommand(),
            new GuideCommand(() => commands, Run),
            new NameCheckCommand(),
            new NotcherCommand(),
        };

        /// <summary>All commands minus those hidden in settings.json. Settings can never be hidden.</summary>
        private void CreateCommands(AddInSettings settings)
        {
            List<IAddInCommand> all = AllCommands();
            var shown = new List<(IAddInCommand Command, int Id)>();
            for (int i = 0; i < all.Count; i++)
            {
                bool hidden = !(all[i] is SettingsCommand)
                              && settings.HiddenCommands.Contains(all[i].Title, StringComparer.OrdinalIgnoreCase);
                if (!hidden)
                {
                    shown.Add((all[i], i));
                }
            }
            // OrderBy is stable, so commands keep their list order inside a section.
            shown = shown.OrderBy(c => ToolGroups.Order.ToList().IndexOf(c.Command.Group)).ToList();
            commands = shown.Select(c => c.Command).ToList();
            commandUserIds = shown.Select(c => c.Id).ToArray();
            Log.Info($"Commands shown: {commands.Count} of {all.Count}");
        }

        /// <summary>Settings problems that are about command names.</summary>
        internal IEnumerable<string> UnknownHiddenCommands(AddInSettings settings)
        {
            List<string> titles = AllCommands().Select(c => c.Title).ToList();
            return settings.HiddenCommands
                .Where(h => !titles.Contains(h, StringComparer.OrdinalIgnoreCase))
                .Select(h => $"commands.hidden: no button called \"{h}\". Buttons: {string.Join(", ", titles)}.");
        }

        // ---- ISwAddin ----

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            Log.Restart();
            try
            {
                app = (ISldWorks)ThisSW;
                cookie = Cookie;
                Log.Info($"ConnectToSW: {Title} {typeof(AddIn).Assembly.GetName().Version}, " +
                         $"SolidWorks {app.RevisionNumber()}, cookie {cookie}");

                bool callbacks = app.SetAddinCallbackInfo2(0, this, cookie);
                Log.Info($"SetAddinCallbackInfo2 -> {callbacks}");

                commandManager = app.GetCommandManager(cookie);
                Log.Info($"GetCommandManager -> {(commandManager == null ? "null" : "ok")}");

                AddInSettings settings = SettingsStore.Load();
                SettingsProblems.Clear();
                SettingsProblems.AddRange(SettingsStore.LastWarnings);
                SettingsProblems.AddRange(UnknownHiddenCommands(settings));

                CreateCommands(settings);
                AddCommandGroup();
                AddTaskPane();
                StartAutoSave(settings);
                ScheduleAddInConfig(settings);
                Log.Info("ConnectToSW: done");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("ConnectToSW failed", ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            Log.Info("DisconnectFromSW");
            autoSaver?.Dispose();
            autoSaver = null;
            startupTimer?.Dispose();
            startupTimer = null;
            RemoveTaskPane();
            try
            {
                RemoveCommandGroup();
            }
            catch (Exception ex)
            {
                Log.Error("RemoveCommandGroup failed", ex);
            }

            if (commandManager != null)
            {
                Marshal.ReleaseComObject(commandManager);
                commandManager = null;
            }
            if (app != null)
            {
                Marshal.ReleaseComObject(app);
                app = null;
            }

            // The add-in must drop every COM reference before SolidWorks unloads it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        private void StartAutoSave(AddInSettings settings)
        {
            try
            {
                autoSaver = new AutoSaver(app);
                autoSaver.Apply(settings.AutoSave);
            }
            catch (Exception ex)
            {
                Log.Error("Auto-save not started", ex);
            }
        }

        /// <summary>
        /// Loads and unloads other add-ins a moment after startup, from the
        /// SolidWorks message loop, rather than from inside ConnectToSW.
        /// </summary>
        private void ScheduleAddInConfig(AddInSettings settings)
        {
            if (settings.LoadAddIns.Count == 0 && settings.UnloadAddIns.Count == 0)
            {
                return;
            }
            startupTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            startupTimer.Tick += (s, e) =>
            {
                startupTimer.Stop();
                ApplyAddInConfig(settings);
                if (SettingsProblems.Count > 0 && app.Frame() is IFrame frame)
                {
                    frame.SetStatusBarText($"{Title}: {SettingsProblems.Count} settings problem(s); open Settings on the {Title} tab");
                }
            };
            startupTimer.Start();
        }

        /// <summary>Runs the add-in load/unload lists; problems are added to <see cref="SettingsProblems"/>.</summary>
        internal void ApplyAddInConfig(AddInSettings settings)
        {
            try
            {
                SettingsProblems.AddRange(AddInManager.Apply(app, settings, Clsid));
            }
            catch (Exception ex)
            {
                Log.Error("Add-in config failed", ex);
                SettingsProblems.Add("Add-in load/unload failed: " + ex.Message);
            }
        }

        /// <summary>Re-reads settings.json and applies what can change without a restart.</summary>
        internal void ReloadSettings()
        {
            AddInSettings settings = SettingsStore.Load();
            SettingsProblems.Clear();
            SettingsProblems.AddRange(SettingsStore.LastWarnings);
            SettingsProblems.AddRange(UnknownHiddenCommands(settings));
            autoSaver?.Apply(settings.AutoSave);
            ApplyAddInConfig(settings);
        }

        // ---- Command callbacks (called by SolidWorks by name; must stay public) ----

        /// <summary>Click handler. <paramref name="data"/> is the command index.</summary>
        public void OnCommand(string data)
        {
            IAddInCommand command = Lookup(data);
            if (command != null)
            {
                Run(command);
            }
        }

        /// <summary>Runs a command from a button, the task pane, or the Tool Guide.</summary>
        private void Run(IAddInCommand command)
        {
            try
            {
                command.Execute(app);
            }
            catch (Exception ex)
            {
                // Exceptions must not cross the COM boundary into SolidWorks.
                Log.Error($"Command '{command.Title}' failed", ex);
                app.SendMsgToUser2(
                    $"{command.Title} failed:\n{ex.Message}",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        /// <summary>Enable-state handler: 1 = enabled, 0 = disabled.</summary>
        public int OnCommandEnable(string data)
        {
            IAddInCommand command = Lookup(data);
            return command != null && IsEnabled(command) ? 1 : 0;
        }

        private bool IsEnabled(IAddInCommand command)
        {
            try
            {
                return app != null && command.IsEnabled(app);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- Task pane ----

        /// <summary>
        /// Adds the LBR CAD Tools task pane: the same tools as the tab, with
        /// section headings and a description for each.
        /// </summary>
        private void AddTaskPane()
        {
            try
            {
                string iconDir = Path.Combine(Log.DataDirectory, "icons");
                string[] icons = IconStrips.Create(iconDir, "pane", new Action<Graphics, Rectangle>[]
                {
                    (g, cell) =>
                    {
                        using (Bitmap icon = Brand.ProductIcon(cell.Width))
                        {
                            g.DrawImage(icon, cell.X, cell.Y);
                        }
                    },
                });
                taskPaneView = app.CreateTaskpaneView3(icons, Title);
                if (taskPaneView == null)
                {
                    Log.Error("CreateTaskpaneView3 returned null");
                    return;
                }
                GuideCommand guide = commands.OfType<GuideCommand>().FirstOrDefault()
                                     ?? new GuideCommand(() => commands, Run);
                toolPane = new ToolPane(commands, IsEnabled, Run, c => guide.Show(app, c));
                toolPane.CreateControl();
                bool shown = taskPaneView.DisplayWindowFromHandlex64(toolPane.Handle.ToInt64());
                Log.Info($"Task pane added, DisplayWindowFromHandlex64 -> {shown}");
            }
            catch (Exception ex)
            {
                Log.Error("Task pane not added", ex);
            }
        }

        private void RemoveTaskPane()
        {
            try
            {
                taskPaneView?.DeleteView();
            }
            catch (Exception ex)
            {
                Log.Error("Task pane DeleteView failed", ex);
            }
            if (taskPaneView != null)
            {
                Marshal.ReleaseComObject(taskPaneView);
                taskPaneView = null;
            }
            toolPane?.Dispose();
            toolPane = null;
        }

        private IAddInCommand Lookup(string data) =>
            int.TryParse(data, out int index) && index >= 0 && index < commands.Count
                ? commands[index]
                : null;

        // ---- Command tab ----

        private void AddCommandGroup()
        {
            int[] userIds = commandUserIds;

            // Rebuild the group when the command list changed since the last load.
            bool ignorePrevious = true;
            if (commandManager.GetGroupDataFromRegistry(MainCommandGroupId, out object registryIds)
                && registryIds is int[] previous)
            {
                ignorePrevious = !previous.SequenceEqual(userIds);
            }

            Log.Info($"Command group: ignorePrevious={ignorePrevious}");

            int errors = 0;
            CommandGroup group = commandManager.CreateCommandGroup2(
                MainCommandGroupId, Title, Description, Description, -1, ignorePrevious, ref errors);
            Log.Info($"CreateCommandGroup2 -> {(group == null ? "null" : "ok")}, " +
                     $"status={(swCreateCommandGroupErrors)errors}");
            if (group == null)
            {
                return;
            }

            // Toolbar buttons (and so tab buttons) need icons.
            string iconDir = Path.Combine(Log.DataDirectory, "icons");
            group.IconList = IconStrips.Create(iconDir, "commands", commands
                .Select(c => (Action<Graphics, Rectangle>)((g, cell) => ToolIcons.Draw(g, cell, c.Icon, ToolGroups.Color(c.Group))))
                .ToList());
            group.MainIconList = IconStrips.Create(iconDir, "main", new Action<Graphics, Rectangle>[]
            {
                (g, cell) =>
                {
                    using (Bitmap icon = Brand.ProductIcon(cell.Width))
                    {
                        g.DrawImage(icon, cell.X, cell.Y);
                    }
                },
            });
            Log.Info($"Icons written to {iconDir}");

            var itemType = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);
            var itemIndexes = new int[commands.Count];
            for (int i = 0; i < commands.Count; i++)
            {
                IAddInCommand c = commands[i];
                if (i > 0 && commands[i - 1].Group != c.Group)
                {
                    group.AddSpacer2(-1, itemType); // separator between sections in the menu and toolbar
                }
                itemIndexes[i] = group.AddCommandItem2(
                    c.Title, -1, c.Hint, c.Title, i,
                    $"{nameof(OnCommand)}({i})", $"{nameof(OnCommandEnable)}({i})",
                    userIds[i], itemType);
                Log.Info($"AddCommandItem2 '{c.Title}' -> index {itemIndexes[i]}");
            }

            group.HasToolbar = true;
            group.HasMenu = true;
            bool activated = group.Activate();
            Log.Info($"CommandGroup.Activate -> {activated}");

            foreach (swDocumentTypes_e docType in TabDocumentTypes)
            {
                CommandTab existing = commandManager.GetCommandTab((int)docType, Title);
                if (existing != null)
                {
                    bool removed = commandManager.RemoveCommandTab(existing);
                    Log.Info($"{docType}: removed old tab -> {removed}");
                }

                if (!commands.Any(c => c.DocumentTypes.Contains(docType)))
                {
                    continue;
                }
                CommandTab tab = commandManager.AddCommandTab((int)docType, Title);
                if (tab == null)
                {
                    Log.Error($"{docType}: AddCommandTab returned null");
                    continue;
                }

                // One box per section; SolidWorks draws a divider between boxes.
                foreach (ToolGroup section in ToolGroups.Order)
                {
                    int[] ids = Enumerable.Range(0, commands.Count)
                        .Where(i => commands[i].Group == section && commands[i].DocumentTypes.Contains(docType))
                        .Select(i => group.get_CommandID(itemIndexes[i]))
                        .ToArray();
                    if (ids.Length == 0)
                    {
                        continue;
                    }
                    var display = ToolGroups.SmallButtons(section)
                        ? swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal
                        : swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
                    CommandTabBox box = tab.AddCommandTabBox();
                    bool added = box.AddCommands(ids, Enumerable.Repeat((int)display, ids.Length).ToArray());
                    Log.Info($"{docType}: {ToolGroups.Name(section)} box, command IDs [{string.Join(", ", ids)}], AddCommands -> {added}");
                }
            }
        }

        private void RemoveCommandGroup()
        {
            if (commandManager == null)
            {
                return;
            }
            foreach (swDocumentTypes_e docType in TabDocumentTypes)
            {
                CommandTab tab = commandManager.GetCommandTab((int)docType, Title);
                if (tab != null)
                {
                    commandManager.RemoveCommandTab(tab);
                }
            }
            commandManager.RemoveCommandGroup(MainCommandGroupId);
        }

        // ---- COM registration (RegAsm only; never runs during a build) ----

        private static string AddInKey(Type t) => $@"SOFTWARE\SolidWorks\AddIns\{{{t.GUID}}}";

        private static string StartupKey(Type t) => $@"Software\SolidWorks\AddInsStartup\{{{t.GUID}}}";

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            // HKLM needs an Administrator prompt.
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(AddInKey(t)))
            {
                key.SetValue(null, 1);
                key.SetValue("Title", Title);
                key.SetValue("Description", Description);
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey(t)))
            {
                key.SetValue(null, 1);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            Registry.LocalMachine.DeleteSubKey(AddInKey(t), false);
            Registry.CurrentUser.DeleteSubKey(StartupKey(t), false);
        }
    }
}
