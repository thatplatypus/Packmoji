using Packmoji.Cli.Projects;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;

namespace Packmoji.Cli.Commands
{
    /// <summary>Finds packages among GitHub's releases, with no registry: see <see cref="DirectPackageSource"/>.</summary>
    internal sealed class DirectFinder(DirectPackageSource source, Lockfile? lockfile, ScopeLimit allowed) : IPackageFinder
    {
        public async Task<Outcome<ListedVersions>> ListAsync(PackageName name, CancellationToken cancellationToken) =>
            await source.ListVersionsAsync(name, cancellationToken) is { } listed
                ? Outcome<ListedVersions>.Of(new ListedVersions(listed.Versions, $"{listed.Repository} has released"))
                : Outcome<ListedVersions>.Failed(DirectPackageSource.NotFound(name, null, source.LookedIn(name)));

        public Task<ResolveResult> ResolveAsync(Manifest manifest, CancellationToken cancellationToken) =>
            DirectResolver.ResolveAsync(manifest, lockfile, source, allowed, cancellationToken);

        public Task<ResolveResult> CheckAsync(CancellationToken cancellationToken) => LockCheck.CheckAsync(lockfile!, source, cancellationToken);

        // To say what a version needs, the source downloaded its archive, and it keeps what it
        // downloads. So every version it gave is in the cache already.
        public Task FetchAsync(PublishedVersion published, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
