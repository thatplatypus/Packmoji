using Packmoji.Cli.Projects;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// Where a command finds packages: among GitHub's releases, or in a registry. A command asks the
    /// same things of either, and what differs between the two is behind this. One is made for a
    /// command and used for all of it, because it remembers what it has been told.
    /// </summary>
    internal interface IPackageFinder
    {
        /// <summary>The versions of a package that something may begin to use, or the problem of the package not being found.</summary>
        Task<Outcome<ListedVersions>> ListAsync(PackageName name, CancellationToken cancellationToken);

        /// <summary>Chooses the versions that answer a manifest, against the project's lockfile if it has one.</summary>
        Task<ResolveResult> ResolveAsync(Manifest manifest, CancellationToken cancellationToken);

        /// <summary>Holds what the project's lockfile holds to what is published, and chooses nothing. Only for a project that has a lockfile.</summary>
        Task<ResolveResult> CheckAsync(CancellationToken cancellationToken);

        /// <summary>Sees to it that the archive of a version this gave is in the cache.</summary>
        Task FetchAsync(PublishedVersion published, CancellationToken cancellationToken);
    }
}
