using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// Everything a build asks of a compiler and of the tools around it. What the compiler is called,
    /// which flags it takes, what it names its files and how it says it has failed are all a
    /// driver's own, so that another compiler, or a fork that does more for itself, is one more
    /// driver and nothing else changes. Decision record 0001 is what the first driver does.
    /// </summary>
    public interface ICompilerDriver
    {
        /// <summary>Finds the compiler and says which it is. It is asked first, and once.</summary>
        ValueTask<BuildStep<CompilerIdentity>> IdentifyAsync(CancellationToken cancellationToken);

        /// <summary>
        /// What stands in the way of a build that has these things to do, found out before anything
        /// is compiled, so that a build that could not end does not begin.
        /// </summary>
        /// <param name="archiving">Whether a package is to be built, which ends with its archive.</param>
        IReadOnlyList<Diagnostic> Lacks(bool archiving);

        /// <summary>Compiles a package's Emojicode code. Gives the object, and leaves what others need of the package in its folder.</summary>
        ValueTask<BuildStep<string>> CompilePackageAsync(PackageCompile compile, CancellationToken cancellationToken);

        /// <summary>Makes a package's archive in its folder, from its objects. Gives the archive.</summary>
        ValueTask<BuildStep<string>> ArchiveAsync(ArchiveRequest archive, CancellationToken cancellationToken);
    }
}
