using System.Collections.Generic;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class AddInConfigTests
    {
        [Fact]
        public void Reads_hidden_commands_and_add_in_lists()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse(
                "{ \"commands\": { \"hidden\": [\"Auto-Save\", \" Batch Export \", \"auto-save\"] },\n" +
                "  \"addIns\": { \"load\": [\"Presentation Manager\"], \"unload\": [\"C:\\\\x\\\\y.dll\"] } }",
                warnings);
            Assert.Empty(warnings);
            Assert.Equal(new[] { "Auto-Save", "Batch Export" }, s.HiddenCommands);
            Assert.Equal(new[] { "Presentation Manager" }, s.LoadAddIns);
            Assert.Equal(new[] { @"C:\x\y.dll" }, s.UnloadAddIns);
        }

        [Fact]
        public void Bad_entries_warn()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse(
                "{ \"commands\": [], \"addIns\": { \"load\": \"A\", \"unload\": [\"B\", 3, \"\"] } }", warnings);
            Assert.Contains(warnings, w => w.Contains("\"commands\" must be an object"));
            Assert.Contains(warnings, w => w.Contains("\"addIns.load\" must be a list"));
            Assert.Contains(warnings, w => w.Contains("skipped 3"));
            Assert.Contains(warnings, w => w.Contains("skipped \"\""));
            Assert.Empty(s.LoadAddIns);
            Assert.Equal(new[] { "B" }, s.UnloadAddIns);
        }

        [Fact]
        public void Entry_in_both_lists_is_dropped()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse("{ \"addIns\": { \"load\": [\"A\", \"B\"], \"unload\": [\"a\"] } }", warnings);
            Assert.Equal(new[] { "B" }, s.LoadAddIns);
            Assert.Empty(s.UnloadAddIns);
            Assert.Contains(warnings, w => w.Contains("both"));
        }

        [Fact]
        public void Writes_all_sections_so_users_can_find_them()
        {
            string json = new AddInSettings().ToJson();
            Assert.Contains("\"hidden\": []", json);
            Assert.Contains("\"load\": []", json);
            Assert.Contains("\"unload\": []", json);

            var s = new AddInSettings();
            s.HiddenCommands.Add("Fix All");
            s.LoadAddIns.Add(@"C:\a.dll");
            var warnings = new List<string>();
            AddInSettings back = AddInSettings.Parse(s.ToJson(), warnings);
            Assert.Empty(warnings);
            Assert.Equal(new[] { "Fix All" }, back.HiddenCommands);
            Assert.Equal(new[] { @"C:\a.dll" }, back.LoadAddIns);
        }
    }
}
