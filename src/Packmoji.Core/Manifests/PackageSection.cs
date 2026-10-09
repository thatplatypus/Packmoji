using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Manifests
{
    /// <param name="Emojicode">The oldest compiler the package builds with.</param>
    /// <param name="Description">One line saying what the package is. Null when the manifest has none.</param>
    /// <param name="License">Null when the manifest has none.</param>
    /// <param name="Repository">Null when the manifest does not say. <c>Manifest.Repository</c> then gives the default.</param>
    public sealed record PackageSection(
        PackageName Name,
        SemanticVersion Version,
        PackageKind Kind,
        CompilerRequirement Emojicode,
        string? Description = null,
        SpdxExpression? License = null,
        RepositoryRef? Repository = null);
}
