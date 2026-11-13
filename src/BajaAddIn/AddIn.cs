using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Lbr.CadTools.AddIn.Commands;
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

        // List order is button order. Changing the list rebuilds the command
        // group on the next load (see AddCommandGroup).
        private static List<IAddInCommand> CreateCommands() => new List<IAddInCommand>
        {
            new SetFixedStateCommand(fix: true),
            new SetFixedStateCommand(fix: false),
            new RestoreFixStatesCommand(),
            new ImportPointsCommand(),
            new ExportPointsCommand(),
            new BatchExportCommand(),
            new AboutCommand(),
        };

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

                commands = CreateCommands();
                AddCommandGroup();
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

        // ---- Command callbacks (called by SolidWorks by name; must stay public) ----

        /// <summary>Click handler. <paramref name="data"/> is the command index.</summary>
        public void OnCommand(string data)
        {
            IAddInCommand command = Lookup(data);
            if (command == null)
            {
                return;
            }
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
            try
            {
                return command != null && command.IsEnabled(app) ? 1 : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private IAddInCommand Lookup(string data) =>
            int.TryParse(data, out int index) && index >= 0 && index < commands.Count
                ? commands[index]
                : null;

        // ---- Command tab ----

        private void AddCommandGroup()
        {
            int[] userIds = Enumerable.Range(0, commands.Count).ToArray();

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
            group.IconList = IconStrips.Create(iconDir, "commands", commands.Select(c => c.IconText).ToList());
            group.MainIconList = IconStrips.Create(iconDir, "main", new[] { "LBR" });
            Log.Info($"Icons written to {iconDir}");

            var itemType = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);
            var itemIndexes = new int[commands.Count];
            for (int i = 0; i < commands.Count; i++)
            {
                IAddInCommand c = commands[i];
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

                int[] ids = Enumerable.Range(0, commands.Count)
                    .Where(i => commands[i].DocumentTypes.Contains(docType))
                    .Select(i => group.get_CommandID(itemIndexes[i]))
                    .ToArray();
                if (ids.Length == 0)
                {
                    continue;
                }

                CommandTab tab = commandManager.AddCommandTab((int)docType, Title);
                if (tab == null)
                {
                    Log.Error($"{docType}: AddCommandTab returned null");
                    continue;
                }
                CommandTabBox box = tab.AddCommandTabBox();
                int[] textTypes = Enumerable
                    .Repeat((int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow, ids.Length)
                    .ToArray();
                bool added = box.AddCommands(ids, textTypes);
                Log.Info($"{docType}: tab created, command IDs [{string.Join(", ", ids)}], AddCommands -> {added}");
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
