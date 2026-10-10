using Packmoji.Core.Building;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>
    /// The compiler has no way to be asked its version but the first line of its help. A version read
    /// wrongly would let a package be built by a compiler it says it cannot be built with, so a
    /// banner that is not understood gives nothing, and is never guessed at.
    /// </summary>
    public sealed class CompilerBannerTests
    {
        // What Emojicode 1.0 beta 2 for Linux printed for --help on 2026-10-10, whole.
        private const string Released =
            "  emojicodec file {OPTIONS}\n" +
            "\n" +
            "    Emojicode Compiler 1.0 beta 2. Visit https://www.emojicode.org for help.\n" +
            "\n" +
            "  OPTIONS:\n" +
            "\n" +
            "      file                              The main file of the package to be\n" +
            "                                        compiled\n" +
            "      -h, --help                        Display this help menu\n" +
            "      -p[package]                       The name of the package\n" +
            "      -o[out]                           Set output path for binary or assembly\n" +
            "      -i[interface]                     Output interface to given path\n" +
            "      -r                                Generate a JSON report about the package\n" +
            "      -c                                Produce object file, do not link\n" +
            "      --json                            Show compiler messages as JSON\n" +
            "      --format                          Format source code\n" +
            "      --color                           Always show compiler messages in color\n" +
            "      -O                                Compile with optimizations\n" +
            "      --emit-llvm                       Print the IR to the standard output\n" +
            "      -S[search path...]                Adds the path to the package search path\n" +
            "                                        (after './packages')\n" +
            "      \"--\" can be used to terminate flag options and force all following\n" +
            "      arguments to be treated as positional options\n" +
            "\n";

        [Fact]
        public void The_released_compiler_is_1_0_0_beta_2() =>
            CompilerBanner.Version(Released).ShouldBe(Sample.Version("1.0.0-beta.2"));

        [Fact]
        public void A_help_text_with_other_line_ends_reads_the_same() =>
            CompilerBanner.Version(Released.Replace("\n", "\r\n")).ShouldBe(Sample.Version("1.0.0-beta.2"));

        [Theory]
        [InlineData("Emojicode Compiler 1.0. Visit https://www.emojicode.org for help.", "1.0.0")]
        [InlineData("Emojicode Compiler 1.2.3. Visit https://www.emojicode.org for help.", "1.2.3")]
        [InlineData("Emojicode Compiler 2.0 rc 1. Visit https://www.emojicode.org for help.", "2.0.0-rc.1")]
        [InlineData("Emojicode Compiler 1.0 beta 12.", "1.0.0-beta.12")]
        [InlineData("Emojicode Compiler 1.0 beta 2", "1.0.0-beta.2")]
        [InlineData("Emojicode Compiler 10.20.30", "10.20.30")]
        [InlineData("Emojicode Compiler 1.0 beta 2.\r\n  OPTIONS:", "1.0.0-beta.2")]
        [InlineData("Emojicode Compiler 1.1\n  OPTIONS:", "1.1.0")]
        public void A_banner_is_two_or_three_numbers_and_perhaps_a_word_and_a_number(string banner, string version) =>
            CompilerBanner.Version(banner).ShouldBe(Sample.Version(version));

        [Theory]
        [InlineData("")]
        [InlineData("👉  Flag could not be matched: help")]
        [InlineData("Emojicode Compiler")]
        [InlineData("Emojicode Compiler. Visit https://www.emojicode.org for help.")]
        [InlineData("Emojicode Compiler beta 2.")]
        [InlineData("Emojicode Compiler 1.")]
        [InlineData("Emojicode Compiler 1 beta 2.")]
        [InlineData("Emojicode Compiler 1.0.0.0.")]
        [InlineData("Emojicode Compiler 1.x.")]
        [InlineData("Emojicode Compiler 1.0 beta.")]
        [InlineData("Emojicode Compiler 1.0 beta two.")]
        [InlineData("Emojicode Compiler 1.0 Beta 2.")]
        [InlineData("Emojicode Compiler 1.0 beta 2 nightly.")]
        [InlineData("Emojicode Compiler 01.0.")]
        [InlineData("Emojicode Compiler 1.0 beta 02.")]
        [InlineData("Emojicode Compiler 99999999999.0.")]
        [InlineData("Another Compiler 1.0 beta 2.")]
        public void A_banner_that_is_not_understood_gives_no_version(string banner) =>
            CompilerBanner.Version(banner).ShouldBeNull();
    }
}
