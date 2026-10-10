using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Building
{
    /// <summary>One locked package as a build left it in a project.</summary>
    /// <param name="Directory">Its folder in the project, which holds its interface, its archive and the compiler's report of it.</param>
    /// <param name="Link">The libraries its manifest says a program that uses it is linked with, in the manifest's order.</param>
    /// <param name="Compiled">Whether this build compiled it. False when it had been built before and was only put in its place.</param>
    public sealed record BuiltPackage(PackageName Name, SemanticVersion Version, string Directory, IReadOnlyList<string> Link, bool Compiled);
}
