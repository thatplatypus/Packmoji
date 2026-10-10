namespace Packmoji.Core.Building
{
    /// <summary>What a driver is asked when a package's objects are to be made into the one file a program is linked with.</summary>
    /// <param name="What">The package as a problem names it.</param>
    /// <param name="Name">The name the package is imported by.</param>
    /// <param name="Objects">The object of its Emojicode code, and one for each of its native files.</param>
    /// <param name="OutputDirectory">The package's folder, where its interface is.</param>
    public sealed record ArchiveRequest(string What, string Name, IReadOnlyList<string> Objects, string OutputDirectory);
}
