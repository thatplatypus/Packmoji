using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Json
{
    public sealed class SourceTextTests
    {
        private const string File = "test.json";

        [Fact]
        public void A_place_is_found_whatever_order_places_are_asked_for_in()
        {
            // Line 1 is bytes 0 to 8 and line 2 begins at byte 9. Each emoji is four bytes and one column.
            var source = new SourceText(Encoding.UTF8.GetBytes("ab🍇cd\nef🍇🍇gh\n"), File);

            source.Locate(19).ShouldBe(new SourceLocation(File, 2, 5));
            source.Locate(20).ShouldBe(new SourceLocation(File, 2, 6));
            source.Locate(10).ShouldBe(new SourceLocation(File, 2, 2));
            source.Locate(6).ShouldBe(new SourceLocation(File, 1, 4));
            source.Locate(7).ShouldBe(new SourceLocation(File, 1, 5));
            source.Locate(0).ShouldBe(new SourceLocation(File, 1, 1));
            source.Locate(20).ShouldBe(new SourceLocation(File, 2, 6));
        }

        [Fact]
        public void A_file_on_one_long_line_is_read_in_time_proportional_to_its_length()
        {
            // 60,000 short strings on one line, about 420 KB. Counting from the start of the line for
            // each of them is some twelve thousand million steps. Counting on from the one before is
            // 420,000, which takes a few milliseconds, so the limit below is not a close one.
            const int count = 60_000;
            var bytes = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Repeat("\"abcd\"", count)) + "]");
            var diagnostics = new List<Diagnostic>();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var root = JsonTreeReader.Read(bytes, File, "resolve the conflict", diagnostics);
            watch.Stop();

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Items.Count.ShouldBe(count);
            root.Items[^1].Location.ShouldBe(new SourceLocation(File, 1, 2 + (7 * (count - 1))));
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Columns_along_a_line_of_emoji_count_code_points()
        {
            const int count = 1_000;
            var bytes = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Repeat("\"🍇\"", count)) + "]");
            var diagnostics = new List<Diagnostic>();

            var root = JsonTreeReader.Read(bytes, File, "resolve the conflict", diagnostics);

            root.ShouldNotBeNull().Items[1].Location.ShouldBe(new SourceLocation(File, 1, 6));
            root.Items[^1].Location.ShouldBe(new SourceLocation(File, 1, 2 + (4 * (count - 1))));
        }
    }
}
