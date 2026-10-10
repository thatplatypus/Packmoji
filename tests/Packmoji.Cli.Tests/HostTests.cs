using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Identity;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests
{
    /// <summary>What pmj takes from the environment it is run in: where its cache is kept, where it works, and a token for GitHub's API.</summary>
    public sealed class HostTests
    {
        [Fact]
        public void The_cache_is_kept_in_the_home_directory_unless_the_environment_says_where()
        {
            using var sandbox = new Sandbox();
            var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".packmoji");

            sandbox.Host().HomeDirectory.ShouldBe(home);
            sandbox.Host(("PACKMOJI_HOME", "")).HomeDirectory.ShouldBe(home);
            sandbox.Host(("PACKMOJI_HOME", sandbox.Home)).HomeDirectory.ShouldBe(sandbox.Home);
            sandbox.Host().WorkingDirectory.ShouldBe(sandbox.Work);
        }

        [Theory]
        [InlineData("GITHUB_TOKEN", "one", "GH_TOKEN", "two", "Bearer one")]
        [InlineData("GH_TOKEN", "two", "GITHUB_TOKEN", "", "Bearer two")]
        [InlineData("GH_TOKEN", "two", "PACKMOJI_HOME", "elsewhere", "Bearer two")]
        [InlineData("GITHUB_TOKEN", " ", "GH_TOKEN", "", null)]
        public async Task The_token_for_the_API_is_the_first_of_the_two_variables_that_is_set_to_something(string one, string value, string other, string otherValue, string? sent)
        {
            using var sandbox = new Sandbox();
            RepositoryRef.TryParse(Sandbox.Grapevine, out var repository, out _).ShouldBeTrue();

            await sandbox.Host((one, value), (other, otherValue)).Releases.ListAsync(repository!, TestContext.Current.CancellationToken);

            var authorization = sandbox.GitHub.Requests.Single().Authorization;
            (authorization is null ? null : authorization.ToString()).ShouldBe(sent);
        }
    }
}
