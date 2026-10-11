using System.Text;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;
using Shouldly;

namespace Packmoji.Indexer.Tests
{
    /// <summary>
    /// GitHub as the indexer sees it, held in memory: the releases of some repositories. It keeps what
    /// it was asked, so that a test can say that a command asked nothing.
    /// </summary>
    internal sealed class MadeUpGitHub : IReleaseHost
    {
        public const string Grapevine = "github.com/thatplatypus/grapevine";

        private readonly List<(string Repository, string Tag, string File, byte[] Bytes)> _releases = [];

        /// <summary>Everything that was asked, lists and files alike, in the order of the asking.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>When set, nothing can be listed, as when GitHub cannot be reached.</summary>
        public bool Unreachable { get; set; }

        /// <summary>Releases one version of a package as <c>pmj pack</c> and a release would. Each need is written as <c>@owner/name@1.2</c>.</summary>
        public MadeUpGitHub Package(string repository, string name, string version, params string[] needs)
        {
            PackageName.TryParse(name, out var package, out _).ShouldBeTrue();
            SemanticVersion.TryParse(version, out var parsed, out _).ShouldBeTrue();
            var table = needs.Length == 0
                ? ""
                : ", \"dependencies\": { " + string.Join(", ", needs.Select(need => $"\"{need[..need.LastIndexOf('@')]}\": \"{need[(need.LastIndexOf('@') + 1)..]}\"")) + " }";
            var manifest = $"{{ \"package\": {{ \"name\": \"{name}\", \"version\": \"{version}\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\", \"repository\": \"{repository}\" }}{table} }}";
            return Release(repository, ReleaseTag.For(package!, parsed!), AssetName.For(package!, parsed!), Archive(manifest));
        }

        /// <summary>A release with one file of any name and any bytes.</summary>
        public MadeUpGitHub Release(string repository, string tag, string file, byte[] bytes)
        {
            _releases.Add((repository, tag, file, bytes));
            return this;
        }

        public MadeUpGitHub Delete(string repository, string tag)
        {
            _releases.RemoveAll(release => release.Repository == repository && release.Tag == tag);
            return this;
        }

        /// <summary>What a download of a release's file gives.</summary>
        public byte[] Bytes(string repository, string tag) => _releases.Single(release => release.Repository == repository && release.Tag == tag).Bytes;

        public static byte[] Archive(string manifest) => PackageArchive.Write([File("packmoji.json", manifest), File("src/lib.🍇", "💭\n")]);

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken)
        {
            Asked.Add($"list {repository}");
            if (Unreachable)
            {
                throw new PackageSourceException(new Diagnostic(DiagnosticCodes.GitHubUnreachable, "GitHub could not be reached.", "the test said so", "try again"));
            }

            IReadOnlyList<ReleaseInfo> releases = _releases
                .Where(release => release.Repository == repository.ToString())
                .Select(release => new ReleaseInfo(release.Tag, IsPrerelease: false, [new ReleaseFile(release.File, release.Bytes.Length, Sha256Digest.Of(release.Bytes), IsUploaded: true)])
                {
                    Published = new DateTimeOffset(2026, 10, 11, 15, 2, 11, TimeSpan.Zero),
                })
                .ToList();
            return Task.FromResult(releases);
        }

        public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken)
        {
            Asked.Add($"download {repository} {tag}");
            var found = _releases.Where(release => release.Repository == repository.ToString() && release.Tag == tag && release.File == asset).Select(release => release.Bytes).FirstOrDefault();
            return Task.FromResult(found is null ? (ReadOnlyMemory<byte>?)null : found);
        }

        private static ArchiveFile File(string path, string text)
        {
            RelativePath.TryParse(path, out var parsed, out _).ShouldBeTrue();
            return new ArchiveFile(parsed!, Encoding.UTF8.GetBytes(text));
        }
    }
}
