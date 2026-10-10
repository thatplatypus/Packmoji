using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj update</c>: each requirement raised to the latest version on its own line, and never past it.</summary>
    public sealed class UpdateTests
    {
        // Two packages with a history: crypto has a later line and a pre-release beyond it, deflate a later line.
        private static async Task<Sandbox> InstalledAsync()
        {
            var sandbox = new Sandbox();
            foreach (var version in new[] { "1.0.0", "1.2.0", "1.10.0", "2.0.0", "2.1.0-rc.1" })
            {
                sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", version);
            }

            foreach (var version in new[] { "0.1.0", "0.1.4", "0.2.0" })
            {
                sandbox.Release("github.com/thatplatypus/deflate", "@thatplatypus/deflate", version);
            }

            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0", "dev:@thatplatypus/deflate@0.1");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            sandbox.GitHub.Requests.Clear();
            return sandbox;
        }

        [Fact]
        public async Task Update_raises_every_requirement_to_the_latest_version_on_its_line_and_locks_what_that_gives()
        {
            using var sandbox = await InstalledAsync();

            var run = await sandbox.RunAsync("update");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Raised 2 requirements in packmoji.json:
                  @thatplatypus/crypto from 1.0 to 1.10.0
                  @thatplatypus/deflate from 0.1 to 0.1.4
                packmoji.lock now holds 2 packages:
                  ~ @thatplatypus/crypto 1.0.0 to 1.10.0
                  ~ @thatplatypus/deflate 0.1.0 to 0.1.4

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Manifest().Dependencies!.Single().Requirement.Text.ShouldBe("1.10.0");
            sandbox.Manifest().DevDependencies!.Single().Requirement.Text.ShouldBe("0.1.4");
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.10.0 in github.com/thatplatypus/crypto", "@thatplatypus/deflate 0.1.4 in github.com/thatplatypus/deflate"]);
            sandbox.Cached().ShouldContain(file => file.StartsWith("thatplatypus/crypto/1.10.0/", StringComparison.Ordinal) && file.EndsWith("/src/lib.🍇", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Only_the_packages_named_are_raised()
        {
            using var sandbox = await InstalledAsync();

            var run = await sandbox.RunAsync("update", "@thatplatypus/deflate");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith($"Raised 1 requirement in packmoji.json:{Environment.NewLine}  @thatplatypus/deflate from 0.1 to 0.1.4{Environment.NewLine}");
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/crypto", "@thatplatypus/deflate 0.1.4 in github.com/thatplatypus/deflate"]);
            sandbox.GitHub.Listings.ShouldBe(["/repos/thatplatypus/deflate/releases"]);
        }

        [Fact]
        public async Task A_dry_run_says_what_would_change_and_leaves_both_files_as_they_are()
        {
            using var sandbox = await InstalledAsync();
            var manifest = sandbox.Read("packmoji.json");
            var locked = sandbox.Read("packmoji.lock");

            var run = await sandbox.RunAsync("update", "--dry-run");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Would raise 2 requirements in packmoji.json:
                  @thatplatypus/crypto from 1.0 to 1.10.0
                  @thatplatypus/deflate from 0.1 to 0.1.4
                packmoji.lock would hold 2 packages:
                  ~ @thatplatypus/crypto 1.0.0 to 1.10.0
                  ~ @thatplatypus/deflate 0.1.0 to 0.1.4
                Nothing was written.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);
        }

        [Fact]
        public async Task When_every_requirement_already_asks_for_the_latest_on_its_line_nothing_is_done()
        {
            using var sandbox = await InstalledAsync();
            await sandbox.RunAsync("update");
            var manifest = sandbox.Read("packmoji.json");
            var written = File.GetLastWriteTimeUtc(sandbox.PathOf("packmoji.lock"));

            var again = await sandbox.RunAsync("update");
            var dry = await sandbox.RunAsync("update", "--dry-run");

            again.Status.ShouldBe(0);
            again.Output.ShouldBe($"Every requirement already asks for the latest version on its line.{Environment.NewLine}");
            dry.Output.ShouldBe(again.Output);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            File.GetLastWriteTimeUtc(sandbox.PathOf("packmoji.lock")).ShouldBe(written);
        }

        [Fact]
        public async Task A_repository_that_holds_several_of_the_packages_is_asked_for_its_releases_once()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.1.0");
            sandbox.Project("@thatplatypus/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");
            await sandbox.RunAsync("install");
            sandbox.GitHub.Requests.Clear();

            var run = await sandbox.RunAsync("update");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith($"Raised 1 requirement in packmoji.json:{Environment.NewLine}  @thatplatypus/crypto from 1.0 to 1.1.0{Environment.NewLine}");
            sandbox.GitHub.Listings.ShouldBe(["/repos/thatplatypus/grapevine/releases"]);
        }

        [Fact]
        public async Task A_lockfile_that_the_manifest_has_moved_on_from_is_brought_up_to_it_even_when_nothing_is_raised()
        {
            using var sandbox = await InstalledAsync();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.10.0", "dev:@thatplatypus/deflate@0.1.4");
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("update");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                packmoji.lock now holds 2 packages:
                  ~ @thatplatypus/crypto 1.0.0 to 1.10.0
                  ~ @thatplatypus/deflate 0.1.0 to 0.1.4

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Read("packmoji.json").ShouldBe(manifest);
        }

        [Theory]
        [InlineData("@thatplatypus/grapevine", "dependency.not-found", "\"@thatplatypus/grapevine\" is not a dependency of this project.")]
        [InlineData("@thatplatypus/crypto@2.0", "name.invalid", "fix: run pmj update @thatplatypus/crypto")]
        [InlineData("crypto", "name.invalid", "a package name is written @scope/name")]
        public async Task A_package_that_cannot_be_updated_stops_update_before_anything_is_asked(string package, string code, string says)
        {
            using var sandbox = await InstalledAsync();
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("update", "@thatplatypus/deflate", package);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            run.Error.ShouldContain(says);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_dry_run_of_what_could_not_be_resolved_says_why_and_ends_as_a_problem()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/grapevine", "0.3.1", "@thatplatypus/crypto@2.0", "@thatplatypus/deflate@0.1");
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "2.0.0");
            sandbox.Release("github.com/thatplatypus/other", "@thatplatypus/other", "1.0.0", "@thatplatypus/crypto@1.0");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/other@1.0");
            await sandbox.RunAsync("install");
            var locked = sandbox.Read("packmoji.lock");

            var run = await sandbox.RunAsync("update", "--dry-run");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[resolve.line-conflict]: ");
            sandbox.Read("packmoji.lock").ShouldBe(locked);
        }

        [Theory]
        [InlineData("update")]
        [InlineData("update", "--dry-run")]
        public async Task A_project_that_requires_attestation_is_not_updated_because_pmj_cannot_check_one_yet(params string[] args)
        {
            using var sandbox = await InstalledAsync();
            sandbox.RequireAttestation();
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync(args);

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldStartWith("error[attestation.unverifiable]: ");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_whose_releases_are_nowhere_pmj_looks_stops_update()
        {
            using var sandbox = await InstalledAsync();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0", "@thatplatypus/gone@1.0");

            var run = await sandbox.RunAsync("update");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[package.not-found]: No release of \"@thatplatypus/gone\" was found.");
        }
    }
}
