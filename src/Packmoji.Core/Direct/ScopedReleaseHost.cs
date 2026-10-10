using Packmoji.Core.Identity;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// A release host that asks nothing of an owner outside a limit on scopes, whatever asked. A
    /// package's scope is the owner its repository belongs to, and everything above this refuses a
    /// package of another scope before it comes here. This is the one place a request cannot go
    /// round, and so the line that keeps the promise if a line above it is ever wrong.
    /// </summary>
    public sealed class ScopedReleaseHost(IReleaseHost inner, ScopeLimit allowed) : IReleaseHost
    {
        public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken)
        {
            Hold(repository);
            return inner.DownloadAsync(repository, tag, asset, maxBytes, cancellationToken);
        }

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken)
        {
            Hold(repository);
            return inner.ListAsync(repository, cancellationToken);
        }

        private void Hold(RepositoryRef repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            if (!allowed.AllowsOwner(repository.Owner))
            {
                throw new PackageSourceException(allowed.RefusesOwner(repository));
            }
        }
    }
}
