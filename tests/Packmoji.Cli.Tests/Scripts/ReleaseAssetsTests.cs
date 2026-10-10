using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Packmoji.Cli.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Scripts
{
    /// <summary>
    /// <c>scripts/release-assets.sh</c>, which names the programs of a release and writes their
    /// digests. Whoever fetches a program holds it to one of those digests, so the names and the
    /// file of digests are held here, where a release is not needed to see them.
    /// </summary>
    public sealed class ReleaseAssetsTests : IDisposable
    {
        private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "scripts", "release-assets.sh");

        private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"))).FullName;

        private string Built => Path.Combine(_root, "built");

        private string Assets => Path.Combine(_root, "assets");

        public void Dispose() => Directory.Delete(_root, recursive: true);

        // What each machine built, as the release workflow gathers it: a directory for each runtime, with the program in it.
        private void Build(params (string Runtime, string Program, string Content)[] programs)
        {
            foreach (var (runtime, program, content) in programs)
            {
                Directory.CreateDirectory(Path.Combine(Built, runtime));
                File.WriteAllText(Path.Combine(Built, runtime, program), content);
            }
        }

        private void BuildAllThree() => Build(("linux-x64", "pmj", "the program for Linux\n"), ("osx-arm64", "pmj", "the program for macOS\n"), ("win-x64", "pmj.exe", "the program for Windows\n"));

        private async Task<ToolRun> RunAsync()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "A release is made on Linux, and the script is run by bash.");
            var run = await new ProcessToolRunner().RunAsync("/bin/bash", [Script, Built, Assets], _root, TestContext.Current.CancellationToken);
            return run.ShouldNotBeNull();
        }

        private static string Digest(string content) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));

        [Fact]
        public async Task The_guide_names_the_assets_of_a_release_as_the_script_names_them()
        {
            BuildAllThree();

            await RunAsync();

            var guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "cli.md"));
            var named = Regex.Matches(guide, @"^\| `(pmj-<version>[^`]*)` \|", RegexOptions.Multiline).Select(match => match.Groups[1].Value);
            named.Order(StringComparer.Ordinal).ShouldBe(
                Directory.GetFiles(Assets).Select(file => Path.GetFileName(file).Replace(PmjHost.Version, "<version>", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task The_three_programs_are_named_for_the_version_and_the_machine_and_their_digests_are_written_beside_them()
        {
            BuildAllThree();

            var run = await RunAsync();

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            var version = PmjHost.Version;
            Directory.GetFiles(Assets).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(
            [
                $"pmj-{version}-linux-x64",
                $"pmj-{version}-osx-arm64",
                $"pmj-{version}-win-x64.exe",
                $"pmj-{version}.sha256",
            ]);
            File.ReadAllText(Path.Combine(Assets, $"pmj-{version}-linux-x64")).ShouldBe("the program for Linux\n");
            File.ReadAllText(Path.Combine(Assets, $"pmj-{version}-win-x64.exe")).ShouldBe("the program for Windows\n");

            // As sha256sum writes and checks: the digest, two spaces, the name, and nothing of a directory.
            File.ReadAllText(Path.Combine(Assets, $"pmj-{version}.sha256")).ShouldBe(
                $"{Digest("the program for Linux\n")}  pmj-{version}-linux-x64\n"
                + $"{Digest("the program for macOS\n")}  pmj-{version}-osx-arm64\n"
                + $"{Digest("the program for Windows\n")}  pmj-{version}-win-x64.exe\n");
        }

        [Fact]
        public async Task It_says_the_digests_it_wrote_so_that_a_run_shows_them()
        {
            BuildAllThree();

            var run = await RunAsync();

            run.Output.ShouldContain($"{Digest("the program for macOS\n")}  pmj-{PmjHost.Version}-osx-arm64");
        }

        [Fact]
        public async Task A_program_that_was_not_built_stops_it_and_no_asset_is_left()
        {
            Build(("linux-x64", "pmj", "the program for Linux\n"), ("osx-arm64", "pmj", "the program for macOS\n"));

            var run = await RunAsync();

            run.Status.ShouldNotBe(0);
            run.Error.ShouldContain("win-x64");
            run.Error.ShouldContain("pmj.exe");
            (Directory.Exists(Assets) && Directory.EnumerateFileSystemEntries(Assets).Any()).ShouldBeFalse();
        }

        [Fact]
        public async Task Assets_that_are_there_already_are_not_written_over()
        {
            BuildAllThree();
            Directory.CreateDirectory(Assets);
            File.WriteAllText(Path.Combine(Assets, "pmj-0.0.1-linux-x64"), "what an earlier run left\n");

            var run = await RunAsync();

            run.Status.ShouldNotBe(0);
            run.Error.ShouldContain("is not empty");
            Directory.GetFiles(Assets).Select(Path.GetFileName).ShouldBe(["pmj-0.0.1-linux-x64"]);
        }
    }
}
