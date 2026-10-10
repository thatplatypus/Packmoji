using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// The tools of a build: where the compiler and the archiver are found, what is said when one is
    /// not, and what is made of a tool that fails, which the compiler does in ways its status hides.
    /// </summary>
    public sealed class BuildToolsTests
    {
        private static async Task<Sandbox> WithCryptoInstalledAsync(string source = "")
        {
            var sandbox = new Sandbox();
            sandbox.ReleaseSource("@thatplatypus/crypto", "1.0.0", source);
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");
            return sandbox;
        }

        private static string Cached(Sandbox sandbox) =>
            sandbox.Plain(Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", sandbox.Lockfile().Packages.Single().Sha256.Hex, "src", "lib.🍇"));

        [Fact]
        public async Task The_compiler_is_the_first_emojicodec_on_the_PATH()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var earlier = sandbox.Tool("earlier/emojicodec", "emojicodec", FakeTools.Banner);
            var empty = Directory.CreateDirectory(Path.Combine(sandbox.Root, "empty")).FullName;
            sandbox.Variables["PATH"] = string.Join(Path.PathSeparator, empty, Path.GetDirectoryName(earlier), sandbox.ToolsDirectory);

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Tools.Calls.Where(call => call.Tool == "emojicodec").Select(call => call.Program).Distinct().ShouldBe([earlier]);
        }

        [Fact]
        public async Task EMOJICODEC_names_the_compiler_whatever_is_on_the_PATH()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var named = sandbox.Tool("elsewhere/a compiler", "emojicodec", FakeTools.Banner);
            sandbox.Variables["EMOJICODEC"] = named;

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Tools.Calls.Where(call => call.Tool == "emojicodec").Select(call => call.Program).Distinct().ShouldBe([named]);
        }

        [Fact]
        public async Task A_name_in_EMOJICODEC_is_looked_for_on_the_PATH_and_a_path_is_taken_from_where_pmj_is_run()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var fork = sandbox.Tool("tools/emojicodec-fork", "emojicodec", FakeTools.Banner);
            var beside = sandbox.Tool("work/tools/emojicodec", "emojicodec", FakeTools.Banner);

            sandbox.Variables["EMOJICODEC"] = "emojicodec-fork";
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Select(call => call.Program).ShouldContain(fork);

            sandbox.Tools.Calls.Clear();
            sandbox.Variables["EMOJICODEC"] = "tools/emojicodec";
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Select(call => call.Program).ShouldBe([beside]);
        }

        [Fact]
        public async Task A_variable_that_is_set_to_nothing_says_nothing()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Variables["EMOJICODEC"] = "";
            sandbox.Variables["AR"] = "";

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task With_no_compiler_on_the_PATH_nothing_is_built_and_pmj_says_how_to_name_one()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "emojicodec"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[compiler.not-found]: The Emojicode compiler was not found.
                  why: no program called emojicodec is in any directory of PATH
                  fix: install Emojicode, or set EMOJICODEC to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
        }

        [Fact]
        public async Task With_no_PATH_at_all_there_is_no_compiler_either()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Variables.Remove("PATH");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[compiler.not-found]: The Emojicode compiler was not found.");
        }

        [Fact]
        public async Task An_EMOJICODEC_that_names_nothing_is_said_to_and_the_PATH_is_not_tried_in_its_place()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Variables["EMOJICODEC"] = "/no/such/compiler";

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[compiler.not-found]: The Emojicode compiler was not found.
                  why: EMOJICODEC names "/no/such/compiler", and there is no such program
                  fix: install Emojicode, or set EMOJICODEC to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_compiler_that_is_there_and_cannot_be_run_is_said_to_be_that()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var file = Path.Combine(sandbox.ToolsDirectory, "emojicodec");
            File.WriteAllText(file, "a file that is no program\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                $"""
                error[compiler.not-found]: The Emojicode compiler could not be run.
                  why: "{file}" is there, and it could not be started
                  fix: check that it is a program for this machine, and that it is yours to run

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task A_compiler_whose_banner_is_not_understood_is_refused_and_never_guessed_at()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var file = sandbox.Tool("tools/emojicodec", "emojicodec", "Emojicode Compiler nightly. Visit https://www.emojicode.org for help.");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[compiler.unknown]: The Emojicode compiler did not say which version it is." + Environment.NewLine);
            run.Error.ShouldContain($"\"{file}\" was asked for its help");
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Built().ShouldBeEmpty();
        }

        // A library that needs at least this compiler, released from a repository of its own name.
        private static void ReleaseNeeding(Sandbox sandbox, string name, string emojicode) =>
            sandbox.Upload($"github.com/thatplatypus/{name}", $"@thatplatypus/{name}", "1.0.0", TestPackage.Archive(
                ("packmoji.json", TestPackage.Manifest($"@thatplatypus/{name}", "1.0.0", "library", null, emojicode, [])),
                ("src/lib.🍇", $"💭 {name}\n")));

        [Fact]
        public async Task Packages_that_ask_for_a_newer_compiler_than_the_one_here_are_each_named_and_nothing_is_compiled()
        {
            using var sandbox = new Sandbox();
            ReleaseNeeding(sandbox, "early", ">=1.0.0-beta.2");
            ReleaseNeeding(sandbox, "released", ">=1.0.0");
            ReleaseNeeding(sandbox, "later", ">=2.1.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/later@1.0", "@thatplatypus/early@1.0", "@thatplatypus/released@1.0");
            var compiler = Path.Combine(sandbox.ToolsDirectory, "emojicodec");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                $"""
                error[compiler.too-old]: "@thatplatypus/later" 1.0.0 needs a newer Emojicode compiler than the one here.
                  why: its manifest asks for >=2.1.0, and the compiler at "{compiler}" says it is 1.0.0-beta.2
                  fix: use a compiler that is new enough, naming it with EMOJICODEC if it is not the first on the PATH, or depend on a version of the package that this compiler builds
                error[compiler.too-old]: "@thatplatypus/released" 1.0.0 needs a newer Emojicode compiler than the one here.
                  why: its manifest asks for >=1.0.0, and the compiler at "{compiler}" says it is 1.0.0-beta.2
                  fix: use a compiler that is new enough, naming it with EMOJICODEC if it is not the first on the PATH, or depend on a version of the package that this compiler builds

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Built().ShouldBeEmpty();
        }

        [Theory]
        [InlineData("Emojicode Compiler 1.0. Visit https://www.emojicode.org for help.")]
        [InlineData("Emojicode Compiler 1.2.")]
        public async Task A_compiler_that_is_new_enough_builds_what_asks_for_it(string banner)
        {
            using var sandbox = new Sandbox();
            ReleaseNeeding(sandbox, "released", ">=1.0.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/released@1.0");
            sandbox.Tool("tools/emojicodec", "emojicodec", banner);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task With_no_archiver_nothing_is_compiled_and_pmj_says_how_to_name_one()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "ar"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The archiver was not found.
                  why: no program called ar is in any directory of PATH
                  fix: install a C toolchain, which has one, or set AR to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_everything_built_before_no_archiver_is_needed()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "ar"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task AR_names_the_archiver()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "ar"));
            var named = sandbox.Tool("elsewhere/llvm-ar", "ar");
            sandbox.Variables["AR"] = named;

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Tools.Calls.Last().Program.ShouldBe(named);
        }

        [Fact]
        public async Task An_archiver_that_fails_stops_the_build_and_nothing_of_the_package_is_kept()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Tools.Fails.Add("ar");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                [crypto] made-up ar: told to fail
                error[build.archive-failed]: The archive of "@thatplatypus/crypto" 1.0.0 could not be made.
                  why: the archiver ended with status 1, and said: made-up ar: told to fail
                  fix: read what the archiver printed, which is above; if it is not an archiver that takes "rcs", set AR to one that does

                """.ReplaceLineEndings(Environment.NewLine));
            Directory.GetDirectories(Path.Combine(sandbox.Home, "built", "thatplatypus", "crypto", "1.0.0")).ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
        }

        [Fact]
        public async Task An_archiver_that_ends_well_and_leaves_no_archive_has_failed()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Tools.Idle.Add("ar");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.archive-failed]: The archive of \"@thatplatypus/crypto\" 1.0.0 could not be made.");
            sandbox.Plain(run.Error).ShouldMatch("  why: the archiver ended as if all were well and did not write \"~/home/built/thatplatypus/crypto/1\\.0\\.0/[0-9a-f]{8}\\.tmp/crypto/libcrypto\\.a\"");
            sandbox.Built().ShouldBeEmpty();
        }

        [Fact]
        public async Task Of_what_a_tool_that_failed_wrote_it_is_what_it_wrote_as_an_error_that_is_given_as_the_reason()
        {
            using var sandbox = await WithCryptoInstalledAsync("📣 Compiling crypto\n💥 Variable \"nothing\" not defined.\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            sandbox.Plain(run.Error).ShouldContain($"  why: the compiler ended with status 1, and said: {Cached(sandbox)}:3:1: 🚨 error: Variable \"nothing\" not defined.");
            run.Error.ShouldContain("[crypto] Compiling crypto" + Environment.NewLine);
        }

        [Fact]
        public async Task Code_the_compiler_refuses_stops_the_build_and_is_reported_in_the_compilers_words()
        {
            using var sandbox = await WithCryptoInstalledAsync("💥 Variable \"nothing\" not defined.\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBe($"Building @thatplatypus/crypto 1.0.0{Environment.NewLine}");
            sandbox.Plain(run.Error).ShouldBe(
                $"""
                [crypto] {Cached(sandbox)}:2:1: 🚨 error: Variable "nothing" not defined.
                [crypto]   💥 Variable "nothing" not defined.
                [crypto]   ⬆️
                error[build.compile-failed]: "@thatplatypus/crypto" 1.0.0 could not be compiled.
                  why: the compiler ended with status 1, and said: {Cached(sandbox)}:2:1: 🚨 error: Variable "nothing" not defined.
                  fix: mend what the compiler names, which is printed above; if the code is a package's and not yours, tell its author

                """.ReplaceLineEndings(Environment.NewLine));
            Directory.GetDirectories(Path.Combine(sandbox.Home, "built", "thatplatypus", "crypto", "1.0.0")).ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
            sandbox.Tools.Calls.ShouldNotContain(call => call.Tool == "ar");
        }

        [Theory]
        [InlineData("🧨 out", "the compiler ended as if all were well, and what it printed holds \"Detected in:\", which is its own check refusing the code it generated")]
        [InlineData("🧨 err", "the compiler ended as if all were well, and what it printed holds \"Detected in:\", which is its own check refusing the code it generated")]
        [InlineData("💣", "the compiler ended with status 70, and said: 💣 The compiler crashed due to an internal problem: made up")]
        public async Task A_compiler_that_fails_in_a_way_its_status_hides_or_that_crashes_has_failed_all_the_same(string mark, string reason)
        {
            using var sandbox = await WithCryptoInstalledAsync(mark + "\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.compile-failed]: \"@thatplatypus/crypto\" 1.0.0 could not be compiled." + Environment.NewLine + "  why: " + reason + Environment.NewLine);
            sandbox.Built().ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
        }

        [Fact]
        public async Task A_compiler_that_ends_well_and_writes_no_object_has_failed()
        {
            using var sandbox = await WithCryptoInstalledAsync("👻\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            sandbox.Plain(run.Error).ShouldMatch("  why: the compiler ended as if all were well and did not write \"~/home/built/thatplatypus/crypto/1\\.0\\.0/[0-9a-f]{8}\\.tmp/work/crypto\\.o\"");
            sandbox.Built().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_compiler_that_ends_well_and_writes_no_interface_has_failed_and_nothing_of_the_package_is_kept()
        {
            using var sandbox = await WithCryptoInstalledAsync("🙈\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            sandbox.Plain(run.Error).ShouldMatch("  why: the compiler ended as if all were well and did not write \"~/home/built/thatplatypus/crypto/1\\.0\\.0/[0-9a-f]{8}\\.tmp/crypto/🏛\"");
            sandbox.Built().ShouldBeEmpty();
        }

        [Fact]
        public async Task What_was_built_before_a_package_that_fails_is_kept_and_nothing_is_put_in_the_project()
        {
            using var sandbox = new Sandbox();
            sandbox.ReleaseSource("@thatplatypus/crypto", "1.0.0", "");
            sandbox.ReleaseSource("@thatplatypus/deflate", "0.1.0", "");
            sandbox.ReleaseSource("@thatplatypus/grapevine", "0.3.0", "💥 no\n", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.compile-failed]: \"@thatplatypus/grapevine\" 0.3.0 could not be compiled.");
            sandbox.Keys("crypto").Count.ShouldBe(1);
            sandbox.Keys("deflate").Count.ShouldBe(1);
            sandbox.Keys("grapevine").ShouldBeEmpty();
            sandbox.Files("packages").ShouldBeEmpty();
        }

        [Fact]
        public async Task What_a_compiler_says_of_code_it_accepts_is_passed_on_behind_the_packages_name()
        {
            using var sandbox = await WithCryptoInstalledAsync("⚠️ Something here is deprecated.\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.Plain(run.Error).ShouldBe($"[crypto] {Cached(sandbox)}:2:1: ⚠️  warning: Something here is deprecated.{Environment.NewLine}");
            run.Output.ShouldEndWith($"Built 1 package into packages/.{Environment.NewLine}");
        }

        [Fact]
        public async Task A_pmj_that_is_stopped_while_the_compiler_runs_ends_as_stopped_and_leaves_nothing_half_built()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            using var stop = new CancellationTokenSource();
            sandbox.Stop = stop.Token;
            sandbox.Tools.Before = async call =>
            {
                if (call.Arguments.Contains("-c"))
                {
                    await stop.CancelAsync();
                }
            };

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(130);
            Directory.GetDirectories(Path.Combine(sandbox.Home, "built", "thatplatypus", "crypto", "1.0.0")).ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
        }
    }
}
