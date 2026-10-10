using System.Text.Json;
using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj tree</c>: what the lockfile holds, drawn for a person or listed for a tool, with nothing asked of anyone.</summary>
    public sealed class TreeTests
    {
        private static async Task<Sandbox> InstalledAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/extra", "@thatplatypus/extra", "2.1.0", "@thatplatypus/crypto@1.0");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "dev:@thatplatypus/extra@2.1");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            sandbox.GitHub.Requests.Clear();
            sandbox.GitHub.Unreachable = true;
            return sandbox;
        }

        [Fact]
        public async Task Tree_draws_the_project_and_under_each_package_what_it_depends_on()
        {
            using var sandbox = await InstalledAsync();

            var run = await sandbox.RunAsync("tree");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                @someone/app 0.1.0
                ├── @thatplatypus/grapevine 0.3.0
                │   ├── @thatplatypus/crypto 1.0.0
                │   └── @thatplatypus/deflate 0.1.0
                └── @thatplatypus/extra 2.1.0 (dev)
                    └── @thatplatypus/crypto 1.0.0

                """.ReplaceLineEndings("\n"));
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_json_the_graph_is_one_object_on_standard_output_and_nothing_else()
        {
            using var sandbox = await InstalledAsync();

            var run = await sandbox.RunAsync("tree", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            using var json = JsonDocument.Parse(run.Output);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
            json.RootElement.GetProperty("diagnostics").GetArrayLength().ShouldBe(0);
            json.RootElement.GetProperty("project").GetProperty("name").GetString().ShouldBe("@someone/app");
            json.RootElement.GetProperty("dependencies")[0].GetProperty("version").GetString().ShouldBe("0.3.0");
            json.RootElement.GetProperty("devDependencies")[0].GetProperty("name").GetString().ShouldBe("@thatplatypus/extra");
            json.RootElement.GetProperty("packages").EnumerateArray().Select(package => package.GetProperty("name").GetString()).ShouldBe(
                ["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/extra", "@thatplatypus/grapevine"]);
            json.RootElement.GetProperty("packages")[3].GetProperty("source").GetString().ShouldBe("github.com/thatplatypus/grapevine");
        }

        [Fact]
        public async Task A_project_that_requires_attestation_can_still_be_drawn_since_drawing_fetches_nothing()
        {
            using var sandbox = await InstalledAsync();
            sandbox.RequireAttestation();

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(0);
            run.Error.ShouldBeEmpty();
            run.Output.ShouldStartWith("@someone/app 0.1.0\n├── @thatplatypus/grapevine 0.3.0\n");
        }

        [Fact]
        public async Task Before_anything_is_installed_tree_says_to_install()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[lock.out-of-date]: There is no packmoji.lock.");
            run.Error.ShouldContain("fix: run pmj install");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_lockfile_that_the_manifest_has_moved_on_from_is_not_drawn_as_if_it_were_the_project()
        {
            using var sandbox = await InstalledAsync();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("tree");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[lock.out-of-date]: packmoji.lock no longer answers what packmoji.json asks for.");
        }

        [Theory]
        [InlineData(false, "project.not-found")]
        [InlineData(true, "lock.out-of-date")]
        public async Task With_json_a_problem_is_in_the_object_and_not_on_standard_error(bool project, string code)
        {
            using var sandbox = new Sandbox();
            if (project)
            {
                sandbox.Project("@someone/app");
            }

            var run = await sandbox.RunAsync("tree", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            using var json = JsonDocument.Parse(run.Output);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            var diagnostic = json.RootElement.GetProperty("diagnostics")[0];
            diagnostic.GetProperty("code").GetString().ShouldBe(code);
            diagnostic.GetProperty("severity").GetString().ShouldBe("error");
            diagnostic.GetProperty("fix").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }
}
