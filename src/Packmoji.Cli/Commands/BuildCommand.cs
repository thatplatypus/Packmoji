using Packmoji.Cli.Building;
using Packmoji.Cli.Output;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj build</c>: compiles what the lockfile holds, each package once for the whole machine,
    /// and puts it in the project. It chooses no version: that is <c>pmj install</c>'s to do.
    /// </summary>
    internal static class BuildCommand
    {
        /// <param name="release">Whether the compiler is to optimize.</param>
        /// <param name="dependenciesOnly">Whether to stop before the project itself, as a tool that compiles the project its own way asks.</param>
        public static async Task<int> RunAsync(PmjHost host, bool release, bool dependenciesOnly, CancellationToken cancellationToken)
        {
            var outcome = await Builder.RunAsync(host, new BuildRequest(release, dependenciesOnly), host.Out, cancellationToken);
            DiagnosticPrinter.Print(host.Error, outcome.Diagnostics.OrderBy(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning).ToList(), outcome.OmittedDiagnostics);
            return outcome.Succeeded ? ExitStatus.Success : ExitStatus.Problem;
        }
    }
}
