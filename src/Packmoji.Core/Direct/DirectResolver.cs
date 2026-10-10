using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
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
        public static Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, DirectPackageSource source, CancellationToken cancellationToken) =>
            ResolveAsync(manifest, existing, source, ScopeLimit.None, cancellationToken);

        /// <param name="allowed">The scopes that may be depended on. A package of another is refused, and is never asked for.</param>
        public static async Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, DirectPackageSource source, ScopeLimit allowed, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allowed);
            while (true)
            {
                var known = source.KnownRepositories;
                var result = await Resolver.ResolveAsync(manifest, existing, source, allowed, cancellationToken);

                // A package can be asked for before the package that shows where its owner keeps
                // things. If something was not found, and a repository was learned of since this
                // resolution began, it may be there, so it is all asked again: the source has kept
                // what it found, and looks only where it has not looked. This ends, since
                // repositories are learned of only by finding packages in them.
                if (source.KnownRepositories == known || !result.Diagnostics.Any(diagnostic => diagnostic.Code is DiagnosticCodes.ResolveVersionMissing or DiagnosticCodes.PackageNotFound))
                {
                    return result;
                }
            }
        }
    }
}
