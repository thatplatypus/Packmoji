using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    /// <summary>
    /// A path in a manifest ends up as an argument to a compiler, and in an archive that is unpacked on
    /// other platforms. A published manifest can never be tightened, so what could be misread in either
    /// place is refused from the first version.
    /// </summary>
    public sealed class PathSafetyTests
    {
        private static readonly string Joiner = char.ConvertFromUtf32(0x200D);
        private static readonly string Family = char.ConvertFromUtf32(0x1F468) + Joiner + char.ConvertFromUtf32(0x1F469) + Joiner + char.ConvertFromUtf32(0x1F467);
        private static readonly string FlagOfEngland = string.Concat(new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F }.Select(char.ConvertFromUtf32));

        [Theory]
        [InlineData("-S/tmp/x.emojic")]
        [InlineData("-fplugin=evil.so")]
        [InlineData("src/-x.cpp")]
        [InlineData("@args.cpp")]
        [InlineData("src/@args.cpp")]
        [InlineData(" ")]
        [InlineData(" src/x.cpp")]
        [InlineData("src/ x.cpp")]
        [InlineData("src /x.cpp")]
        [InlineData("src/x.cpp ")]
        [InlineData(".. ")]
        [InlineData("...")]
        [InlineData("src/x.")]
        [InlineData("src./x.cpp")]
        public void A_part_that_a_compiler_or_another_platform_would_read_differently_is_refused(string text)
        {
            RelativePath.TryParse(text, out var path, out var pathError).ShouldBeFalse();
            GlobPattern.TryParse(text, out var pattern, out var patternError).ShouldBeFalse();

            path.ShouldBeNull();
            pattern.ShouldBeNull();
            pathError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.PathInvalid);
            patternError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.GlobInvalid);
        }

        [Theory]
        [InlineData("src/a-b.cpp")]
        [InlineData("src/a@b.cpp")]
        [InlineData("a b/c d.txt")]
        [InlineData(".hidden/.x")]
        [InlineData("a.b/c.d")]
        [InlineData("!important")]
        public void A_hyphen_an_at_sign_a_space_or_a_dot_inside_a_part_is_fine(string text)
        {
            RelativePath.TryParse(text, out _, out var error).ShouldBeTrue(error?.Reason);
        }

        [Theory]
        [InlineData(0x2028)] // line separator
        [InlineData(0x2029)] // paragraph separator
        [InlineData(0x202E)] // right-to-left override
        [InlineData(0x2066)] // left-to-right isolate
        [InlineData(0x200E)] // left-to-right mark
        [InlineData(0x200B)] // zero width space
        [InlineData(0xFEFF)] // zero width no-break space
        [InlineData(0x00AD)] // soft hyphen
        [InlineData(0xE0001)] // language tag, which no emoji uses
        public void A_character_that_cannot_be_seen_or_that_reorders_text_is_refused_everywhere(int codePoint)
        {
            var character = char.ConvertFromUtf32(codePoint);

            RelativePath.TryParse("src/li" + character + "b.emojic", out _, out var pathError).ShouldBeFalse();
            GlobPattern.TryParse("src/*" + character + ".emojic", out _, out var patternError).ShouldBeFalse();

            pathError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.PathInvalid);
            patternError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.GlobInvalid);
            ManifestRules.CheckDescription("one" + character + "two").ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.DescriptionInvalid);
        }

        [Fact]
        public void An_emoji_sequence_is_an_ordinary_name_though_it_is_built_with_characters_that_cannot_be_seen()
        {
            RelativePath.TryParse("src/" + Family + ".🍇", out _, out var pathError).ShouldBeTrue(pathError?.Reason);
            GlobPattern.TryParse("src/**/" + FlagOfEngland + "*.🍇", out _, out var patternError).ShouldBeTrue(patternError?.Reason);
            ManifestRules.CheckDescription("For " + Family + " and " + FlagOfEngland).ShouldBeNull();
        }

        [Fact]
        public void A_manifest_cannot_name_a_file_that_would_read_as_a_compiler_option()
        {
            var result = ManifestReader.Read("""
                {
                  "package": { "name": "@thatplatypus/crypto", "version": "1.0.0", "kind": "library", "emojicode": ">=1.0.0-beta.2" },
                  "build": { "entry": "-S/tmp/x.emojic" },
                  "native": { "sources": ["-fplugin=evil.so", "@args.cpp"], "includeDirs": ["-I/etc"] }
                }
                """);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
            [
                DiagnosticCodes.PathInvalid,
                DiagnosticCodes.GlobInvalid,
                DiagnosticCodes.GlobInvalid,
                DiagnosticCodes.PathInvalid,
            ]);
        }
    }
}
