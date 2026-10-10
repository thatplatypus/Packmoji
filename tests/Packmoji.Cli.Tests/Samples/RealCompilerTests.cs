using System.Text.Json;
using Packmoji.Cli.Building;
using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Samples
{
    /// <summary>
    /// The sample projects, built by the Emojicode compiler as it is released. Every other test has a
    /// compiler that is made up from what the real one was read and seen to do, and these are where
    /// pmj and the real one meet: its flags, its files, its C++ headers, a real archive and a real link.
    /// </summary>
    /// <remarks>
    /// The compiler runs on x86-64 alone, so these run only where they are asked for:
    /// <c>scripts/real-compiler.sh</c> asks, in a container that has the compiler. Anywhere else
    /// each says why it was not run.
    /// </remarks>
    [Trait("Category", "RealCompiler")]
    public sealed class RealCompilerTests
    {
        private const string App = "hello-pkg/app";

        // The digest of emojicodec in Emojicode-1.0-beta.2-Linux-x86_64.tar.gz, as its release held it on 2026-10-10.
        private const string ReleasedCompiler = "08da67b417a11db6a3647ccc3e563f541706fa5f38426b89c19ab3f68ecbaa91";

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static void OnlyWhereAsked() =>
            Assert.SkipUnless(
                Environment.GetEnvironmentVariable("PACKMOJI_REAL_COMPILER") == "1",
                "This needs the Emojicode compiler, which is released for x86-64 alone. scripts/real-compiler.sh runs it in a container that has one.");

        // The sample on a machine with real tools: its libraries packed and released, and the application installed as it is locked.
        private static async Task<Sandbox> WithTheSampleInstalledAsync()
        {
            var sandbox = Sandbox.WithRealTools();
            await sandbox.ReleaseTheSampleAsync();
            var installed = await sandbox.RunInAsync(App, "install", "--locked");
            installed.Error.ShouldBeEmpty();
            installed.Status.ShouldBe(0);
            return sandbox;
        }

        // What a built program writes when it is run. pmj run gives a program pmj's own output, which a test cannot read.
        private static async Task<ToolRun> RunAsync(Sandbox sandbox, string program) =>
            (await new ProcessToolRunner().RunAsync(sandbox.PathOf(program), [], sandbox.PathOf(App), Cancellation)).ShouldNotBeNull();

        [Fact]
        public async Task The_sample_is_built_and_says_what_its_two_packages_and_its_cpp_give_it()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();

            var built = await sandbox.RunInAsync(App, "build");

            built.Error.ShouldBeEmpty();
            built.Status.ShouldBe(0);
            built.Output.ShouldBe(
                """
                Building @thatplatypus/hello_words 0.1.0
                Building @thatplatypus/hello_greeter 0.1.0
                Built 2 packages into packages/.
                Building @thatplatypus/hello 0.1.0
                Built the application target/debug/hello.

                """.ReplaceLineEndings(Environment.NewLine));
            (await RunAsync(sandbox, $"{App}/target/debug/hello")).ShouldBe(new ToolRun(0, "Hello, Packmoji!\n42\n", ""));
        }

        [Fact]
        public async Task The_sample_is_run_by_pmj_and_ends_as_its_program_ends()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();

            var run = await sandbox.RunInAsync(App, "run");

            run.Status.ShouldBe(0);
            run.Error.ShouldEndWith($"Built the application target/debug/hello.{Environment.NewLine}");
        }

        [Fact]
        public async Task With_release_the_optimized_sample_says_the_same()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();

            var built = await sandbox.RunInAsync(App, "build", "--release");

            built.Error.ShouldBeEmpty();
            built.Status.ShouldBe(0);
            (await RunAsync(sandbox, $"{App}/target/release/hello")).ShouldBe(new ToolRun(0, "Hello, Packmoji!\n42\n", ""));
        }

        [Fact]
        public async Task What_was_built_for_one_project_is_not_built_again_for_another_that_locks_the_same()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();
            (await sandbox.RunInAsync(App, "build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Write("another/packmoji.json", sandbox.Read($"{App}/packmoji.json"));
            sandbox.Write("another/packmoji.lock", sandbox.Read($"{App}/packmoji.lock"));

            var again = await sandbox.RunInAsync("another", "build", "--dependencies-only");

            again.Error.ShouldBeEmpty();
            again.Output.ShouldBe($"2 packages were built before, and are in packages/.{Environment.NewLine}");
        }

        [Fact]
        public async Task A_tool_is_told_of_the_released_compiler_and_where_a_real_interface_archive_and_report_are()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();

            var run = await sandbox.RunInAsync(App, "build", "--dependencies-only", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            using var answer = JsonDocument.Parse(run.Output);
            var compiler = answer.RootElement.GetProperty("compiler");
            compiler.GetProperty("version").GetString().ShouldBe("1.0.0-beta.2");
            compiler.GetProperty("sha256").GetString().ShouldBe(ReleasedCompiler);

            var greeter = answer.RootElement.GetProperty("packages").EnumerateArray().Single(package => package.GetProperty("bareName").GetString() == "hello_greeter");
            greeter.GetProperty("link").EnumerateArray().Select(library => library.GetString()).ShouldBe(["m"]);
            var directory = greeter.GetProperty("directory").GetString()!;

            // An interface begins with what its package imports, an archive with the word every archive begins with, and the report is JSON.
            File.ReadAllText(Path.Combine(directory, "🏛")).ShouldStartWith("📦 hello_words 🏠");
            File.ReadAllBytes(Path.Combine(directory, "libhello_greeter.a")).Take(8).ShouldBe("!<arch>\n"u8.ToArray());
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "documentation.json")));
            report.RootElement.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()).ShouldBe(["🙋"]);
        }

        [Fact]
        public async Task Code_the_real_compiler_refuses_is_reported_in_its_own_words_and_no_program_is_left()
        {
            OnlyWhereAsked();
            using var sandbox = await WithTheSampleInstalledAsync();
            sandbox.Write($"{App}/src/main.🍇", "🏁 🍇\n  😀 nothing❗️\n🍉\n");

            var built = await sandbox.RunInAsync(App, "build");

            built.Status.ShouldBe(1);
            built.Error.ShouldContain("[hello] ");
            built.Error.ShouldContain("🚨 error: Variable \"nothing\" not defined.");
            built.Error.ShouldContain("error[build.compile-failed]: \"@thatplatypus/hello\" 0.1.0 could not be compiled.");
            sandbox.Has($"{App}/target/debug/hello").ShouldBeFalse();
        }
    }
}
