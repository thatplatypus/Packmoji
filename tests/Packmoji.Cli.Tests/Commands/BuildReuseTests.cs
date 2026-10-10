using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// A package is compiled once for the whole machine. What decides whether a build may take what
    /// was built before is its key, so these hold pmj to both halves: nothing is compiled twice, and
    /// nothing is taken that was built from something else.
    /// </summary>
    public sealed class BuildReuseTests
    {
        private static async Task<Sandbox> WithGrapevineBuiltAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Clear();
            return sandbox;
        }

        // Another project on the same machine, with the two files of the first and nothing else.
        private static void Copy(Sandbox sandbox, string directory)
        {
            sandbox.Write(directory + "/packmoji.json", sandbox.Read("packmoji.json"));
            sandbox.Write(directory + "/packmoji.lock", sandbox.Read("packmoji.lock"));
        }

        [Fact]
        public async Task A_second_build_compiles_nothing()
        {
            using var sandbox = await WithGrapevineBuiltAsync();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"3 packages were built before, and are in packages/.{Environment.NewLine}");
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldBe(["emojicodec --help"]);
        }

        [Fact]
        public async Task One_package_that_was_built_before_is_said_as_one()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");
            await sandbox.RunAsync("build", "--dependencies-only");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Output.ShouldBe($"1 package was built before, and is in packages/.{Environment.NewLine}");
        }

        [Fact]
        public async Task Another_project_that_locks_the_same_packages_is_given_them_without_a_compile()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            Copy(sandbox, "another");

            var run = await sandbox.RunInAsync("another", "build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Files("another/packages").Count.ShouldBe(12);
            foreach (var file in sandbox.Files("packages"))
            {
                File.ReadAllBytes(sandbox.PathOf("another/packages/" + file)).ShouldBe(File.ReadAllBytes(sandbox.PathOf("packages/" + file)), file);
            }
        }

        [Fact]
        public async Task Another_compiler_builds_every_package_again_and_what_the_first_built_is_kept()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            var first = sandbox.Key("crypto");
            sandbox.Tool("tools/emojicodec", "emojicodec", FakeTools.Banner, "a fork that prints the banner of the release");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            run.Output.ShouldEndWith($"Built 3 packages into packages/.{Environment.NewLine}");
            sandbox.Tools.Compiles.Count().ShouldBe(3);
            sandbox.Keys("crypto").Count.ShouldBe(2);
            sandbox.Keys("grapevine").Count.ShouldBe(2);

            // The project now holds what this compiler built, and not what the other did.
            var placed = sandbox.Read("packages/crypto/pmj-build.json");
            placed.ShouldNotContain($"\"key\": \"{first}");
            placed.ShouldContain($"\"key\": \"{sandbox.Keys("crypto").Single(key => key != first)}");
        }

        [Fact]
        public async Task The_same_compiler_in_another_place_is_the_same_compiler()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            var moved = Path.Combine(sandbox.Root, "elsewhere", "emojicodec-1.0-beta.2");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Copy(Path.Combine(sandbox.ToolsDirectory, "emojicodec"), moved);
            sandbox.Variables["EMOJICODEC"] = moved;

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_is_built_again_for_a_project_that_locks_another_version_of_what_it_depends_on()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.1.0");
            sandbox.Write("newer/packmoji.json", TestPackage.Manifest("@someone/newer", "0.1.0", "app", null, "@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1"));
            (await sandbox.RunInAsync("newer", "install")).Status.ShouldBe(0);

            var run = await sandbox.RunInAsync("newer", "build", "--dependencies-only");

            // The same grapevine, compiled against another crypto, is another build. deflate depends on neither.
            run.Error.ShouldBeEmpty();
            run.Output.ShouldBe(
                """
                Building @thatplatypus/crypto 1.1.0
                Building @thatplatypus/grapevine 0.3.0
                Built 2 packages into packages/, and 1 was built before.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Keys("grapevine").Count.ShouldBe(2);
            sandbox.Keys("deflate").Count.ShouldBe(1);
            sandbox.Read("newer/packages/grapevine/libgrapevine.a").ShouldContain($"  import crypto {FakeTools.Short(sandbox.Read("newer/packages/crypto/🏛"))}");
            sandbox.Read("packages/grapevine/libgrapevine.a").ShouldContain($"  import crypto {FakeTools.Short(sandbox.Read("packages/crypto/🏛"))}");
            sandbox.Read("newer/packages/crypto/🏛").ShouldNotBe(sandbox.Read("packages/crypto/🏛"));
        }

        [Fact]
        public async Task Two_packages_built_now_and_two_built_before_are_said_as_they_are()
        {
            using var sandbox = new Sandbox();
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                sandbox.Release($"github.com/thatplatypus/{name}", $"@thatplatypus/{name}", "1.0.0");
            }

            await sandbox.InstallAsync("@someone/app", "@thatplatypus/a@1.0", "@thatplatypus/b@1.0");
            await sandbox.RunAsync("build", "--dependencies-only");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/a@1.0", "@thatplatypus/b@1.0", "@thatplatypus/c@1.0", "@thatplatypus/d@1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Output.ShouldEndWith($"Built 2 packages into packages/, and 2 were built before.{Environment.NewLine}");
        }

        [Fact]
        public async Task When_what_pmj_keeps_is_deleted_it_is_built_again()
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            sandbox.ForgetBuilt();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.Tools.Compiles.Count().ShouldBe(3);
            sandbox.Built().Count.ShouldBe(12);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not a stamp")]
        [InlineData("{ \"version\": 1, \"key\": \"0000000000000000000000000000000000000000000000000000000000000000\" }")]
        public async Task What_is_kept_without_a_stamp_that_gives_its_key_is_not_used_and_is_built_over(string? stamp)
        {
            using var sandbox = await WithGrapevineBuiltAsync();
            var file = sandbox.BuiltFile("crypto", "pmj-build.json");
            var archive = sandbox.BuiltFile("crypto", "libcrypto.a");
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            if (stamp is not null)
            {
                File.WriteAllText(file, stamp);
            }

            File.SetAttributes(archive, FileAttributes.Normal);
            File.WriteAllText(archive, "what was left by a build that never ended");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Tools.Compiles.Count().ShouldBe(1);
            File.ReadAllText(sandbox.BuiltFile("crypto", "libcrypto.a")).ShouldStartWith("made-up archive");
            sandbox.Keys("crypto").Count.ShouldBe(1);
            sandbox.Built().ShouldNotContain(kept => kept.Contains(".tmp-", StringComparison.Ordinal));
        }
    }
}
