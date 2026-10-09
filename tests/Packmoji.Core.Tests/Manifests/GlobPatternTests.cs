using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class GlobPatternTests
    {
        [Theory]
        [InlineData("src/**/*.emojic")]
        [InlineData("src/**/*.🍇")]
        [InlineData("native/*.cpp")]
        [InlineData("*.🍇")]
        [InlineData("**")]
        [InlineData("src/**")]
        [InlineData("**/x")]
        [InlineData("src/lib?.emojic")]
        [InlineData("src/lib.emojic")]
        [InlineData("a*b/c?d")]
        public void A_pattern_of_stars_and_question_marks_is_accepted(string text)
        {
            GlobPattern.TryParse(text, out var pattern, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            pattern.ShouldNotBeNull().Value.ShouldBe(text);
            pattern.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/src/*.emojic")]
        [InlineData("src/")]
        [InlineData("src//x")]
        [InlineData("./src/*")]
        [InlineData("../*")]
        [InlineData("src\\*.emojic")]
        [InlineData("C:/*")]
        [InlineData("src/a**b")]
        [InlineData("src/***")]
        [InlineData("src/**.emojic")]
        [InlineData("src/[ab].emojic")]
        [InlineData("src/{a,b}.emojic")]
        [InlineData("!src/x")]
        public void Anything_else_is_refused(string text)
        {
            GlobPattern.TryParse(text, out var pattern, out var error).ShouldBeFalse();

            pattern.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.GlobInvalid);
        }

        [Fact]
        public void A_pattern_may_be_255_characters_and_no_more()
        {
            GlobPattern.TryParse(new string('a', 255), out _, out _).ShouldBeTrue();
            GlobPattern.TryParse(new string('a', 256), out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void Two_stars_inside_a_longer_part_say_what_two_stars_are_for()
        {
            GlobPattern.TryParse("src/**.emojic", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Reason.ShouldContain("**");
        }

        [Fact]
        public void Two_patterns_of_one_text_are_equal() => Sample.Glob("src/**/*.🍇").ShouldBe(Sample.Glob("src/**/*.🍇"));
    }
}
