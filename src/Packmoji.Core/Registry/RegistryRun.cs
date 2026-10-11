using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// One run of what keeps a registry. It asks each listed repository for its releases, and makes
    /// the registry's next state from the last one: a release that claims to be a version of a package
    /// and is one is recorded, one that is not is refused and that is recorded, and a version whose
    /// bytes are no longer the ones first recorded is quarantined.
    /// </summary>
    /// <remarks>
    /// What is recorded of a version is never changed afterwards but for its status, and a version is
    /// never removed. A run looks at nothing twice: a version that is recorded, and a release that was
    /// refused, are known again by the digest the host lists for the release's file, and nothing is
    /// downloaded for them. The same releases and the same registry give the same next state,
    /// whatever order anything was listed in.
    /// </remarks>
    public static class RegistryRun
    {
        /// <param name="before">The registry as it stands.</param>
        /// <param name="repositories">The repositories whose releases are looked at.</param>
        /// <param name="now">The moment of the run, which is what it records of when a thing was seen.</param>
        public static async Task<RegistryRunResult> RunAsync(
            RegistryState before,
            IReadOnlyList<RepositoryRef> repositories,
            IReleaseHost host,
            DateTimeOffset now,
            RegistryLimits limits,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(repositories);
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(limits);

            var run = new Run(before, host, new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero), limits);
            foreach (var repository in repositories.Distinct().OrderBy(repository => repository.ToString(), StringComparer.Ordinal))
            {
                await run.LookAtAsync(repository, cancellationToken);
            }

            return run.Finish();
        }

        // A release that claims to be a version of a package: its tag names one, and it carries the
        // file of that name, which the host says is uploaded and gives a digest for.
        private sealed record Claim(string Tag, string Bare, SemanticVersion Version, string Asset, ReleaseFile File, ReleaseInfo Release);

        private sealed class Run(RegistryState before, IReleaseHost host, DateTimeOffset now, RegistryLimits limits)
        {
            private readonly Dictionary<PackageName, Dictionary<SemanticVersion, RegistryVersion>> _packages =
                before.Packages.ToDictionary(package => package.Name, package => package.Versions.ToDictionary(version => version.Version));

            // What is refused in each repository that was looked at, or that could not be. A
            // repository that is no longer listed is not here: nothing of it is known any more.
            private readonly Dictionary<RepositoryRef, List<RefusedRelease>> _refused = [];
            private readonly List<RegistryNote> _notes = [];
            private int _downloads;
            private long _bytes;

            public async Task LookAtAsync(RepositoryRef repository, CancellationToken cancellationToken)
            {
                var refusedBefore = before.RefusedIn(repository)?.Releases ?? [];
                IReadOnlyList<ReleaseInfo> releases;
                try
                {
                    releases = await host.ListAsync(repository, cancellationToken);
                }
                catch (PackageSourceException failure)
                {
                    // Left as it was: a repository that could not be asked is not one that has released nothing.
                    _refused[repository] = refusedBefore.ToList();
                    Say(RegistryNoteKind.Unreachable, $"could not list the releases of {repository} ({failure.Diagnostic.Code})");
                    return;
                }

                var refused = _refused[repository] = [];
                var claimed = new HashSet<(string Bare, SemanticVersion Version)>();
                var tags = new HashSet<string>(StringComparer.Ordinal);
                foreach (var claim in Claims(releases))
                {
                    tags.Add(claim.Tag);
                    claimed.Add((claim.Bare, claim.Version));
                    if (await JudgeAsync(repository, claim, refusedBefore.FirstOrDefault(release => release.Tag == claim.Tag), cancellationToken) is { } refusal)
                    {
                        refused.Add(refusal);
                    }
                }

                foreach (var gone in refusedBefore.Where(release => !tags.Contains(release.Tag)))
                {
                    Say(RegistryNoteKind.NoLongerRefused, $"no longer refused: {gone.Tag} is gone from {repository}");
                }

                foreach (var (name, versions) in _packages.OrderBy(package => package.Key))
                {
                    foreach (var version in versions.Values.Where(version => version.Source == repository && !claimed.Contains((name.Name, version.Version))).OrderBy(version => version.Version))
                    {
                        Say(RegistryNoteKind.Gone, $"gone: the release of {name} {version.Version} is no longer in {repository}");
                    }
                }
            }

            public RegistryRunResult Finish() => new(
                new RegistryState(
                    _packages.Select(package => new RegistryPackage(package.Key, package.Value.Values.OrderBy(version => version.Version).ToList())),
                    _refused.Select(file => new RefusedReleases(file.Key, file.Value.OrderBy(release => release.Tag, StringComparer.Ordinal).ToList()))),
                _notes);

            // In order of tag, so that what a run says, and what it leaves when it reaches its limit, is the same every time.
            private static IEnumerable<Claim> Claims(IReadOnlyList<ReleaseInfo> releases)
            {
                var claims = new List<Claim>();
                var tags = new HashSet<string>(StringComparer.Ordinal);
                foreach (var release in releases)
                {
                    if (release.Published is null || !ReleaseTag.TryParse(release.Tag, out var bare, out var version) || !tags.Add(release.Tag))
                    {
                        continue;
                    }

                    var asset = $"{bare}-{version}{AssetName.Suffix}";
                    if (release.Files.FirstOrDefault(file => string.Equals(file.Name, asset, StringComparison.Ordinal)) is { IsUploaded: true, Sha256: not null } file)
                    {
                        claims.Add(new Claim(release.Tag, bare, version, asset, file, release));
                    }
                }

                return claims.OrderBy(claim => claim.Tag, StringComparer.Ordinal);
            }

            /// <summary>Looks at one claim, and gives what is to be recorded of it when it is refused.</summary>
            private async Task<RefusedRelease?> JudgeAsync(RepositoryRef repository, Claim claim, RefusedRelease? refusedBefore, CancellationToken cancellationToken)
            {
                var listed = claim.File.Sha256!;

                // The scope a release claims is the owner of the repository it is in. That is the
                // whole of the check on who may publish under a scope, and nothing a manifest says changes it.
                if (!PackageName.TryParse($"@{repository.Owner}/{claim.Bare}", out var name, out _))
                {
                    return Refuse(repository, claim, refusedBefore, DiagnosticCodes.NameReserved, RefusalReasons.Of(RefusalReasons.ReservedName, claim.Bare));
                }

                if (_packages.TryGetValue(name, out var versions) && versions.TryGetValue(claim.Version, out var held))
                {
                    if (held.Source != repository)
                    {
                        return Refuse(repository, claim, refusedBefore, DiagnosticCodes.ReleaseInvalid, RefusalReasons.Of(RefusalReasons.AlreadyIndexed, held.Source));
                    }

                    // The host's own digest holds every version at every run for the price of the
                    // list. A version that was quarantined is known to have changed, and stays so.
                    if (held.Sha256 == listed || held.Status == VersionStatus.Quarantined)
                    {
                        return null;
                    }

                    if (AtLimit(repository, claim))
                    {
                        return null;
                    }

                    // The list is not believed until the bytes have been seen: a version is never
                    // quarantined on the host's word that its file changed.
                    Sha256Digest current;
                    try
                    {
                        if (await DownloadAsync(repository, claim, cancellationToken) is not { } bytes)
                        {
                            return null;
                        }

                        current = Sha256Digest.Of(bytes.Span);
                    }
                    catch (PackageSourceException failure) when (failure.Diagnostic.Code == DiagnosticCodes.ArchiveInvalid)
                    {
                        // Too large to be downloaded whole, and so too large to be the bytes that
                        // were recorded. Only what they now are is taken from the list.
                        current = listed;
                    }

                    if (current != held.Sha256)
                    {
                        versions[claim.Version] = held with { Status = VersionStatus.Quarantined, Quarantined = new RegistryQuarantine(now, current) };
                        Say(RegistryNoteKind.Quarantined, $"quarantined {name} {claim.Version}: its file in {repository} is no longer the bytes that were recorded");
                    }

                    return null;
                }

                if (refusedBefore is not null && refusedBefore.Sha256 == listed)
                {
                    return refusedBefore;
                }

                if (claim.File.Size > PackageArchive.MaxBytes)
                {
                    return Refuse(repository, claim, null, DiagnosticCodes.ArchiveInvalid, RefusalReasons.TooLarge);
                }

                if (AtLimit(repository, claim))
                {
                    return null;
                }

                ReadOnlyMemory<byte>? downloaded;
                try
                {
                    downloaded = await DownloadAsync(repository, claim, cancellationToken);
                }
                catch (PackageSourceException failure) when (failure.Diagnostic.Code == DiagnosticCodes.ArchiveInvalid)
                {
                    // More bytes than the list said, and more than an archive may be.
                    return Refuse(repository, claim, null, DiagnosticCodes.ArchiveInvalid, RefusalReasons.TooLarge);
                }

                if (downloaded is not { } archive)
                {
                    return null;
                }

                if (Sha256Digest.Of(archive.Span) != listed)
                {
                    return Refuse(repository, claim, null, DiagnosticCodes.ReleaseInvalid, RefusalReasons.NotTheListedDigest);
                }

                var verdict = ReleaseRule.Judge(name, claim.Version, repository, archive);
                if (!verdict.Taken)
                {
                    return verdict.Fault switch
                    {
                        ReleaseFault.NotAnArchive => Refuse(repository, claim, null, DiagnosticCodes.ArchiveInvalid, RefusalReasons.NotAnArchive),
                        ReleaseFault.OtherPackage => Refuse(repository, claim, null, DiagnosticCodes.ReleaseInvalid, RefusalReasons.Of(RefusalReasons.OtherPackage, verdict.Manifest!.Package.Name)),
                        ReleaseFault.OtherVersion => Refuse(repository, claim, null, DiagnosticCodes.ReleaseInvalid, RefusalReasons.Of(RefusalReasons.OtherVersion, verdict.Manifest!.Package.Version)),
                        ReleaseFault.OtherRepository => Refuse(repository, claim, null, DiagnosticCodes.ReleaseInvalid, RefusalReasons.Of(RefusalReasons.OtherRepository, verdict.Manifest!.Repository)),
                        _ => Refuse(repository, claim, null, DiagnosticCodes.ReleaseInvalid, RefusalReasons.ManifestUnreadable),
                    };
                }

                if (!_packages.TryGetValue(name, out versions))
                {
                    _packages[name] = versions = [];
                }

                var published = verdict.Published;
                versions[claim.Version] = new RegistryVersion(
                    claim.Version,
                    VersionStatus.Active,
                    repository,
                    published.Sha256,
                    archive.Length,
                    published.Verified,
                    claim.Release.IsImmutable,
                    claim.Release.Published!.Value.ToUniversalTime(),
                    now,
                    published.Dependencies);
                Say(RegistryNoteKind.Added, $"added {name} {claim.Version} from {repository}");
                return null;
            }

            // A release that was refused before for the same file is the same refusal, and is said once: when it was first refused.
            private RefusedRelease Refuse(RepositoryRef repository, Claim claim, RefusedRelease? refusedBefore, string code, string why)
            {
                if (refusedBefore is not null && refusedBefore.Sha256 == claim.File.Sha256 && refusedBefore.Code == code && refusedBefore.Why == why)
                {
                    return refusedBefore;
                }

                Say(RegistryNoteKind.Refused, $"refused {claim.Tag} in {repository}: {why}");
                return new RefusedRelease(claim.Tag, claim.File.Sha256!, now, code, why);
            }

            /// <summary>Whether the run has downloaded all it may. A claim that meets the limit is said to be left, and is looked at by the next run.</summary>
            private bool AtLimit(RepositoryRef repository, Claim claim)
            {
                if (_downloads < limits.MaxDownloads && _bytes + claim.File.Size <= limits.MaxBytes)
                {
                    return false;
                }

                Say(RegistryNoteKind.Left, $"left for the next run: {claim.Tag} in {repository}");
                return true;
            }

            /// <summary>
            /// The file of a claim, or null when it is gone or could not be had. Each asking counts
            /// against the run's limit, and so do the bytes that came, whatever the list said they would be.
            /// </summary>
            private async Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, Claim claim, CancellationToken cancellationToken)
            {
                _downloads++;
                try
                {
                    var bytes = await host.DownloadAsync(repository, claim.Tag, claim.Asset, PackageArchive.MaxBytes, cancellationToken);
                    _bytes += bytes?.Length ?? 0;
                    return bytes;
                }
                catch (PackageSourceException failure) when (failure.Diagnostic.Code != DiagnosticCodes.ArchiveInvalid)
                {
                    Say(RegistryNoteKind.Unreachable, $"could not download {claim.Tag} from {repository} ({failure.Diagnostic.Code})");
                    return null;
                }
            }

            private void Say(RegistryNoteKind kind, string text) => _notes.Add(new RegistryNote(kind, text));
        }
    }
}
