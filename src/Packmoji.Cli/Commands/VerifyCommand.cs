using Packmoji.Cli.Cache;
using Packmoji.Cli.Output;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Registry;
using Packmoji.Core.Reports;
using Packmoji.Core.Resolution;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj verify</c>: downloads every locked package again, and holds what its release carries and
    /// what the cache holds of it to the lockfile. It is a check and changes nothing, not even the
    /// cache: what it finds is for a person to act on.
    /// </summary>
    /// <remarks>
    /// With a registry named, each locked package is held to what the registry records of it as
    /// well. An install of what the cache holds asks nobody anything, so this is where a version
    /// that was withdrawn or quarantined after it was locked is learned of.
    /// </remarks>
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
            var held = 0;

            // Asked of a store that holds nothing, so that every package is downloaded again and nothing is kept.
            var releases = new DirectPackageSource(project.Releases, new NoStore(), project.Manifest, lockfile);
            var reachable = true;

            // Asked of the registry itself, and never of the cache in its place.
            var registry = project.Registry is null ? null : new RegistryPackageSource(project.Registry);
            var readable = true;
            foreach (var package in lockfile.Packages.OrderBy(package => package.Name).ThenBy(package => package.Version))
            {
                if (registry is not null && readable)
                {
                    try
                    {
                        var check = await LockCheck.CheckAsync(lockfile with { Packages = [package] }, registry, cancellationToken);
                        found.AddRange(check.Diagnostics);
                        omitted += check.OmittedDiagnostics;
                    }
                    catch (PackageSourceException failure)
                    {
                        found.Add(failure.Diagnostic);

                        // A registry that cannot be read for one package cannot be read for the next. A
                        // file of it that does not read is that file's fault alone, and the rest are still asked for.
                        readable = failure.Diagnostic.Code is not DiagnosticCodes.RegistryUnreachable;
                    }
                }

                if (reachable)
                {
                    try
                    {
                        // Each on its own, so that what is wrong with one release does not keep the next from being looked at.
                        var check = await LockCheck.CheckAsync(lockfile with { Packages = [package] }, releases, cancellationToken);
                        found.AddRange(check.Diagnostics);
                        omitted += check.OmittedDiagnostics;
                    }
                    catch (PackageSourceException failure)
                    {
                        found.Add(failure.Diagnostic);

                        // What GitHub could not be asked of one package it cannot be asked of the next, and saying so once is enough.
                        reachable = failure.Diagnostic.Code is not (DiagnosticCodes.GitHubUnreachable or DiagnosticCodes.GitHubRateLimited or DiagnosticCodes.ConfigInvalid);
                    }
                }

                // Whatever GitHub said or could not say, the cache is held to the lockfile.
                var cached = project.Store.Examine(package.Name, package.Version, package.Sha256);
                found.AddRange(cached.Problems);
                held += cached.Held ? 1 : 0;
            }

            // The registry's record and the release itself can say the same thing against the lockfile, in the same words. It is one problem.
            var problems = found.Distinct().OrderBy(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToList();
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
                    host.Out.Write(Said(lockfile.Packages.Count, held, registry is not null));
                }
            }

            return sound ? ExitStatus.Success : ExitStatus.Problem;
        }

        // What is said when nothing is wrong: what was held to the lockfile, and how much of it the cache had to hold.
        private static string Said(int packages, int held, bool withRegistry)
        {
            if (packages == 0)
            {
                return "Nothing to verify: the project depends on no package." + Environment.NewLine;
            }

            var one = packages == 1;
            var releases = (one, withRegistry) switch
            {
                (true, false) => $"Verified 1 package: its release is what {LockfileReader.FileName} holds.",
                (false, false) => $"Verified {packages} packages: the release of each is what {LockfileReader.FileName} holds.",
                (true, true) => $"Verified 1 package: its release, and what the registry records of it, are what {LockfileReader.FileName} holds.",
                (false, true) => $"Verified {packages} packages: the release of each, and what the registry records of it, are what {LockfileReader.FileName} holds.",
            };
            var cache = held switch
            {
                0 => one ? "The cache holds nothing of it." : "The cache holds none of them.",
                _ when held == packages => one ? "The cache's copy of it is what was locked too." : "The cache's copy of each is what was locked too.",
                _ => $"The cache holds {held} of them, and each of those is what was locked too.",
            };
            return releases + Environment.NewLine + cache + Environment.NewLine;
        }
    }
}
