using Packmoji.Core.Registry;

namespace Packmoji.Indexer
{
    /// <summary>What reading a registry from a directory gave: the registry, or everything that stopped it being one. Never both.</summary>
    /// <param name="Problems">Each problem as a line that is fit to print, in order of the path it is about.</param>
    /// <param name="Bytes">The bytes of each file that was read, by its path. A reader passes over what it does not know, so what a file holds is not always all that is in it.</param>
    internal sealed record RegistryRead(RegistryState? State, IReadOnlyList<string> Problems, IReadOnlyDictionary<string, byte[]> Bytes);
}
