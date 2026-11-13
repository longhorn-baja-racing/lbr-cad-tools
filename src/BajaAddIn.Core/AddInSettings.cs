using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lbr.CadTools.Core
{
    public sealed class AutoSaveSettings
    {
        public const int MinIntervalMinutes = 1;
        public const int MaxIntervalMinutes = 120;
        public const int DefaultIntervalMinutes = 10;

        public bool Enabled { get; set; }

        private int intervalMinutes = DefaultIntervalMinutes;

        /// <summary>Clamped to <see cref="MinIntervalMinutes"/>..<see cref="MaxIntervalMinutes"/>.</summary>
        public int IntervalMinutes
        {
            get => intervalMinutes;
            set => intervalMinutes = Math.Max(MinIntervalMinutes, Math.Min(MaxIntervalMinutes, value));
        }
    }

    /// <summary>
    /// The add-in settings file (settings.json). Reading is forgiving: a bad or
    /// missing value falls back to its default with a warning, and sections
    /// this version does not know are kept when the file is written back.
    /// </summary>
    public sealed class AddInSettings
    {
        private readonly Dictionary<string, object> unknown = new Dictionary<string, object>();

        public AutoSaveSettings AutoSave { get; } = new AutoSaveSettings();

        /// <summary>Titles of LBR CAD Tools buttons to leave off the tab (applies at the next SolidWorks start).</summary>
        public List<string> HiddenCommands { get; } = new List<string>();

        /// <summary>Other add-ins to load at startup: titles from Tools > Add-Ins, or DLL paths.</summary>
        public List<string> LoadAddIns { get; } = new List<string>();

        /// <summary>Other add-ins to unload at startup: titles from Tools > Add-Ins, or DLL paths.</summary>
        public List<string> UnloadAddIns { get; } = new List<string>();

        /// <summary>Parses settings; problems go to <paramref name="warnings"/>.</summary>
        /// <exception cref="JsonException">The text is not valid JSON.</exception>
        public static AddInSettings Parse(string json, List<string> warnings)
        {
            var settings = new AddInSettings();
            if (string.IsNullOrWhiteSpace(json))
            {
                return settings;
            }
            if (!(Json.Parse(json) is Dictionary<string, object> root))
            {
                warnings.Add("The settings file must be a JSON object ({ ... }); using defaults.");
                return settings;
            }

            foreach (KeyValuePair<string, object> pair in root)
            {
                switch (pair.Key)
                {
                    case "autoSave":
                        ReadAutoSave(pair.Value, settings.AutoSave, warnings);
                        break;
                    case "commands":
                        if (Section(pair.Value, "commands", warnings) is Dictionary<string, object> commands)
                        {
                            ReadStrings(commands, "hidden", "commands.hidden", settings.HiddenCommands, warnings);
                        }
                        break;
                    case "addIns":
                        if (Section(pair.Value, "addIns", warnings) is Dictionary<string, object> addIns)
                        {
                            ReadStrings(addIns, "load", "addIns.load", settings.LoadAddIns, warnings);
                            ReadStrings(addIns, "unload", "addIns.unload", settings.UnloadAddIns, warnings);
                            foreach (string both in settings.LoadAddIns.Intersect(settings.UnloadAddIns, StringComparer.OrdinalIgnoreCase).ToList())
                            {
                                warnings.Add($"\"{both}\" is in both addIns.load and addIns.unload; it is left alone.");
                                settings.LoadAddIns.RemoveAll(s => string.Equals(s, both, StringComparison.OrdinalIgnoreCase));
                                settings.UnloadAddIns.RemoveAll(s => string.Equals(s, both, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                        break;
                    default:
                        settings.unknown[pair.Key] = pair.Value;
                        break;
                }
            }
            return settings;
        }

        private static object Section(object value, string name, List<string> warnings)
        {
            if (value is Dictionary<string, object>)
            {
                return value;
            }
            warnings.Add($"\"{name}\" must be an object; it is ignored.");
            return null;
        }

        private static void ReadStrings(Dictionary<string, object> obj, string key, string name, List<string> target,
                                        List<string> warnings)
        {
            if (!obj.TryGetValue(key, out object value) || value == null)
            {
                return;
            }
            if (!(value is List<object> list))
            {
                warnings.Add($"\"{name}\" must be a list like [\"a\", \"b\"]; it is ignored.");
                return;
            }
            foreach (object item in list)
            {
                if (item is string s && s.Trim().Length > 0)
                {
                    if (!target.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        target.Add(s.Trim());
                    }
                }
                else
                {
                    warnings.Add($"\"{name}\" should only hold text; skipped {Json.Write(item).Trim()}.");
                }
            }
        }

        private static void ReadAutoSave(object value, AutoSaveSettings target, List<string> warnings)
        {
            if (!(value is Dictionary<string, object> obj))
            {
                warnings.Add("\"autoSave\" must be an object; using defaults.");
                return;
            }
            if (obj.TryGetValue("enabled", out object enabled))
            {
                if (enabled is bool b)
                {
                    target.Enabled = b;
                }
                else
                {
                    warnings.Add("\"autoSave.enabled\" must be true or false.");
                }
            }
            if (obj.TryGetValue("intervalMinutes", out object interval))
            {
                if (interval is double d && d == Math.Floor(d)
                    && d >= AutoSaveSettings.MinIntervalMinutes && d <= AutoSaveSettings.MaxIntervalMinutes)
                {
                    target.IntervalMinutes = (int)d;
                }
                else
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "\"autoSave.intervalMinutes\" must be a whole number from {0} to {1}; using {2}.",
                        AutoSaveSettings.MinIntervalMinutes, AutoSaveSettings.MaxIntervalMinutes, target.IntervalMinutes));
                }
            }
        }

        public string ToJson()
        {
            var root = new Dictionary<string, object>
            {
                ["autoSave"] = new Dictionary<string, object>
                {
                    ["enabled"] = AutoSave.Enabled,
                    ["intervalMinutes"] = AutoSave.IntervalMinutes,
                },
                ["commands"] = new Dictionary<string, object>
                {
                    ["hidden"] = HiddenCommands.Cast<object>().ToList(),
                },
                ["addIns"] = new Dictionary<string, object>
                {
                    ["load"] = LoadAddIns.Cast<object>().ToList(),
                    ["unload"] = UnloadAddIns.Cast<object>().ToList(),
                },
            };
            foreach (KeyValuePair<string, object> pair in unknown)
            {
                root[pair.Key] = pair.Value;
            }
            return Json.Write(root);
        }
    }
}
