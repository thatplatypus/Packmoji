namespace Packmoji.Cli.Building
{
    /// <summary>A package on its way into what pmj keeps of built packages: a directory beside its place, which becomes the entry in one step.</summary>
    /// <param name="Root">The directory that becomes the entry.</param>
    /// <param name="PackageDirectory">The folder in it that is named for the package, which holds what others need of the package.</param>
    /// <param name="WorkDirectory">Where what is made on the way goes. It is gone before the entry is kept.</param>
    internal sealed record StagedBuild(string Root, string PackageDirectory, string WorkDirectory);
}
