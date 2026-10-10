using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>What a package source knows of one version of one package.</summary>
    /// <param name="Source">The repository whose release holds it.</param>
    /// <param name="Sha256">The digest of its archive, as recorded when the version was first seen.</param>
    /// <param name="Verified">How far the source could verify it.</param>
    /// <param name="Dependencies">
    /// Its manifest's <c>dependencies</c> table. What it needs only to be developed is no part of
    /// this, so a resolution never follows it.
    /// </param>
    public sealed record PublishedVersion(
        PackageName Name,
        SemanticVersion Version,
        VersionStatus Status,
        RepositoryRef Source,
        Sha256Digest Sha256,
        VerificationLevel Verified,
        IReadOnlyList<Dependency> Dependencies);
}
