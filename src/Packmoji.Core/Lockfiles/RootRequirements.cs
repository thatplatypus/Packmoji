using Packmoji.Core.Manifests;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// What the manifest asked for when the lockfile was written. Comparing it with the manifest of
    /// today says exactly, and without the network, whether the lockfile still answers the manifest.
    /// </summary>
    public sealed record RootRequirements(IReadOnlyList<Dependency> Dependencies, IReadOnlyList<Dependency> DevDependencies);
}
