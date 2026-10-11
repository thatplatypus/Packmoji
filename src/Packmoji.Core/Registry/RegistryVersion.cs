using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// What a registry recorded of one version when it first saw it. None of it changes afterwards
    /// but the status: a version that is published is never written over, so that a digest in a
    /// lockfile means the same thing for ever.
    /// </summary>
    /// <param name="Source">The repository whose release holds it.</param>
    /// <param name="Sha256">The digest of the release's file as it was when the version was first seen.</param>
    /// <param name="Size">How many bytes that file is.</param>
    /// <param name="IsImmutable">Whether the host said the release could not be changed, when the version was first seen.</param>
    /// <param name="Published">When the host says the release was published.</param>
    /// <param name="Indexed">When the version was first seen.</param>
    /// <param name="Dependencies">Its manifest's <c>dependencies</c>.</param>
    /// <param name="Quarantined">What was seen of its bytes when they were found changed. There exactly when the status is quarantined.</param>
    public sealed record RegistryVersion(
        SemanticVersion Version,
        VersionStatus Status,
        RepositoryRef Source,
        Sha256Digest Sha256,
        int Size,
        VerificationLevel Verified,
        bool IsImmutable,
        DateTimeOffset Published,
        DateTimeOffset Indexed,
        IReadOnlyList<Dependency> Dependencies,
        RegistryQuarantine? Quarantined = null)
    {
        /// <summary>
        /// Whether another record says the same of this version in everything that may never change:
        /// all of it but the status, and the quarantine that goes with a status.
        /// </summary>
        public bool SameButForStatus(RegistryVersion other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Version == other.Version
                && Source == other.Source
                && Sha256 == other.Sha256
                && Size == other.Size
                && Verified == other.Verified
                && IsImmutable == other.IsImmutable
                && Published == other.Published
                && Indexed == other.Indexed
                && Needs(this).SequenceEqual(Needs(other), StringComparer.Ordinal);
        }

        // What a version needs, as text in one order: a list is the same list whatever order it was made in.
        private static IEnumerable<string> Needs(RegistryVersion version) =>
            version.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal);
    }
}
