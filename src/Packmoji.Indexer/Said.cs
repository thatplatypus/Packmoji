using Packmoji.Core.Diagnostics;

namespace Packmoji.Indexer
{
    /// <summary>
    /// How the indexer says a problem that was found in a file: on one line, beginning with where. A
    /// diagnostic's own text is fit to print whatever it was made from, and it never begins the line.
    /// </summary>
    /// <remarks>
    /// That is not enough for everything that may read the indexer's output. A workflow's runner
    /// takes a line that holds <c>##[</c> anywhere for a command to itself, and a diagnostic repeats
    /// what a file says. So the workflow stops the runner's commands for as long as the check prints.
    /// </remarks>
    internal static class Said
    {
        public static string Of(Diagnostic diagnostic)
        {
            var where = diagnostic.Location is { } location ? $"{location.File}:{location.Line}:{location.Column}: " : "";
            return $"{where}{diagnostic.Message} That is because {diagnostic.Reason}.";
        }

        public static string Count(int count, string thing) => count switch
        {
            0 => $"no {thing}",
            1 => $"1 {thing}",
            _ => $"{count} {thing}s",
        };
    }
}
