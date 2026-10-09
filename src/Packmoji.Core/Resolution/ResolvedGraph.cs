using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Resolution
{
    /// <summary>The exact packages a build uses: one version of each, in order of full name.</summary>
    public sealed record ResolvedGraph(IReadOnlyList<ResolvedPackage> Packages)
    {
        /// <summary>The lockfile that records this graph, and what the manifest asked for that led to it.</summary>
        public Lockfile ToLockfile(Manifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return new Lockfile(
                RootRequirements.From(manifest),
                Packages
                    .Select(package => new LockedPackage(
                        package.Published.Name,
                        package.Published.Version,
                        package.Published.Source,
                        package.Published.Sha256,
                        package.Published.Verified,
                        package.Dependencies))
                    .ToList());
        }
    }
}
