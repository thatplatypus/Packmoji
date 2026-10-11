using Shouldly;
using Xunit;

namespace Packmoji.Indexer.Tests
{
    /// <summary>
    /// <c>run</c>: the registry in a directory is brought to its next state from the releases of the
    /// listed repositories. What is held here is what is left on the disk and what is said, and that
    /// a registry or a list that does not read stops the command before anything is asked or written.
    /// </summary>
    public sealed class RunCommandTests : IDisposable
    {
        private const string G = MadeUpGitHub.Grapevine;

        private readonly Desk _desk = new();

        public void Dispose() => _desk.Dispose();

        private Desk WithTwoPackages()
        {
            _desk.GitHub.Package(G, "@thatplatypus/crypto", "0.1.0").Package(G, "@thatplatypus/grapevine", "0.1.0", "@thatplatypus/crypto@0.1");
            return _desk;
        }

        [Fact]
        public async Task A_first_run_makes_the_directory_and_writes_a_file_for_each_package()
        {
            var status = await WithTwoPackages().IndexAsync();

            status.ShouldBe(0);
            Desk.Files(_desk.Registry).ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/grapevine.json"]);
            Desk.Read(_desk.Registry, "v1/packages/thatplatypus/grapevine.json").ShouldBe(
                $$"""
                {
                  "version": 1,
                  "name": "@thatplatypus/grapevine",
                  "versions": [
                    {
                      "version": "0.1.0",
                      "status": "active",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "{{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_desk.GitHub.Bytes(G, "grapevine-v0.1.0")))}}",
                      "size": {{_desk.GitHub.Bytes(G, "grapevine-v0.1.0").Length}},
                      "verified": "checksum",
                      "immutable": false,
                      "published": "2026-10-11T15:02:11Z",
                      "indexed": "2026-10-11T15:17:40Z",
                      "dependencies": {
                        "@thatplatypus/crypto": "0.1"
                      }
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
            _desk.Out.ShouldBe(
                $"""
                added @thatplatypus/crypto 0.1.0 from {G}
                added @thatplatypus/grapevine 0.1.0 from {G}
                The registry holds 2 packages, 2 versions and no refused release.

                """.ReplaceLineEndings("\n"));
            _desk.Error.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_is_written_as_UTF_8_with_no_mark_before_it_and_with_the_line_ends_of_the_registry()
        {
            await WithTwoPackages().IndexAsync();

            var bytes = File.ReadAllBytes(Path.Combine(_desk.Registry, "v1", "packages", "thatplatypus", "crypto.json"));

            bytes[0].ShouldBe((byte)'{');
            bytes.ShouldNotContain((byte)'\r');
            bytes[^1].ShouldBe((byte)'\n');
        }

        [Fact]
        public async Task A_second_run_writes_nothing_and_says_only_what_the_registry_holds()
        {
            await WithTwoPackages().IndexAsync();
            var written = Desk.Files(_desk.Registry).ToDictionary(path => path, path => File.GetLastWriteTimeUtc(Path.Combine(_desk.Registry, path)));
            var held = Desk.Files(_desk.Registry).ToDictionary(path => path, path => Desk.Read(_desk.Registry, path));
            _desk.Clock = Desk.Now.AddHours(1);

            var status = await _desk.IndexAsync();

            status.ShouldBe(0);
            _desk.Out.ShouldBe("The registry holds 2 packages, 2 versions and no refused release.\n");
            foreach (var path in Desk.Files(_desk.Registry))
            {
                Desk.Read(_desk.Registry, path).ShouldBe(held[path]);
                File.GetLastWriteTimeUtc(Path.Combine(_desk.Registry, path)).ShouldBe(written[path]);
            }
        }

        [Fact]
        public async Task A_release_that_is_refused_is_written_down_and_said_and_its_file_goes_when_the_release_does()
        {
            _desk.GitHub.Package(G, "@thatplatypus/crypto", "0.1.0").Release(G, "deflate-v0.1.0", "deflate-0.1.0.pmj.tar.gz", [1, 2, 3]);

            await _desk.IndexAsync();

            Desk.Files(_desk.Registry).ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/refused/thatplatypus/grapevine.json"]);
            Desk.Read(_desk.Registry, "v1/refused/thatplatypus/grapevine.json").ShouldContain("\"why\": \"its file is not an archive as pmj pack writes one\"");
            _desk.Out.ShouldBe(
                $"""
                added @thatplatypus/crypto 0.1.0 from {G}
                refused deflate-v0.1.0 in {G}: its file is not an archive as pmj pack writes one
                The registry holds 1 package, 1 version and 1 refused release.

                """.ReplaceLineEndings("\n"));

            _desk.GitHub.Delete(G, "deflate-v0.1.0");
            await _desk.IndexAsync();

            Desk.Files(_desk.Registry).ShouldBe(["v1/packages/thatplatypus/crypto.json"]);
            _desk.Out.ShouldBe(
                $"""
                no longer refused: deflate-v0.1.0 is gone from {G}
                The registry holds 1 package, 1 version and no refused release.

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task A_run_that_finds_nothing_to_index_leaves_no_file_and_says_so()
        {
            var status = await _desk.IndexAsync();

            status.ShouldBe(0);
            Desk.Files(_desk.Registry).ShouldBeEmpty();
            _desk.Out.ShouldBe("The registry holds no package, no version and no refused release.\n");
        }

        [Fact]
        public async Task A_repository_that_cannot_be_reached_is_said_and_the_run_still_ends_well()
        {
            _desk.GitHub.Unreachable = true;

            var status = await _desk.IndexAsync();

            status.ShouldBe(0);
            _desk.Out.ShouldStartWith($"could not list the releases of {G} (github.unreachable)\n");
        }

        [Fact]
        public async Task The_moment_that_is_recorded_is_the_clocks()
        {
            _desk.Clock = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

            await WithTwoPackages().IndexAsync();

            Desk.Read(_desk.Registry, "v1/packages/thatplatypus/crypto.json").ShouldContain("\"indexed\": \"2027-01-02T03:04:05Z\"");
        }

        [Theory]
        [InlineData("github.com/thatplatypus/grapevine\nnot a repository\n", ":2:1: \"not a repository\" is not a valid repository.")]
        [InlineData("github.com/thatplatypus/grapevine\ngithub.com/thatplatypus/crypto\n", ":2:1: github.com/thatplatypus/crypto is out of order.")]
        public async Task A_list_that_does_not_read_stops_the_run_and_says_where_before_anything_is_asked_or_written(string list, string said)
        {
            File.WriteAllText(WithTwoPackages().List, list);

            var status = await _desk.IndexAsync();

            status.ShouldBe(1);
            _desk.Error.ShouldStartWith($"The list of repositories cannot be read.\n  {_desk.List}{said}");
            _desk.Out.ShouldBeEmpty();
            _desk.GitHub.Asked.ShouldBeEmpty();
            Directory.Exists(_desk.Registry).ShouldBeFalse();
        }

        [Fact]
        public async Task A_list_that_is_not_there_stops_the_run()
        {
            File.Delete(WithTwoPackages().List);

            var status = await _desk.IndexAsync();

            status.ShouldBe(1);
            _desk.Error.ShouldBe($"The list of repositories cannot be read.\n  {_desk.List} is not there, or cannot be opened.\n");
            _desk.GitHub.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_registry_that_does_not_read_stops_the_run_and_says_what_is_wrong_before_anything_is_asked_or_written()
        {
            await WithTwoPackages().IndexAsync();
            var crypto = Desk.Read(_desk.Registry, "v1/packages/thatplatypus/crypto.json");
            Desk.Put(_desk.Registry, "v1/packages/thatplatypus/crypto.json", crypto.Replace("\"active\"", "\"fine\""));
            Desk.Put(_desk.Registry, "v1/notes.txt", "a note");
            _desk.GitHub.Package(G, "@thatplatypus/deflate", "0.1.0").Asked.Clear();

            var status = await _desk.IndexAsync();

            status.ShouldBe(1);
            _desk.Error.ShouldBe(
                """
                The registry cannot be read.
                  v1/notes.txt is not a file of a registry.
                  v1/packages/thatplatypus/crypto.json:7:17: "fine" is not a status. That is because a version is "active", "yanked" or "quarantined".

                """.ReplaceLineEndings("\n"));
            _desk.Out.ShouldBeEmpty();
            _desk.GitHub.Asked.ShouldBeEmpty();
            Desk.Files(_desk.Registry).ShouldBe(["v1/notes.txt", "v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/grapevine.json"]);
        }

        [Fact]
        public async Task GitHub_as_the_environment_describes_it_has_to_be_usable_and_what_is_wrong_is_said_before_anything_is_written()
        {
            var status = await IndexerCommandLine.RunAsync(
                ["run", "--registry", _desk.Registry, "--repositories", _desk.List],
                _desk.Host(problem: "PACKMOJI_GITHUB is set and PACKMOJI_GITHUB_API is not."),
                TestContext.Current.CancellationToken);

            status.ShouldBe(1);
            _desk.Host().Error.ToString().ShouldBe("PACKMOJI_GITHUB is set and PACKMOJI_GITHUB_API is not." + Environment.NewLine);
            _desk.GitHub.Asked.ShouldBeEmpty();
            Directory.Exists(_desk.Registry).ShouldBeFalse();
        }
    }
}
