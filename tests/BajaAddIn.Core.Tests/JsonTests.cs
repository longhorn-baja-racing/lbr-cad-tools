using System.Collections.Generic;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void Parses_all_value_types()
        {
            var obj = (Dictionary<string, object>)Json.Parse(
                "{ \"s\": \"a\\\"b\\\\c\\u0041\", \"n\": -1.5e2, \"t\": true, \"f\": false, \"z\": null, \"a\": [1, \"x\", []], \"o\": {} }");
            Assert.Equal("a\"b\\cA", obj["s"]);
            Assert.Equal(-150.0, obj["n"]);
            Assert.Equal(true, obj["t"]);
            Assert.Equal(false, obj["f"]);
            Assert.Null(obj["z"]);
            var a = (List<object>)obj["a"];
            Assert.Equal(1.0, a[0]);
            Assert.Equal("x", a[1]);
            Assert.Empty((List<object>)a[2]);
            Assert.Empty((Dictionary<string, object>)obj["o"]);
        }

        [Fact]
        public void Accepts_line_comments_bom_and_crlf()
        {
            var obj = (Dictionary<string, object>)Json.Parse("\uFEFF// settings\r\n{\r\n  \"a\": 1 // one\r\n}\r\n");
            Assert.Equal(1.0, obj["a"]);
        }

        [Theory]
        [InlineData("{\"a\": 1,}", "line 1")]
        [InlineData("{\n\"a\" 1}", "line 2, column 5")]
        [InlineData("{\"p\": \"C:\\Temp\"}", "write \\\\ for each \\")]
        [InlineData("[1, 2", "missing ']'")]
        [InlineData("{} x", "after the end")]
        [InlineData("\"abc", "Unterminated")]
        [InlineData("", "end of file")]
        public void Reports_errors_with_position(string text, string expected)
        {
            JsonException ex = Assert.Throws<JsonException>(() => Json.Parse(text));
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void Writes_indented_ascii_and_round_trips()
        {
            var value = new Dictionary<string, object>
            {
                ["path"] = "C:\\cad\\\u00e9.SLDPRT",
                ["n"] = 10,
                ["d"] = 0.1,
                ["list"] = new List<object> { "a", true, null },
                ["empty"] = new List<object>(),
            };
            string json = Json.Write(value);
            Assert.Equal(
                "{\n" +
                "  \"path\": \"C:\\\\cad\\\\\\u00e9.SLDPRT\",\n" +
                "  \"n\": 10,\n" +
                "  \"d\": 0.1,\n" +
                "  \"list\": [\n" +
                "    \"a\",\n" +
                "    true,\n" +
                "    null\n" +
                "  ],\n" +
                "  \"empty\": []\n" +
                "}\n", json);

            var back = (Dictionary<string, object>)Json.Parse(json);
            Assert.Equal("C:\\cad\\\u00e9.SLDPRT", back["path"]);
            Assert.Equal(10.0, back["n"]);
        }
    }

    public class AddInSettingsTests
    {
        [Fact]
        public void Defaults_when_empty()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse("", warnings);
            Assert.False(s.AutoSave.Enabled);
            Assert.Equal(10, s.AutoSave.IntervalMinutes);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Reads_auto_save()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse("{\"autoSave\": {\"enabled\": true, \"intervalMinutes\": 5}}", warnings);
            Assert.True(s.AutoSave.Enabled);
            Assert.Equal(5, s.AutoSave.IntervalMinutes);
            Assert.Empty(warnings);
        }

        [Theory]
        [InlineData("{\"autoSave\": {\"enabled\": \"yes\"}}", "enabled")]
        [InlineData("{\"autoSave\": {\"intervalMinutes\": 0}}", "intervalMinutes")]
        [InlineData("{\"autoSave\": {\"intervalMinutes\": 2.5}}", "intervalMinutes")]
        [InlineData("{\"autoSave\": 3}", "must be an object")]
        [InlineData("[1]", "JSON object")]
        public void Bad_values_warn_and_keep_defaults(string json, string expected)
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse(json, warnings);
            Assert.Contains(warnings, w => w.Contains(expected));
            Assert.False(s.AutoSave.Enabled);
            Assert.Equal(10, s.AutoSave.IntervalMinutes);
        }

        [Fact]
        public void Keeps_unknown_sections_when_written_back()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse("{\"future\": {\"x\": 1}, \"autoSave\": {\"enabled\": true}}", warnings);
            s.AutoSave.IntervalMinutes = 500;
            string json = s.ToJson();
            Assert.Contains("\"future\"", json);
            AddInSettings back = AddInSettings.Parse(json, warnings);
            Assert.True(back.AutoSave.Enabled);
            Assert.Equal(AutoSaveSettings.MaxIntervalMinutes, back.AutoSave.IntervalMinutes);
            Assert.Empty(warnings);
        }
    }

    public class AutoSavePolicyTests
    {
        [Fact]
        public void Saves_only_visible_modified_writable_saved_documents()
        {
            var docs = new[]
            {
                new OpenDocInfo(@"C:\a.SLDPRT", true, false, true),   // 0 save
                new OpenDocInfo(@"C:\b.SLDPRT", true, false, false),  // not modified
                new OpenDocInfo(@"C:\c.SLDPRT", false, false, true),  // hidden component
                new OpenDocInfo(@"C:\d.SLDPRT", true, true, true),    // read-only
                new OpenDocInfo("", true, false, true),               // never saved
                new OpenDocInfo(@"C:\e.SLDASM", true, false, true),   // 5 save
            };
            Assert.Equal(new[] { 0, 5 }, AutoSavePolicy.ToSave(docs));
            Assert.Equal(1, AutoSavePolicy.NeverSaved(docs));
        }

        [Fact]
        public void Status_text()
        {
            var t = new System.DateTime(2026, 11, 13, 14, 5, 0);
            Assert.Equal("Auto-saved 2 documents at 14:05", AutoSavePolicy.Status(t, 2, null, 0));
            Assert.Equal("Auto-save 14:05: nothing to save; 1 new document needs a first Save",
                AutoSavePolicy.Status(t, 0, new string[0], 1));
            Assert.Equal("Auto-saved 1 document at 14:05; FAILED: a.SLDPRT (see log)",
                AutoSavePolicy.Status(t, 1, new[] { "a.SLDPRT" }, 0));
        }
    }
}
