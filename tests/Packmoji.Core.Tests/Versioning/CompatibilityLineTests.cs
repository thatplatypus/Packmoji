using Packmoji.Core.Tests.TestSupport;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Versioning
{
    public sealed class CompatibilityLineTests
    {
        [Theory]
        [InlineData("1.0.0", "1.x")]
        [InlineData("1.9.3", "1.x")]
        [InlineData("2.0.0-rc.1", "2.x")]
        [InlineData("0.4.1", "0.4.x")]
        [InlineData("0.0.3", "0.0.x")]
        public void A_version_is_on_the_line_of_its_major_or_below_one_of_its_minor(string version, string line) =>
            CompatibilityLine.Of(Sample.Version(version)).ToString().ShouldBe(line);

        [Theory]
        [InlineData("1.2.3", "1.9.0", true)]
        [InlineData("1.2.3", "2.0.0", false)]
        [InlineData("1.0.0", "1.0.0-beta.1", true)]
        [InlineData("0.4.1", "0.4.9", true)]
        [InlineData("0.4.1", "0.5.0", false)]
        [InlineData("0.0.3", "0.0.9", true)]
        [InlineData("0.9.0", "1.0.0", false)]
        public void Two_versions_share_a_line_or_do_not(string left, string right, bool same)
        {
            var line = CompatibilityLine.Of(Sample.Version(left));

            line.Contains(Sample.Version(right)).ShouldBe(same);
            (line == CompatibilityLine.Of(Sample.Version(right))).ShouldBe(same);
        }

        [Fact]
        public void Only_a_line_below_one_has_a_minor()
        {
            CompatibilityLine.Of(Sample.Version("0.4.1")).Minor.ShouldBe(4);
            CompatibilityLine.Of(Sample.Version("3.4.1")).Minor.ShouldBeNull();
            CompatibilityLine.Of(Sample.Version("3.4.1")).Major.ShouldBe(3);
        }
    }
}
