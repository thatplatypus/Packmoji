namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// Every diagnostic code Packmoji raises. A code is public: tools match on it, so one never changes
    /// meaning once it has been released.
    /// </summary>
    public static class DiagnosticCodes
    {
        public const string FileTooLarge = "file.too-large";
        public const string JsonSyntax = "json.syntax";
        public const string JsonDuplicateKey = "json.duplicate-key";
        public const string JsonWrongType = "json.wrong-type";
        public const string KeyUnknown = "key.unknown";
        public const string KeyMissing = "key.missing";
        public const string NameInvalid = "name.invalid";
        public const string NameReserved = "name.reserved";
        public const string VersionInvalid = "version.invalid";
        public const string VersionBuildMetadata = "version.build-metadata";
        public const string RequirementInvalid = "requirement.invalid";
        public const string CompilerInvalid = "compiler.invalid";
        public const string KindInvalid = "kind.invalid";
        public const string DescriptionInvalid = "description.invalid";
        public const string LicenseInvalid = "license.invalid";
        public const string RepositoryInvalid = "repository.invalid";
        public const string RepositoryOwnerMismatch = "repository.owner-mismatch";
        public const string DependencySelf = "dependency.self";
        public const string DependencyDuplicate = "dependency.duplicate";
        public const string DependencyNameCollision = "dependency.name-collision";
        public const string PathInvalid = "path.invalid";
        public const string GlobInvalid = "glob.invalid";
        public const string LinkInvalid = "link.invalid";
        public const string ListEmpty = "list.empty";
        public const string ListDuplicate = "list.duplicate";
        public const string EntrySuffix = "entry.suffix";
        public const string EntryNotFound = "entry.not-found";
        public const string EntryAmbiguous = "entry.ambiguous";
        public const string LockUnsupportedVersion = "lock.unsupported-version";
        public const string LockDuplicatePackage = "lock.duplicate-package";
        public const string LockNameCollision = "lock.name-collision";
        public const string LockTagMismatch = "lock.tag-mismatch";
        public const string LockAssetMismatch = "lock.asset-mismatch";
        public const string LockDanglingDependency = "lock.dangling-dependency";
        public const string LockRootUnsatisfied = "lock.root-unsatisfied";
        public const string LockUnreachable = "lock.unreachable";
        public const string LockMismatch = "lock.mismatch";
        public const string PinInvalid = "pin.invalid";
        public const string Sha256Invalid = "sha256.invalid";
        public const string VerifiedInvalid = "verified.invalid";
        public const string ResolveVersionMissing = "resolve.version-missing";
        public const string ResolveYanked = "resolve.yanked";
        public const string ResolveYankedLocked = "resolve.yanked-locked";
        public const string ResolveQuarantined = "resolve.quarantined";
        public const string ResolveLineConflict = "resolve.line-conflict";
        public const string ResolveNameCollision = "resolve.name-collision";
        public const string ResolveCycle = "resolve.cycle";
        public const string ResolveGraphTooLarge = "resolve.graph-too-large";
        public const string ProjectNotFound = "project.not-found";
        public const string ProjectExists = "project.exists";
        public const string ProjectUnreadable = "project.unreadable";
        public const string DependencyExists = "dependency.exists";
        public const string DependencyNotFound = "dependency.not-found";
        public const string PackageNotFound = "package.not-found";
        public const string VersionNoneReleased = "version.none-released";
        public const string ReleaseInvalid = "release.invalid";
        public const string ArchiveInvalid = "archive.invalid";
        public const string PackNothing = "pack.nothing";
        public const string PackUnportable = "pack.unportable";
        public const string LockOutOfDate = "lock.out-of-date";
        public const string AttestationUnverifiable = "attestation.unverifiable";
        public const string GitHubUnreachable = "github.unreachable";
        public const string GitHubRateLimited = "github.rate-limited";
        public const string CacheUnusable = "cache.unusable";
    }
}
