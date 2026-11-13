using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lbr.CadTools.Core;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Loads and saves %LOCALAPPDATA%\LBR CAD Tools\settings.json. A missing or
    /// broken file never stops the add-in: it falls back to defaults and logs why.
    /// </summary>
    internal static class SettingsStore
    {
        public static string FilePath { get; } = Path.Combine(Log.DataDirectory, "settings.json");

        /// <summary>Problems found by the last <see cref="Load"/>, for the user to see.</summary>
        public static List<string> LastWarnings { get; } = new List<string>();

        public static AddInSettings Load()
        {
            LastWarnings.Clear();
            if (!File.Exists(FilePath))
            {
                return new AddInSettings();
            }
            try
            {
                AddInSettings settings = AddInSettings.Parse(File.ReadAllText(FilePath), LastWarnings);
                foreach (string w in LastWarnings)
                {
                    Log.Info("settings.json: " + w);
                }
                return settings;
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                LastWarnings.Add($"{FilePath} could not be read, so defaults are used: {ex.Message}");
                Log.Error("settings.json not loaded", ex);
                return new AddInSettings();
            }
        }

        /// <summary>
        /// Writes the settings. Refuses to overwrite a file that is not valid
        /// JSON, so a hand edit with a typo is not silently thrown away.
        /// </summary>
        public static void Save(AddInSettings settings)
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    Json.Parse(File.ReadAllText(FilePath));
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"{FilePath} has a JSON error, so it was not overwritten. Fix or delete it, then try again.\n{ex.Message}");
                }
            }
            Directory.CreateDirectory(Log.DataDirectory);
            File.WriteAllText(FilePath, settings.ToJson(), new UTF8Encoding(false));
        }
    }
}
