using Packmoji.Core.Diagnostics;

namespace Packmoji.Cli.Cache
{
    /// <summary>What a look at the cache's copy of one package found.</summary>
    /// <param name="Held">Whether the cache holds anything of the package at all: its archive, or files unpacked from it.</param>
    /// <param name="Problems">Every way in which what it holds is not what it should be.</param>
    internal sealed record CacheFinding(bool Held, IReadOnlyList<Diagnostic> Problems);
}
