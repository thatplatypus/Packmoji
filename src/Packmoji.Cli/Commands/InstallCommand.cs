using Packmoji.Cli.Output;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj install</c>: fetches what the lockfile holds. Versions are chosen only when the manifest
    /// asks for something else than the lockfile recorded, so the same lockfile gives the same bytes
    /// on every machine and on every day.
    /// </summary>
    internal static class InstallCommand
    {
        /// <param name="locked">Whether a lockfile that would have to be written or changed is an error, as it should be where nobody is there to see it change.</param>
        public static async Task<int> RunAsync(PmjHost host, bool locked, CancellationToken cancellationToken)
        {
            var opened = ProjectSession.OpenToFetch(host);
            if (!opened.Succeeded)
            {
                return DiagnosticPrinter.Report(host, opened.Diagnostics, opened.Omitted);
            }

            var project = opened.Value;
            if (project.IsLocked)
            {
                return await project.InstallLockedAsync(cancellationToken);
            }

            return locked
                ? DiagnosticPrinter.Report(host, CommandDiagnostics.LockWouldChange(missing: project.Lockfile is null))
                : await project.ResolveAsync(project.Manifest, project.Source(), said: null, cancellationToken);
        }
    }
}
