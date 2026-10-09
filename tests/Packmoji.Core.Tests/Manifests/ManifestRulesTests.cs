using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class ManifestRulesTests
    {
        [Theory]
        [InlineData("HTTP framework for Emojicode")]
        [InlineData("🍇")]
        [InlineData("a b")]
        public void A_description_of_one_line_is_accepted(string text) => ManifestRules.CheckDescription(text).ShouldBeNull();

        [Theory]
        [InlineData("")]
        [InlineData(" leading space")]
        [InlineData("trailing space ")]
        [InlineData("two\nlines")]
        [InlineData("a\ttab")]
        [InlineData("a\rreturn")]
        public void Any_other_description_is_refused(string text) =>
            ManifestRules.CheckDescription(text).ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.DescriptionInvalid);

        [Fact]
        public void A_description_may_be_200_code_points_and_no_more()
        {
            ManifestRules.CheckDescription(new string('a', 200)).ShouldBeNull();
            ManifestRules.CheckDescription(new string('a', 201)).ShouldBeComplete();

            // An emoji is two UTF-16 units and one code point, and it is code points that are counted.
            ManifestRules.CheckDescription(string.Concat(Enumerable.Repeat("🍇", 200))).ShouldBeNull();
            ManifestRules.CheckDescription(string.Concat(Enumerable.Repeat("🍇", 201))).ShouldBeComplete();
        }

        [Theory]
        [InlineData("pthread")]
        [InlineData("m")]
        [InlineData("ssl")]
        [InlineData("stdc++")]
        [InlineData("c++abi")]
        [InlineData("z_1.2-b")]
        [InlineData("9lib")]
        [InlineData("_private")]
        public void A_library_name_is_accepted(string text) => ManifestRules.CheckLinkName(text).ShouldBeNull();

        [Theory]
        [InlineData("")]
        [InlineData("-lm")]
        [InlineData("-Wl,--whole-archive")]
        [InlineData("--version")]
        [InlineData("lib m")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("lib;rm")]
        [InlineData("$(x)")]
        [InlineData("a,b")]
        [InlineData("ｍ")] // fullwidth m
        public void Anything_that_could_be_a_linker_flag_or_a_path_is_refused(string text) =>
            ManifestRules.CheckLinkName(text).ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.LinkInvalid);

        [Fact]
        public void A_library_name_may_be_64_characters_and_no_more()
        {
            ManifestRules.CheckLinkName(new string('a', 64)).ShouldBeNull();
            ManifestRules.CheckLinkName(new string('a', 65)).ShouldBeComplete();
        }

        [Theory]
        [InlineData("src/lib.emojic", true)]
        [InlineData("src/lib.🍇", true)]
        [InlineData("main.🍇", true)]
        [InlineData("src/lib.EMOJIC", false)]
        [InlineData("src/lib.txt", false)]
        [InlineData("src/lib", false)]
        [InlineData("src/.emojic", false)]
        [InlineData(".🍇", false)]
        [InlineData("src.emojic/lib", false)]
        public void A_source_file_has_a_name_and_one_of_the_two_suffixes(string path, bool expected) =>
            ManifestRules.IsSourceFile(path).ShouldBe(expected);
    }
}
