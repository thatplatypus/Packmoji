using Packmoji.Core.Registry;

namespace Packmoji.Indexer
{
    /// <summary>What reading a registry from a directory gave: the registry, or everything that stopped it being one. Never both.</summary>
    /// <param name="Problems">Each problem as a line that is fit to print, in order of the path it is about.</param>
    internal sealed record RegistryRead(RegistryState? State, IReadOnlyList<string> Problems);
}
