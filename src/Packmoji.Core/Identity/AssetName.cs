using Packmoji.Core.Versioning;

namespace Packmoji.Core.Identity
{
    /// <summary>
    /// The one file a release carries for Packmoji: the package's source archive. GitHub's own source
    /// archives are not promised to stay the same bytes, so a checksum could not be held against them.
    /// </summary>
    public static class AssetName
    {
        public const string Suffix = ".pmj.tar.gz";

        public static string For(PackageName package, SemanticVersion version)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(version);
            return $"{package.Name}-{version}{Suffix}";
        }
    }
}
