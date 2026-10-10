using Packmoji.Core.Archives;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Archives
{
    public sealed class PathOrderTests
    {
        private static int Compare(string one, string other) => Math.Sign(PathOrder.Instance.Compare(one, other));

        [Fact]
        public void Paths_are_in_order_of_code_point_which_is_not_the_order_dotnet_gives_strings()
        {
            // The last characters of the first plane come before every emoji by code point, and after
            // them by UTF-16 unit, because an emoji is written there as two units that begin lower.
            var endOfFirstPlane = char.ConvertFromUtf32(0xFFFD);
            var grapes = char.ConvertFromUtf32(0x1F347);

            Compare(endOfFirstPlane, grapes).ShouldBe(-1);
            Compare(grapes, endOfFirstPlane).ShouldBe(1);
            Math.Sign(string.CompareOrdinal(endOfFirstPlane, grapes)).ShouldBe(1);
        }

        [Theory]
        [InlineData("a", "a", 0)]
        [InlineData("", "", 0)]
        [InlineData("src/lib", "src/lib", 0)]
        [InlineData("a", "ab", -1)]
        [InlineData("ab", "a", 1)]
        [InlineData("B", "a", -1)]
        [InlineData("a/b", "a.b", 1)]
        public void A_path_that_is_the_start_of_another_comes_first_and_equal_paths_are_equal(string one, string other, int expected)
        {
            Compare(one, other).ShouldBe(expected);
        }
    }
}
