using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Versioning
{
    public sealed class CompilerRequirementTests
    {
        [Theory]
        [InlineData(">=1.0.0", "1.0.0")]
        [InlineData(">=1.0.0-beta.2", "1.0.0-beta.2")]
        [InlineData(">=0.9.0", "0.9.0")]
        public void The_one_accepted_spelling_is_parsed(string text, string minimum)
        {
            CompilerRequirement.TryParse(text, out var requirement, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            requirement.ShouldNotBeNull();
            requirement.Minimum.ShouldBe(Sample.Version(minimum));
            requirement.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.0.0")]
        [InlineData(">= 1.0.0")]
        [InlineData(">=1.0")]
        [InlineData(">1.0.0")]
        [InlineData("=>1.0.0")]
        [InlineData("^1.0.0")]
        [InlineData(">=1.0.0 <2.0.0")]
        [InlineData(">=1.0.0+build")]
        [InlineData("*")]
        public void Every_other_spelling_is_refused(string text)
        {
            CompilerRequirement.TryParse(text, out var requirement, out var error).ShouldBeFalse();

            requirement.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.CompilerInvalid);
        }

        [Theory]
        [InlineData("1.0.0", "\">=1.0.0\"")]
        [InlineData(">= 1.0.0-beta.2", "\">=1.0.0-beta.2\"")]
        public void The_fix_names_the_spelling_that_was_meant(string text, string expectedInFix)
        {
            CompilerRequirement.TryParse(text, out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain(expectedInFix);
        }

        [Fact]
        public void The_only_released_compiler_does_not_satisfy_a_requirement_of_the_final_release()
        {
            var beta2 = Sample.Version("1.0.0-beta.2");

            Sample.Compiler(">=1.0.0").IsSatisfiedBy(beta2).ShouldBeFalse();
            Sample.Compiler(">=1.0.0-beta.2").IsSatisfiedBy(beta2).ShouldBeTrue();
        }

        [Fact]
        public void There_is_no_upper_bound()
        {
            var requirement = Sample.Compiler(">=1.0.0-beta.2");

            requirement.IsSatisfiedBy(Sample.Version("1.0.0")).ShouldBeTrue();
            requirement.IsSatisfiedBy(Sample.Version("7.3.0")).ShouldBeTrue();
            requirement.IsSatisfiedBy(Sample.Version("1.0.0-beta.1")).ShouldBeFalse();
        }
    }
}
