using Shouldly;
using Xunit;

namespace Packmoji.Indexer.Tests
{
    /// <summary>
    /// <c>check</c>: what a run made of a registry is held to the registry as it stood. It is what
    /// stands between a run and the registry's history, so what is held here is that it says yes to
    /// what a run may do, no to everything else, and writes the words of a commit only for a yes.
    /// </summary>
    public sealed class CheckCommandTests : IDisposable
    {
        private const string G = MadeUpGitHub.Grapevine;

        private readonly Desk _desk = new();

        public void Dispose() => _desk.Dispose();

        private string Message => Path.Combine(_desk.Root, "message.txt");

        /// <summary>A registry that holds crypto 0.1.0, as <c>before</c>, and a copy of it for a test to change, as <c>after</c>.</summary>
        private async Task<(string Before, string After)> BegunAsync()
        {
            _desk.GitHub.Package(G, "@thatplatypus/crypto", "0.1.0");
            await _desk.IndexAsync();
            return (_desk.Copy(_desk.Registry, "before"), _desk.Registry);
        }

        private Task<int> CheckAsync(string before, string after) => _desk.RunAsync("check", "--before", before, "--after", after, "--message", Message);

        [Fact]
        public async Task What_a_run_added_is_allowed_and_the_words_of_its_commit_are_written()
        {
            var (before, after) = await BegunAsync();
            _desk.GitHub.Package(G, "@thatplatypus/deflate", "0.1.0").Release(G, "json-v1.0.0", "json-1.0.0.pmj.tar.gz", [1, 2, 3]);
            await _desk.IndexAsync();
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_desk.GitHub.Bytes(G, "deflate-v0.1.0")));

            var status = await CheckAsync(before, after);

            status.ShouldBe(0);
            var changes = $"added @thatplatypus/deflate 0.1.0 from {G}, sha256 {digest}\nrefused json-v1.0.0 in {G}: the name json is one of the compiler's own packages\n";
            _desk.Out.ShouldBe(changes);
            _desk.Error.ShouldBeEmpty();
            File.ReadAllText(Message).ShouldBe("chore(registry): 1 added, 1 refused\n\n" + changes);
        }

        [Fact]
        public async Task A_registry_that_begins_is_checked_against_a_directory_that_is_not_there()
        {
            var (_, after) = await BegunAsync();

            var status = await CheckAsync(Path.Combine(_desk.Root, "nothing-yet"), after);

            status.ShouldBe(0);
            File.ReadAllText(Message).ShouldStartWith("chore(registry): 1 added\n\nadded @thatplatypus/crypto 0.1.0 ");
        }

        [Fact]
        public async Task When_nothing_changed_that_is_said_and_there_are_no_words_for_a_commit()
        {
            var (before, after) = await BegunAsync();
            File.WriteAllText(Message, "left from another time");

            var status = await CheckAsync(before, after);

            status.ShouldBe(0);
            _desk.Out.ShouldBe("Nothing has changed.\n");
            File.ReadAllText(Message).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_digest_that_was_changed_is_refused_and_no_words_are_written_for_a_commit()
        {
            var (before, after) = await BegunAsync();
            var crypto = Desk.Read(after, "v1/packages/thatplatypus/crypto.json");
            var recorded = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_desk.GitHub.Bytes(G, "crypto-v0.1.0")));
            var other = new string('a', 64);
            Desk.Put(after, "v1/packages/thatplatypus/crypto.json", crypto.Replace(recorded, other));
            File.WriteAllText(Message, "left from another time");

            var status = await CheckAsync(before, after);

            status.ShouldBe(1);
            _desk.Error.ShouldBe(
                $"""
                The registry may not become what the run made of it.
                  what is recorded of @thatplatypus/crypto 0.1.0 has changed: its sha256 was {recorded} and is {other}

                """.ReplaceLineEndings("\n"));
            _desk.Out.ShouldBeEmpty();
            File.ReadAllText(Message).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_that_was_removed_is_refused()
        {
            var (before, after) = await BegunAsync();
            File.Delete(Path.Combine(after, "v1", "packages", "thatplatypus", "crypto.json"));

            var status = await CheckAsync(before, after);

            status.ShouldBe(1);
            _desk.Error.ShouldContain("the file of @thatplatypus/crypto is gone, and a package is never removed");
        }

        [Theory]
        [InlineData("v1/packages/thatplatypus/crypto.json.bak", "{}", "v1/packages/thatplatypus/crypto.json.bak is not a file of a registry.")]
        [InlineData("README.md", "hello", "README.md is not a file of a registry.")]
        [InlineData("v1/packages/thatplatypus/deflate.json", "{ \"version\": 1 }", "v1/packages/thatplatypus/deflate.json:1:1: The key \"name\" is missing.")]
        [InlineData("v1/refused/thatplatypus/grapevine.json", "not JSON", "v1/refused/thatplatypus/grapevine.json:1:2: The file is not valid JSON.")]
        [InlineData("v1/packages/thatplatypus/deflate.json", "{ \"version\": 1, \"name\": \"@thatplatypus/crypto\", \"versions\": [] }", "The file is that of \"@thatplatypus/crypto\".")]
        public async Task What_a_run_made_has_to_be_a_registry_and_nothing_else(string path, string text, string said)
        {
            var (before, after) = await BegunAsync();
            Desk.Put(after, path, text);

            var status = await CheckAsync(before, after);

            status.ShouldBe(1);
            _desk.Error.ShouldStartWith("What the run made cannot be read as a registry.\n");
            _desk.Error.ShouldContain(said);
            File.ReadAllText(Message).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_registry_that_does_not_read_as_it_stands_is_said_to_be_that_and_not_the_runs_work()
        {
            var (before, after) = await BegunAsync();
            Desk.Put(before, "v1/packages/thatplatypus/crypto.json", "{}");

            var status = await CheckAsync(before, after);

            status.ShouldBe(1);
            _desk.Error.ShouldStartWith("The registry as it stands cannot be read.\n  v1/packages/thatplatypus/crypto.json:1:1: ");
        }

        [Fact]
        public async Task A_link_in_what_a_run_made_is_refused_and_is_not_followed()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "Making a symbolic link there takes a right that a test does not have.");
            var (before, after) = await BegunAsync();
            var elsewhere = _desk.Copy(after, "elsewhere");
            File.CreateSymbolicLink(Path.Combine(after, "v1", "packages", "thatplatypus", "deflate.json"), Path.Combine(elsewhere, "v1", "packages", "thatplatypus", "crypto.json"));
            Directory.CreateSymbolicLink(Path.Combine(after, "v1", "packages", "someone"), Path.Combine(elsewhere, "v1", "packages", "thatplatypus"));

            var status = await CheckAsync(before, after);

            status.ShouldBe(1);
            _desk.Error.ShouldBe(
                """
                What the run made cannot be read as a registry.
                  v1/packages/someone is a link, and a registry holds none.
                  v1/packages/thatplatypus/deflate.json is a link, and a registry holds none.

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task The_words_of_a_commit_need_not_be_asked_for()
        {
            var (before, after) = await BegunAsync();

            var status = await _desk.RunAsync("check", "--before", before, "--after", after);

            status.ShouldBe(0);
            File.Exists(Message).ShouldBeFalse();
        }

        [Fact]
        public async Task A_check_asks_nothing_of_GitHub()
        {
            var (before, after) = await BegunAsync();
            _desk.GitHub.Asked.Clear();

            await CheckAsync(before, after);

            _desk.GitHub.Asked.ShouldBeEmpty();
        }
    }
}
