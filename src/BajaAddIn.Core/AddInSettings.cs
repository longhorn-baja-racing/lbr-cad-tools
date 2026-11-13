using System;
using System.Collections.Generic;
using System.Globalization;

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
                    default:
                        settings.unknown[pair.Key] = pair.Value;
                        break;
                }
            }
            return settings;
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
            };
            foreach (KeyValuePair<string, object> pair in unknown)
            {
                root[pair.Key] = pair.Value;
            }
            return Json.Write(root);
        }
    }
}
