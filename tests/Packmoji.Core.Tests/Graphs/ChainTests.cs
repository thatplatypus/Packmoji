using Packmoji.Core.Graphs;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Graphs
{
    public sealed class ChainTests
    {
        private static string[] Steps(int count) => Enumerable.Range(1, count).Select(number => $"s{number}").ToArray();

        [Fact]
        public void A_chain_is_its_steps_with_an_arrow_between_each()
        {
            Chain.Text(["@thatplatypus/app", "@thatplatypus/grapevine@0.3.0", "@thatplatypus/crypto@1.0"])
                .ShouldBe("@thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
        }

        [Fact]
        public void A_chain_of_one_step_is_that_step() => Chain.Text(["@thatplatypus/app"]).ShouldBe("@thatplatypus/app");

        [Fact]
        public void A_chain_of_eight_steps_is_shown_whole()
        {
            Chain.Text(Steps(8)).ShouldBe("s1 → s2 → s3 → s4 → s5 → s6 → s7 → s8");
        }

        [Fact]
        public void A_longer_chain_keeps_its_two_ends_and_counts_what_is_between()
        {
            Chain.Text(Steps(9)).ShouldBe("s1 → s2 → s3 → s4 → (2 more) → s7 → s8 → s9");
            Chain.Text(Steps(5000)).ShouldBe("s1 → s2 → s3 → s4 → (4993 more) → s4998 → s4999 → s5000");
        }

        [Fact]
        public void Only_the_steps_that_are_shown_are_ever_made()
        {
            var made = new List<int>();

            Chain.Text(1_000_000, index =>
            {
                made.Add(index);
                return "s";
            });

            made.ShouldBe([0, 1, 2, 3, 999_997, 999_998, 999_999]);
        }
    }
}
