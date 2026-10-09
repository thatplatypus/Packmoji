namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// Every diagnostic code Packmoji raises. A code is public: tools match on it, so one never changes
    /// meaning once it has been released.
    /// </summary>
    public static class DiagnosticCodes
    {
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
        public const string PinInvalid = "pin.invalid";
        public const string Sha256Invalid = "sha256.invalid";
        public const string VerifiedInvalid = "verified.invalid";
    }
}
