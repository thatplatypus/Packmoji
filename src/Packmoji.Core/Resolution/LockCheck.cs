using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// Checks a lockfile against what is published, without choosing any version. A manifest that has
    /// not changed is not resolved again, and yet what it locked may since have been yanked or
    /// quarantined, or may no longer be what its release holds.
    /// </summary>
    public static class LockCheck
    {
        /// <returns>
        /// The lockfile's own packages as the graph, when nothing stops them being used. A yanked
        /// version is a warning and goes on being used, because the lockfile already holds it.
        /// </returns>
        public static async Task<ResolveResult> CheckAsync(Lockfile lockfile, IPackageSource source, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            ArgumentNullException.ThrowIfNull(source);

            var errors = new DiagnosticList();
            var warnings = new DiagnosticList();
            var packages = new List<ResolvedPackage>();
            foreach (var locked in lockfile.Packages.OrderBy(package => package.Name).ThenBy(package => package.Version))
            {
                if (await source.AskAsync(locked.Name, locked.Version, cancellationToken) is not { } published)
                {
                    errors.Add(() => ResolveDiagnostics.LockedVersionMissing(locked));
                    continue;
                }

                if (published.Status == VersionStatus.Quarantined)
                {
                    errors.Add(() => ResolveDiagnostics.LockedQuarantined(locked));
                }

                if (published.Sha256 != locked.Sha256 || published.Source != locked.Source)
                {
                    errors.Add(() => ResolveDiagnostics.LockMismatch(locked, published));
                }

                if (published.Status == VersionStatus.Yanked)
                {
                    warnings.Add(() => ResolveDiagnostics.YankedLocked(locked.Name, locked.Version));
                }

                // How far the lockfile verified a version is the lockfile's to say, so that the graph
                // of a check writes the same lockfile again.
                packages.Add(new ResolvedPackage(published with { Verified = locked.Verified }, locked.Dependencies));
            }

            return ResolveResult.From(errors, warnings, () => new ResolvedGraph(packages));
        }
    }
}
