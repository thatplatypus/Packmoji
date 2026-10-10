using Packmoji.Core.Identity;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// Where releases are: GitHub, to everything but a test. It is asked two things and no more, so
    /// that finding a package without a registry can be worked out, and tested, with no network.
    /// </summary>
    /// <remarks>
    /// Not being able to find out is a <see cref="PackageSourceException"/>, and never an answer:
    /// a release that could not be looked for must not be taken for one that is not there.
    /// </remarks>
    public interface IReleaseHost
    {
        /// <summary>
        /// The bytes of one file of one release. Null when the repository, the release or the file is
        /// not there. A file of more than <paramref name="maxBytes"/> is not downloaded, and is a
        /// <see cref="PackageSourceException"/>.
        /// </summary>
        Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken);

        /// <summary>Every published release of a repository. None when the repository is not there.</summary>
        Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken);
    }
}
