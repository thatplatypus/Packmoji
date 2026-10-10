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

        [Theory]
        [InlineData("ghp_token\n")]
        [InlineData("  ghp_token\r\n")]
        [InlineData("\tghp_token ")]
        public async Task Space_and_line_ends_around_a_token_are_no_part_of_it(string value)
        {
            using var sandbox = new Sandbox();
            RepositoryRef.TryParse(Sandbox.Grapevine, out var repository, out _).ShouldBeTrue();

            await sandbox.Host(("GITHUB_TOKEN", value)).Releases.ListAsync(repository!, TestContext.Current.CancellationToken);

            sandbox.GitHub.Requests.Single().Authorization!.ToString().ShouldBe("Bearer ghp_token");
        }

        [Theory]
        [InlineData("GITHUB_TOKEN", "ghp_tok en")]
        [InlineData("GH_TOKEN", "ghp_tok\nen")]
        [InlineData("GITHUB_TOKEN", "ghp_tok\u00E9n")]
        public async Task A_token_that_could_not_be_one_is_a_problem_that_names_its_variable_and_never_shows_it(string variable, string value)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host((variable, value), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            Errors(host).ShouldStartWith($"error[config.invalid]: {variable} does not hold a token.");
            Errors(host).ShouldContain("  fix: ");
            (Errors(host) + host.Out).ShouldNotContain("ghp_tok");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_token_that_cannot_be_used_is_not_passed_over_for_the_one_after_it()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host(("GITHUB_TOKEN", "ghp_one two"), ("GH_TOKEN", "ghp_three"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            Errors(host).ShouldStartWith("error[config.invalid]: GITHUB_TOKEN does not hold a token.");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("PACKMOJI_GITHUB", "ghe.example.com", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB", "localhost:8123", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB", "127.0.0.1:8123", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB", "ftp://ghe.example.com", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB_API", "ghe.example.com/api/v3", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB_API", "not a url", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB_API", "/srv/mirror", "it does not begin with http:// or https://")]
        [InlineData("PACKMOJI_GITHUB", "https://", "it cannot be read as an address")]
        [InlineData("PACKMOJI_GITHUB", "https://someone:ghp_a-password@ghe.example.com", "it holds something other than the name of a machine and a path")]
        [InlineData("PACKMOJI_GITHUB_API", "https://ghe.example.com/api/v3?per_page=1", "it holds something other than the name of a machine and a path")]
        [InlineData("PACKMOJI_GITHUB_API", "https://ghe.example.com/api/v3#top", "it holds something other than the name of a machine and a path")]
        public async Task An_address_that_is_not_one_is_a_problem_that_names_the_variable_and_nothing_is_asked_of_anyone(string variable, string value, string why)
        {
            // Someone with a GitHub of their own who mistypes its address must not be sent to the
            // real one, where packages of the same names may be, and where their token would go.
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host((variable, value), ("GITHUB_TOKEN", "ghp_a-secret"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            Errors(host).ShouldStartWith($"error[config.invalid]: {variable} is not an address pmj can use.");
            Errors(host).ShouldContain($"  why: {why}");
            Errors(host).ShouldContain("  fix: ");
            Errors(host).ShouldNotContain("ghp_a-");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Theory]
        [InlineData("PACKMOJI_GITHUB", "https://ghe.example.com", "PACKMOJI_GITHUB_API")]
        [InlineData("PACKMOJI_GITHUB_API", "https://ghe.example.com/api/v3", "PACKMOJI_GITHUB")]
        public async Task One_address_without_the_other_is_a_problem_for_pmj_would_then_speak_to_two_GitHubs(string set, string value, string unset)
        {
            // With the first alone, versions would be listed from api.github.com, and the token
            // meant for the GitHub that was named would be sent there.
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host((set, value), (unset, " "), ("GITHUB_TOKEN", "ghp_a-secret"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            Errors(host).ShouldStartWith($"error[config.invalid]: {set} is set and {unset} is not.");
            Errors(host).ShouldContain("  fix: set both");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_command_that_asks_nothing_of_GitHub_is_not_stopped_by_an_address_it_would_never_use()
        {
            using var sandbox = new Sandbox();
            var host = sandbox.Host(("PACKMOJI_GITHUB", "not a url"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["new", "@thatplatypus/hello"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(0);
            sandbox.Has("hello/packmoji.json").ShouldBeTrue();
        }

        [Fact]
        public async Task What_the_cache_already_holds_is_installed_whatever_the_environment_says_of_GitHub()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            var asked = sandbox.GitHub.Requests.Count;
            var host = sandbox.Host(("PACKMOJI_GITHUB", "ghe.example.com"), ("GITHUB_TOKEN", "two words"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["install", "--locked"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(0);
            Errors(host).ShouldBeEmpty();
            sandbox.GitHub.Requests.Count.ShouldBe(asked);
        }

        [Fact]
        public async Task Verify_says_once_what_it_was_told_and_cannot_use_and_asks_nothing_of_anyone()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.Lockfile().Packages.Count.ShouldBe(3);
            var asked = sandbox.GitHub.Requests.Count;
            var host = sandbox.Host(("PACKMOJI_GITHUB_API", "not a url"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["verify"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            (Errors(host).Split("error[config.invalid]").Length - 1).ShouldBe(1);
            sandbox.GitHub.Requests.Count.ShouldBe(asked);
        }

        private static string Errors(PmjHost host) => host.Error.ToString() ?? "";
    }
}
