using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Answers for what a lockfile holds from the cache, and asks something else only for what the
    /// cache does not have. A lockfile says which bytes a version is, so bytes of that digest are the
    /// version, whoever is or is not there to ask: an install whose locked bytes are all in the cache
    /// asks nothing of anyone, with a registry as without one.
    /// </summary>
    /// <remarks>
    /// What is answered from the cache is active, since nothing was asked that could say otherwise.
    /// So a version that has since been withdrawn or quarantined is learned of when the registry is
    /// asked: by a resolution, by a fetch of what the cache does not hold, and by <c>pmj verify</c>.
    /// </remarks>
    public sealed class LockedFromCache : IPackageSource
    {
        private readonly Dictionary<(PackageName Name, SemanticVersion Version), LockedPackage> _locked;
        private readonly IAssetStore _store;
        private readonly IPackageSource _otherwise;

        /// <param name="otherwise">What is asked about a version the lockfile does not hold, or whose locked bytes the cache does not have.</param>
        public LockedFromCache(Lockfile lockfile, IAssetStore store, IPackageSource otherwise)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(otherwise);
            _locked = lockfile.Packages.ToDictionary(package => (package.Name, package.Version));
            _store = store;
            _otherwise = otherwise;
        }

        public async ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            if (_locked.TryGetValue((name, version), out var locked)
                && await _store.FindAsync(name, version, locked.Sha256, cancellationToken) is { } kept
                && Sha256Digest.Of(kept.Span) == locked.Sha256)
            {
                return DirectPackageSource.Describe(name, version, locked.Source, kept);
            }

            return await _otherwise.FindAsync(name, version, cancellationToken);
        }
    }
}
