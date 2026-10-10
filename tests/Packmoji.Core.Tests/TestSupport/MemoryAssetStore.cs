using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>The cache, held in memory.</summary>
    internal sealed class MemoryAssetStore : IAssetStore
    {
        private readonly Dictionary<string, ReadOnlyMemory<byte>> _kept = [];

        /// <summary>The digests of what is kept.</summary>
        public IReadOnlyCollection<string> Kept => _kept.Keys;

        public ValueTask<ReadOnlyMemory<byte>?> FindAsync(PackageName name, SemanticVersion version, Sha256Digest digest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_kept.TryGetValue(digest.Hex, out var archive) ? archive : (ReadOnlyMemory<byte>?)null);

        public ValueTask KeepAsync(PackageName name, SemanticVersion version, Sha256Digest digest, ReadOnlyMemory<byte> archive, CancellationToken cancellationToken)
        {
            _kept[digest.Hex] = archive;
            return ValueTask.CompletedTask;
        }
    }
}
