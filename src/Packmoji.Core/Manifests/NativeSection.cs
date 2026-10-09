namespace Packmoji.Core.Manifests
{
    /// <param name="Sources">The C and C++ files compiled into the package's archive.</param>
    /// <param name="IncludeDirs">Directories of the package's own headers.</param>
    /// <param name="Link">Libraries a program that uses the package must be linked with, each named as <c>-l</c> would name it.</param>
    public sealed record NativeSection(
        IReadOnlyList<GlobPattern>? Sources = null,
        IReadOnlyList<RelativePath>? IncludeDirs = null,
        IReadOnlyList<string>? Link = null);
}
