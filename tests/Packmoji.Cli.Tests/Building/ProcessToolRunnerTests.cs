using System.Diagnostics;
using Packmoji.Cli.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Building
{
    /// <summary>
    /// Starting a real program. Every other test of a build has made-up tools in the place of this,
    /// so here is where pmj is held to what a compiler needs of it: its arguments as they are, its
    /// directory, both of its streams read to their ends, and an end when pmj is stopped.
    /// </summary>
    public sealed class ProcessToolRunnerTests : IDisposable
    {
        private const string Shell = "/bin/sh";

        private readonly ProcessToolRunner _tools = new();
        private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"))).FullName;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        // There is a shell to stand in for a tool wherever a compiler runs, and nowhere else.
        private static void NeedsAShell() => Assert.SkipWhen(OperatingSystem.IsWindows(), "No Emojicode compiler runs on Windows, and these tests stand a shell in for one.");

        private Task<ToolRun?> Sh(string script, params string[] arguments) =>
            _tools.RunAsync(Shell, ["-c", script, "sh", .. arguments], _directory, Cancellation);

        [Fact]
        public async Task A_program_is_run_to_its_end_and_gives_its_status_and_what_it_wrote_to_each_stream()
        {
            NeedsAShell();

            var run = await Sh("echo to the output; echo to the error >&2; exit 3");

            run.ShouldBe(new ToolRun(3, "to the output\n", "to the error\n"));
        }

        [Fact]
        public async Task Each_argument_reaches_the_program_as_it_is_with_no_shell_to_read_it_first()
        {
            NeedsAShell();

            var run = await Sh("printf '%s|' \"$@\"", "two words", "$HOME", "*", "a;b", "-o", "🏛");

            run!.Output.ShouldBe("two words|$HOME|*|a;b|-o|🏛|");
        }

        [Fact]
        public async Task The_program_is_run_in_the_directory_it_is_given()
        {
            NeedsAShell();
            File.WriteAllText(Path.Combine(_directory, "only-here.txt"), "");

            var run = await Sh("ls");

            run!.Output.ShouldBe("only-here.txt\n");
        }

        [Fact]
        public async Task A_program_that_writes_more_to_both_streams_than_a_pipe_holds_is_read_to_the_end_of_each()
        {
            NeedsAShell();

            var run = await Sh("i=0; while [ $i -lt 20000 ]; do echo \"out $i\"; echo \"err $i\" >&2; i=$((i+1)); done");

            run!.Status.ShouldBe(0);
            run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(20_000);
            run.Error.ShouldEndWith("err 19999\n");
        }

        [Fact]
        public async Task A_program_that_waits_to_be_typed_to_is_told_at_once_that_nothing_will_be()
        {
            NeedsAShell();

            var run = await Sh("cat; echo done");

            run!.Output.ShouldBe("done\n");
        }

        [Fact]
        public async Task A_program_that_is_not_there_gives_nothing_and_neither_does_a_file_that_is_no_program()
        {
            NeedsAShell();
            var text = Path.Combine(_directory, "notes.txt");
            File.WriteAllText(text, "not a program");

            (await _tools.RunAsync(Path.Combine(_directory, "no-such-program"), [], _directory, Cancellation)).ShouldBeNull();
            (await _tools.RunAsync(text, [], _directory, Cancellation)).ShouldBeNull();
            (await _tools.RunAttachedAsync(Path.Combine(_directory, "no-such-program"), [], _directory, Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task A_pmj_that_is_stopped_stops_the_program_it_started()
        {
            NeedsAShell();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            var running = _tools.RunAsync(Shell, ["-c", "echo $$ > pid; exec sleep 60"], _directory, stop.Token);
            var pid = Path.Combine(_directory, "pid");
            while (!File.Exists(pid) || File.ReadAllText(pid).Length == 0)
            {
                await Task.Delay(20, Cancellation);
            }

            var started = Stopwatch.StartNew();
            await stop.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => running);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
            Should.Throw<ArgumentException>(() => Process.GetProcessById(int.Parse(File.ReadAllText(pid).Trim(), System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public async Task A_program_run_with_pmjs_own_streams_gives_its_status()
        {
            NeedsAShell();

            (await _tools.RunAttachedAsync(Shell, ["-c", "exit 7"], _directory, Cancellation)).ShouldBe(7);
        }
    }
}
