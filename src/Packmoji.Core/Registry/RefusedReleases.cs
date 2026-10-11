using Packmoji.Core.Identity;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// The releases of one repository that claim to be a package and were refused, in order of tag.
    /// It is one file of the registry, so that an author can see why a release is not there, and so
    /// that a release that was refused is not downloaded again at every run.
    /// </summary>
    public sealed record RefusedReleases(RepositoryRef Repository, IReadOnlyList<RefusedRelease> Releases)
    {
        /// <summary>What was refused under a tag, or null when nothing was.</summary>
        public RefusedRelease? Find(string tag) => Releases.FirstOrDefault(release => string.Equals(release.Tag, tag, StringComparison.Ordinal));
    }
}
