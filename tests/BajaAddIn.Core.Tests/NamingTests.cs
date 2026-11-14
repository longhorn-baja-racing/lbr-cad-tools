using System.Collections.Generic;
using System.Linq;
using Lbr.CadTools.Core;
using Xunit;

namespace Lbr.CadTools.Core.Tests
{
    public class NamingTests
    {
        private static readonly ICollection<string> Teams = NamingConvention.DefaultTeams.Keys.ToList();

        private static NameCheck One(string name, CadFileKind kind) =>
            NamingConvention.CheckOne(new NamedFile(name, kind), Teams);

        [Theory]
        [InlineData("aDYN_0006_SuspensionControlArmLeft_REV01_V02", CadFileKind.Assembly)]
        [InlineData("POW_0012_CVTEngine_REVSTD", CadFileKind.Part)]
        [InlineData("dCHA_0001_ChassisFrameLeft_REV02_V5", CadFileKind.Drawing)]
        [InlineData("ERG_0003_PedalTab_REV01_V01", CadFileKind.Part)]
        public void Examples_from_the_convention_pass(string name, CadFileKind kind)
        {
            NameCheck c = One(name, kind);
            Assert.True(c.Ok, string.Join(" | ", c.Problems));
        }

        [Fact]
        public void Parses_every_field()
        {
            CadName n = NamingConvention.Parse("aDYN_0006_SuspensionControlArmLeft_REV01_V02");
            Assert.Equal("a", n.Prefix);
            Assert.Equal("DYN", n.Team);
            Assert.Equal(6, n.Number);
            Assert.Equal("SuspensionControlArmLeft", n.PartName);
            Assert.Equal(1, n.Revision);
            Assert.Equal(2, n.Version);
            Assert.Equal("DYN_0006", n.Key);
            Assert.Equal("aDYN_0006_SuspensionControlArmLeft_REV01_V02", n.ToString());

            CadName std = NamingConvention.Parse("POW_0012_CVTEngine_REVSTD");
            Assert.True(std.IsStandard);
            Assert.Null(std.Version);
        }

        [Fact]
        public void Format_pads_numbers()
        {
            Assert.Equal("aCHA_0042_FrontHoop_REV03_V07", NamingConvention.Format(CadFileKind.Assembly, "CHA", 42, "FrontHoop", 3, 7));
            Assert.Equal("POW_0001_Bolt_REVSTD", NamingConvention.Format(CadFileKind.Part, "POW", 1, "Bolt", null, null));
        }

        [Theory]
        [InlineData("CHA_0001_Frame_REV01_V01", CadFileKind.Assembly, "start with \"a\"")]
        [InlineData("aCHA_0001_Frame_REV01_V01", CadFileKind.Part, "Parts have no letter")]
        [InlineData("CHA_0001_Frame_REV01_V01", CadFileKind.Drawing, "start with \"d\"")]
        [InlineData("XYZ_0001_Frame_REV01_V01", CadFileKind.Part, "Team code XYZ")]
        [InlineData("CHA_0001_Frame_REV01", CadFileKind.Part, "Add the version")]
        [InlineData("CHA_1_Frame_REV01_V01", CadFileKind.Part, "4-digit number")]
        [InlineData("cha_0001_Frame_REV01_V01", CadFileKind.Part, "capitals")]
        [InlineData("CHA_0001_Front-Hoop_REV01_V01", CadFileKind.Part, "letters and digits only")]
        [InlineData("CHA_0001_Frame_R1_V01", CadFileKind.Part, "REV##")]
        [InlineData("CHA_0001_Frame_REV01_Version1", CadFileKind.Part, "version, e.g. V01")]
        [InlineData("Front Hoop", CadFileKind.Part, "No spaces")]
        [InlineData("Part1", CadFileKind.Part, "Does not follow")]
        [InlineData("CHA_0001_Front_Hoop_REV01_V01", CadFileKind.Part, "Too many underscores")]
        public void Explains_what_is_wrong(string name, CadFileKind kind, string expected)
        {
            NameCheck c = One(name, kind);
            Assert.False(c.Ok);
            Assert.Contains(c.Problems, p => p.Contains(expected));
        }

