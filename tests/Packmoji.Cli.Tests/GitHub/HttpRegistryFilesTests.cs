using System.Net;
using System.Text;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.GitHub;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.GitHub
{
    /// <summary>
    /// How pmj reads a registry: a file by its address, with no token, and nothing else. A registry
    /// that cannot be read is said to be that, and is never taken for a file that is not there.
    /// </summary>
    public sealed class HttpRegistryFilesTests
    {
        private const string Path = "v1/packages/thatplatypus/crypto.json";

        private static readonly byte[] File = Encoding.UTF8.GetBytes("{ \"the\": \"file\" }\n");

        private static FakeGitHub WithTheFile()
        {
            var github = new FakeGitHub();
            github.Registry[Path] = File;
            return github;
        }

        private static HttpRegistryFiles Files(FakeGitHub github, string address = FakeGitHub.RegistryAddress, TimeSpan? quiet = null) =>
            new(new HttpClient(github), new Uri(address), "pmj/0.1.0", quiet);

        private static async Task<Diagnostic> ShouldFail(Func<Task> act, string code)
        {
            var thrown = await Should.ThrowAsync<PackageSourceException>(act);
            thrown.Diagnostic.Code.ShouldBe(code);
            thrown.Diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
            thrown.Diagnostic.Reason.ShouldNotBeNullOrWhiteSpace();
            thrown.Diagnostic.Fix.ShouldContain("--direct");
            return thrown.Diagnostic;
        }

        [Fact]
        public async Task A_file_is_read_by_its_address_under_the_registrys_with_no_token_and_pmjs_own_name()
        {
            var github = WithTheFile();

            var bytes = await Files(github).ReadAsync(Path, 1000, TestContext.Current.CancellationToken);

            bytes.ShouldNotBeNull().ToArray().ShouldBe(File);
            var request = github.Requests.ShouldHaveSingleItem();
            request.Uri.ToString().ShouldBe("https://registry.example/files/v1/packages/thatplatypus/crypto.json");
            request.Authorization.ShouldBeNull();
            request.UserAgent.ShouldBe("pmj/0.1.0");
        }

        [Theory]
        [InlineData("https://registry.example/files/")]
        [InlineData("https://registry.example/files")]
        public async Task The_address_of_a_registry_is_the_same_with_a_slash_at_its_end_and_without(string address)
        {
            var github = WithTheFile();
            var files = Files(github, address);

            (await files.ReadAsync(Path, 1000, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            files.Address.ShouldBe("https://registry.example/files");
            github.RegistryReads.ShouldBe([Path]);
        }

        [Theory]
        [InlineData("ftp://registry.example/files")]
        [InlineData("https://someone:a-password@registry.example/files")]
        [InlineData("https://registry.example/files?ref=main")]
        [InlineData("https://registry.example/files#v1")]
        public void An_address_that_could_not_be_a_registrys_is_refused_and_is_not_said_again(string address)
        {
            var thrown = Should.Throw<ArgumentException>(() => Files(new FakeGitHub(), address));

            thrown.ParamName.ShouldBe("address");
            thrown.Message.ShouldNotContain("a-password");
            thrown.Message.ShouldNotContain("registry.example");
        }

        [Fact]
        public async Task A_file_the_registry_does_not_have_is_no_file_and_no_error()
        {
            (await Files(WithTheFile()).ReadAsync("v1/packages/thatplatypus/deflate.json", 1000, TestContext.Current.CancellationToken)).ShouldBeNull();
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.NoContent)]
        public async Task An_answer_that_is_neither_the_file_nor_that_there_is_none_is_the_registry_not_reached_and_gives_the_status(HttpStatusCode status)
        {
            var github = WithTheFile();
            github.RegistryAnswer = status;

            var diagnostic = await ShouldFail(() => Files(github).ReadAsync(Path, 1000, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryUnreachable);

            diagnostic.Message.ShouldBe("The registry could not be read.");
            diagnostic.Reason.ShouldBe($"the request for https://registry.example/files/{Path} was answered with status {(int)status}");
        }

        [Fact]
        public async Task A_network_that_is_down_is_said_to_be_so_and_is_not_taken_for_a_file_that_is_not_there()
        {
            var github = WithTheFile();
            github.Unreachable = true;

            var diagnostic = await ShouldFail(() => Files(github).ReadAsync(Path, 1000, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryUnreachable);

            diagnostic.Reason.ShouldStartWith($"the request for https://registry.example/files/{Path} failed: ");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_file_over_the_limit_is_refused_whether_or_not_its_length_is_said_first(bool saysLength)
        {
            var github = WithTheFile();
            github.SaysLength = saysLength;

            var diagnostic = await ShouldFail(() => Files(github).ReadAsync(Path, File.Length - 1, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryInvalid);

            diagnostic.Reason.ShouldBe($"https://registry.example/files/{Path} is more than {File.Length - 1} bytes, which is the most a file of a registry may be");
        }

        [Fact]
        public async Task A_file_of_exactly_the_limit_is_read() =>
            (await Files(WithTheFile()).ReadAsync(Path, File.Length, TestContext.Current.CancellationToken)).ShouldNotBeNull().Length.ShouldBe(File.Length);

        // With a limit of its own, for what this holds is that pmj does not wait for ever, and a test that waited for ever would hold nothing.
        [Fact(Timeout = 10_000)]
        public async Task A_file_that_goes_quiet_part_way_is_given_up_and_said_to_be_the_registry_not_reached()
        {
            var github = WithTheFile();
            github.Stalls = true;

            var diagnostic = await ShouldFail(() => Files(github, quiet: TimeSpan.FromMilliseconds(200)).ReadAsync(Path, 1000, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryUnreachable);

            diagnostic.Reason.ShouldEndWith("the answer stopped coming");
        }

        [Fact]
        public async Task A_read_that_is_stopped_by_whoever_asked_for_it_is_stopped_and_is_not_blamed_on_the_registry()
        {
            using var stop = new CancellationTokenSource();
            await stop.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => Files(WithTheFile()).ReadAsync(Path, 1000, stop.Token));
        }
    }
}
