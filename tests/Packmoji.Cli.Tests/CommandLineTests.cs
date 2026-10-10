using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests
{
    public sealed class CommandLineTests
    {
        private static async Task<(int Status, string Output, string Error)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var status = await PmjCommandLine.RunAsync(args, output, error, TestContext.Current.CancellationToken);
            return (status, output.ToString(), error.ToString());
        }

        [Fact]
        public async Task Version_prints_a_version_and_ends_with_zero()
        {
            var (status, output, error) = await RunAsync("--version");

            status.ShouldBe(0);
            output.Trim().ShouldMatch(@"^\d+\.\d+\.\d+");
            error.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public async Task Help_says_what_pmj_is_and_ends_with_zero(string flag)
        {
            var (status, output, error) = await RunAsync(flag);

            status.ShouldBe(0);
            output.ShouldContain("Packmoji, the package manager for Emojicode.");
            output.ShouldContain("--version");
            error.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_no_arguments_pmj_shows_its_help_instead_of_saying_nothing()
        {
            var (status, output, _) = await RunAsync();

            status.ShouldBe(0);
            output.ShouldContain("Packmoji, the package manager for Emojicode.");
        }

        [Theory]
        [InlineData("frobnicate")]
        [InlineData("--no-such-option")]
        public async Task Something_pmj_does_not_know_ends_with_the_status_of_a_command_line_it_could_not_read(string argument)
        {
            var (status, output, error) = await RunAsync(argument);

            status.ShouldBe(ExitStatus.Usage);
            output.ShouldBeEmpty();
            error.ShouldContain(argument);
            error.ShouldContain("Run pmj --help to see how it is used.");
        }

        [Fact]
        public async Task An_argument_that_is_missing_is_a_command_line_pmj_could_not_read_and_the_command_is_named()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new");

            run.Status.ShouldBe(ExitStatus.Usage);
            run.Error.ShouldContain("Run pmj new --help to see how it is used.");
            sandbox.Files().ShouldBeEmpty();
        }

        [Fact]
        public async Task Help_for_a_command_says_what_it_takes()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new", "--help");

            run.Status.ShouldBe(0);
            run.Output.ShouldContain("--lib");
            run.Output.ShouldContain("@scope/name");
            run.Error.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("--direct", "new", "@thatplatypus/hello")]
        [InlineData("new", "@thatplatypus/hello", "--direct")]
        public async Task Direct_is_accepted_wherever_it_is_put_and_changes_nothing(params string[] args)
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync(args);

            run.Status.ShouldBe(0);
            sandbox.Has("hello/packmoji.json").ShouldBeTrue();
        }

        [Fact]
        public async Task A_fault_in_pmj_ends_with_a_status_of_its_own_and_asks_to_be_reported()
        {
            using var sandbox = new Sandbox { Output = new BrokenWriter() };

            var run = await sandbox.RunAsync("new", "@thatplatypus/hello");

            run.Status.ShouldBe(ExitStatus.InternalError);
            run.Error.ShouldContain("pmj failed in a way it should not have: InvalidOperationException: the test broke it");
            run.Error.ShouldContain("https://github.com/thatplatypus/Packmoji/issues");
        }

        // Standard output that fails in a way nothing in pmj expects.
        private sealed class BrokenWriter : StringWriter
        {
            public override void WriteLine(string? value) => throw new InvalidOperationException("the test broke it");
        }
    }
}
