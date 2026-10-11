using Packmoji.Cli.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Scripts
{
    /// <summary>
    /// <c>scripts/glibc-floor.sh</c>, which reads from a Linux program which C library it asks for.
    /// A native program built on a newer Linux does not start on an older one, so a release is held
    /// to the oldest it promises to start on. The tests give it a made-up <c>objdump</c>.
    /// </summary>
    public sealed class GlibcFloorTests : IDisposable
    {
        private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "scripts", "glibc-floor.sh");

        private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose() => Directory.Delete(_root, recursive: true);

        /// <param name="symbols">What objdump prints of the program: a line for each thing it takes from a library.</param>
        private async Task<ToolRun> RunAsync(string floor, params string[] symbols)
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "It is a Linux program that is asked, and the script is run by bash.");
            var tools = new ProcessToolRunner();
            var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
            File.WriteAllText(Path.Combine(bin, "objdump"), "#!/bin/sh\ncat <<'SAID'\n" + string.Join('\n', symbols) + "\nSAID\n");
            (await tools.RunAsync("/bin/chmod", ["+x", Path.Combine(bin, "objdump")], _root, TestContext.Current.CancellationToken)).ShouldNotBeNull().Status.ShouldBe(0);
            var program = Path.Combine(_root, "pmj");
            File.WriteAllText(program, "a program\n");

            var run = await tools.RunAsync("/bin/sh", ["-c", "PATH=\"$1:$PATH\" exec bash \"$2\" \"$3\" \"$4\"", "sh", bin, Script, program, floor], _root, TestContext.Current.CancellationToken);
            return run.ShouldNotBeNull();
        }

        private static string Needs(string version, string symbol) => $"0000000000000000      DF *UND*\t0000000000000000 ({version}) {symbol}";

        [Fact]
        public async Task A_program_that_asks_for_nothing_newer_than_what_is_promised_passes_and_is_said_to()
        {
            var run = await RunAsync("2.35", Needs("GLIBC_2.2.5", "puts"), Needs("GLIBC_2.34", "pthread_create"), Needs("GLIBC_2.17", "clock_gettime"));

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldContain("2.34");
            run.Output.ShouldContain("2.35");
        }

        [Fact]
        public async Task A_program_that_asks_for_a_newer_one_fails_and_both_are_named()
        {
            var run = await RunAsync("2.35", Needs("GLIBC_2.2.5", "puts"), Needs("GLIBC_2.38", "__isoc23_strtol"));

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("glibc 2.38");
            run.Error.ShouldContain("2.35");
        }

        [Theory]
        [InlineData("GLIBC_2.4", 0)]
        [InlineData("GLIBC_2.35", 0)]
        [InlineData("GLIBC_2.36", 1)]
        [InlineData("GLIBC_2.100", 1)]
        [InlineData("GLIBC_3.0", 1)]
        public async Task Versions_are_compared_as_numbers_and_not_as_text(string needed, int status)
        {
            (await RunAsync("2.35", Needs(needed, "something"))).Status.ShouldBe(status);
        }

        // As text, 2.9 comes after 2.36, and would be taken for the newest of the three.
        [Fact]
        public async Task Of_several_versions_a_program_is_held_to_the_newest_as_a_number()
        {
            var run = await RunAsync("2.35", Needs("GLIBC_2.9", "inotify_init1"), Needs("GLIBC_2.36", "arc4random"), Needs("GLIBC_2.4", "__stack_chk_fail"));

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("glibc 2.36");
        }

        [Fact]
        public async Task What_is_not_a_version_of_the_c_library_is_not_counted()
        {
            var run = await RunAsync("2.35", Needs("GLIBCXX_3.4.99", "from the C++ library"), Needs("GLIBC_PRIVATE", "private"), Needs("GCC_9.0", "from the compiler's library"), Needs("GLIBC_2.17", "clock_gettime"));

            run.Status.ShouldBe(0);
            run.Output.ShouldContain("2.17");
        }

        [Fact]
        public async Task A_program_that_asks_for_no_version_at_all_passes()
        {
            var run = await RunAsync("2.35", "pmj:     file format elf64-x86-64", "DYNAMIC SYMBOL TABLE:");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }
    }
}
