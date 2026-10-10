namespace Packmoji.Cli.Building
{
    /// <summary>What a build was asked for.</summary>
    /// <param name="Release">Whether the compiler is to optimize, the packages as well as the project.</param>
    /// <param name="DependenciesOnly">Whether to stop when the packages are built and in their place, before the project.</param>
    internal sealed record BuildRequest(bool Release, bool DependenciesOnly);
}
