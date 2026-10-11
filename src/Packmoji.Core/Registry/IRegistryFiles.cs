using Packmoji.Core.Direct;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Where a registry's files are read from: an address, to everything but a test. A registry is
    /// files and nothing else, so reading one is asking for a file by its path, and that is all this
    /// is asked.
    /// </summary>
    /// <remarks>
    /// Not being able to find out is a <see cref="PackageSourceException"/>, and never an answer: a
    /// file that could not be asked for must not be taken for one that is not there.
    /// </remarks>
    public interface IRegistryFiles
    {
        /// <summary>Where the registry is, for a person to be told.</summary>
        string Address { get; }

        /// <summary>
        /// The bytes of the file at a path, or null when the registry has no such file. A file of
        /// more than <paramref name="maxBytes"/> is not read, and is a <see cref="PackageSourceException"/>.
        /// </summary>
        Task<ReadOnlyMemory<byte>?> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken);
    }
}
