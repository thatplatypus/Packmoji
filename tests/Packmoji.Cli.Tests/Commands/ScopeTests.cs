using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// <c>PACKMOJI_SCOPES</c>: the scopes a pmj may depend on. It is set on a machine that runs other
    /// people's projects, so a package outside it is refused wherever it is met, and before anything
    /// is asked of GitHub about it.
    /// </summary>
    public sealed class ScopeTests
    {
        private const string Other = "github.com/someone/thing";

        // Grapevine's three packages of thatplatypus, and one package of someone else.
        private static Sandbox WithTwoOwners()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Release(Other, "@someone/thing", "1.0.0");
            return sandbox;
        }

        // A project that locks packages of both owners, installed while there was no limit.
        private static async Task<Sandbox> WithBothLockedAsync()
        {
            var sandbox = WithTwoOwners();
            await sandbox.InstallAsync("@you/site", "@thatplatypus/grapevine@0.3", "@someone/thing@1.0");
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.GitHub.Requests.Clear();
            return sandbox;
        }

        private static void ShouldHaveAskedOnlyOf(Sandbox sandbox, string owner) =>
            sandbox.GitHub.Requests.Select(request => request.Uri.AbsolutePath).ShouldAllBe(path => path.Contains($"/{owner}/", StringComparison.Ordinal));

        [Fact]
        public async Task A_package_outside_the_list_that_the_manifest_asks_for_is_refused_before_anything_is_asked_of_GitHub()
        {
            using var sandbox = WithTwoOwners();
            sandbox.Project("@you/site", "@thatplatypus/grapevine@0.3", "@someone/thing@1.0");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus,emojicode";

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[scope.not-allowed]: "@someone/thing" is outside the scopes pmj is limited to here.
                  why: packmoji.json asks for it, and PACKMOJI_SCOPES allows only emojicode and thatplatypus
                  fix: depend only on packages of those scopes; the list is set by whoever runs pmj here

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Theory]
        [InlineData("install")]
        [InlineData("install", "--locked")]
        [InlineData("tree")]
        [InlineData("verify")]
        [InlineData("update")]
        [InlineData("remove", "@thatplatypus/grapevine")]
        [InlineData("add", "@thatplatypus/crypto@1.0")]
        [InlineData("build")]
        [InlineData("build", "--dependencies-only")]
        [InlineData("run")]
        public async Task A_package_outside_the_list_that_the_lockfile_holds_is_refused_by_every_command_that_reads_what_a_project_depends_on(params string[] command)
        {
            using var sandbox = await WithBothLockedAsync();
            var (manifest, lockfile) = (sandbox.Read("packmoji.json"), sandbox.Read("packmoji.lock"));
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync(command);

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[scope.not-allowed]: \"@someone/thing\" is outside the scopes pmj is limited to here." + Environment.NewLine);

            // Both files name it, and it is the manifest that a person can change.
            run.Error.ShouldContain("  why: packmoji.json asks for it, and PACKMOJI_SCOPES allows only thatplatypus");
            run.Error.ShouldContain("  fix: depend only on packages of that scope; the list is set by whoever runs pmj here");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Tools.Calls.ShouldBeEmpty();
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(lockfile);
        }

        [Fact]
        public async Task A_package_that_only_the_lockfile_holds_is_said_to_be_held_there()
        {
            // Locked while there was no limit: an allowed package brought it with it, and the manifest never named it.
            using var sandbox = WithTwoOwners();
            sandbox.Release("github.com/thatplatypus/wrapper", "@thatplatypus/wrapper", "1.0.0", "@someone/thing@1.0");
            await sandbox.InstallAsync("@you/site", "@thatplatypus/wrapper@1.0");
            sandbox.GitHub.Requests.Clear();
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[scope.not-allowed]: \"@someone/thing\" is outside the scopes pmj is limited to here." + Environment.NewLine);
            run.Error.ShouldContain("  why: packmoji.lock holds it, and PACKMOJI_SCOPES allows only thatplatypus");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        // A project that locked packages of both owners, and whose manifest has since been mended to ask for the allowed one alone.
        private static async Task<Sandbox> WithAMendedManifestAsync()
        {
            var sandbox = await WithBothLockedAsync();
            sandbox.Project("@you/site", "@thatplatypus/grapevine@0.3");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";
            return sandbox;
        }

        [Theory]
        [InlineData("install", "--locked")]
        [InlineData("tree")]
        [InlineData("verify")]
        [InlineData("build", "--dependencies-only")]
        public async Task Once_the_manifest_no_longer_asks_for_it_a_command_that_needs_the_lockfile_says_only_that_the_lockfile_is_out_of_date(params string[] command)
        {
            using var sandbox = await WithAMendedManifestAsync();

            var run = await sandbox.RunAsync(command);

            // Which is what tells whoever ran it to resolve, and resolving is what mends the lockfile.
            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[lock.out-of-date]: ");
            run.Error.ShouldNotContain("scope.not-allowed");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("install")]
        [InlineData("update")]
        [InlineData("remove", "@thatplatypus/grapevine")]
        [InlineData("add", "@thatplatypus/crypto@1.0", "--repository", "github.com/thatplatypus/grapevine")]
        public async Task Once_the_manifest_no_longer_asks_for_it_a_command_that_resolves_locks_the_project_again_without_it(params string[] command)
        {
            using var sandbox = await WithAMendedManifestAsync();
            sandbox.ForgetCache();

            var run = await sandbox.RunAsync(command);

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Lockfile().Packages.ShouldAllBe(package => package.Name.Scope == "thatplatypus");
            ShouldHaveAskedOnlyOf(sandbox, "thatplatypus");
        }

        [Fact]
        public async Task A_package_outside_the_list_that_is_asked_for_only_to_develop_is_refused_as_any_other_is()
        {
            // The lockfile is from before the manifest asked for it, so only the manifest names it.
            using var sandbox = WithTwoOwners();
            await sandbox.InstallAsync("@you/site", "@thatplatypus/grapevine@0.3");
            sandbox.Project("@you/site", "@thatplatypus/grapevine@0.3", "dev:@someone/thing@1.0");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[scope.not-allowed]: \"@someone/thing\" is outside the scopes pmj is limited to here." + Environment.NewLine);
            run.Error.ShouldContain("  why: packmoji.json asks for it, and PACKMOJI_SCOPES allows only thatplatypus");
        }

        [Fact]
        public async Task Every_package_outside_the_list_is_named_once_in_order_of_name()
        {
            using var sandbox = WithTwoOwners();
            sandbox.Release("github.com/another/apple", "@another/apple", "2.0.0");
            await sandbox.InstallAsync("@you/site", "@someone/thing@1.0", "@thatplatypus/grapevine@0.3", "dev:@another/apple@2.0");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(1);
            run.Error.Split(Environment.NewLine).Where(line => line.StartsWith("error[", StringComparison.Ordinal)).ShouldBe(
            [
                "error[scope.not-allowed]: \"@another/apple\" is outside the scopes pmj is limited to here.",
                "error[scope.not-allowed]: \"@someone/thing\" is outside the scopes pmj is limited to here.",
            ]);
        }

        [Fact]
        public async Task A_lockfile_that_names_more_packages_outside_the_list_than_anyone_reads_is_answered_with_a_hundred_and_a_count()
        {
            // A stranger's file decides how many there are, so the answer must not grow with it.
            using var sandbox = Sandbox.WithGrapevine();
            var names = Enumerable.Range(0, 105).Select(index => $"@crowd/pkg{index:000}").ToList();
            foreach (var name in names)
            {
                sandbox.Release($"github.com/crowd/{name[7..]}", name, "1.0.0");
            }

            await sandbox.InstallAsync("@you/site", [.. names.Select(name => name + "@1.0")]);
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var tool = await sandbox.RunAsync("tree", "--json");
            var person = await sandbox.RunAsync("tree");

            using var answer = System.Text.Json.JsonDocument.Parse(tool.Output);
            answer.RootElement.GetProperty("diagnostics").GetArrayLength().ShouldBe(100);
            answer.RootElement.GetProperty("omittedDiagnostics").GetInt32().ShouldBe(5);
            person.Error.Split(Environment.NewLine).Count(line => line.StartsWith("error[scope.not-allowed]", StringComparison.Ordinal)).ShouldBe(100);
            person.Error.ShouldContain("5 more");
        }

        [Fact]
        public async Task Add_refuses_a_package_outside_the_list_before_it_asks_for_its_versions_and_changes_nothing()
        {
            using var sandbox = WithTwoOwners();
            sandbox.Project("@you/site");
            var manifest = sandbox.Read("packmoji.json");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("add", "@someone/thing");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[scope.not-allowed]: "@someone/thing" is outside the scopes pmj is limited to here.
                  why: pmj add was given it, and PACKMOJI_SCOPES allows only thatplatypus
                  fix: depend only on packages of that scope; the list is set by whoever runs pmj here

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_package_outside_the_list_that_an_allowed_package_depends_on_is_refused_and_its_owner_is_never_asked()
        {
            using var sandbox = WithTwoOwners();
            sandbox.Release("github.com/thatplatypus/wrapper", "@thatplatypus/wrapper", "1.0.0", "@someone/thing@1.0");
            sandbox.Project("@you/site", "@thatplatypus/wrapper@1.0");
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[scope.not-allowed]: "@someone/thing" is outside the scopes pmj is limited to here.
                  why: "@thatplatypus/wrapper" 1.0.0 depends on it (@you/site → @thatplatypus/wrapper@1.0.0 → @someone/thing@1.0), and PACKMOJI_SCOPES allows only thatplatypus
                  fix: depend only on packages of that scope; the list is set by whoever runs pmj here

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldNotBeEmpty();
            ShouldHaveAskedOnlyOf(sandbox, "thatplatypus");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task With_everything_inside_the_list_the_commands_do_what_they_do_with_no_limit()
        {
            using var sandbox = WithTwoOwners();
            sandbox.Variables["PACKMOJI_SCOPES"] = "someone, thatplatypus";

            await sandbox.InstallAsync("@you/site", "@thatplatypus/grapevine@0.3", "@someone/thing@1.0");

            sandbox.Lockfile().Packages.Count.ShouldBe(4);
            (await sandbox.RunAsync("tree")).Status.ShouldBe(0);
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
        }

        [Fact]
        public async Task A_tool_is_told_of_a_package_outside_the_list_in_its_answer()
        {
            using var sandbox = await WithBothLockedAsync();
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("install", "--locked", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            answer.RootElement.GetProperty("diagnostics").EnumerateArray().ShouldHaveSingleItem().GetProperty("code").GetString().ShouldBe("scope.not-allowed");
        }

        [Theory]
        [InlineData("install")]
        [InlineData("tree")]
        [InlineData("verify")]
        [InlineData("update")]
        [InlineData("remove", "@thatplatypus/grapevine")]
        [InlineData("add", "@thatplatypus/crypto@1.0")]
        [InlineData("build", "--dependencies-only")]
        [InlineData("run")]
        public async Task A_list_that_cannot_be_read_stops_every_command_that_reads_what_a_project_depends_on_and_is_never_no_limit(params string[] command)
        {
            using var sandbox = await WithBothLockedAsync();
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus,,emojicode";

            var run = await sandbox.RunAsync(command);

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[config.invalid]: PACKMOJI_SCOPES is not a list of scopes.
                  why: it has a comma with no scope beside it
                  fix: write the scopes that are allowed with commas between them, as in PACKMOJI_SCOPES=thatplatypus,emojicode; to allow every scope, do not set it

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Theory]
        [InlineData(false, "tree")]
        [InlineData(false, "install")]
        [InlineData(false, "run")]
        [InlineData(true, "tree")]
        [InlineData(true, "run")]
        public async Task A_list_that_cannot_be_read_is_said_before_either_file_of_the_project_is_read(bool withAManifestThatCannotBeRead, string command)
        {
            // Whoever runs pmj on a server learns of a mistyped limit from the first command, and not from the first project that happens to read.
            using var sandbox = new Sandbox();
            if (withAManifestThatCannotBeRead)
            {
                sandbox.Write("packmoji.json", "{ this is not a manifest");
            }

            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus,,";

            var run = await sandbox.RunAsync(command);

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[config.invalid]: PACKMOJI_SCOPES is not a list of scopes." + Environment.NewLine);
            run.Error.ShouldNotContain("project.not-found");
            run.Error.ShouldNotContain("json.");
        }

        [Fact]
        public async Task A_list_that_is_set_to_nothing_is_a_list_that_cannot_be_read_and_not_no_limit()
        {
            // On a server the variable is filled in from a setting, and a setting that is missing fills it with nothing.
            using var sandbox = WithTwoOwners();
            sandbox.Project("@you/site", "@someone/thing@1.0");
            sandbox.Variables["PACKMOJI_SCOPES"] = "";

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[config.invalid]: PACKMOJI_SCOPES is not a list of scopes.
                  why: it names no scope
                  fix: write the scopes that are allowed with commas between them, as in PACKMOJI_SCOPES=thatplatypus,emojicode; to allow every scope, do not set it

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_command_that_reads_nothing_of_what_a_project_depends_on_takes_no_notice_of_the_list()
        {
            using var sandbox = new Sandbox();
            sandbox.Variables["PACKMOJI_SCOPES"] = "not a list,,";

            (await sandbox.RunAsync("new", "@you/site")).Status.ShouldBe(0);
            (await sandbox.RunInAsync("site", "pack")).Status.ShouldBe(0);
        }
    }
}
