using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// One package at the one version a build uses, and everything needed to fetch it and to know that
    /// what was fetched is what was locked.
    /// </summary>
    public sealed record LockedPackage(
        PackageName Name,
        SemanticVersion Version,
        RepositoryRef Source,
        Sha256Digest Sha256,
        VerificationLevel Verified,
        IReadOnlyList<LockedDependency> Dependencies)
    {
        /// <summary>
        /// The tag of the release. It follows from the name and the version, so it is worked out and
        /// never stored: a lockfile cannot then hold a tag that belongs to another package.
        /// </summary>
        public string ReleaseTag => Identity.ReleaseTag.For(Name, Version);

        public string Asset => AssetName.For(Name, Version);
    }
}
