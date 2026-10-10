using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>
    /// What the tests read a path by, tried with the separator of a machine other than this one:
    /// a test that fails only on Windows cannot be had again at a desk that is not one.
    /// </summary>
    public sealed class PlainTextTests
    {
        [Fact]
        public void A_path_as_Windows_writes_it_is_read_with_slashes()
        {
            PlainText.Slashed(@"Built C:\Users\me\work\target\debug\app.", '\\').ShouldBe("Built C:/Users/me/work/target/debug/app.");
        }

        [Fact]
        public void A_path_in_a_JSON_answer_is_read_with_slashes_though_JSON_writes_each_backslash_as_two()
        {
            PlainText.Slashed("""{ "directory": "C:\\Users\\me\\work\\packages" }""", '\\').ShouldBe("""{ "directory": "C:/Users/me/work/packages" }""");
        }

        [Fact]
        public void A_quote_that_JSON_wrote_with_a_backslash_is_left_as_JSON_wrote_it()
        {
            PlainText.Slashed("""{ "message": "\"@a/b\" is not in \"C:\\Users\\me\"" }""", '\\').ShouldBe("""{ "message": "\"@a/b\" is not in \"C:/Users/me\"" }""");
        }

        [Fact]
        public void Where_a_path_is_written_with_slashes_nothing_is_changed()
        {
            const string text = """{ "message": "\"@a/b\" is not in \"/home/me\", nor in a\\b" }""";

            PlainText.Slashed(text, '/').ShouldBe(text);
        }

        [Fact]
        public void A_text_is_read_with_the_separator_of_the_machine_it_is_read_on()
        {
            PlainText.Slashed(Path.Combine("a", "b")).ShouldBe("a/b");
        }
    }
}
