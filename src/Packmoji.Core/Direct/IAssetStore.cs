using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// Where archives are kept once they have been downloaded: the cache. An archive is kept under
    /// its own digest, so what is asked for by a digest is those bytes or nothing, and two versions
    /// of the truth about one release can be kept side by side without either spoiling the other.
    /// </summary>
    public interface IAssetStore
    {
        /// <summary>The archive with this digest, or null when it is not kept.</summary>
        ValueTask<ReadOnlyMemory<byte>?> FindAsync(PackageName name, SemanticVersion version, Sha256Digest digest, CancellationToken cancellationToken);

        ValueTask KeepAsync(PackageName name, SemanticVersion version, Sha256Digest digest, ReadOnlyMemory<byte> archive, CancellationToken cancellationToken);
    }
}
