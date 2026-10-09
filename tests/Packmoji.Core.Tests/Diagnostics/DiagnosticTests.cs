using System.Globalization;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    /// <summary>
    /// A diagnostic repeats text from a file that someone else wrote, and it is printed to a terminal.
    /// These tests hold it to carrying nothing that could drive the terminal or disguise what it says.
    /// </summary>
    public sealed class DiagnosticTests
    {
        // Built from their numbers so that none of these characters, which cannot be seen, is in this file.
        private static readonly string Escape = char.ConvertFromUtf32(0x1B);
        private static readonly string RightToLeftOverride = char.ConvertFromUtf32(0x202E);
        private static readonly string LineSeparator = char.ConvertFromUtf32(0x2028);
        private static readonly string ZeroWidthSpace = char.ConvertFromUtf32(0x200B);
        private static readonly string Joiner = char.ConvertFromUtf32(0x200D);

        private static string Shown(int codePoint) => "\\" + "u{" + codePoint.ToString("X4", CultureInfo.InvariantCulture) + "}";

        [Fact]
        public void A_control_character_is_shown_as_its_number_and_not_passed_on()
        {
            var diagnostic = new Diagnostic("x.y", "bad" + Escape + "[2Jname", "line one\nline two", "tab\there");

            diagnostic.Message.ShouldBe("bad" + Shown(0x1B) + "[2Jname");
            diagnostic.Reason.ShouldBe("line one" + Shown(0x0A) + "line two");
            diagnostic.Fix.ShouldBe("tab" + Shown(0x09) + "here");
        }

        [Fact]
        public void A_character_that_reverses_text_or_breaks_a_line_or_cannot_be_seen_is_shown_as_its_number()
        {
            var diagnostic = new Diagnostic("x.y", "a" + RightToLeftOverride + "b", "c" + LineSeparator + "d", "e" + ZeroWidthSpace + "f");

            diagnostic.Message.ShouldBe("a" + Shown(0x202E) + "b");
            diagnostic.Reason.ShouldBe("c" + Shown(0x2028) + "d");
            diagnostic.Fix.ShouldBe("e" + Shown(0x200B) + "f");
        }

        [Fact]
        public void Ordinary_text_and_emoji_sequences_are_left_exactly_as_they_are()
        {
            var family = char.ConvertFromUtf32(0x1F468) + Joiner + char.ConvertFromUtf32(0x1F469) + Joiner + char.ConvertFromUtf32(0x1F467);
            var flagOfEngland = string.Concat(new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F }.Select(char.ConvertFromUtf32));
            var text = "\"src/lib.🍇\" is fine, as are é, " + family + " and " + flagOfEngland + ".";

            var diagnostic = new Diagnostic("x.y", text, text, text);

            diagnostic.Message.ShouldBe(text);
            diagnostic.Reason.ShouldBe(text);
            diagnostic.Fix.ShouldBe(text);
        }

        [Fact]
        public void Text_given_to_a_copy_is_made_safe_as_well()
        {
            var diagnostic = new Diagnostic("x.y", "m", "r", "f") with { Fix = "run" + Escape + "this", Reason = "two\nlines" };

            diagnostic.Fix.ShouldBe("run" + Shown(0x1B) + "this");
            diagnostic.Reason.ShouldBe("two" + Shown(0x0A) + "lines");
        }

        [Fact]
        public void Very_long_text_is_cut_short()
        {
            var diagnostic = new Diagnostic("x.y", new string('a', 8_000_000), new string('b', 1_000), "f");

            diagnostic.Message.Length.ShouldBe(1_003);
            diagnostic.Message.ShouldEndWith("a...");
            diagnostic.Reason.ShouldBe(new string('b', 1_000));
        }

        [Fact]
        public void Two_diagnostics_of_the_same_text_are_equal()
        {
            var left = new Diagnostic("x.y", "m" + Escape, "r", "f", new SourceLocation("a.json", 1, 2));
            var right = new Diagnostic("x.y", "m" + Escape, "r", "f", new SourceLocation("a.json", 1, 2));

            left.ShouldBe(right);
            left.ShouldNotBe(right with { Fix = "another" });
        }

        [Fact]
        public void A_name_taken_from_a_file_reaches_its_diagnostic_made_safe()
        {
            PackageName.TryParse("@a/b" + Escape + "[2J", out _, out var error).ShouldBeFalse();

            error.ShouldNotBeNull().Message.ShouldNotContain(Escape);
            error.Message.ShouldContain(Shown(0x1B));
        }
    }
}
