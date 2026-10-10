using Packmoji.Core.Manifests;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// What the manifest asked for when the lockfile was written. Comparing it with the manifest of
    /// today says exactly, and without the network, whether the lockfile still answers the manifest.
    /// </summary>
    public sealed record RootRequirements(IReadOnlyList<Dependency> Dependencies, IReadOnlyList<Dependency> DevDependencies)
    {
        /// <summary>What a manifest asks for today. A table the manifest does not have is empty.</summary>
        public static RootRequirements From(Manifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return new RootRequirements(manifest.Dependencies ?? [], manifest.DevDependencies ?? []);
        }

        /// <summary>
        /// Whether both ask for the same: the same packages with equal requirements in each of the two
        /// tables. Neither the order of a table nor the spelling of a requirement matters, so a manifest
        /// that was only rearranged still matches its lockfile. A record's own equality cannot say
        /// this, because it compares two lists by which list they are and not by what they hold.
        /// </summary>
        public bool Matches(RootRequirements other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Same(Dependencies, other.Dependencies) && Same(DevDependencies, other.DevDependencies);
        }

        private static bool Same(IReadOnlyList<Dependency> one, IReadOnlyList<Dependency> other) =>
            one.Count == other.Count && InOrder(one).SequenceEqual(InOrder(other));

        // Sorted by exactly what makes two requirements equal, so equal tables come out in one order.
        private static IEnumerable<Dependency> InOrder(IReadOnlyList<Dependency> requirements) =>
            requirements.OrderBy(requirement => requirement.Name).ThenBy(requirement => requirement.Requirement.Minimum);
    }
}
