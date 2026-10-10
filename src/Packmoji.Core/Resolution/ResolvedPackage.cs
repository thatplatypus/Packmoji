using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Resolution
{
    /// <summary>One package of a build, at the version selected for it.</summary>
    /// <param name="Published">The selected version, as the package source gave it.</param>
    /// <param name="Dependencies">Each package the selected version depends on, with the version selected for that package, in order of name.</param>
    public sealed record ResolvedPackage(PublishedVersion Published, IReadOnlyList<LockedDependency> Dependencies);
}
