namespace Packmoji.Core.Building
{
    /// <summary>What a driver is asked when a package is to be compiled.</summary>
    /// <param name="What">The package as a problem names it: <c>"@owner/name" 1.0.0</c>.</param>
    /// <param name="Name">The name it is imported by.</param>
    /// <param name="Entry">Its main file.</param>
    /// <param name="SearchPaths">Directories that each hold a folder for a package it needs. Every package it depends on, directly or through another, has to be found in one of them.</param>
    /// <param name="OutputDirectory">Where what others need of it goes: the folder that a search path finds under its name.</param>
    /// <param name="WorkDirectory">Where what is made on the way goes. It holds no <c>packages</c> directory.</param>
    public sealed record PackageCompile(
        string What,
        string Name,
        string Entry,
        IReadOnlyList<string> SearchPaths,
        string OutputDirectory,
        string WorkDirectory,
        bool Optimized);
}
