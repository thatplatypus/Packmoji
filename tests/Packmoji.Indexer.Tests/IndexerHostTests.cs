using Shouldly;
using Xunit;

namespace Packmoji.Indexer.Tests
{
    /// <summary>
    /// What the indexer takes from its environment: a token for GitHub's API, and another address for
    /// GitHub in a test. What the environment says and cannot be used is a problem that is said, and
    /// is never passed over for what the indexer does when nothing is said.
    /// </summary>
    public sealed class IndexerHostTests
    {
        private const string Token = "ghs_a-token-that-must-not-be-seen";

        private static IndexerHost From(params (string Name, string Value)[] variables)
        {
            var set = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
            return IndexerHost.From(name => set.GetValueOrDefault(name), new HttpClient(), TextWriter.Null, TextWriter.Null);
        }

        [Fact]
        public void With_nothing_said_GitHub_is_GitHub_and_no_token_is_needed()
        {
            var host = From();

            host.Problem.ShouldBeNull();
            host.Releases.ShouldNotBeNull();
        }

        [Theory]
        [InlineData("GITHUB_TOKEN")]
        [InlineData("GH_TOKEN")]
        public void A_token_is_taken_from_either_variable_and_space_around_it_is_no_part_of_it(string variable) =>
            From((variable, $"  {Token}\n")).Problem.ShouldBeNull();

        [Fact]
        public void A_variable_that_is_set_to_nothing_says_nothing() =>
            From(("GITHUB_TOKEN", ""), ("PACKMOJI_GITHUB", " "), ("PACKMOJI_GITHUB_API", "")).Problem.ShouldBeNull();

        [Fact]
        public void A_token_that_could_not_be_sent_is_a_problem_that_names_its_variable_and_never_shows_it()
        {
            var problem = From(("GITHUB_TOKEN", "two words")).Problem.ShouldNotBeNull();

            problem.ShouldStartWith("GITHUB_TOKEN does not hold a token");
            problem.ShouldNotContain("two words");
        }

        [Theory]
        [InlineData("PACKMOJI_GITHUB", "PACKMOJI_GITHUB_API")]
        [InlineData("PACKMOJI_GITHUB_API", "PACKMOJI_GITHUB")]
        public void One_address_for_GitHub_without_the_other_is_a_problem(string set, string unset) =>
            From((set, "http://localhost:8123")).Problem.ShouldBe($"{set} is set and {unset} is not: the two are the addresses of one GitHub.");

        [Theory]
        [InlineData("github.example:8123")]
        [InlineData("ftp://github.example")]
        [InlineData("https://name:hunter2@github.example")]
        [InlineData("https://github.example/?page=2")]
        public void An_address_that_cannot_be_used_is_a_problem_that_never_shows_it(string address)
        {
            var problem = From(("PACKMOJI_GITHUB", address), ("PACKMOJI_GITHUB_API", "http://localhost:8124")).Problem.ShouldNotBeNull();

            problem.ShouldStartWith("PACKMOJI_GITHUB is not an address that can be used");
            problem.ShouldNotContain("hunter2");
            problem.ShouldNotContain("github.example");
        }

        [Fact]
        public void The_first_thing_that_cannot_be_used_is_the_problem_that_is_said() =>
            From(("PACKMOJI_GITHUB", "nowhere"), ("GITHUB_TOKEN", "two words")).Problem.ShouldNotBeNull().ShouldStartWith("PACKMOJI_GITHUB is not an address");

        [Fact]
        public void Both_addresses_together_are_taken() =>
            From(("PACKMOJI_GITHUB", "http://localhost:8123"), ("PACKMOJI_GITHUB_API", "http://localhost:8124/api")).Problem.ShouldBeNull();
    }
}
