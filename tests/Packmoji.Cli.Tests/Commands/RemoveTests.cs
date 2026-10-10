using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj remove</c>: one thing fewer that the project asks for, and a lockfile that holds only what is still needed.</summary>
    public sealed class RemoveTests
    {
        private static async Task<Sandbox> WithGrapevineAndExtraAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/extra", "@thatplatypus/extra", "2.1.0", "@thatplatypus/crypto@1.0");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "dev:@thatplatypus/extra@2.1");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            return sandbox;
        }

        [Fact]
        public async Task Remove_takes_a_package_out_of_the_manifest_and_out_of_the_lockfile_with_all_that_only_it_needed()
        {
            using var sandbox = await WithGrapevineAndExtraAsync();
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("remove", "@thatplatypus/grapevine");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Removed @thatplatypus/grapevine.
                packmoji.lock now holds 2 packages:
                  - @thatplatypus/deflate 0.1.0
                  - @thatplatypus/grapevine 0.3.0

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Manifest().DevDependencies!.Single().Name.ToString().ShouldBe("@thatplatypus/extra");
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine", "@thatplatypus/extra 2.1.0 in github.com/thatplatypus/extra"]);
        }

        [Fact]
        public async Task It_is_taken_out_of_whichever_table_holds_it()
        {
            using var sandbox = await WithGrapevineAndExtraAsync();

            var run = await sandbox.RunAsync("remove", "@thatplatypus/extra");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Removed @thatplatypus/extra.");
            sandbox.Manifest().DevDependencies.ShouldBeNull();
            sandbox.Locked().Count.ShouldBe(3);
        }

        [Fact]
        public async Task Removing_the_last_dependency_leaves_a_manifest_with_no_table_and_a_lockfile_that_holds_nothing()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var bare = sandbox.Read("packmoji.json");
            await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            var run = await sandbox.RunAsync("remove", "@thatplatypus/grapevine");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith($"Removed @thatplatypus/grapevine.{Environment.NewLine}packmoji.lock now holds no package:{Environment.NewLine}  - @thatplatypus/crypto 1.0.0");
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Lockfile().Packages.ShouldBeEmpty();
            ManifestText(sandbox).ShouldBe(ManifestText(bare));
        }

        [Fact]
        public async Task With_no_lockfile_remove_takes_a_place_to_look_as_every_command_that_resolves_does()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/extra", "@thatplatypus/extra", "2.1.0");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0", "@thatplatypus/extra@2.1");

            var lost = await sandbox.RunAsync("remove", "@thatplatypus/extra");
            lost.Status.ShouldBe(1);
            lost.Error.ShouldContain("error[package.not-found]: ");

            var run = await sandbox.RunAsync("remove", "@thatplatypus/extra", "--repository", Sandbox.Grapevine);

            run.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);
        }

        [Theory]
        [InlineData("@thatplatypus/crypto", "dependency.not-found", "\"@thatplatypus/crypto\" is not a dependency of this project.")]
        [InlineData("@thatplatypus/grapevine@0.3", "name.invalid", "fix: run pmj remove @thatplatypus/grapevine")]
        [InlineData("grapevine", "name.invalid", "a package name is written @scope/name")]
        public async Task What_the_manifest_does_not_ask_for_cannot_be_removed(string package, string code, string says)
        {
            using var sandbox = await WithGrapevineAndExtraAsync();
            var manifest = sandbox.Read("packmoji.json");
            var locked = sandbox.Read("packmoji.lock");

            var run = await sandbox.RunAsync("remove", package);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            run.Error.ShouldContain(says);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
        }

        // A manifest as pmj writes it, whatever spacing its author gave it.
        private static string ManifestText(Sandbox sandbox) => ManifestText(sandbox.Read("packmoji.json"));

        private static string ManifestText(string json)
        {
            var read = Packmoji.Core.Manifests.ManifestReader.Read(json);
            read.Diagnostics.ShouldBeEmpty();
            return Packmoji.Core.Manifests.ManifestWriter.Write(read.Value!);
        }
    }
}
