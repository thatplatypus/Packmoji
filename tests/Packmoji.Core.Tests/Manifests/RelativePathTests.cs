using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class RelativePathTests
    {
        [Theory]
        [InlineData("src/lib.emojic")]
        [InlineData("src/lib.🍇")]
        [InlineData("main.🍇")]
        [InlineData("native/include")]
        [InlineData("a/b/c/d.txt")]
        [InlineData("with space/file name.txt")]
        [InlineData(".hidden/x")]
        [InlineData("!important")]
        public void A_path_inside_the_package_is_accepted(string text)
        {
            RelativePath.TryParse(text, out var path, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            path.ShouldNotBeNull().Value.ShouldBe(text);
            path.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/src/lib.emojic")]
        [InlineData("src/")]
        [InlineData("src//lib.emojic")]
        [InlineData("./src/lib.emojic")]
        [InlineData("../shared/bytes.🍇")]
        [InlineData("src/../lib.emojic")]
        [InlineData("src/..")]
        [InlineData("src\\lib.emojic")]
        [InlineData("C:/src/lib.emojic")]
        [InlineData("src/lib.emojic:stream")]
        [InlineData("src/*.emojic")]
        [InlineData("src/**/lib.emojic")]
        [InlineData("src/lib?.emojic")]
        [InlineData("src/[ab].emojic")]
        [InlineData("src/{a,b}.emojic")]
        [InlineData("src/li\tb.emojic")]
        [InlineData("src/lib\n.emojic")]
        [InlineData("src/a<b.emojic")]
        [InlineData("src/a>b.emojic")]
        [InlineData("src/\"quoted\".emojic")]
        [InlineData("src/a|b.emojic")]
        public void Anything_else_is_refused(string text)
        {
            RelativePath.TryParse(text, out var path, out var error).ShouldBeFalse();

            path.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.PathInvalid);
        }

        [Fact]
        public void A_path_may_be_255_characters_and_no_more()
        {
            RelativePath.TryParse(new string('a', 255), out _, out _).ShouldBeTrue();
            RelativePath.TryParse(new string('a', 256), out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void A_part_of_a_path_may_be_255_bytes_and_no_more_however_few_characters_that_is()
        {
            // Most disks hold a name of at most 255 bytes, and count them in UTF-8. Grapes are four bytes each.
            var grapes = string.Concat(Enumerable.Repeat("🍇", 63));
            RelativePath.TryParse($"src/{grapes}abc", out _, out _).ShouldBeTrue();

            RelativePath.TryParse($"src/{grapes}abcd", out var path, out var error).ShouldBeFalse();
            path.ShouldBeNull();
            error.ShouldBeComplete().Reason.ShouldContain("255 bytes");
        }

        [Fact]
        public void A_character_that_windows_keeps_for_itself_says_so()
        {
            RelativePath.TryParse("src/a<b.emojic", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Reason.ShouldContain("Windows");
        }

        [Fact]
        public void The_fix_for_backslashes_is_the_same_path_with_slashes()
        {
            RelativePath.TryParse("src\\native\\net.cpp", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Reason.ShouldContain("backslash");
            error!.Fix.ShouldContain("\"src/native/net.cpp\"");
        }

        [Fact]
        public void A_path_that_leaves_the_package_says_so()
        {
            RelativePath.TryParse("../shared/bytes.🍇", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Reason.ShouldContain("inside the package");
        }

        [Fact]
        public void Two_paths_of_one_text_are_equal() => Sample.Path("src/lib.🍇").ShouldBe(Sample.Path("src/lib.🍇"));
    }
}
