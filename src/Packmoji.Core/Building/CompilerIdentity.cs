using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Building
{
    /// <summary>Which compiler a build uses.</summary>
    /// <param name="Path">Where its file is.</param>
    /// <param name="Version">The version it gives of itself, which is what a package's requirement is held to.</param>
    /// <param name="Sha256">The digest of its file, which is what tells one compiler from another: two can give the same version.</param>
    public sealed record CompilerIdentity(string Path, SemanticVersion Version, Sha256Digest Sha256);
}
