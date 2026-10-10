using Packmoji.Cli.Output;
using Packmoji.Core.Reports;

namespace Packmoji.Cli.Commands
{
    /// <summary><c>pmj tree</c>: what the lockfile holds. It reads the project's two files and asks nothing of anyone.</summary>
    internal static class TreeCommand
    {
        /// <param name="json">Whether to answer a tool, with one JSON object, and not a person, with a drawing.</param>
        public static int Run(PmjHost host, bool json)
        {
            var opened = ProjectSession.Open(host);
            if (!opened.Succeeded)
            {
                return DiagnosticPrinter.Report(host, opened.Diagnostics, opened.Omitted, json);
            }

            var project = opened.Value;
            if (!project.IsLocked)
            {
                return DiagnosticPrinter.Report(host, [CommandDiagnostics.NotInstalled(missing: project.Lockfile is null)], json: json);
            }

            host.Out.Write(json ? TreeReport.Json(project.Manifest, project.Lockfile) : TreeReport.Text(project.Manifest, project.Lockfile));
            return ExitStatus.Success;
        }
    }
}
