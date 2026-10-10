using Packmoji.Core.Diagnostics;

namespace Packmoji.Cli.Output
{
    /// <summary>
    /// Whom a command is answering: a person, or a tool that asked with <c>--json</c>. A command
    /// that stops says why through this, so that every way it can stop is said the one way: on
    /// standard error for a person, and as one object on the output for a tool.
    /// </summary>
    internal readonly record struct Reply(PmjHost Host, bool Json)
    {
        /// <summary>Says the problems that stopped the command, and gives the status it ends with.</summary>
        public int Stop(IReadOnlyList<Diagnostic> problems, int omitted = 0) => DiagnosticPrinter.Report(Host, problems, omitted, Json);

        /// <summary>Says the one problem that stopped the command, and gives the status it ends with.</summary>
        public int Stop(Diagnostic problem) => Stop([problem]);
    }
}
