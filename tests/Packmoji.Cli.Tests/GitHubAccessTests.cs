using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests
{
    /// <summary>Which GitHub pmj speaks to, and what it tells it: a token for the API alone, and never with a download.</summary>
    public sealed class GitHubAccessTests
    {
        [Theory]
        [InlineData("GITHUB_TOKEN")]
        [InlineData("GH_TOKEN")]
        public async Task A_token_is_sent_to_the_API_and_never_with_a_download_and_is_in_nothing_pmj_prints(string variable)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host((variable, "ghp_a-secret-that-must-not-leak"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(0);
            var api = sandbox.GitHub.Requests.Where(request => request.Uri.Host == "api.github.com").ToList();
            var downloads = sandbox.GitHub.Requests.Where(request => request.Uri.Host == "github.com").ToList();
            api.Count.ShouldBe(1);
            api[0].Authorization!.ToString().ShouldBe("Bearer ghp_a-secret-that-must-not-leak");
            downloads.ShouldNotBeEmpty();
            downloads.ShouldAllBe(request => request.Authorization == null);
            sandbox.GitHub.Requests.ShouldAllBe(request => request.UserAgent.StartsWith("pmj/0."));
            (host.Out.ToString() + host.Error + sandbox.Read("packmoji.lock") + sandbox.Read("packmoji.json")).ShouldNotContain("ghp_");
        }

        [Fact]
        public async Task The_addresses_of_GitHub_and_of_its_API_can_be_others()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var host = sandbox.Host(("PACKMOJI_GITHUB", "http://127.0.0.1:8123/"), ("PACKMOJI_GITHUB_API", "http://api.localhost:8123"), ("PACKMOJI_HOME", sandbox.Home));

            var status = await PmjCommandLine.RunAsync(["add", "@thatplatypus/grapevine"], host, TestContext.Current.CancellationToken);

            status.ShouldBe(0);
            sandbox.GitHub.Requests.Select(request => request.Uri.GetLeftPart(UriPartial.Path)).ShouldBe(
            [
                "http://api.localhost:8123/repos/thatplatypus/grapevine/releases",
                "http://127.0.0.1:8123/thatplatypus/grapevine/releases/download/grapevine-v0.3.0/grapevine-0.3.0.pmj.tar.gz",
                "http://127.0.0.1:8123/thatplatypus/crypto/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "http://127.0.0.1:8123/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "http://127.0.0.1:8123/thatplatypus/deflate/releases/download/deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
                "http://127.0.0.1:8123/thatplatypus/grapevine/releases/download/deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
            ]);
        }
    }
}
