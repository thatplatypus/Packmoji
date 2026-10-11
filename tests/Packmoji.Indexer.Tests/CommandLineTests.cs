using Shouldly;
using Xunit;

namespace Packmoji.Indexer.Tests
{
    /// <summary>How the indexer is asked: a command and its options, each once. Asked any other way it does nothing, and says how.</summary>
    public sealed class CommandLineTests : IDisposable
    {
        private readonly Desk _desk = new();

        public void Dispose() => _desk.Dispose();

        public static TheoryData<string[], string> Mistakes => new()
        {
            { [], "No command was given." },
            { ["index"], "\"index\" is not a command." },
            { ["run"], "run needs --registry." },
            { ["run", "--registry", "r"], "run needs --repositories." },
            { ["run", "--registry"], "--registry needs a value." },
            { ["run", "--registry", "r", "--repositories", "l", "--registry", "s"], "--registry was given twice." },
            { ["run", "--registry", "r", "--repositories", "l", "--now", "then"], "\"--now\" is not an option of run." },
            { ["run", "--registry", "r", "--repositories", "l", "more"], "\"more\" is not an option of run." },
            { ["run", "--registry", "--repositories", "--repositories", "l"], "--registry needs a value." },
            { ["check", "--before", "b"], "check needs --after." },
            { ["check", "--after", "a"], "check needs --before." },
            { ["check", "--before", "b", "--after", "a", "--registry", "r"], "\"--registry\" is not an option of check." },
            { ["check", "--before", "b", "--after", "a", "--message"], "--message needs a value." },
            { ["run", "--registry", "", "--repositories", "l"], "--registry needs a value." },
        };

        [Theory]
        [MemberData(nameof(Mistakes))]
        public async Task A_command_that_is_not_asked_as_it_is_written_does_nothing_and_says_how_it_is_asked(string[] args, string said)
        {
            var status = await _desk.RunAsync(args);

            status.ShouldBe(2);
            _desk.Error.ShouldBe(
                said + "\n" +
                """
                usage:
                  Packmoji.Indexer run --registry <directory> --repositories <file>
                  Packmoji.Indexer check --before <directory> --after <directory> [--message <file>]

                """.ReplaceLineEndings("\n"));
            _desk.Out.ShouldBeEmpty();
            _desk.GitHub.Asked.ShouldBeEmpty();
            Directory.Exists(_desk.Registry).ShouldBeFalse();
        }

        [Fact]
        public async Task The_options_of_a_command_may_come_in_any_order()
        {
            var status = await _desk.RunAsync("run", "--repositories", _desk.List, "--registry", _desk.Registry);

            status.ShouldBe(0);
        }
    }
}
