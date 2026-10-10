using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// <c>pmj run</c>: builds an application and runs it. The program is the one that speaks on the
    /// output, so what pmj has to say of the build goes where problems go, and pmj ends as the
    /// program ended.
    /// </summary>
    public sealed class RunTests
    {
        private static Sandbox AnApplication(string source = "🏁 🍇\n  😀 🔤Hello🔤❗️\n🍉\n")
        {
            var sandbox = new Sandbox();
            sandbox.Project("@someone/app");
            sandbox.Write("src/main.🍇", source);
            return sandbox;
        }

        [Fact]
        public async Task An_application_is_built_and_run_and_only_the_program_speaks_on_the_output()
        {
            using var sandbox = AnApplication();

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Hello{Environment.NewLine}");
            run.Error.ShouldBe(
                """
                Building @someone/app 0.1.0
                Built the application target/debug/app.

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task The_packages_an_application_depends_on_are_built_first_as_a_build_builds_them()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("src/main.🍇", "📦 grapevine 🏠\n🏁 🍇\n  😀 🔤Serving🔤❗️\n🍉\n");

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Serving{Environment.NewLine}");
            run.Error.ShouldStartWith($"Building @thatplatypus/crypto 1.0.0{Environment.NewLine}");
            run.Error.ShouldContain($"Built 3 packages into packages/.{Environment.NewLine}");
            sandbox.Files("packages").Count.ShouldBe(12);
        }

        [Fact]
        public async Task What_follows_the_two_dashes_is_given_to_the_program_as_it_is_and_is_nothing_to_pmj()
        {
            using var sandbox = AnApplication();

            var run = await sandbox.RunAsync("run", "--", "--release", "-x", "two words", "--", "@scope/name");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Hello{Environment.NewLine}given: --release|-x|two words|--|@scope/name{Environment.NewLine}");
            sandbox.Has("target/debug/app").ShouldBeTrue();
            sandbox.Has("target/release/app").ShouldBeFalse();
        }

        [Fact]
        public async Task The_program_is_run_in_the_directory_pmj_was_run_in_and_pmj_ends_as_the_program_ended()
        {
            using var sandbox = AnApplication("🏁 🍇\n  🚪 7\n🍉\n");

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(7);
            var program = sandbox.Tools.Calls.Last();
            program.Tool.ShouldBe("program");
            program.Program.ShouldBe(sandbox.PathOf("target/debug/app"));
            program.WorkingDirectory.ShouldBe(sandbox.Work);
        }

        [Fact]
        public async Task With_release_the_optimized_program_is_the_one_that_is_run()
        {
            using var sandbox = AnApplication();

            var run = await sandbox.RunAsync("run", "--release");

            run.Status.ShouldBe(0);
            run.Error.ShouldEndWith($"Built the application target/release/app.{Environment.NewLine}");
            sandbox.Tools.Calls.Last().Program.ShouldBe(sandbox.PathOf("target/release/app"));
            sandbox.Tools.Compiles.Last().Arguments.ShouldContain("-O");
        }

        [Fact]
        public async Task A_library_has_no_program_to_run_and_is_not_built_for_one()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", TestPackage.Manifest("@someone/shelf", "0.1.0", "library", null));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[run.not-an-app]: "@someone/shelf" is a library, and has no program to run.
                  why: packmoji.json gives its kind as "library", and pmj run builds an application and runs it
                  fix: run pmj build to build the library, or run this in an application that depends on it

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public async Task An_application_that_does_not_build_is_not_run()
        {
            using var sandbox = AnApplication();
            (await sandbox.RunAsync("run")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Clear();
            sandbox.Write("src/main.🍇", "🏁 🍇\n💥 Variable \"nothing\" not defined.\n🍉\n");

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[build.compile-failed]: \"@someone/app\" 0.1.0 could not be compiled.");
            sandbox.Tools.Calls.ShouldNotContain(call => call.Tool == "program");
        }

        [Fact]
        public async Task A_program_that_was_built_and_cannot_be_started_is_said_to_be_that()
        {
            using var sandbox = AnApplication();
            sandbox.Tools.Unstartable.Add("program");

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(1);
            run.Error.ShouldEndWith(
                $"""
                error[run.failed]: The program was built and could not be started.
                  why: "{sandbox.PathOf("target/debug/app")}" is there, and this machine would not run it
                  fix: check that the compiler and the C++ compiler both build for this machine: a program built for another one cannot be run here

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task Where_there_is_no_project_there_is_nothing_to_run()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[project.not-found]: ");
            sandbox.Files().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_pmj_that_is_stopped_while_the_program_runs_ends_as_stopped()
        {
            using var sandbox = AnApplication();
            using var stop = new CancellationTokenSource();
            sandbox.Stop = stop.Token;
            sandbox.Tools.Before = async call =>
            {
                if (call.Tool == "program")
                {
                    await stop.CancelAsync();
                }
            };

            var run = await sandbox.RunAsync("run");

            run.Status.ShouldBe(130);
            run.Output.ShouldBeEmpty();
        }
    }
}
