using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// GitHub as whatever keeps a registry sees it, held in memory: the releases of some repositories,
    /// and what the list of them says of each file. A test can make the list say other than the bytes,
    /// and it keeps what was asked, so that a test can say what a run looked at and what it left alone.
    /// </summary>
    internal sealed class ListedReleases : IReleaseHost
    {
        public const string Grapevine = "github.com/thatplatypus/grapevine";

        public static readonly DateTimeOffset PublishedAt = new(2026, 10, 11, 15, 2, 11, TimeSpan.Zero);

        private readonly List<Listed> _releases = [];

        /// <summary>Every repository whose releases were listed, in the order of the asking.</summary>
        public List<string> Listings { get; } = [];

        /// <summary>Every download asked for, as <c>github.com/owner/repo tag</c>, in the order of the asking.</summary>
        public List<string> Downloads { get; } = [];

        /// <summary>The repositories whose releases cannot be listed, as when GitHub cannot be reached.</summary>
        public HashSet<string> Unlistable { get; } = new(StringComparer.Ordinal);

        /// <summary>When set, a list is given and no file can be downloaded.</summary>
        public bool DownloadsFail { get; set; }

        /// <summary>When set, the releases of a repository are listed last first.</summary>
        public bool Backwards { get; set; }

        /// <summary>
        /// Releases one version of a package as <c>pmj pack</c> and a release would. The manifest says
        /// where the package lives when that is not a repository of its own name. Each dependency is
        /// written as <c>@owner/name@1.2</c>.
        /// </summary>
        public ListedReleases Package(string repository, string name, string version, params string[] dependencies) =>
            PackageWith(repository, name, version, Sample.ManifestJson(name, version, repository, dependencies));

        /// <summary>The same, with a manifest that the test wrote.</summary>
        public ListedReleases PackageWith(string repository, string name, string version, string manifest)
        {
            var package = Sample.Name(name);
            return Release(repository, ReleaseTag.For(package, Sample.Version(version)), AssetName.For(package, Sample.Version(version)), Archive(manifest));
        }

        /// <summary>A release with one file of any name and any bytes.</summary>
        public ListedReleases Release(string repository, string tag, string file, byte[] bytes)
        {
            _releases.Add(new Listed(repository, tag, file, bytes));
            return this;
        }

        /// <summary>Changes what is released under a tag, or what the list says of it.</summary>
        public ListedReleases Change(string repository, string tag, Action<Listed> change)
        {
            change(Find(repository, tag));
            return this;
        }

        /// <summary>Deletes a release, as its author may.</summary>
        public ListedReleases Delete(string repository, string tag)
        {
            _releases.Remove(Find(repository, tag));
            return this;
        }

        /// <summary>The archive of a package: its manifest and one source file.</summary>
        public static byte[] Archive(string manifest) => PackageArchive.Write([Sample.File("packmoji.json", manifest), Sample.File("src/lib.🍇", "💭\n")]);

        public Listed Find(string repository, string tag) => _releases.Single(release => release.Repository == repository && release.Tag == tag);

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Listings.Add(repository.ToString());
            if (Unlistable.Contains(repository.ToString()))
            {
                throw Unreachable();
            }

            IEnumerable<Listed> listed = _releases.Where(release => release.Repository == repository.ToString());
            IReadOnlyList<ReleaseInfo> releases = (Backwards ? listed.Reverse() : listed)
                .Select(release => new ReleaseInfo(
                    release.Tag,
                    IsPrerelease: false,
                    [new ReleaseFile(release.File, release.ListedSize ?? release.Bytes.Length, release.SaysDigest ? release.ListedDigest ?? Sha256Digest.Of(release.Bytes) : null, release.IsUploaded), new ReleaseFile("notes.txt", 12, null, IsUploaded: true)])
                {
                    Published = release.Published,
                    IsImmutable = release.IsImmutable,
                })
                .ToList();
            return Task.FromResult(releases);
        }

        public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Downloads.Add($"{repository} {tag}");
            if (DownloadsFail)
            {
                throw Unreachable();
            }

            var release = _releases.SingleOrDefault(listed => listed.Repository == repository.ToString() && listed.Tag == tag && listed.File == asset);
            if (release is null || release.GoneWhenAsked)
            {
                return Task.FromResult<ReadOnlyMemory<byte>?>(null);
            }

            if (release.Bytes.Length > maxBytes)
            {
                throw new PackageSourceException(new Diagnostic(DiagnosticCodes.ArchiveInvalid, "The archive is too large.", "the test made it so", "none"));
            }

            return Task.FromResult<ReadOnlyMemory<byte>?>(release.Bytes);
        }

        private static PackageSourceException Unreachable() =>
            new(new Diagnostic(DiagnosticCodes.GitHubUnreachable, "GitHub could not be reached.", "the test said so", "try again"));

        /// <summary>One release and its one file, and what the list says of them.</summary>
        internal sealed class Listed(string repository, string tag, string file, byte[] bytes)
        {
            public string Repository { get; } = repository;

            public string Tag { get; } = tag;

            public string File { get; set; } = file;

            /// <summary>What a download gives.</summary>
            public byte[] Bytes { get; set; } = bytes;

            /// <summary>The digest the list gives when it is not that of the bytes.</summary>
            public Sha256Digest? ListedDigest { get; set; }

            /// <summary>Whether the list gives a digest at all.</summary>
            public bool SaysDigest { get; set; } = true;

            /// <summary>The size the list gives when it is not that of the bytes.</summary>
            public long? ListedSize { get; set; }

            public bool IsUploaded { get; set; } = true;

            public DateTimeOffset? Published { get; set; } = PublishedAt;

            public bool IsImmutable { get; set; }

            /// <summary>Whether the file is listed and is gone by the time it is asked for.</summary>
            public bool GoneWhenAsked { get; set; }
        }
    }
}
