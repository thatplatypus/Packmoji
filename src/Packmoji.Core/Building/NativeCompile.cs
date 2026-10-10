namespace Packmoji.Core.Building
{
    /// <summary>What a driver is asked when one of a package's native files is to be compiled.</summary>
    /// <param name="What">The package as a problem names it.</param>
    /// <param name="Source">The file to compile.</param>
    /// <param name="IncludeDirectories">The package's own directories of headers. The compiler's headers are the driver's to add.</param>
    /// <param name="WorkDirectory">Where the object goes.</param>
    /// <param name="Number">Which of the package's native files this is, counted from 0, so that two files of one name have an object each.</param>
    public sealed record NativeCompile(
        string What,
        NativeLanguage Language,
        string Source,
        IReadOnlyList<string> IncludeDirectories,
        string WorkDirectory,
        int Number);
}
