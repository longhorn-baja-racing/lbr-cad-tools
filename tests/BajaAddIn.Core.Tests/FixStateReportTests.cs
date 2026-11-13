using System;
using System.Linq;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class FixStateReportTests
    {
        private static readonly string[] None = Array.Empty<string>();

        [Fact]
        public void EmptyAssembly()
        {
            Assert.Equal("The assembly has no top-level components.", FixStateReport.Format(true, 0, 0, None));
        }

        [Fact]
        public void FixSomeWithSomeAlreadyFixed()
        {
            string msg = FixStateReport.Format(true, 3, 2, None);
            Assert.StartsWith("Fixed 3 top-level components. 2 were already fixed.", msg);
            Assert.Contains("all configurations", msg);
        }

        [Fact]
        public void SingularWording()
        {
            Assert.StartsWith("Fixed 1 top-level component. 1 was already fixed.", FixStateReport.Format(true, 1, 1, None));
        }

        [Fact]
        public void AllAlreadyFloating()
        {
            Assert.Equal("All 4 top-level components are already floating.", FixStateReport.Format(false, 0, 4, None));
        }

        [Fact]
        public void FloatPointsToFixAllAndHasNoConfigurationNote()
        {
            string msg = FixStateReport.Format(false, 2, 0, None);
            Assert.Equal("Floated 2 top-level components.\n\nCtrl+Z cannot undo this. Use Fix All to reverse it.", msg);
        }

        [Fact]
        public void FixPointsToFloatAll()
        {
            Assert.Contains("Ctrl+Z cannot undo this. Use Float All to reverse it.", FixStateReport.Format(true, 1, 0, None));
        }

        [Fact]
        public void NoUndoNoteWhenNothingChanged()
        {
            Assert.DoesNotContain("Ctrl+Z", FixStateReport.Format(true, 0, 3, None));
        }

        [Fact]
        public void FailuresAreListedAndTruncated()
        {
            string[] failed = Enumerable.Range(1, 12).Select(i => $"Part{i}-1").ToArray();
            string msg = FixStateReport.Format(true, 0, 0, failed);
            Assert.Contains("Could not change 12 components:", msg);
            Assert.Contains("Part10-1", msg);
            Assert.DoesNotContain("Part11-1", msg);
            Assert.Contains("...and 2 more", msg);
        }

        [Fact]
        public void OutputIsAscii()
        {
            string msg = FixStateReport.Format(true, 5, 3, new[] { "Frame-1" });
            Assert.All(msg, ch => Assert.True(ch == '\n' || (ch >= 0x20 && ch <= 0x7E)));
        }

        [Fact]
        public void NegativeCountsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FixStateReport.Format(true, -1, 0, None));
        }
    }
}
