using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// A resolution with no registry behind it. The resolver itself is unchanged: this only sees to
    /// it that the order packages are asked for in does not decide whether they are found.
    /// </summary>
    public static class DirectResolver
    {
        public static async Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, DirectPackageSource source, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(source);
            while (true)
            {
                var known = source.KnownRepositories;
                var result = await Resolver.ResolveAsync(manifest, existing, source, cancellationToken);
                if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCodes.ResolveVersionMissing))
                {
                    return result;
                }

                // A package can be asked for before the package that shows where its owner keeps
                // things. If a repository was learned of since this resolution began, what was not
                // found may be there, so it is all asked again: the source has kept what it found,
                // and looks only where it has not looked. This ends, since repositories are learned
                // of only by finding packages in them.
                if (source.KnownRepositories == known)
                {
                    return result.WithErrors(source.Unplaced()
                        .Select(unplaced => DirectPackageSource.NotFound(unplaced.Name, unplaced.Version, source.LookedIn(unplaced.Name, unplaced.Version)))
                        .ToList());
                }
            }
        }
    }
}
