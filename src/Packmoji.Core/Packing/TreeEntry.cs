namespace Packmoji.Core.Packing
{
    /// <summary>One file found in a package's directory.</summary>
    /// <param name="Path">Where it is inside the package, with <c>/</c> between its parts, as the disk spells it.</param>
    /// <param name="Size">Its length in bytes.</param>
    /// <param name="IsLink">Whether it is a symbolic link and not a file.</param>
    public sealed record TreeEntry(string Path, long Size, bool IsLink);
}
