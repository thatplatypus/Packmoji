using Packmoji.Cli.Building;
using Packmoji.Cli.Output;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Reports;

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
        /// <param name="json">Whether to answer a tool, with one JSON object that says where each package is.</param>
        public static async Task<int> RunAsync(PmjHost host, bool release, bool dependenciesOnly, bool json, CancellationToken cancellationToken)
        {
            // A tool is told once, at the end. What the build does on the way is said to a person alone.
            var outcome = await Builder.RunAsync(host, new BuildRequest(release, dependenciesOnly), json ? TextWriter.Null : host.Out, cancellationToken);
            var problems = outcome.Diagnostics.OrderBy(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToList();
            if (json)
            {
                host.Out.Write(BuildReport.Json(
                    problems,
                    outcome.OmittedDiagnostics,
                    outcome.Compiler,
                    Path.Combine(host.WorkingDirectory, PlacedPackages.DirectoryName),
                    outcome.Packages,
                    outcome.Project));
            }
            else
            {
                DiagnosticPrinter.Print(host.Error, problems, outcome.OmittedDiagnostics);
            }

            return outcome.Succeeded ? ExitStatus.Success : ExitStatus.Problem;
        }
    }
}
