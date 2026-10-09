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
        public async Task Something_pmj_does_not_know_is_refused_with_a_status_that_is_not_zero(string argument)
        {
            var (status, output, error) = await RunAsync(argument);

            status.ShouldNotBe(0);
            (output + error).ShouldContain(argument);
        }
    }
}
