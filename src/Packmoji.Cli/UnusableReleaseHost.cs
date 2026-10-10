using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;

namespace Packmoji.Cli
{
    /// <summary>
    /// GitHub, to a pmj that was told something about it that it cannot use. Nothing is asked of
    /// anyone: whatever first needs GitHub is given the problem instead. A command that needs
    /// nothing of GitHub never meets it, and so is not stopped by it.
    /// </summary>
    internal sealed class UnusableReleaseHost(Diagnostic problem) : IReleaseHost
    {
        public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromException<ReadOnlyMemory<byte>?>(new PackageSourceException(problem));

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<ReleaseInfo>>(new PackageSourceException(problem));
    }
}
