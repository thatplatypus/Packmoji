using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Building
{
    /// <summary>What a build made of the project itself.</summary>
    /// <param name="Output">For an application, the program. For a library, the folder that holds its interface and its archive.</param>
    public sealed record BuiltProject(PackageName Name, SemanticVersion Version, PackageKind Kind, string Output);
}
