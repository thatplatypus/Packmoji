namespace Packmoji.Core.Building
{
    /// <summary>What a driver is asked when the code of an application is to be compiled.</summary>
    /// <param name="What">The application as a problem names it.</param>
    /// <param name="Name">Its bare name, which its object is named for.</param>
    /// <param name="Entry">Its main file.</param>
    /// <param name="SearchPaths">Directories that each hold a folder for a package it needs.</param>
    /// <param name="WorkDirectory">Where the object goes. It holds no <c>packages</c> directory.</param>
    public sealed record ProgramCompile(
        string What,
        string Name,
        string Entry,
        IReadOnlyList<string> SearchPaths,
        string WorkDirectory,
        bool Optimized);
}
