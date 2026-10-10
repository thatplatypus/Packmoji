using Packmoji.Core.Manifests;

namespace Packmoji.Core.Archives
{
    /// <summary>One file of a package: where it is inside the package, and what it holds.</summary>
    public sealed record ArchiveFile(RelativePath Path, ReadOnlyMemory<byte> Content);
}
