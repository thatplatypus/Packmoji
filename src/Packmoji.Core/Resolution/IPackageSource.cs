using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// What the resolver asks about published packages. It asks only for exact versions, one at a
    /// time, and never for a list of them: minimal version selection needs nothing more, and that is
    /// what lets a repository's releases answer as readily as a registry.
    /// </summary>
    public interface IPackageSource
    {
        /// <summary>
        /// One version of one package, or null when that version was never published. Not being able
        /// to find out, as when the network fails, is an exception and never null: a version that
        /// could not be looked for must not be taken for one that does not exist.
        /// </summary>
        ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken);
    }
}
