using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Reports;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// <c>--json</c> on the four commands that lock packages: <c>add</c>, <c>remove</c>,
    /// <c>install</c> and <c>update</c>. A tool that drives pmj reads what was done from one object
    /// on the output, and from nothing else.
    /// </summary>
    public sealed class LockJsonTests
    {
        private static async Task<Sandbox> WithGrapevineInstalledAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            return sandbox;
        }

        // What a command is to have said: the answer for the lockfile that is there now, against the one
        // before. An answer for a tool has the same line ends on every machine.
        private static string Answer(Sandbox sandbox, Lockfile? before, bool written) =>
            LockReport.Json([], 0, written, before, sandbox.Lockfile());

        [Fact]
        public async Task Install_answers_a_tool_with_what_it_locked_and_nothing_else_on_either_stream()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("install", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, before: null, written: true));
            run.Output.ShouldContain("\"written\": true");
            run.Output.ShouldContain("\"change\": \"added\"");
            run.Output.ShouldContain("\"name\": \"@thatplatypus/crypto\"");
            sandbox.Lockfile().Packages.Count.ShouldBe(3);
        }

        [Fact]
        public async Task Install_with_a_lockfile_that_answers_the_manifest_writes_nothing_and_still_says_what_is_locked()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var locked = sandbox.Lockfile();

            var run = await sandbox.RunAsync("install", "--locked", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, locked, written: false));
            run.Output.ShouldContain("\"written\": false");
            run.Output.ShouldContain("\"changes\": []");
            run.Output.ShouldContain("\"name\": \"@thatplatypus/grapevine\"");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Install_locked_tells_a_tool_that_the_lockfile_has_to_be_made_first_with_a_code_and_nothing_else(bool stale)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/other", "@thatplatypus/other", "1.0.0");
            if (stale)
            {
                await sandbox.InstallAsync("@someone/app", "@thatplatypus/other@1.0");
            }

            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            var before = sandbox.Has("packmoji.lock") ? sandbox.Read("packmoji.lock") : null;

            var run = await sandbox.RunAsync("install", "--locked", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            run.Output.ShouldStartWith("{\n  \"ok\": false,\n");
            run.Output.ShouldContain("\"code\": \"lock.out-of-date\"");
            run.Output.ShouldNotContain("\"written\"");
            run.Output.ShouldNotContain("\"packages\"");
            (sandbox.Has("packmoji.lock") ? sandbox.Read("packmoji.lock") : null).ShouldBe(before);
        }

        [Fact]
        public async Task Add_answers_with_what_came_into_the_lockfile()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine@0.3", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, before: null, written: true));
            sandbox.Lockfile().Packages.Count.ShouldBe(3);
            sandbox.Read("packmoji.json").ShouldContain("@thatplatypus/grapevine");
        }

        [Fact]
        public async Task Add_of_what_is_asked_for_already_changes_nothing_and_says_so_to_a_tool()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var locked = sandbox.Lockfile();

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine@0.3", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, locked, written: false));
        }

        [Fact]
        public async Task Remove_answers_with_what_went_from_the_lockfile()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var locked = sandbox.Lockfile();

            var run = await sandbox.RunAsync("remove", "@thatplatypus/grapevine", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, locked, written: true));
            run.Output.ShouldContain("\"change\": \"removed\"");
            run.Output.ShouldContain("\"packages\": []");
        }

        [Fact]
        public async Task Update_answers_with_what_moved_in_the_lockfile()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var locked = sandbox.Lockfile();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/grapevine", "0.3.1", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("update", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, locked, written: true));
            run.Output.ShouldContain("\"change\": \"moved\"");
            run.Output.ShouldContain("\"from\": \"0.3.0\"");
            run.Output.ShouldContain("\"to\": \"0.3.1\"");
        }

        [Fact]
        public async Task Update_asked_only_what_it_would_do_writes_nothing_and_answers_with_what_would_move()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var (manifest, lockfile) = (sandbox.Read("packmoji.json"), sandbox.Read("packmoji.lock"));
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/grapevine", "0.3.1", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("update", "--dry-run", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldContain("\"written\": false");
            run.Output.ShouldContain("\"change\": \"moved\"");
            run.Output.ShouldContain("\"to\": \"0.3.1\"");
            run.Output.ShouldContain("\"version\": \"0.3.1\"");
            run.Output.ShouldNotContain("Nothing was written");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(lockfile);
        }

        [Fact]
        public async Task Update_with_nothing_to_raise_says_to_a_tool_that_nothing_changed()
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var locked = sandbox.Lockfile();

            var run = await sandbox.RunAsync("update", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(Answer(sandbox, locked, written: false));
        }

        [Theory]
        [InlineData("add", "@thatplatypus/nothing-of-the-kind")]
        [InlineData("add", "not a name")]
        [InlineData("remove", "@thatplatypus/crypto")]
        [InlineData("update", "@thatplatypus/crypto")]
        [InlineData("install", "--repository", "not a repository")]
        public async Task A_command_that_could_not_do_what_it_was_asked_answers_with_the_problem_alone_and_writes_nothing(params string[] command)
        {
            using var sandbox = await WithGrapevineInstalledAsync();
            var (manifest, lockfile) = (sandbox.Read("packmoji.json"), sandbox.Read("packmoji.lock"));

            var run = await sandbox.RunAsync([.. command, "--json"]);

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            run.Output.ShouldStartWith("{\n  \"ok\": false,\n  \"diagnostics\": [\n");
            run.Output.ShouldEndWith("  \"omittedDiagnostics\": 0\n}\n");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(lockfile);
        }

        [Theory]
        [InlineData("add", "@thatplatypus/crypto")]
        [InlineData("remove", "@thatplatypus/crypto")]
        [InlineData("install")]
        [InlineData("update")]
        public async Task Where_there_is_no_project_each_of_the_four_says_so_in_the_answer(params string[] command)
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync([.. command, "--json"]);

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            run.Output.ShouldContain("\"code\": \"project.not-found\"");
        }

        [Fact]
        public async Task Files_that_could_not_be_written_are_a_problem_in_the_answer_and_no_change_is_claimed()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            Directory.CreateDirectory(sandbox.PathOf("packmoji.lock"));

            var run = await sandbox.RunAsync("install", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            answer.RootElement.GetProperty("diagnostics").EnumerateArray().ShouldHaveSingleItem().GetProperty("code").GetString().ShouldBe("project.unreadable");
            answer.RootElement.TryGetProperty("written", out _).ShouldBeFalse();
            answer.RootElement.TryGetProperty("changes", out _).ShouldBeFalse();
        }

        [Fact]
        public async Task What_GitHub_could_not_be_asked_is_a_problem_in_the_answer_like_any_other()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("install", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(1);
            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            answer.RootElement.GetProperty("diagnostics").EnumerateArray().ShouldHaveSingleItem().GetProperty("code").GetString().ShouldBe("github.unreachable");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }
    }
}
