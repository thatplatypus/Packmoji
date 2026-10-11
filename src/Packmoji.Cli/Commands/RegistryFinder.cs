using Packmoji.Cli.Projects;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// Finds packages in a registry. The registry says which versions there are, what each needs,
    /// where it lives and which bytes it is, so nothing is downloaded until a version has been
    /// chosen, and what is then downloaded from GitHub is held to what the registry said.
    /// </summary>
    internal sealed class RegistryFinder(IRegistryFiles files, IReleaseHost releases, IAssetStore store, Lockfile? lockfile, ScopeLimit allowed) : IPackageFinder
    {
        private readonly RegistryPackageSource _source = new(files);

        public async Task<Outcome<ListedVersions>> ListAsync(PackageName name, CancellationToken cancellationToken) =>
            await _source.ListVersionsAsync(name, cancellationToken) is { } versions
                ? Outcome<ListedVersions>.Of(new ListedVersions(versions, "the registry holds"))
                : Outcome<ListedVersions>.Failed(RegistryPackageSource.NotFound(name, files.Address));

        // Every version is asked of the registry, whatever the cache holds: a resolution is where a
        // version that has been withdrawn or quarantined since is learned of.
        public Task<ResolveResult> ResolveAsync(Manifest manifest, CancellationToken cancellationToken) =>
            Resolver.ResolveAsync(manifest, lockfile, _source, allowed, cancellationToken);

        // What the lockfile holds and the cache has is the version, whoever is or is not there to
        // ask, so an install of what is all in the cache asks nothing of anyone.
        public Task<ResolveResult> CheckAsync(CancellationToken cancellationToken) =>
            LockCheck.CheckAsync(lockfile!, new LockedFromCache(lockfile!, store, _source), cancellationToken);

        public Task FetchAsync(PublishedVersion published, CancellationToken cancellationToken) =>
            RegistryFetch.FetchAsync(published, releases, store, cancellationToken);
    }
}
