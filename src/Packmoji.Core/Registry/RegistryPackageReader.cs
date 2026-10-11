using System.Globalization;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Reads the file a registry keeps of one package. It is read by every pmj that was ever
    /// released, and a registry gains keys as it grows, so a key this reader does not know is passed
    /// over. A key it does know has to be there and has to be well formed: what is read here says
    /// which bytes a version is.
    /// </summary>
    public static class RegistryPackageReader
    {
        /// <summary>The most bytes a package's file may be: 4 MiB, which is room for some ten thousand versions.</summary>
        public const int MaxBytes = 4_194_304;

        /// <summary>
        /// What to do about a file that does not read. Whoever reads a registry cannot put it right,
        /// so every diagnostic here gives this fix.
        /// </summary>
        public const string KeeperFix = "tell whoever keeps the registry: its files are written by the registry's own indexer, and the registry's history says what this one held before";

        /// <param name="expected">The package whose file this is taken to be, by the path it was read from.</param>
        /// <param name="file">The file's path, for the place a diagnostic gives.</param>
        public static ReadResult<RegistryPackage> Read(ReadOnlyMemory<byte> utf8, PackageName expected, string file)
        {
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(file);
            if (utf8.Length > MaxBytes)
            {
                return ReadResult<RegistryPackage>.Failure(
                    [
                        new Diagnostic(
                            DiagnosticCodes.FileTooLarge,
                            $"\"{file}\" is too large to be a package's file.",
                            $"a package's file in a registry is at most {MaxBytes} bytes, which is 4 MiB and room for some ten thousand versions",
                            KeeperFix,
                            new SourceLocation(file, 1, 1)),
                    ],
                    0);
            }

            var diagnostics = new DiagnosticList();
            var package = ReadPackage(utf8, expected, file, diagnostics);
            return diagnostics.Count > 0 || package is null
                ? ReadResult<RegistryPackage>.Failure(diagnostics.Select(diagnostic => diagnostic with { Fix = KeeperFix }).ToList(), diagnostics.Omitted)
                : ReadResult<RegistryPackage>.Success(package);
        }

        // No object here is asked to finish, which is where a key that is not known would be refused.
        private static RegistryPackage? ReadPackage(ReadOnlyMemory<byte> utf8, PackageName expected, string file, DiagnosticList diagnostics)
        {
            var tree = JsonTreeReader.Read(utf8, file, KeeperFix, diagnostics);
            if (tree is null)
            {
                return null;
            }

            if (tree.Kind != JsonKind.Object)
            {
                diagnostics.Add(JsonDiagnostics.RootNotObject(tree));
                return null;
            }

            var root = new JsonObjectReader(tree, diagnostics);
            var formatItem = root.Required("version", JsonKind.Number, "\"version\": 1");
            if (formatItem is not null && formatItem.Text != "1")
            {
                // Another format may give its keys another meaning, so nothing else that could be said of the file would mean anything.
                diagnostics.Clear();
                diagnostics.Add(Rule(
                    formatItem,
                    $"The file says it is of version {formatItem.Text} of the format.",
                    $"this pmj reads version {RegistryPackage.FormatVersion}, which is the only one that a directory named v1 holds"));
                return null;
            }

            var nameItem = root.Required("name", JsonKind.String, $"\"name\": \"{expected}\"");
            var versionsItem = root.Required("versions", JsonKind.Array, "\"versions\": []");

            var name = nameItem.Parse<PackageName>(PackageName.TryParse, diagnostics);
            if (name is not null && name != expected)
            {
                diagnostics.Add(Rule(
                    nameItem!,
                    $"The file is that of \"{name}\".",
                    $"it was read as the file of \"{expected}\", and a package's file is at the path that its name gives"));
            }

            var versions = new List<RegistryVersion>();
            var seen = new HashSet<SemanticVersion>();
            foreach (var item in versionsItem?.Items ?? [])
            {
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(JsonDiagnostics.WrongType("An entry of \"versions\"", JsonKind.Object, item, removable: false));
                }
                else if (ReadVersion(item, expected, seen, diagnostics) is { } version)
                {
                    versions.Add(version);
                }
            }

            return diagnostics.Count > 0 || name is null || versionsItem is null
                ? null
                : new RegistryPackage(name, versions.OrderBy(version => version.Version).ToList());
        }

        private static RegistryVersion? ReadVersion(JsonItem item, PackageName package, HashSet<SemanticVersion> seen, DiagnosticList diagnostics)
        {
            var reader = new JsonObjectReader(item, diagnostics);
            var versionItem = reader.Required("version", JsonKind.String, "\"version\": \"1.0.0\"");
            var statusItem = reader.Required("status", JsonKind.String, "\"status\": \"active\"");
            var sourceItem = reader.Required("source", JsonKind.String, "\"source\": \"github.com/owner/repo\"");
            var sha256Item = reader.Required("sha256", JsonKind.String, "\"sha256\": \"<64 hexadecimal digits>\"");
            var sizeItem = reader.Required("size", JsonKind.Number, "\"size\": <how many bytes the archive is>");
            var verifiedItem = reader.Required("verified", JsonKind.String, "\"verified\": \"checksum\"");
            var immutableItem = reader.Required("immutable", JsonKind.Boolean, "\"immutable\": false");
            var publishedItem = reader.Required("published", JsonKind.String, $"\"published\": \"{RegistryTime.Example}\"");
            var indexedItem = reader.Required("indexed", JsonKind.String, $"\"indexed\": \"{RegistryTime.Example}\"");
            var dependenciesItem = reader.Required("dependencies", JsonKind.Object, "\"dependencies\": {}");
            var quarantinedItem = reader.Optional("quarantined", JsonKind.Object);

            var version = versionItem.Parse<SemanticVersion>(SemanticVersion.TryParse, diagnostics);
            if (version is not null && !seen.Add(version))
            {
                diagnostics.Add(Rule(versionItem!, $"The version {version} is there twice.", "a version has one entry in a package's file"));
            }

            var status = ReadStatus(statusItem, diagnostics);
            var source = sourceItem.Parse<RepositoryRef>(RepositoryRef.TryParse, diagnostics);
            if (source is not null && !source.BelongsTo(package))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.RepositoryOwnerMismatch,
                    $"The source \"{source}\" does not belong to \"{package}\".",
                    $"a package is released from a repository owned by its scope, \"{package.Scope}\"",
                    KeeperFix,
                    sourceItem!.Location));
            }

            var sha256 = sha256Item.Parse<Sha256Digest>(Sha256Digest.TryParse, diagnostics);
            var size = ReadSize(sizeItem, diagnostics);
            var verified = ReadVerified(verifiedItem, diagnostics);
            var published = ReadTime(publishedItem, diagnostics);
            var indexed = ReadTime(indexedItem, diagnostics);
            var dependencies = ReadDependencies(dependenciesItem, package, diagnostics);
            var quarantined = quarantinedItem is null ? null : ReadQuarantine(quarantinedItem, diagnostics);

            // Said of the key and not of what was made of it: a quarantine that is there and could
            // not be read has been reported already, and is not also missing.
            var saysQuarantine = item.Members.Exists(member => string.Equals(member.Name, "quarantined", StringComparison.Ordinal));
            if (status == VersionStatus.Quarantined && !saysQuarantine)
            {
                diagnostics.Add(Rule(
                    statusItem!,
                    "The version is quarantined, and its \"quarantined\" is missing.",
                    "a quarantined version says when its bytes were seen to have changed, and what they then were"));
            }
            else if (status is not null && status != VersionStatus.Quarantined && quarantinedItem is not null)
            {
                diagnostics.Add(Rule(
                    quarantinedItem,
                    "The version says when it was quarantined, and its status is not \"quarantined\".",
                    $"its status is \"{RegistryStatuses.Name(status.Value)}\", and only a quarantined version has a \"quarantined\""));
            }

            if (version is null || status is null || source is null || sha256 is null || size is null || verified is null
                || immutableItem is null || published is null || indexed is null || dependencies is null
                || (status == VersionStatus.Quarantined && quarantined is null))
            {
                return null;
            }

            return new RegistryVersion(
                version,
                status.Value,
                source,
                sha256,
                size.Value,
                verified.Value,
                immutableItem.IsTrue,
                published.Value,
                indexed.Value,
                dependencies,
                status == VersionStatus.Quarantined ? quarantined : null);
        }

        private static VersionStatus? ReadStatus(JsonItem? item, DiagnosticList diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            if (RegistryStatuses.TryParse(item.Text, out var status))
            {
                return status;
            }

            diagnostics.Add(Rule(item, $"\"{item.Text}\" is not a status.", "a version is \"active\", \"yanked\" or \"quarantined\""));
            return null;
        }

        private static int? ReadSize(JsonItem? item, DiagnosticList diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            if (int.TryParse(item.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size is >= 1 and <= PackageArchive.MaxBytes)
            {
                return size;
            }

            diagnostics.Add(Rule(item, $"{item.Text} is not the size of an archive.", $"an archive is a whole number of bytes, from 1 to {PackageArchive.MaxBytes}"));
            return null;
        }

        private static VerificationLevel? ReadVerified(JsonItem? item, DiagnosticList diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            if (VerificationLevels.TryParse(item.Text, out var level))
            {
                return level;
            }

            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.VerifiedInvalid,
                $"\"{item.Text}\" is not a level of verification.",
                "a version is verified by checksum or by attestation",
                KeeperFix,
                item.Location));
            return null;
        }

        private static DateTimeOffset? ReadTime(JsonItem? item, DiagnosticList diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            if (RegistryTime.TryParse(item.Text, out var time))
            {
                return time;
            }

            diagnostics.Add(Rule(item, $"\"{item.Text}\" is not a time as a registry writes one.", $"a time is written in UTC, to the second, as {RegistryTime.Example}"));
            return null;
        }

        private static RegistryQuarantine? ReadQuarantine(JsonItem item, DiagnosticList diagnostics)
        {
            var reader = new JsonObjectReader(item, diagnostics);
            var seenItem = reader.Required("seen", JsonKind.String, $"\"seen\": \"{RegistryTime.Example}\"");
            var sha256Item = reader.Required("sha256", JsonKind.String, "\"sha256\": \"<64 hexadecimal digits>\"");

            var seen = ReadTime(seenItem, diagnostics);
            var sha256 = sha256Item.Parse<Sha256Digest>(Sha256Digest.TryParse, diagnostics);
            return seen is null || sha256 is null ? null : new RegistryQuarantine(seen.Value, sha256);
        }

        // Each key is a package and each value a requirement, as a manifest's own table has them.
        private static List<Dependency>? ReadDependencies(JsonItem? item, PackageName package, DiagnosticList diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            var dependencies = new List<Dependency>();
            foreach (var member in item.Members)
            {
                PackageName? name = null;
                if (PackageName.TryParse(member.Name, out var parsed, out var notName))
                {
                    name = parsed;
                }
                else
                {
                    diagnostics.Add(notName with { Location = member.NameLocation });
                }

                VersionRequirement? requirement = null;
                if (member.Value.Kind != JsonKind.String)
                {
                    diagnostics.Add(JsonDiagnostics.WrongType($"The requirement on \"{member.Name}\"", JsonKind.String, member.Value, removable: false));
                }
                else
                {
                    requirement = member.Value.Parse<VersionRequirement>(VersionRequirement.TryParse, diagnostics);
                }

                if (name is not null && name == package)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DependencySelf,
                        $"\"{package}\" is said to need itself.",
                        "a package does not depend on itself",
                        KeeperFix,
                        member.NameLocation));
                }
                else if (name is not null && requirement is not null)
                {
                    dependencies.Add(new Dependency(name, requirement));
                }
            }

            return dependencies;
        }

        private static Diagnostic Rule(JsonItem at, string message, string reason) =>
            new(DiagnosticCodes.RegistryInvalid, message, reason, KeeperFix, at.Location);
    }
}
