using Packmoji.Core.Lockfiles;

namespace Packmoji.Cli.Building
{
    /// <summary>A built package that a project is to have in its own directory.</summary>
    /// <param name="Name">The name it is imported by, which is the name of its folder.</param>
    /// <param name="Key">The key it was built under.</param>
    /// <param name="From">The folder that holds it among the built packages pmj keeps.</param>
    internal sealed record PlacedPackage(string Name, Sha256Digest Key, string From);
}
