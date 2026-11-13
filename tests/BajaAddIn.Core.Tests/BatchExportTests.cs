using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class BatchExportTests
    {
        private static ExportItem Part(string path, string config = "Default", string pn = null) =>
            new ExportItem(path, config, DocKind.Part, pn);

        [Fact]
        public void Names_by_part_number_else_file_name()
        {
            var names = ExportNaming.AssignBaseNames(new[]
            {
                Part(@"C:\cad\Bracket.SLDPRT", pn: " LBR-1001 "),
                Part(@"C:\cad\Plate.SLDPRT"),
                Part(@"C:\cad\Tab.SLDPRT", pn: ""),
            });
            Assert.Equal(new[] { "LBR-1001", "Plate", "Tab" }, names);
        }

        [Fact]
        public void Adds_configuration_only_when_a_file_has_several()
        {
            var names = ExportNaming.AssignBaseNames(new[]
            {
                Part(@"C:\cad\Tube.SLDPRT", "Short"),
                Part(@"C:\cad\Tube.SLDPRT", "Long"),
                Part(@"C:\cad\Plate.SLDPRT", "Default"),
            });
            Assert.Equal(new[] { "Tube-Short", "Tube-Long", "Plate" }, names);
        }

        [Fact]
        public void Duplicate_names_get_a_counter_ignoring_case()
        {
            var names = ExportNaming.AssignBaseNames(new[]
            {
                Part(@"C:\a\Plate.SLDPRT"),
                Part(@"C:\b\plate.sldprt"),
                Part(@"C:\c\X.SLDPRT", pn: "PLATE"),
            });
            Assert.Equal(new[] { "Plate", "plate-2", "PLATE-3" }, names);
        }

        [Fact]
        public void Drawings_are_named_separately_from_models()
        {
            var names = ExportNaming.AssignBaseNames(new[]
            {
                Part(@"C:\cad\Plate.SLDPRT"),
                new ExportItem(@"C:\cad\Plate.SLDDRW", "", DocKind.Drawing, null),
            });
            Assert.Equal(new[] { "Plate", "Plate" }, names);
        }

        [Theory]
        [InlineData("A/B:C*D?", "A_B_C_D_")]
        [InlineData("name. ", "name")]
        [InlineData("\"x\"", "_x_")]
        [InlineData("   ", "_")]
        public void Sanitizes_file_names(string input, string expected)
        {
            Assert.Equal(expected, ExportNaming.Sanitize(input));
        }

        [Theory]
        [InlineData(@"C:\cad\Plate.v2.SLDPRT", "Plate.v2")]
        [InlineData("/home/x/Tab.sldprt", "Tab")]
        [InlineData("NoExt", "NoExt")]
        public void File_stem(string path, string expected)
        {
            Assert.Equal(expected, ExportNaming.FileStem(path));
        }

        [Fact]
        public void Formats_apply_to_the_right_documents()
        {
            Assert.True(ExportFormats.AppliesTo(ExportFormat.Step, DocKind.Assembly));
            Assert.False(ExportFormats.AppliesTo(ExportFormat.Step, DocKind.Drawing));
            Assert.True(ExportFormats.AppliesTo(ExportFormat.Pdf, DocKind.Drawing));
            Assert.False(ExportFormats.AppliesTo(ExportFormat.Pdf, DocKind.Part));
            Assert.True(ExportFormats.AppliesTo(ExportFormat.FlatPatternDxf, DocKind.Part));
            Assert.False(ExportFormats.AppliesTo(ExportFormat.FlatPatternDxf, DocKind.Assembly));
            Assert.All(ExportFormats.All, f => Assert.NotEmpty(ExportFormats.Suffix(f)));
            Assert.Equal(ExportFormats.All.Count, ExportFormats.All.Select(ExportFormats.Suffix).Distinct().Count());
        }

        [Fact]
        public void Report_counts_failures_and_notes()
        {
            string s = BatchExportReport.Format(@"C:\out", 5,
                Enumerable.Range(1, 12).Select(i => $"f{i}.step: error").ToList(),
                new[] { "Skipped 2 Toolbox parts." });
            Assert.StartsWith("Exported 5 files to\nC:\\out", s);
            Assert.Contains("12 exports failed:", s);
            Assert.Contains("f10.step", s);
            Assert.DoesNotContain("f11.step", s);
            Assert.Contains("...and 2 more", s);
            Assert.EndsWith("\n\nSkipped 2 Toolbox parts.", s);
        }

        [Fact]
        public void Report_nothing_and_singular()
        {
            Assert.Equal("Nothing to export with the chosen formats.", BatchExportReport.Format("x", 0, null, null));
            Assert.Equal("Exported 1 file to\nC:\\out", BatchExportReport.Format(@"C:\out", 1, null, null));
        }
    }
}
