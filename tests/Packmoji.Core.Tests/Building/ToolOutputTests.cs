using Packmoji.Core.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>
    /// A compiler repeats the line of code it objects to, and the code may be a package's that
    /// someone else wrote. What a tool printed goes to a terminal, so it is made fit to print, as
    /// every diagnostic is.
    /// </summary>
    public sealed class ToolOutputTests
    {
        private static readonly string Escape = char.ConvertFromUtf32(0x1B);

        [Fact]
        public void What_a_tool_printed_is_given_a_line_at_a_time_and_its_empty_lines_are_left_out() =>
            ToolOutput.Lines("one\n\n    two  \r\nthree\n").ShouldBe(["one", "    two", "three"]);

        [Fact]
        public void A_character_that_could_drive_a_terminal_is_shown_as_its_number() =>
            ToolOutput.Lines($"error: {Escape}[2Jgone").ShouldBe(["error: \\" + "u{001B}[2Jgone"]);

        [Fact]
        public void What_was_written_over_a_line_is_a_line_of_its_own() =>
            ToolOutput.Lines("compiling\rdone").ShouldBe(["compiling", "done"]);

        [Fact]
        public void Emoji_are_left_as_they_are() =>
            ToolOutput.Lines("/src/lib.🍇:2:5: 🚨 error: no 🏛\n    ⬆️").ShouldBe(["/src/lib.🍇:2:5: 🚨 error: no 🏛", "    ⬆️"]);

        [Fact]
        public void A_line_of_absurd_length_is_cut()
        {
            var line = ToolOutput.Lines(new string('a', 5_000)).ShouldHaveSingleItem();

            line.ShouldBe(new string('a', 1_000) + "...");
        }

        [Fact]
        public void A_tool_that_printed_nothing_gives_no_lines()
        {
            ToolOutput.Lines("").ShouldBeEmpty();
            ToolOutput.Lines("\n \n").ShouldBeEmpty();
        }
    }
}
