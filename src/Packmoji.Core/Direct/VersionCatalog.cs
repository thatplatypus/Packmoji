using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// Which versions of a package a repository has released, and which of them is the latest. This
    /// is what <c>pmj add</c> and <c>pmj update</c> need and a resolution never does: a resolution
    /// asks for exact versions only.
    /// </summary>
    public static class VersionCatalog
    {
        /// <summary>
        /// The versions of a package among a repository's releases, lowest first: each release whose
        /// tag is the package's and which carries the package's archive.
        /// </summary>
        public static IReadOnlyList<SemanticVersion> Of(PackageName name, IReadOnlyList<ReleaseInfo> releases)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(releases);
            var versions = new List<SemanticVersion>();
            foreach (var release in releases)
            {
                if (ReleaseTag.TryParse(release.Tag, out var tagged, out var version)
                    && tagged == name.Name
                    && release.Assets.Contains(AssetName.For(name, version), StringComparer.Ordinal))
                {
                    versions.Add(version);
                }
            }

            return versions.Distinct().Order().ToList();
        }

        /// <summary>The highest version that is not a pre-release, or null when there is none. A pre-release is used only by naming it.</summary>
        public static SemanticVersion? Latest(IReadOnlyList<SemanticVersion> versions)
        {
            ArgumentNullException.ThrowIfNull(versions);
            return versions.Where(version => !version.IsPrerelease).Max();
        }

        /// <summary>
        /// The highest version that is not a pre-release, on the requirement's own line and above its
        /// minimum. Null when the requirement already asks for the latest there is. Moving to another
        /// line is a decision, and this never makes it.
        /// </summary>
        public static SemanticVersion? LatestOnLine(IReadOnlyList<SemanticVersion> versions, VersionRequirement current)
        {
            ArgumentNullException.ThrowIfNull(versions);
            ArgumentNullException.ThrowIfNull(current);
            return versions.Where(version => !version.IsPrerelease && current.Line.Contains(version) && version > current.Minimum).Max();
        }
    }
}
