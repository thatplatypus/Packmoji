using Packmoji.Cli.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Scripts
{
    /// <summary>
    /// <c>scripts/registry-tree.sh</c> and <c>scripts/registry-commit.sh</c>, which stand between the
    /// indexer and the branch the registry is kept on. Each test has a repository of its own to push
    /// to, made on the spot, so that what is held is what git does with them and nothing is pushed
    /// anywhere real.
    /// </summary>
    public sealed class RegistryScriptsTests : IDisposable
    {
        private const string Bot = "github-actions[bot] <41898282+github-actions[bot]@users.noreply.github.com>";
        private const string Words = "chore(registry): 1 added\n\nadded @thatplatypus/crypto 0.1.0 from github.com/thatplatypus/grapevine, sha256 e3b0\n";

        private static readonly string Tree = Path.Combine(AppContext.BaseDirectory, "scripts", "registry-tree.sh");
        private static readonly string Commit = Path.Combine(AppContext.BaseDirectory, "scripts", "registry-commit.sh");

        // Git with nothing of the machine's own settings, so that no hook and no signing of its owner's
        // is run by a test, and with a clock that stands still.
        private static readonly string[] Quiet =
        [
            "GIT_CONFIG_GLOBAL=/dev/null",
            "GIT_CONFIG_SYSTEM=/dev/null",
            "GIT_CONFIG_NOSYSTEM=1",
            "GIT_TERMINAL_PROMPT=0",
            "GIT_AUTHOR_NAME=A Test",
            "GIT_AUTHOR_EMAIL=test@example.com",
            "GIT_COMMITTER_NAME=A Test",
            "GIT_COMMITTER_EMAIL=test@example.com",
            "GIT_AUTHOR_DATE=2026-10-11T15:17:40Z",
            "GIT_COMMITTER_DATE=2026-10-11T15:17:40Z",
        ];

        private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"))).FullName;
        private int _seeds;

        /// <summary>The repository that is pushed to, which stands where GitHub does.</summary>
        private string Origin => Path.Combine(_root, "origin.git");

        /// <summary>The repository the scripts are run in, as a workflow's checkout is.</summary>
        private string Work => Path.Combine(_root, "work");

        private string Taken => Path.Combine(_root, "taken");

        private string Told => Path.Combine(_root, "told.txt");

        private string WordsFile => Path.Combine(_root, "words.txt");

        public void Dispose()
        {
            // A file that git left read-only cannot be deleted on every machine until it is said that it may be.
            foreach (var file in Directory.EnumerateFiles(_root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }

        private async Task<ToolRun> RunAsync(string directory, IEnumerable<string> environment, params string[] command)
        {
            var run = await new ProcessToolRunner().RunAsync("/usr/bin/env", [.. Quiet, .. environment, .. command], directory, TestContext.Current.CancellationToken);
            return run.ShouldNotBeNull();
        }

        private async Task<string> GitAsync(string directory, params string[] arguments)
        {
            var run = await RunAsync(directory, [], ["git", .. arguments]);
            run.Status.ShouldBe(0, run.Error);
            return run.Output.ReplaceLineEndings("\n");
        }

        /// <summary>Makes the repository that is pushed to, and the one the scripts are run in, which has pushed its own first commit there.</summary>
        private async Task BeginAsync()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "The scripts are run by bash, which a test does not look for on Windows.");
            await GitAsync(_root, "init", "--quiet", "--bare", "--initial-branch=main", Origin);
            await GitAsync(_root, "init", "--quiet", "--initial-branch=main", Work);
            File.WriteAllText(Path.Combine(Work, "README.md"), "the tool\n");
            await GitAsync(Work, "add", "README.md");
            await GitAsync(Work, "commit", "--quiet", "-m", "chore: begin");
            await GitAsync(Work, "remote", "add", "origin", Origin);
            await GitAsync(Work, "push", "--quiet", "origin", "main");
            File.WriteAllText(WordsFile, Words);
        }

        /// <summary>Puts a commit of these files on the registry's branch, from somewhere else, as a person or an earlier run would. Gives its id.</summary>
        private async Task<string> SeedAsync(string branch, params (string Path, string Text)[] files)
        {
            var seed = Path.Combine(_root, $"seed-{++_seeds}");
            await GitAsync(_root, "init", "--quiet", "--initial-branch=seed", seed);
            await GitAsync(seed, "remote", "add", "origin", Origin);
            if ((await RunAsync(seed, [], "git", "fetch", "--quiet", "origin", $"refs/heads/{branch}")).Status == 0)
            {
                await GitAsync(seed, "reset", "--quiet", "--hard", "FETCH_HEAD");
            }

            foreach (var file in Directory.EnumerateFiles(seed, "*", SearchOption.AllDirectories).Where(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                File.Delete(file);
            }

            foreach (var (path, text) in files)
            {
                Put(seed, path, text);
            }

            await GitAsync(seed, "add", "--all");
            await GitAsync(seed, "commit", "--quiet", "--allow-empty", "-m", $"chore(registry): seed {_seeds}");
            await GitAsync(seed, "push", "--quiet", "origin", $"HEAD:refs/heads/{branch}");
            return (await GitAsync(seed, "rev-parse", "HEAD")).Trim();
        }

        private static void Put(string directory, string path, string text)
        {
            var file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        private static string[] Files(string directory) =>
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToArray()
                : [];

        private Task<ToolRun> TreeAsync(params string[] environment) => RunAsync(Work, environment, "/bin/bash", Tree, Taken, Told);

        private Task<ToolRun> CommitAsync(params string[] environment) => RunAsync(Work, environment, "/bin/bash", Commit, Taken, WordsFile, Told);

        /// <summary>Where a branch of the repository that is pushed to is, or nothing when it has no such branch.</summary>
        private async Task<string> TipAsync(string branch = "registry")
        {
            var run = await RunAsync(Origin, [], "git", "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}");
            return run.Status == 0 ? run.Output.Trim() : "";
        }

        private async Task<string[]> HeldAsync(string branch = "registry") =>
            (await GitAsync(Origin, "ls-tree", "-r", "--name-only", branch)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        [Fact]
        public async Task With_no_branch_yet_the_registry_is_taken_as_nothing_and_no_commit_is_told()
        {
            await BeginAsync();
            File.WriteAllText(Told, "left from another time");

            var run = await TreeAsync();

            run.Status.ShouldBe(0, run.Error);
            run.Output.ShouldContain("has no branch \"registry\" yet");
            Directory.Exists(Taken).ShouldBeTrue();
            Files(Taken).ShouldBeEmpty();
            File.ReadAllText(Told).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_v1_of_the_branch_is_taken_and_nothing_else_of_it_and_its_commit_is_told()
        {
            await BeginAsync();
            var seeded = await SeedAsync(
                "registry",
                ("README.md", "what the branch is\n"),
                ("notes.txt", "a note\n"),
                ("v1/packages/thatplatypus/crypto.json", "{ \"crypto\": 🍇 }\n"),
                ("v1/refused/thatplatypus/grapevine.json", "{}\n"));

            var run = await TreeAsync();

            run.Status.ShouldBe(0, run.Error);
            Files(Taken).ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/refused/thatplatypus/grapevine.json"]);
            File.ReadAllText(Path.Combine(Taken, "v1", "packages", "thatplatypus", "crypto.json")).ShouldBe("{ \"crypto\": 🍇 }\n");
            File.ReadAllText(Told).ShouldBe(seeded + "\n");
            run.Output.ShouldContain(seeded);
        }

        [Fact]
        public async Task A_branch_that_holds_no_v1_is_a_registry_that_holds_nothing_and_its_commit_is_still_told()
        {
            await BeginAsync();
            var seeded = await SeedAsync("registry", ("README.md", "what the branch is\n"));

            var run = await TreeAsync();

            run.Status.ShouldBe(0, run.Error);
            Files(Taken).ShouldBeEmpty();
            File.ReadAllText(Told).ShouldBe(seeded + "\n");
        }

        [Fact]
        public async Task A_directory_that_holds_something_is_not_written_into()
        {
            await BeginAsync();
            await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            Put(Taken, "mine.txt", "mine\n");

            var run = await TreeAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("holds something already");
            Files(Taken).ShouldBe(["mine.txt"]);
        }

        [Theory]
        [InlineData("nowhere")]
        [InlineData("gone")]
        public async Task A_remote_that_cannot_be_asked_is_not_taken_for_a_registry_that_has_not_begun(string remote)
        {
            await BeginAsync();
            await GitAsync(Work, "remote", "add", "gone", Path.Combine(_root, "no-such-repository.git"));

            var run = await TreeAsync($"REGISTRY_REMOTE={remote}");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"\"{remote}\" could not be asked");
            run.Output.ShouldNotContain("holds nothing");
            File.ReadAllText(Told).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_branch_is_the_one_that_is_named_and_another_is_not_looked_at()
        {
            await BeginAsync();
            await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            var rehearsal = await SeedAsync("registry-rehearsal", ("v1/packages/thatplatypus/deflate.json", "{}\n"));

            var run = await TreeAsync("REGISTRY_BRANCH=registry-rehearsal");

            run.Status.ShouldBe(0, run.Error);
            Files(Taken).ShouldBe(["v1/packages/thatplatypus/deflate.json"]);
            File.ReadAllText(Told).ShouldBe(rehearsal + "\n");
        }

        [Theory]
        [InlineData("a branch")]
        [InlineData("-registry")]
        [InlineData("registry..old")]
        public async Task A_name_that_no_branch_can_have_is_refused_by_both(string branch)
        {
            await BeginAsync();
            File.WriteAllText(Told, "");
            Directory.CreateDirectory(Taken);

            var tree = await RunAsync(Work, [$"REGISTRY_BRANCH={branch}"], "/bin/bash", Tree, Path.Combine(_root, "other"), Told);
            var commit = await CommitAsync($"REGISTRY_BRANCH={branch}");

            tree.Status.ShouldBe(1);
            tree.Error.ShouldContain("cannot be the name of a branch");
            commit.Status.ShouldBe(1);
            commit.Error.ShouldContain("cannot be the name of a branch");
        }

        [Fact]
        public async Task Asked_in_any_other_way_each_says_how_it_is_asked()
        {
            await BeginAsync();

            var tree = await RunAsync(Work, [], "/bin/bash", Tree);
            var commit = await RunAsync(Work, [], "/bin/bash", Commit, Taken, WordsFile);

            tree.Status.ShouldBe(2);
            tree.Error.ShouldStartWith("usage: scripts/registry-tree.sh ");
            commit.Status.ShouldBe(2);
            commit.Error.ShouldStartWith("usage: scripts/registry-commit.sh ");
        }

        [Fact]
        public async Task The_first_commit_makes_the_branch_with_the_registry_and_a_page_that_says_what_the_branch_is()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{ \"crypto\": 🍇 }\n");

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            (await HeldAsync()).ShouldBe(["README.md", "v1/packages/thatplatypus/crypto.json"]);
            (await GitAsync(Origin, "log", "-1", "--format=%an <%ae>|%cn <%ce>|%P|", "registry")).Trim().ShouldBe($"{Bot}|{Bot}||");
            (await GitAsync(Origin, "log", "-1", "--format=%B", "registry")).TrimEnd('\n').ShouldBe(Words.TrimEnd('\n'));
            (await GitAsync(Origin, "show", "registry:README.md")).ShouldStartWith("# The registry\n");
            (await GitAsync(Origin, "show", "registry:v1/packages/thatplatypus/crypto.json")).ShouldBe("{ \"crypto\": 🍇 }\n");
            run.Output.ShouldContain(await TipAsync());
        }

        [Fact]
        public async Task A_commit_follows_the_one_the_registry_was_taken_from_and_changes_the_v1_and_nothing_else()
        {
            await BeginAsync();
            var seeded = await SeedAsync(
                "registry",
                ("README.md", "written by hand\n"),
                ("notes.txt", "a note\n"),
                ("v1/packages/thatplatypus/crypto.json", "{ \"before\": true }\n"),
                ("v1/refused/thatplatypus/grapevine.json", "{}\n"));
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{ \"before\": false }\n");
            Put(Taken, "v1/packages/thatplatypus/deflate.json", "{}\n");
            File.Delete(Path.Combine(Taken, "v1", "refused", "thatplatypus", "grapevine.json"));

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            (await HeldAsync()).ShouldBe(["README.md", "notes.txt", "v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/deflate.json"]);
            (await GitAsync(Origin, "log", "-1", "--format=%P", "registry")).Trim().ShouldBe(seeded);
            (await GitAsync(Origin, "show", "registry:README.md")).ShouldBe("written by hand\n");
            (await GitAsync(Origin, "show", "registry:v1/packages/thatplatypus/crypto.json")).ShouldBe("{ \"before\": false }\n");
        }

        [Fact]
        public async Task A_registry_that_has_come_to_hold_nothing_is_committed_as_no_v1_at_all()
        {
            await BeginAsync();
            await SeedAsync("registry", ("README.md", "written by hand\n"), ("v1/refused/thatplatypus/grapevine.json", "{}\n"));
            (await TreeAsync()).Status.ShouldBe(0);
            Directory.Delete(Path.Combine(Taken, "v1"), recursive: true);

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            (await HeldAsync()).ShouldBe(["README.md"]);
        }

        [Fact]
        public async Task With_no_words_there_is_nothing_to_commit_and_the_branch_stays_where_it_is()
        {
            await BeginAsync();
            var seeded = await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/deflate.json", "{}\n");
            File.WriteAllText(WordsFile, "");

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            run.Output.ShouldContain("nothing is committed");
            (await TipAsync()).ShouldBe(seeded);
        }

        [Fact]
        public async Task When_the_branch_holds_the_registry_already_nothing_is_committed()
        {
            await BeginAsync();
            var seeded = await SeedAsync("registry", ("README.md", "written by hand\n"), ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            (await TreeAsync()).Status.ShouldBe(0);

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            run.Output.ShouldContain("holds this already");
            (await TipAsync()).ShouldBe(seeded);
        }

        [Fact]
        public async Task A_branch_that_has_moved_since_refuses_the_commit_and_keeps_what_it_has()
        {
            await BeginAsync();
            await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            (await TreeAsync()).Status.ShouldBe(0);
            var moved = await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{ \"by\": \"a person\" }\n"));
            Put(Taken, "v1/packages/thatplatypus/deflate.json", "{}\n");

            var run = await CommitAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("refused the commit");
            (await TipAsync()).ShouldBe(moved);
            (await HeldAsync()).ShouldBe(["v1/packages/thatplatypus/crypto.json"]);
        }

        [Fact]
        public async Task A_branch_that_was_made_since_refuses_a_first_commit()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            var made = await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            Put(Taken, "v1/packages/thatplatypus/deflate.json", "{}\n");

            var run = await CommitAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("refused the commit");
            (await TipAsync()).ShouldBe(made);
        }

        [Fact]
        public async Task A_link_is_not_committed_and_no_branch_is_made()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{}\n");
            File.CreateSymbolicLink(Path.Combine(Taken, "v1", "packages", "thatplatypus", "deflate.json"), Path.Combine(Work, "README.md"));

            var run = await CommitAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("\"v1/packages/thatplatypus/deflate.json\" is not a plain file");
            (await TipAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_that_could_be_run_is_not_committed()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "#!/bin/sh\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(Taken, "v1", "packages", "thatplatypus", "crypto.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var run = await CommitAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("is not a plain file");
            (await TipAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task What_stands_beside_the_v1_in_the_directory_is_not_committed()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{}\n");
            Put(Taken, "README.md", "not the branch's\n");
            Put(Taken, "v2/packages/thatplatypus/crypto.json", "{}\n");
            Put(Taken, ".github/workflows/mine.yml", "on: push\n");

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            (await HeldAsync()).ShouldBe(["README.md", "v1/packages/thatplatypus/crypto.json"]);
            (await GitAsync(Origin, "show", "registry:README.md")).ShouldStartWith("# The registry\n");
        }

        [Theory]
        [InlineData("main", "does not hold the id of a commit")]
        [InlineData("refs/heads/registry", "does not hold the id of a commit")]
        [InlineData("0000000000000000000000000000000000000000", "is not here")]
        public async Task What_is_told_of_the_commit_to_follow_has_to_be_the_id_of_one_that_is_here(string told, string said)
        {
            await BeginAsync();
            await SeedAsync("registry", ("v1/packages/thatplatypus/crypto.json", "{}\n"));
            Put(Taken, "v1/packages/thatplatypus/deflate.json", "{}\n");
            File.WriteAllText(Told, told + "\n");

            var run = await CommitAsync();

            run.Status.ShouldBe(1);
            run.Error.ShouldContain(said);
        }

        [Fact]
        public async Task The_files_of_the_repository_it_is_run_in_and_what_is_staged_there_are_left_as_they_were()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{}\n");
            Put(Work, "staged.txt", "staged\n");
            await GitAsync(Work, "add", "staged.txt");
            Put(Work, "README.md", "changed and not staged\n");
            var before = await GitAsync(Work, "status", "--porcelain");

            var run = await CommitAsync();

            run.Status.ShouldBe(0, run.Error);
            (await GitAsync(Work, "status", "--porcelain")).ShouldBe(before);
            before.ShouldBe(" M README.md\nA  staged.txt\n", StringCompareShould.IgnoreLineEndings);
            (await GitAsync(Work, "rev-parse", "--abbrev-ref", "HEAD")).Trim().ShouldBe("main");
            (await HeldAsync()).ShouldBe(["README.md", "v1/packages/thatplatypus/crypto.json"]);
        }

        [Fact]
        public async Task A_commit_goes_to_the_branch_that_is_named()
        {
            await BeginAsync();
            (await TreeAsync("REGISTRY_BRANCH=registry-rehearsal")).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{}\n");

            var run = await CommitAsync("REGISTRY_BRANCH=registry-rehearsal");

            run.Status.ShouldBe(0, run.Error);
            (await TipAsync()).ShouldBeEmpty();
            (await HeldAsync("registry-rehearsal")).ShouldBe(["README.md", "v1/packages/thatplatypus/crypto.json"]);
        }

        [Fact]
        public async Task Whoever_is_named_as_the_author_is_the_author()
        {
            await BeginAsync();
            (await TreeAsync()).Status.ShouldBe(0);
            Put(Taken, "v1/packages/thatplatypus/crypto.json", "{}\n");

            (await CommitAsync("REGISTRY_AUTHOR_NAME=The Keeper", "REGISTRY_AUTHOR_EMAIL=keeper@example.com")).Status.ShouldBe(0);

            (await GitAsync(Origin, "log", "-1", "--format=%an <%ae>|%cn <%ce>", "registry")).Trim().ShouldBe("The Keeper <keeper@example.com>|The Keeper <keeper@example.com>");
        }
    }
}
