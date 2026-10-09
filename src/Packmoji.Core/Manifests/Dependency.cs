using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Manifests
{
    /// <summary>One line of a dependency table: a package, and the minimum version of it that is asked for.</summary>
    public sealed record Dependency(PackageName Name, VersionRequirement Requirement);
}
