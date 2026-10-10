using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// A package source with no registry behind it. A package version is the release tagged
    /// <c>&lt;name&gt;-v&lt;version&gt;</c> in a repository that the package's scope owns, and the
    /// work is knowing which repository, because a name says who owns a package and not always where
    /// it is kept.
    /// </summary>
    /// <remarks>
    /// A package is looked for, in this order: where the project's lockfile says it lives, where pmj
    /// was told, in a repository of its own name, and then in the other repositories of its owner
    /// that this source has come to know: the project's own, and those of packages it has found. A
    /// release is taken only when the manifest in its archive says that it is the package and the
    /// version looked for, and that the package lives where it was found.
    /// </remarks>
    public sealed class DirectPackageSource : IPackageSource
    {
        private readonly IReleaseHost _host;
        private readonly IAssetStore _store;
        private readonly Dictionary<PackageName, RepositoryRef> _locked = [];
        private readonly Dictionary<(PackageName Name, SemanticVersion Version), Sha256Digest> _digests = [];
        private readonly IReadOnlyDictionary<PackageName, RepositoryRef> _told;
        private readonly Dictionary<string, SortedSet<string>> _known = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RepositoryRef> _repositories = new(StringComparer.Ordinal);
        private readonly Dictionary<(PackageName Name, SemanticVersion Version), PublishedVersion> _found = [];
        private readonly Dictionary<(PackageName Name, SemanticVersion Version), List<RepositoryRef>> _lookedIn = [];
        private readonly Dictionary<PackageName, List<RepositoryRef>> _listedIn = [];
        private readonly Dictionary<string, IReadOnlyList<ReleaseInfo>> _releases = new(StringComparer.Ordinal);

        /// <param name="project">The project's manifest, for the repository the project itself is in.</param>
        /// <param name="lockfile">The project's lockfile when it has one, for where its packages live and for the digests that the cache may answer for.</param>
        /// <param name="told">Where a package lives, for packages someone has said it of.</param>
        public DirectPackageSource(
            IReleaseHost host,
            IAssetStore store,
            Manifest project,
            Lockfile? lockfile,
            IReadOnlyDictionary<PackageName, RepositoryRef>? told = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(project);
            _host = host;
            _store = store;
            _told = told ?? new Dictionary<PackageName, RepositoryRef>();

            Learn(project.Repository);
            foreach (var package in lockfile?.Packages ?? [])
            {
                _locked.TryAdd(package.Name, package.Source);
                _digests.TryAdd((package.Name, package.Version), package.Sha256);
                Learn(package.Source);
            }
        }

        /// <summary>
        /// How many repositories this source knows of. A resolution that ended with a version it
        /// could not find is worth running again when this has grown since it began.
        /// </summary>
        public int KnownRepositories => _repositories.Count;

        public async ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            var key = (name, version);
            if (_found.TryGetValue(key, out var found))
            {
                return found;
            }

            if (_digests.TryGetValue(key, out var digest)
                && await _store.FindAsync(name, version, digest, cancellationToken) is { } kept
                && Sha256Digest.Of(kept.Span) == digest)
            {
                return _found[key] = Describe(name, version, _locked[name], kept);
            }

            if (!_lookedIn.TryGetValue(key, out var lookedIn))
            {
                _lookedIn[key] = lookedIn = [];
            }

            foreach (var repository in Candidates(name).Where(candidate => !lookedIn.Contains(candidate)).ToList())
            {
                lookedIn.Add(repository);
                if (await _host.DownloadAsync(repository, ReleaseTag.For(name, version), AssetName.For(name, version), PackageArchive.MaxBytes, cancellationToken) is not { } archive)
                {
                    continue;
                }

                var published = Describe(name, version, repository, archive);
                await _store.KeepAsync(name, version, published.Sha256, archive, cancellationToken);
                Learn(repository);
                return _found[key] = published;
            }

            return null;
        }

        /// <summary>
        /// The versions of a package that have been released, from the first place that has any, and
        /// which place that is. Null when no place this source looks in has one.
        /// </summary>
        public async Task<(RepositoryRef Repository, IReadOnlyList<SemanticVersion> Versions)?> ListVersionsAsync(PackageName name, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            var listedIn = _listedIn[name] = [];
            foreach (var repository in Candidates(name).ToList())
            {
                listedIn.Add(repository);
                // One repository can hold many packages, and its releases are one list for all of
                // them, so the list is asked for once.
                if (!_releases.TryGetValue(repository.ToString(), out var releases))
                {
                    _releases[repository.ToString()] = releases = await _host.ListAsync(repository, cancellationToken);
                }

                var versions = VersionCatalog.Of(name, releases);
                if (versions.Count > 0)
                {
                    Learn(repository);
                    return (repository, versions);
                }
            }

            return null;
        }

        /// <summary>The repositories a version was looked for in, in the order they were tried.</summary>
        public IReadOnlyList<RepositoryRef> LookedIn(PackageName name, SemanticVersion version) =>
            _lookedIn.TryGetValue((name, version), out var lookedIn) ? lookedIn : [];

        /// <summary>The repositories a package's versions were last listed from, in the order they were tried.</summary>
        public IReadOnlyList<RepositoryRef> LookedIn(PackageName name) => _listedIn.TryGetValue(name, out var listedIn) ? listedIn : [];

        /// <summary>
        /// The versions that were looked for and not found, of packages no version of which was found
        /// at all: those this source cannot place, as against those it can and that lack a version.
        /// </summary>
        public IReadOnlyList<(PackageName Name, SemanticVersion Version)> Unplaced() =>
            _lookedIn.Keys
                .Where(key => !_found.ContainsKey(key) && !_found.Keys.Any(found => found.Name == key.Name) && !_locked.ContainsKey(key.Name))
                .OrderBy(key => key.Name)
                .ThenBy(key => key.Version)
                .ToList();

        /// <summary>Says that a package could not be placed: where pmj looked for it, and how to say where it is.</summary>
        public static Diagnostic NotFound(PackageName name, SemanticVersion? version, IReadOnlyList<RepositoryRef> lookedIn)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(lookedIn);
            var places = string.Join(" and in ", lookedIn.Select(repository => repository.ToString()));
            return new Diagnostic(
                DiagnosticCodes.PackageNotFound,
                version is null ? $"No release of \"{name}\" was found." : $"No release of \"{name}\" {version} was found.",
                version is null
                    ? $"pmj looked in {places} for a release tagged {name.Name}-v and a version, and there is none"
                    : $"pmj looked for the release {ReleaseTag.For(name, version)} in {places}, and it is not there",
                $"if the package shares a repository with others, say which, once: pmj add {name} --repository github.com/{name.Scope}/<repository>");
        }

        // In the order they are tried. Every one of them is owned by the package's scope: that is
        // the whole of the check on who may publish a package, and it is never set aside.
        private IEnumerable<RepositoryRef> Candidates(PackageName name)
        {
            var candidates = new List<RepositoryRef>();
            if (_locked.TryGetValue(name, out var locked))
            {
                candidates.Add(locked);
            }

            if (_told.TryGetValue(name, out var told))
            {
                candidates.Add(told);
            }

            candidates.Add(RepositoryRef.DefaultFor(name));
            if (_known.TryGetValue(name.Scope, out var known))
            {
                candidates.AddRange(known.Select(repository => _repositories[repository]));
            }

            return candidates.Where(candidate => candidate.BelongsTo(name)).Distinct();
        }

        private void Learn(RepositoryRef repository)
        {
            if (_repositories.TryAdd(repository.ToString(), repository))
            {
                if (!_known.TryGetValue(repository.Owner, out var known))
                {
                    _known[repository.Owner] = known = new SortedSet<string>(StringComparer.Ordinal);
                }

                known.Add(repository.ToString());
            }
        }

        private static PublishedVersion Describe(PackageName name, SemanticVersion version, RepositoryRef repository, ReadOnlyMemory<byte> archive)
        {
            var files = PackageArchive.Read(archive);
            if (!files.Succeeded)
            {
                throw Invalid(name, version, repository, files.Diagnostics[0].Reason, tellTheAuthor: true);
            }

            var manifest = ManifestReader.Read(files.Value.First(file => file.Path.Value == ManifestReader.FileName).Content);
            if (!manifest.Succeeded)
            {
                var first = manifest.Diagnostics[0];
                throw Invalid(name, version, repository, $"its {ManifestReader.FileName} cannot be read: {first.Message} That is because {first.Reason}", tellTheAuthor: true);
            }

            var package = manifest.Value.Package;
            if (package.Name != name)
            {
                throw Invalid(name, version, repository, $"the manifest in its archive is that of \"{package.Name}\"", tellTheAuthor: true);
            }

            if (package.Version != version)
            {
                throw Invalid(name, version, repository, $"the manifest in its archive gives the version {package.Version}", tellTheAuthor: true);
            }

            if (manifest.Value.Repository != repository)
            {
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.ReleaseInvalid,
                    $"The release {ReleaseTag.For(name, version)} in {repository} is not \"{name}\" {version}.",
                    $"the manifest in its archive says that the package lives in {manifest.Value.Repository}, and a release counts only where its package says it lives",
                    $"tell its author: the manifest has to give \"repository\": \"{repository}\" under \"package\", or the release has to be made in {manifest.Value.Repository}"));
            }

            return new PublishedVersion(
                name,
                version,
                VersionStatus.Active,
                repository,
                Sha256Digest.Of(archive.Span),
                VerificationLevel.Checksum,
                manifest.Value.Dependencies ?? []);
        }

        private static PackageSourceException Invalid(PackageName name, SemanticVersion version, RepositoryRef repository, string reason, bool tellTheAuthor) =>
            new(new Diagnostic(
                DiagnosticCodes.ReleaseInvalid,
                $"The release {ReleaseTag.For(name, version)} in {repository} is not \"{name}\" {version}.",
                reason,
                tellTheAuthor
                    ? "tell its author: a release's archive has to be made by pmj pack from the package's own directory, at the version its tag names"
                    : "try again"));
    }
}
