namespace Packmoji.Core.Building
{
    /// <summary>A built package that a program is linked with.</summary>
    /// <param name="Name">The name it is imported by.</param>
    /// <param name="Directory">Its folder, which holds its archive.</param>
    public sealed record LinkedPackage(string Name, string Directory);
}
