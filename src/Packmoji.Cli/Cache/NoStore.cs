using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Cache
{
    /// <summary>
    /// A store that holds nothing and keeps nothing. It is what a check is given, so that what the
    /// check downloads is looked at and let go, and the cache is left exactly as it was found.
    /// </summary>
    internal sealed class NoStore : IAssetStore
    {
        public ValueTask<ReadOnlyMemory<byte>?> FindAsync(PackageName name, SemanticVersion version, Sha256Digest digest, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

        public ValueTask KeepAsync(PackageName name, SemanticVersion version, Sha256Digest digest, ReadOnlyMemory<byte> archive, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
