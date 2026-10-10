using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// The project's own <c>packages</c> directory. The compiler takes the first folder of a package's
    /// name that it finds there, so the directory has to hold what is locked and nothing stale. And
    /// someone may keep packages of their own in it, so pmj changes only what it put there.
    /// </summary>
    public sealed class BuildPlacingTests
    {
        private static async Task<Sandbox> WithGrapevineBuiltAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            return sandbox;
        }

        [Fact]
        public async Task A_folder_with_a_locked_packages_name_that_pmj_did_not_put_there_stops_the_build_and_is_left_as_it_was()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("packages/deflate/🏛", "💭 built by hand\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[packages.foreign]: "packages/deflate" is in the way of the package pmj built.
                  why: it is in the project already and has no pmj-build.json, so pmj did not put it there, and pmj changes nothing in "packages" that is not its own
                  fix: move it away or delete it: the compiler takes the first "deflate" it finds, and that has to be the one that is locked

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Files("packages").ShouldBe(["deflate/🏛"]);
            sandbox.Read("packages/deflate/🏛").ShouldBe("💭 built by hand\n");
        }

        [Fact]
        public async Task A_file_with_a_locked_packages_name_is_in_the_way_as_a_folder_is()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("packages/crypto", "not a folder");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[packages.foreign]: \"packages/crypto\" is in the way");
            sandbox.Files("packages").ShouldBe(["crypto"]);
        }

        [Fact]
        public async Task A_folder_of_someones_own_under_another_name_is_left_alone()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("packages/mine/🏛", "💭 built by hand\n");
            sandbox.Write("packages/notes.txt", "what these are\n");

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Read("packages/mine/🏛").ShouldBe("💭 built by hand\n");
            sandbox.Read("packages/notes.txt").ShouldBe("what these are\n");
            sandbox.Files("packages").Count.ShouldBe(14);
        }

        [Fact]
        public async Task What_pmj_put_there_for_a_package_that_is_no_longer_locked_is_taken_away()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.Files("packages").ShouldBe(["crypto/documentation.json", "crypto/libcrypto.a", "crypto/pmj-build.json", "crypto/🏛"]);
        }

        [Fact]
        public async Task When_nothing_is_locked_any_more_what_pmj_put_there_is_taken_away_and_what_it_did_not_stays()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            sandbox.Write("packages/mine/🏛", "💭 built by hand\n");
            await sandbox.InstallAsync("@someone/app");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Nothing to build: the project depends on no package.{Environment.NewLine}");
            sandbox.Files("packages").ShouldBe(["mine/🏛"]);
        }

        [Fact]
        public async Task What_is_there_already_and_is_the_build_that_is_wanted_is_not_written_again()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            var archive = sandbox.PathOf("packages/crypto/libcrypto.a");
            File.SetLastWriteTimeUtc(archive, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            File.GetLastWriteTimeUtc(archive).ShouldBe(new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        }

        [Fact]
        public async Task A_package_in_the_project_that_has_lost_a_file_is_put_there_again()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            File.Delete(sandbox.PathOf("packages/crypto/🏛"));

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Read("packages/crypto/🏛").ShouldBe(File.ReadAllText(sandbox.BuiltFile("crypto", "🏛")));
        }

        [Fact]
        public async Task A_stamp_that_is_too_large_to_be_one_is_not_read_and_the_package_is_put_there_again()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            var stamp = sandbox.PathOf("packages/crypto/pmj-build.json");
            var written = File.ReadAllText(stamp);

            // Still JSON, and still the right key: only far more of it than a stamp is.
            File.WriteAllText(stamp, new string(' ', 2 * 1024 * 1024) + written);

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            File.ReadAllText(stamp).ShouldBe(written);
        }

        [Fact]
        public async Task A_folder_pmj_left_half_way_to_its_place_is_cleared_away_by_the_next_build()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            sandbox.Write("packages/crypto.tmp-0123456789abcdef0123456789abcdef/pmj-build.json", "{}");

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Files("packages").Count.ShouldBe(12);
        }

        [Fact]
        public async Task A_second_build_of_one_project_waits_for_the_first_and_says_so_once()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");
            var compiling = new TaskCompletionSource();
            var go = new TaskCompletionSource();
            sandbox.Tools.Before = async call =>
            {
                if (call.Arguments.Contains("-c"))
                {
                    compiling.TrySetResult();
                    await go.Task;
                }
            };

            var first = sandbox.RunAsync("build", "--dependencies-only");
            await compiling.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var second = sandbox.RunAsync("build", "--dependencies-only");
            await Task.Delay(400, TestContext.Current.CancellationToken);
            second.IsCompleted.ShouldBeFalse();
            go.SetResult();

            (await first).Status.ShouldBe(0);
            var waited = await second;
            waited.Status.ShouldBe(0);
            waited.Error.ShouldBe($"Waiting for another pmj that is building this project.{Environment.NewLine}");
            waited.Output.ShouldBe($"1 package was built before, and is in packages/.{Environment.NewLine}");
            sandbox.Tools.Compiles.Count().ShouldBe(1);
        }
    }
}
