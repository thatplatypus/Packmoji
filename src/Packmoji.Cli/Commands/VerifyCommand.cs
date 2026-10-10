using Packmoji.Cli.Cache;
using Packmoji.Cli.Output;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Reports;
using Packmoji.Core.Resolution;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj verify</c>: downloads every locked package again, and holds what its release carries and
    /// what the cache holds of it to the lockfile. It is a check and changes nothing, not even the
    /// cache: what it finds is for a person to act on.
    /// </summary>
    internal static class VerifyCommand
    {
        /// <param name="json">Whether to answer a tool, with one JSON object.</param>
        public static async Task<int> RunAsync(PmjHost host, bool json, CancellationToken cancellationToken)
        {
            var opened = ProjectSession.OpenToFetch(host);
            if (!opened.Succeeded)
            {
                return DiagnosticPrinter.Report(host, opened.Diagnostics, opened.Omitted, json);
            }

            var project = opened.Value;
            if (!project.IsLocked)
            {
                return DiagnosticPrinter.Report(host, [CommandDiagnostics.NotInstalled(missing: project.Lockfile is null)], json: json);
            }

            var lockfile = project.Lockfile;
            var found = new List<Diagnostic>();
            var omitted = 0;
            try
            {
                // Asked of a store that holds nothing, so that every package is downloaded again and nothing is kept.
                var releases = new DirectPackageSource(host.Releases, new NoStore(), project.Manifest, lockfile);
                var check = await LockCheck.CheckAsync(lockfile, releases, cancellationToken);
                found.AddRange(check.Diagnostics);
                omitted = check.OmittedDiagnostics;
            }
            catch (PackageSourceException failure)
            {
                // What could not be asked of GitHub does not stop the cache being held to the lockfile.
                found.Add(failure.Diagnostic);
            }

            foreach (var package in lockfile.Packages.OrderBy(package => package.Name).ThenBy(package => package.Version))
            {
                found.AddRange(project.Store.Examine(package.Name, package.Version, package.Sha256));
            }

            var problems = found.OrderBy(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToList();
            var sound = problems.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
            if (json)
            {
                host.Out.Write(VerifyReport.Json(lockfile, problems, omitted));
            }
            else
            {
                DiagnosticPrinter.Print(host.Error, problems, omitted);
                if (sound)
                {
                    host.Out.WriteLine(lockfile.Packages.Count == 0
                        ? "Nothing to verify: the project depends on no package."
                        : $"Verified {ProjectSession.Count(lockfile.Packages.Count)}: each is what {LockfileReader.FileName} holds, in its release and in the cache.");
                }
            }

            return sound ? ExitStatus.Success : ExitStatus.Problem;
        }
    }
}
