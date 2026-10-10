using Packmoji.Core.Packing;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Packing
{
    /// <summary>
    /// A pattern selects the same files on every machine: it is matched by code point, with no eye to
    /// case and none to a culture, against paths written with a slash.
    /// </summary>
    public sealed class GlobMatcherTests
    {
        [Theory]
        [InlineData("src/lib.🍇", "src/lib.🍇")]
        [InlineData("*.🍇", "crypto.🍇")]
        [InlineData("*.🍇", "a.b.🍇")]
        [InlineData("src/*.emojic", "src/a.emojic")]
        [InlineData("src/*", "src/anything at all")]
        [InlineData("native/*.cpp", "native/net.cpp")]
        [InlineData("a*", "a")]
        [InlineData("src/lib*", "src/lib")]
        [InlineData("a*c", "ac")]
        [InlineData("a*c", "abbbc")]
        [InlineData("a*b*c", "a-b-b-c")]
        [InlineData("*a*", "banana")]
        [InlineData("???.txt", "abc.txt")]
        [InlineData("?.🍇", "🧪.🍇")]
        [InlineData("src/**/*.🍇", "src/lib.🍇")]
        [InlineData("src/**/*.🍇", "src/a/lib.🍇")]
        [InlineData("src/**/*.🍇", "src/a/b/c/lib.🍇")]
        [InlineData("**/*.h", "net.h")]
        [InlineData("**/*.h", "native/include/net.h")]
        [InlineData("native/**", "native/a/b.c")]
        [InlineData("a/**/b/**/c", "a/b/c")]
        [InlineData("a/**/b/**/c", "a/x/b/y/z/c")]
        [InlineData(".config/*", ".config/x")]
        [InlineData("src/.hidden", "src/.hidden")]
        [InlineData("src/.*", "src/.hidden")]
        public void A_pattern_selects(string pattern, string path)
        {
            GlobMatcher.IsMatch(Sample.Glob(pattern), path).ShouldBeTrue();
        }

        [Theory]
        [InlineData("src/lib.🍇", "src/Lib.🍇")]
        [InlineData("*.🍇", "src/crypto.🍇")]
        [InlineData("*.🍇", "crypto.emojic")]
        [InlineData("src/*.emojic", "src/a/b.emojic")]
        [InlineData("src/*", "src")]
        [InlineData("a*c", "abd")]
        [InlineData("???.txt", "ab.txt")]
        [InlineData("???.txt", "abcd.txt")]
        [InlineData("?.🍇", "ab.🍇")]
        [InlineData("src/**/*.🍇", "lib.🍇")]
        [InlineData("src/**/*.🍇", "other/lib.🍇")]
        [InlineData("src/**/*.🍇", "src/a/lib.emojic")]
        [InlineData("a/**/b/**/c", "a/c")]
        [InlineData("native/**", "native")]
        public void A_pattern_does_not_select(string pattern, string path)
        {
            GlobMatcher.IsMatch(Sample.Glob(pattern), path).ShouldBeFalse();
        }

        [Theory]
        [InlineData("*", ".DS_Store")]
        [InlineData("src/*", "src/.hidden")]
        [InlineData("src/?hidden", "src/.hidden")]
        [InlineData("src/**/*.🍇", "src/.git/lib.🍇")]
        [InlineData("**/*.🍇", ".cache/lib.🍇")]
        [InlineData("native/**", "native/.DS_Store")]
        public void A_name_that_begins_with_a_dot_is_selected_only_by_a_pattern_that_writes_the_dot(string pattern, string path)
        {
            GlobMatcher.IsMatch(Sample.Glob(pattern), path).ShouldBeFalse();
        }

        [Fact]
        public void A_pattern_of_many_stars_against_a_long_name_is_answered_at_once()
        {
            // Written naively, this is the pattern that takes for ever.
            var pattern = Sample.Glob(string.Concat(Enumerable.Repeat("a*", 60)) + "b");
            var name = new string('a', 200);

            GlobMatcher.IsMatch(pattern, name).ShouldBeFalse();
        }
    }
}
