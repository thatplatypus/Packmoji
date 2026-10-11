using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// A package source that can say more truly than the resolver why a version is not there. A
    /// registry knows every version there is, so of one it does not have the resolver can say that
    /// it was never published. A source that has to go and look can only say where it looked.
    /// </summary>
    internal interface IExplainsMissing
    {
        /// <summary>What to report of a version this source did not give, or null to let the resolver say that it was never published.</summary>
        /// <param name="chain">How the resolution came to ask for the version, as the resolver writes a chain.</param>
        Diagnostic? Missing(PackageName name, SemanticVersion version, string chain);

        /// <summary>
        /// What to report of a version that a lockfile holds and this source did not give, or null
        /// to let it be said that the version is no longer published. That is true of a source
        /// which found the version once where the lockfile says. It is not true of one that the
        /// lockfile was written without.
        /// </summary>
        /// <param name="chain">How a resolution came to ask for the version, or null when the lockfile alone is why it is asked about.</param>
        Diagnostic? MissingThoughLocked(PackageName name, SemanticVersion version, string? chain);
    }
}