        [Fact]
        public void Assembly_check_flags_revision_mismatch_but_not_standard_parts()
        {
            var top = new NamedFile("aCHA_0001_Frame_REV02_V01", CadFileKind.Assembly);
            List<NameCheck> checks = NamingConvention.CheckAssembly(top, new[]
            {
                new NamedFile("CHA_0002_FrontHoop_REV02_V03", CadFileKind.Part),
                new NamedFile("CHA_0003_RearHoop_REV01_V01", CadFileKind.Part),
                new NamedFile("POW_0012_CVTEngine_REVSTD", CadFileKind.Part),
            }, Teams);
            Assert.Equal(4, checks.Count);
            Assert.True(checks.Single(c => c.File.BaseName.Contains("FrontHoop")).Ok);
            Assert.True(checks.Single(c => c.File.BaseName.Contains("CVTEngine")).Ok);
            Assert.Contains(checks.Single(c => c.File.BaseName.Contains("RearHoop")).Problems, p => p.Contains("REV01 does not match"));
        }

        [Fact]
        public void Assembly_check_flags_a_number_used_twice()
        {
            var top = new NamedFile("aERG_0001_PedalBox_REV01_V01", CadFileKind.Assembly);
            List<NameCheck> checks = NamingConvention.CheckAssembly(top, new[]
            {
                new NamedFile("ERG_0002_Pedal_REV01_V01", CadFileKind.Part),
                new NamedFile("ERG_0002_Spring_REV01_V01", CadFileKind.Part),
                new NamedFile("CHA_0002_Spring_REV01_V01", CadFileKind.Part), // other team: fine
            }, Teams);
            Assert.Contains(checks.Single(c => c.File.BaseName.Contains("Pedal_")).Problems, p => p.Contains("each team number can be used once"));
            Assert.Contains(checks.Single(c => c.File.BaseName.StartsWith("ERG_0002_Spring")).Problems, p => p.Contains("also used for Pedal"));
            Assert.True(checks.Single(c => c.File.BaseName.StartsWith("CHA")).Ok);
        }

        [Fact]
        public void Assembly_check_flags_two_versions_of_one_part_but_not_its_drawing()
        {
            var top = new NamedFile("aERG_0001_PedalBox_REV01_V01", CadFileKind.Assembly);
            List<NameCheck> checks = NamingConvention.CheckAssembly(top, new[]
            {
                new NamedFile("ERG_0002_Pedal_REV01_V01", CadFileKind.Part),
                new NamedFile("ERG_0002_Pedal_REV01_V02", CadFileKind.Part),
                new NamedFile("dERG_0002_Pedal_REV01_V02", CadFileKind.Drawing),
            }, Teams);
            Assert.All(checks.Where(c => c.File.Kind == CadFileKind.Part), c => Assert.Contains(c.Problems, p => p.Contains("More than one")));
            Assert.True(checks.Single(c => c.File.Kind == CadFileKind.Drawing).Ok);
            Assert.True(checks[0].Ok); // the assembly itself, number 0001
        }

        [Fact]
        public void Next_number_is_one_above_the_highest_for_that_team()
        {
            string[] names =
            {
                "ERG_0002_Pedal_REV01_V01", "aERG_0007_PedalBox_REV01_V01", "CHA_0040_Frame_REV01_V01", "random file",
                "erg_0009_Spring_REV01_V01", "dERG_0011_Old",
            };
            Assert.Equal(12, NamingConvention.NextNumber("ERG", names)); // misnamed files still hold their numbers
            Assert.Equal(41, NamingConvention.NextNumber("CHA", names));
            Assert.Equal(1, NamingConvention.NextNumber("DYN", names));
        }

        [Theory]
        [InlineData("front hoop left", "FrontHoopLeft")]
        [InlineData("seat-belt tab", "SeatBeltTab")]
        [InlineData("  CVT engine ", "CVTEngine")]
        public void Part_name_from_free_text(string text, string expected)
        {
            Assert.Equal(expected, NamingConvention.ToPartName(text));
        }

        [Fact]
        public void Settings_read_and_write_team_codes()
        {
            var warnings = new List<string>();
            AddInSettings s = AddInSettings.Parse("{ \"naming\": { \"teams\": { \"BRK\": \"Brakes\", \"bad\": \"x\", \"CHA\": \"Chassis\" } } }", warnings);
            Assert.Equal(new[] { "BRK", "CHA" }, s.Naming.Teams.Keys);
            Assert.Single(warnings);

            AddInSettings back = AddInSettings.Parse(s.ToJson(), warnings = new List<string>());
            Assert.Empty(warnings);
            Assert.Equal("Brakes", back.Naming.Teams["BRK"]);

            Assert.Equal(new[] { "CHA", "DYN", "ERG", "POW" }, AddInSettings.Parse("{}", warnings).Naming.Teams.Keys);
        }
    }
}
