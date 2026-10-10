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
    }
}
