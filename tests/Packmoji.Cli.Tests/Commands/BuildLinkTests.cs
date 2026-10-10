using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// A project's directory may have come from someone else, links and all. A build clears what it
    /// finds where it is about to write, and writes there. So a link at one of the places it writes
    /// into would have it clear and write somewhere it was never pointed at, and none is followed.
    /// </summary>
    public sealed class BuildLinkTests
    {
        private const string Main = "📦 grapevine 🏠\n🏁 🍇\n  😀 🔤Hello from the app🔤❗️\n🍉\n";

        // An application that imports grapevine, installed and not built, beside a directory of
        // someone's own that holds something at every place a build would clear if it were led there.
        private static async Task<(Sandbox Sandbox, string Own)> WithSomethingToLoseAsync()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "Making a symbolic link there needs a right that a test does not have.");
            var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("src/main.🍇", Main);
            var own = Path.Combine(sandbox.Root, "own");
            foreach (var kept in new[] { "app/letters.txt", "debug/app/letters.txt", "debug/letters.txt", "obj/debug/letters.txt", "letters.txt" })
            {
                var path = Path.Combine(own, kept.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "not pmj's to touch\n");
            }

            return (sandbox, own);
        }

        [Theory]
        [InlineData("packages", "build", "--dependencies-only")]
        [InlineData("target", "build", "--dependencies-only")]
        [InlineData("target", "build")]
        [InlineData("target/obj", "build")]
        [InlineData("target/debug", "build")]
        [InlineData("target/release", "build", "--release")]
        [InlineData("target/debug", "run")]
        public async Task A_directory_that_a_build_writes_into_and_that_is_a_link_is_not_followed(string place, params string[] command)
        {
            var (sandbox, own) = await WithSomethingToLoseAsync();
            using (sandbox)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sandbox.PathOf(place))!);
                Directory.CreateSymbolicLink(sandbox.PathOf(place), own);
                var before = sandbox.Elsewhere();

                var run = await sandbox.RunAsync(command);

                // First of all, nothing of someone's own is gone, and nothing was put beside it.
                sandbox.Elsewhere().ShouldBe(before);
                run.Status.ShouldBe(1);
                run.Output.ShouldBeEmpty();
                run.Error.ShouldBe(
                    $"""
                    error[project.unreadable]: "{place}" is a symbolic link, and pmj does not build through one.
                      why: a build clears what is at the places it writes to and then writes there, and a link can lead anywhere on this machine
                      fix: delete the link, and pmj makes a directory in its place

                    """.ReplaceLineEndings(Environment.NewLine));
                sandbox.Tools.Calls.ShouldBeEmpty();
            }
        }

        [Fact]
        public async Task A_link_where_the_lock_of_a_build_is_kept_is_not_opened()
        {
            var (sandbox, own) = await WithSomethingToLoseAsync();
            using (sandbox)
            {
                Directory.CreateDirectory(sandbox.PathOf("target"));
                File.CreateSymbolicLink(sandbox.PathOf("target/.pmj-lock"), Path.Combine(own, "not-there-yet.txt"));
                var before = sandbox.Elsewhere();

                var run = await sandbox.RunAsync("build", "--dependencies-only");

                run.Status.ShouldBe(1);
                run.Error.ShouldStartWith("error[project.unreadable]: \"target/.pmj-lock\" is a symbolic link, and pmj does not build through one.");
                sandbox.Elsewhere().ShouldBe(before);
            }
        }

        [Fact]
        public async Task With_only_its_dependencies_to_build_a_link_where_the_project_itself_would_be_built_is_no_matter()
        {
            var (sandbox, own) = await WithSomethingToLoseAsync();
            using (sandbox)
            {
                Directory.CreateDirectory(sandbox.PathOf("target"));
                Directory.CreateSymbolicLink(sandbox.PathOf("target/debug"), own);
                var before = sandbox.Elsewhere();

                (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

                sandbox.Elsewhere().ShouldBe(before);
            }
        }

        [Fact]
        public async Task A_link_at_the_very_place_of_what_is_built_is_taken_away_and_what_it_led_to_is_left()
        {
            var (sandbox, own) = await WithSomethingToLoseAsync();
            using (sandbox)
            {
                Directory.CreateDirectory(sandbox.PathOf("target/debug"));
                Directory.CreateDirectory(sandbox.PathOf("target/obj"));
                Directory.CreateSymbolicLink(sandbox.PathOf("target/debug/app"), own);
                Directory.CreateSymbolicLink(sandbox.PathOf("target/obj/debug"), own);
                var before = sandbox.Elsewhere();

                (await sandbox.RunAsync("build")).Status.ShouldBe(0);

                sandbox.Elsewhere().ShouldBe(before);
                new FileInfo(sandbox.PathOf("target/debug/app")).LinkTarget.ShouldBeNull();
                new DirectoryInfo(sandbox.PathOf("target/obj/debug")).LinkTarget.ShouldBeNull();
                sandbox.Read("target/debug/app").ShouldEndWith("says Hello from the app" + Environment.NewLine);
            }
        }
    }
}
