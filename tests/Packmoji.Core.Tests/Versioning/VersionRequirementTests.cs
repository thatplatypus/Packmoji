using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Versioning
{
    public sealed class VersionRequirementTests
    {
        [Theory]
        [InlineData("1.2", "1.2.0", "1.x")]
        [InlineData("1.2.3", "1.2.3", "1.x")]
        [InlineData("0.4", "0.4.0", "0.4.x")]
        [InlineData("0.4.1", "0.4.1", "0.4.x")]
        [InlineData("0.0.3", "0.0.3", "0.0.x")]
        [InlineData("1.0.0-beta.1", "1.0.0-beta.1", "1.x")]
        public void Every_accepted_form_has_its_minimum_and_its_line(string text, string minimum, string line)
        {
            VersionRequirement.TryParse(text, out var requirement, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            requirement.ShouldNotBeNull();
            requirement.Text.ShouldBe(text);
            requirement.ToString().ShouldBe(text);
            requirement.Minimum.ShouldBe(Sample.Version(minimum));
            requirement.Line.ToString().ShouldBe(line);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("^1.2")]
        [InlineData("~1.2")]
        [InlineData(">=1.2")]
        [InlineData("1.*")]
        [InlineData("*")]
        [InlineData("v1.2")]
        [InlineData("1.2-beta")]
        [InlineData("1.2.3+build")]
        [InlineData("1.2 ")]
        [InlineData(" 1.2")]
        [InlineData("1.2, 1.3")]
        [InlineData("1.2 - 1.4")]
        [InlineData("1.2.3.4")]
        [InlineData("1.02")]
        [InlineData("latest")]
        public void Every_other_form_is_refused(string text)
        {
            VersionRequirement.TryParse(text, out var requirement, out var error).ShouldBeFalse();

            requirement.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.RequirementInvalid);
        }

        [Theory]
        [InlineData("^1.2", "\"1.2\"")]
        [InlineData(">=0.4.1", "\"0.4.1\"")]
        [InlineData("v1.2", "\"1.2\"")]
        [InlineData("1", "\"1.0\"")]
        public void The_fix_names_the_spelling_that_was_meant(string text, string expectedInFix)
        {
            VersionRequirement.TryParse(text, out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain(expectedInFix);
        }

        [Theory]
        [InlineData("1.2", "1.2.0", true)]
        [InlineData("1.2", "1.9.9", true)]
        [InlineData("1.2", "1.1.9", false)]
        [InlineData("1.2", "2.0.0", false)]
        [InlineData("1.2", "2.0.0-rc.1", false)]
        [InlineData("1.2", "1.2.0-beta.1", false)] // a pre-release of 1.2.0 comes before 1.2.0
        [InlineData("0.4.1", "0.4.1", true)]
        [InlineData("0.4.1", "0.4.9", true)]
        [InlineData("0.4.1", "0.4.0", false)]
        [InlineData("0.4.1", "0.5.0", false)]
        [InlineData("0.0.3", "0.0.9", true)]
        [InlineData("0.0.3", "0.1.0", false)]
        [InlineData("1.0.0-beta.1", "1.0.0-beta.2", true)]
        [InlineData("1.0.0-beta.1", "1.0.0", true)]
        [InlineData("1.0.0-beta.1", "1.0.0-alpha", false)]
        public void A_version_satisfies_a_requirement_on_its_line_at_or_above_its_minimum(string requirement, string version, bool satisfied) =>
            Sample.Requirement(requirement).IsSatisfiedBy(Sample.Version(version)).ShouldBe(satisfied);

        [Fact]
        public void Two_spellings_of_one_minimum_are_equal()
        {
            var short_ = Sample.Requirement("1.2");
            var long_ = Sample.Requirement("1.2.0");

            short_.ShouldBe(long_);
            (short_ == long_).ShouldBeTrue();
            short_.GetHashCode().ShouldBe(long_.GetHashCode());
            short_.Text.ShouldBe("1.2");
            long_.Text.ShouldBe("1.2.0");
            short_.ShouldNotBe(Sample.Requirement("1.2.1"));
        }
    }
}
