namespace Packmoji.Core.Building
{
    /// <summary>What a driver is asked when an application's objects are to be linked into a program.</summary>
    /// <param name="What">The application as a problem names it.</param>
    /// <param name="Objects">The object of its Emojicode code, and one for each of its native files.</param>
    /// <param name="Packages">Every package it is built with. Which of them it uses is the linker's to find out.</param>
    /// <param name="Libraries">The libraries that the application's manifest and its packages' manifests say to link with. The compiler's own are the driver's to add.</param>
    /// <param name="Program">The file to write.</param>
    public sealed record LinkRequest(
        string What,
        IReadOnlyList<string> Objects,
        IReadOnlyList<LinkedPackage> Packages,
        IReadOnlyList<string> Libraries,
        string Program);
}
