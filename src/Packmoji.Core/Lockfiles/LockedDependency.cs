using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>What a locked package depends on: another locked package, at exactly the version the lockfile holds.</summary>
    public sealed record LockedDependency(PackageName Name, SemanticVersion Version);
}
