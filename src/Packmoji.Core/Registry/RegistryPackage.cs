using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// What a registry holds of one package: every version of it that has been indexed, lowest first.
    /// It is one file of the registry, at the path the package's name gives.
    /// </summary>
    public sealed record RegistryPackage(PackageName Name, IReadOnlyList<RegistryVersion> Versions)
    {
        /// <summary>The version of the format that the directory <c>v1</c> holds.</summary>
        public const int FormatVersion = 1;

        /// <summary>What is held of one version, or null when that version has not been indexed.</summary>
        public RegistryVersion? Find(SemanticVersion version) => Versions.FirstOrDefault(held => held.Version == version);
    }
}
