using Packmoji.Core.Lockfiles;

namespace Packmoji.Cli.Building
{
    /// <summary>One locked package as a build left it.</summary>
    /// <param name="Directory">Its folder in the project, which holds its interface and its archive.</param>
    /// <param name="Link">The libraries its manifest says a program that uses it is linked with.</param>
    /// <param name="Compiled">Whether this build compiled it. False when it had been built before and was only put in its place.</param>
    internal sealed record BuiltPackage(LockedPackage Package, string Directory, IReadOnlyList<string> Link, bool Compiled);
}
