using System.Net;
using System.Text;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.GitHub;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.GitHub
{
    /// <summary>
    /// How pmj speaks to GitHub: a download by a release's own address, which needs no API and no
    /// token, and a list of releases from the API, which takes a token when there is one. A token is
    /// a secret, so these also hold it to going to the API alone and to never being printed.
    /// </summary>
    public sealed class GitHubReleaseHostTests
    {
        private const string Token = "ghp_a-token-that-must-not-be-seen";

        private static readonly byte[] Archive = Encoding.UTF8.GetBytes("the bytes of an archive");

        private static RepositoryRef Repository(string text)
        {
            RepositoryRef.TryParse(text, out var repository, out var error).ShouldBeTrue(error?.Reason);
            return repository!;
        }

        private static readonly RepositoryRef Grapevine = Repository("github.com/thatplatypus/grapevine");

        private static GitHubReleaseHost Host(FakeGitHub github, string? token = null) => new(new HttpClient(github), token: token, userAgent: "pmj/0.1.0");

        private static FakeGitHub WithCrypto() => new FakeGitHub().Upload("thatplatypus/Grapevine", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", Archive);

        private static async Task<Diagnostic> ShouldFail(Func<Task> act, string code)
        {
            var thrown = await Should.ThrowAsync<PackageSourceException>(act);
            thrown.Diagnostic.Code.ShouldBe(code);
            thrown.Diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
            thrown.Diagnostic.Reason.ShouldNotBeNullOrWhiteSpace();
            thrown.Diagnostic.Fix.ShouldNotBeNullOrWhiteSpace();
            (thrown.Diagnostic.Message + thrown.Diagnostic.Reason + thrown.Diagnostic.Fix + thrown).ShouldNotContain(Token);
            return thrown.Diagnostic;
        }

        [Fact]
        public async Task A_release_is_downloaded_by_its_own_address_with_no_token()
        {
            var github = WithCrypto();

            var bytes = await Host(github, Token).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1000, TestContext.Current.CancellationToken);

            bytes.ShouldNotBeNull().ToArray().ShouldBe(Archive);
            var request = github.Requests.ShouldHaveSingleItem();
            request.Uri.ToString().ShouldBe("https://github.com/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz");
            request.Authorization.ShouldBeNull();
            request.UserAgent.ShouldBe("pmj/0.1.0");
        }

        [Theory]
        [InlineData("github.com/thatplatypus/grapevine", "crypto-v9.9.9", "crypto-9.9.9.pmj.tar.gz")]
        [InlineData("github.com/thatplatypus/grapevine", "crypto-v1.0.0", "other.tar.gz")]
        [InlineData("github.com/thatplatypus/nowhere", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz")]
        public async Task A_release_a_file_or_a_repository_that_is_not_there_is_no_bytes_and_no_error(string repository, string tag, string asset)
        {
            var bytes = await Host(WithCrypto()).DownloadAsync(Repository(repository), tag, asset, 1000, TestContext.Current.CancellationToken);

            bytes.ShouldBeNull();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_file_over_the_limit_is_refused_whether_or_not_its_length_is_said_first(bool saysLength)
        {
            var github = WithCrypto();
            github.SaysLength = saysLength;

            var diagnostic = await ShouldFail(
                () => Host(github).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", Archive.Length - 1, TestContext.Current.CancellationToken),
                DiagnosticCodes.ArchiveInvalid);

            diagnostic.Reason.ShouldContain($"{Archive.Length - 1} bytes");
        }

        [Fact]
        public async Task A_file_of_exactly_the_limit_is_downloaded()
        {
            var bytes = await Host(WithCrypto()).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", Archive.Length, TestContext.Current.CancellationToken);

            bytes.ShouldNotBeNull().Length.ShouldBe(Archive.Length);
        }

        [Fact]
        public async Task A_network_that_is_down_is_said_to_be_so_and_is_not_taken_for_a_missing_release()
        {
            var github = WithCrypto();
            github.Unreachable = true;

            var diagnostic = await ShouldFail(
                () => Host(github, Token).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1000, TestContext.Current.CancellationToken),
                DiagnosticCodes.GitHubUnreachable);

            diagnostic.Message.ShouldBe("GitHub could not be reached.");
            diagnostic.Reason.ShouldContain("crypto-v1.0.0");
        }

        [Fact]
        public async Task A_download_that_goes_quiet_part_way_is_given_up_and_said_to_be_the_network()
        {
            var github = WithCrypto();
            github.Stalls = true;
            var host = new GitHubReleaseHost(new HttpClient(github), userAgent: "pmj/0.1.0", quiet: TimeSpan.FromMilliseconds(50));

            var diagnostic = await ShouldFail(
                () => host.DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1024, TestContext.Current.CancellationToken),
                DiagnosticCodes.GitHubUnreachable);

            diagnostic.Reason.ShouldContain("the answer stopped coming");
        }

        [Fact]
        public async Task A_download_that_is_stopped_by_whoever_asked_for_it_is_stopped_and_is_not_blamed_on_the_network()
        {
            var github = WithCrypto();
            github.Stalls = true;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            stop.CancelAfter(TimeSpan.FromMilliseconds(50));

            await Should.ThrowAsync<OperationCanceledException>(
                () => Host(github).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1024, stop.Token));
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task An_answer_that_is_neither_the_file_nor_not_found_is_an_error_that_gives_the_status(HttpStatusCode status)
        {
            var github = WithCrypto();
            github.Answer = status;

            var diagnostic = await ShouldFail(
                () => Host(github).DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1000, TestContext.Current.CancellationToken),
                DiagnosticCodes.GitHubUnreachable);

            diagnostic.Reason.ShouldContain(((int)status).ToString());
        }

        [Fact]
        public async Task The_releases_of_a_repository_are_listed_from_the_api_and_drafts_are_left_out()
        {
            var github = WithCrypto()
                .Upload("thatplatypus/Grapevine", "crypto-v1.1.0-rc.1", "crypto-1.1.0-rc.1.pmj.tar.gz", Archive, prerelease: true)
                .Upload("thatplatypus/Grapevine", "crypto-v2.0.0", "crypto-2.0.0.pmj.tar.gz", Archive, draft: true)
                .Upload("thatplatypus/Grapevine", "grapevine-v0.3.0", "grapevine-0.3.0.pmj.tar.gz", Archive)
                .Upload("thatplatypus/Grapevine", "grapevine-v0.3.0", "notes.txt", Archive);

            var releases = await Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken);

            releases.Select(release => (release.Tag, release.IsPrerelease, string.Join(" ", release.Assets))).ShouldBe(
            [
                ("crypto-v1.0.0", false, "crypto-1.0.0.pmj.tar.gz"),
                ("crypto-v1.1.0-rc.1", true, "crypto-1.1.0-rc.1.pmj.tar.gz"),
                ("grapevine-v0.3.0", false, "grapevine-0.3.0.pmj.tar.gz notes.txt"),
            ]);
            var request = github.Requests.ShouldHaveSingleItem();
            request.Uri.ToString().ShouldBe("https://api.github.com/repos/thatplatypus/grapevine/releases?per_page=100&page=1");
            request.Authorization.ShouldBeNull();
            request.UserAgent.ShouldBe("pmj/0.1.0");
            request.Accept.ShouldBe("application/vnd.github+json");
        }

        [Fact]
        public async Task A_token_goes_to_the_api_and_to_nothing_else()
        {
            var github = WithCrypto();
            var host = Host(github, Token);

            await host.ListAsync(Grapevine, TestContext.Current.CancellationToken);
            await host.DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1000, TestContext.Current.CancellationToken);

            github.Requests[0].Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer " + Token);
            github.Requests[1].Authorization.ShouldBeNull();
        }

        [Fact]
        public async Task More_than_a_page_of_releases_is_asked_for_page_by_page()
        {
            var github = new FakeGitHub();
            for (var number = 0; number < 250; number++)
            {
                github.Upload("thatplatypus/Grapevine", $"crypto-v1.0.{number}", $"crypto-1.0.{number}.pmj.tar.gz", Archive);
            }

            var releases = await Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken);

            releases.Count.ShouldBe(250);
            github.Requests.Select(request => request.Uri.Query).ShouldBe(["?per_page=100&page=1", "?per_page=100&page=2", "?per_page=100&page=3"]);
        }

        [Fact]
        public async Task A_repository_that_is_not_there_or_has_released_nothing_has_no_releases()
        {
            var github = WithCrypto().Create("thatplatypus/empty");

            (await Host(github).ListAsync(Repository("github.com/thatplatypus/nowhere"), TestContext.Current.CancellationToken)).ShouldBeEmpty();
            (await Host(github).ListAsync(Repository("github.com/thatplatypus/empty"), TestContext.Current.CancellationToken)).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_limit_on_requests_is_said_to_be_that_with_when_it_lifts_and_how_to_raise_it()
        {
            var github = WithCrypto();
            github.RateLimited = true;

            var diagnostic = await ShouldFail(() => Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubRateLimited);

            // 1791599224 seconds after the first of January 1970.
            diagnostic.Reason.ShouldContain("2026-10-10 02:27 UTC");
            diagnostic.Fix.ShouldContain("GITHUB_TOKEN");
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task Being_told_to_come_back_later_is_the_limit_too_though_requests_remain(HttpStatusCode status)
        {
            // What GitHub's documentation says it answers to requests that come too fast, as against too many.
            var github = WithCrypto();
            github.Answer = status;
            github.AnswerHeaders["retry-after"] = "60";
            github.AnswerHeaders["x-ratelimit-remaining"] = "41";

            var diagnostic = await ShouldFail(() => Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubRateLimited);

            diagnostic.Reason.ShouldBe("its limit on requests has been reached");
        }

        [Fact]
        public async Task A_list_that_is_refused_for_another_reason_is_not_called_the_limit()
        {
            var github = WithCrypto();
            github.Answer = HttpStatusCode.Forbidden;
            github.AnswerHeaders["x-ratelimit-remaining"] = "41";

            var diagnostic = await ShouldFail(() => Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubUnreachable);

            diagnostic.Reason.ShouldContain("status 403");
        }

        [Fact]
        public async Task A_tag_or_a_file_name_is_one_part_of_the_address_whatever_is_in_it()
        {
            var github = WithCrypto();

            (await Host(github).DownloadAsync(Grapevine, "a/b?c#d", "e f%.gz", 1024, TestContext.Current.CancellationToken)).ShouldBeNull();

            var asked = github.Requests.ShouldHaveSingleItem().Uri;
            asked.AbsolutePath.ShouldBe("/thatplatypus/grapevine/releases/download/a%2Fb%3Fc%23d/e%20f%25.gz");
            asked.Query.ShouldBeEmpty();
            asked.Fragment.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_a_token_the_limit_is_something_to_wait_for()
        {
            var github = WithCrypto();
            github.RateLimited = true;

            var diagnostic = await ShouldFail(() => Host(github, Token).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubRateLimited);

            diagnostic.Fix.ShouldContain("wait");
        }

        [Fact]
        public async Task A_token_that_github_refuses_is_named_by_where_it_came_from_and_never_shown()
        {
            var github = WithCrypto();
            github.Answer = HttpStatusCode.Unauthorized;

            var diagnostic = await ShouldFail(() => Host(github, Token).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubUnreachable);

            diagnostic.Message.ShouldBe("GitHub refused the token it was given.");
            diagnostic.Fix.ShouldContain("GITHUB_TOKEN");
        }

        [Theory]
        [InlineData("this is not JSON")]
        [InlineData("{ \"message\": \"an object and not a list\" }")]
        [InlineData("[ { \"draft\": false } ]")]
        [InlineData("[ { \"tag_name\": 7 } ]")]
        [InlineData("[ { \"tag_name\": \"crypto-v1.0.0\", \"assets\": \"none\" } ]")]
        [InlineData("[ 7 ]")]
        public async Task A_list_that_is_not_what_github_sends_is_an_error_and_not_an_empty_list(string body)
        {
            var github = WithCrypto();
            github.ListBody = body;

            await ShouldFail(() => Host(github).ListAsync(Grapevine, TestContext.Current.CancellationToken), DiagnosticCodes.GitHubUnreachable);
        }

        [Fact]
        public async Task Another_address_can_stand_in_for_github_and_for_its_api()
        {
            var github = WithCrypto();
            var host = new GitHubReleaseHost(new HttpClient(github), new Uri("http://127.0.0.1:8080/mirror"), new Uri("http://127.0.0.1:8080/api/"), null, "pmj/0.1.0");

            await host.DownloadAsync(Grapevine, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1000, TestContext.Current.CancellationToken);
            await host.ListAsync(Grapevine, TestContext.Current.CancellationToken);

            github.Requests.Select(request => request.Uri.ToString()).ShouldBe(
            [
                "http://127.0.0.1:8080/mirror/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "http://127.0.0.1:8080/api/repos/thatplatypus/grapevine/releases?per_page=100&page=1",
            ]);
        }
    }
}
