using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// Repositories and their releases, held in memory: what GitHub is to the code that finds packages
    /// without a registry. It keeps what it was asked, so that a test can say where pmj looked.
    /// </summary>
    internal sealed class FakeReleaseHost : IReleaseHost
    {
        private readonly Dictionary<(string Repository, string Tag), (bool Prerelease, Dictionary<string, byte[]> Assets)> _releases = [];

        /// <summary>Every download asked for, as <c>github.com/owner/repo tag</c>, in the order of the asking.</summary>
        public List<string> Downloads { get; } = [];

        /// <summary>Every repository whose releases were listed.</summary>
        public List<string> Listings { get; } = [];

        /// <summary>When set, every request fails as it does when GitHub cannot be reached.</summary>
        public bool Down { get; set; }

        /// <summary>
        /// Releases one version of a package from a repository, as <c>pmj pack</c> and a release would:
        /// the tag, and the one asset, which holds a manifest and a source file. The manifest says where
        /// the package lives when that is not a repository of its own name. Each dependency is written
        /// as <c>@owner/name@1.2</c>.
        /// </summary>
        public FakeReleaseHost Release(string repository, string name, string version, params string[] dependencies) =>
            ReleaseWith(repository, name, version, Sample.ManifestJson(name, version, repository, dependencies));

        /// <summary>The same, with a manifest that the test wrote. It has a name of its own so that one dependency is never taken for a manifest.</summary>
        public FakeReleaseHost ReleaseWith(string repository, string name, string version, string manifest)
        {
            var package = Sample.Name(name);
            var archive = PackageArchive.Write([Sample.File("packmoji.json", manifest), Sample.File("src/lib.🍇", $"💭 {name} {version}\n")]);
            return Upload(repository, ReleaseTag.For(package, Sample.Version(version)), AssetName.For(package, Sample.Version(version)), archive, Sample.Version(version).IsPrerelease);
        }

        /// <summary>A release with one asset of any name and any bytes.</summary>
        public FakeReleaseHost Upload(string repository, string tag, string asset, byte[] bytes, bool prerelease = false)
        {
            if (!_releases.TryGetValue((repository, tag), out var release))
            {
                release = (prerelease, []);
                _releases.Add((repository, tag), release);
            }

            release.Assets[asset] = bytes;
            return this;
        }

        public byte[] Archive(string repository, string name, string version) =>
            _releases[(repository, ReleaseTag.For(Sample.Name(name), Sample.Version(version)))].Assets[AssetName.For(Sample.Name(name), Sample.Version(version))];

        public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDown();
            Downloads.Add($"{repository} {tag}");
            // Said in two steps: an array that is null turns into memory that is empty, and not into no memory.
            if (_releases.TryGetValue((repository.ToString(), tag), out var release) && release.Assets.TryGetValue(asset, out var bytes))
            {
                return Task.FromResult<ReadOnlyMemory<byte>?>(bytes);
            }

            return Task.FromResult<ReadOnlyMemory<byte>?>(null);
        }

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDown();
            Listings.Add(repository.ToString());
            IReadOnlyList<ReleaseInfo> releases = _releases
                .Where(release => release.Key.Repository == repository.ToString())
                .Select(release => new ReleaseInfo(release.Key.Tag, release.Value.Prerelease, release.Value.Assets.Keys.ToList()))
                .ToList();
            return Task.FromResult(releases);
        }

        private void ThrowIfDown()
        {
            if (Down)
            {
                throw new PackageSourceException(new Diagnostic(DiagnosticCodes.GitHubUnreachable, "GitHub could not be reached.", "the test said so", "try again"));
            }
        }
    }
}
