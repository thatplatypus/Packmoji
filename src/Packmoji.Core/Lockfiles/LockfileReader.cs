using System.Globalization;
using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Graphs;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// Reads <c>packmoji.lock</c>, and checks that it agrees with itself. pmj writes this file and no
    /// one edits it, so whatever is wrong with one, the thing to do next is to have it written again,
    /// and that is the fix every diagnostic here gives.
    /// </summary>
    public static class LockfileReader
    {
        public const string FileName = "packmoji.lock";

        /// <summary>
        /// The most bytes a lockfile may be: 4 MiB, which is room for some nine thousand packages. It
        /// is public so that whatever takes a lockfile from a disk can refuse it by its length, before
        /// reading it at all.
        /// </summary>
        public const int MaxBytes = 4_194_304;

        internal const string RegenerateFix = "delete packmoji.lock and run pmj install to write it again";

        public static ReadResult<Lockfile> Read(string text, string file = FileName)
        {
            ArgumentNullException.ThrowIfNull(text);

            // A text is never fewer bytes than it has characters, so one that is far too long is
            // refused without being counted, and none that is too long is ever copied.
            if (text.Length > MaxBytes || Encoding.UTF8.GetByteCount(text) > MaxBytes)
            {
                return TooLarge(file);
            }

            return Read(Encoding.UTF8.GetBytes(text), file);
        }

        public static ReadResult<Lockfile> Read(ReadOnlyMemory<byte> utf8, string file = FileName)
        {
            if (utf8.Length > MaxBytes)
            {
                return TooLarge(file);
            }

            var diagnostics = new DiagnosticList();
            var lockfile = ReadLockfile(utf8, file, diagnostics);
            if (diagnostics.Count > 0 || lockfile is null)
            {
                return ReadResult<Lockfile>.Failure(
                    diagnostics
                        .Select(diagnostic => diagnostic.Code == DiagnosticCodes.LockUnsupportedVersion ? diagnostic : diagnostic with { Fix = RegenerateFix })
                        .ToList(),
                    diagnostics.Omitted);
            }

            return ReadResult<Lockfile>.Success(lockfile);
        }

        private static ReadResult<Lockfile> TooLarge(string file) =>
            ReadResult<Lockfile>.Failure(
                [
                    new Diagnostic(
                        DiagnosticCodes.FileTooLarge,
                        $"\"{file}\" is too large to be a lockfile.",
                        $"a lockfile is at most {MaxBytes} bytes, which is 4 MiB and room for some nine thousand packages",
                        RegenerateFix,
                        new SourceLocation(file, 1, 1)),
                ],
                0);

        private static Lockfile? ReadLockfile(ReadOnlyMemory<byte> utf8, string file, DiagnosticList diagnostics)
        {
            var tree = JsonTreeReader.Read(utf8, file, RegenerateFix, diagnostics);
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
            var versionItem = root.Required("version", JsonKind.Number, "\"version\": 1");
            if (versionItem is not null && versionItem.Text != "1")
            {
                // Another version may have other keys, so nothing else this reader could say about the
                // file would mean anything.
                diagnostics.Clear();
                diagnostics.Add(UnsupportedVersion(versionItem));
                return null;
            }

            var rootItem = root.Required("root", JsonKind.Object, "\"root\": { \"dependencies\": [], \"devDependencies\": [] }");
            var packagesItem = root.Required("packages", JsonKind.Array, "\"packages\": []");
            root.Finish();

            List<Located<Dependency>>? dependencies = null;
            List<Located<Dependency>>? devDependencies = null;
            if (rootItem is not null)
            {
                var rootReader = new JsonObjectReader(rootItem, diagnostics);
                dependencies = ReadRequirements(rootReader.Required("dependencies", JsonKind.Array, "\"dependencies\": []"), "dependencies", diagnostics);
                devDependencies = ReadRequirements(rootReader.Required("devDependencies", JsonKind.Array, "\"devDependencies\": []"), "devDependencies", diagnostics);
                rootReader.Finish();
            }

            var entries = ReadEntries(packagesItem, diagnostics);

            if (diagnostics.Count > 0 || dependencies is null || devDependencies is null || entries is null)
            {
                return null;
            }

            // A lockfile that does not hold together is checked only once every part of it has been
            // read: a part that could not be read would otherwise look like a part that is missing.
            CheckConsistency(dependencies.Concat(devDependencies).ToList(), entries, diagnostics);
            if (diagnostics.Count == 0)
            {
                // A circle is looked for only in a lockfile that otherwise holds together: each package
                // then has one entry, and everything an entry depends on is an entry too.
                CheckCircles(entries, diagnostics);
            }

            if (diagnostics.Count > 0)
            {
                return null;
            }

            return new Lockfile(
                new RootRequirements(
                    dependencies.Select(requirement => requirement.Value).ToList(),
                    devDependencies.Select(requirement => requirement.Value).ToList()),
                entries.Select(entry => entry.Package).ToList());
        }

        private static Diagnostic UnsupportedVersion(JsonItem version)
        {
            var newer = int.TryParse(version.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > Lockfile.FormatVersion;
            return new Diagnostic(
                DiagnosticCodes.LockUnsupportedVersion,
                $"Lockfile version {version.Text} is not supported.",
                $"this pmj reads lockfiles of version {Lockfile.FormatVersion}",
                newer ? "upgrade pmj to a version that reads this lockfile" : RegenerateFix,
                version.Location);
        }

        private static List<Located<Dependency>>? ReadRequirements(JsonItem? array, string key, DiagnosticList diagnostics) =>
            ReadPins<VersionRequirement>(array, key, VersionRequirement.TryParse, diagnostics)
                ?.Select(pin => new Located<Dependency>(new Dependency(pin.Value.Name, pin.Value.Value), pin.Location))
                .ToList();

        // Reads strings of the form "@owner/name@value". A full name begins with an @ and neither a
        // version nor a requirement can hold one, so the last @ is always the one between the two.
        private static List<Located<(PackageName Name, T Value)>>? ReadPins<T>(JsonItem? array, string key, TryParser<T> parser, DiagnosticList diagnostics)
            where T : class
        {
            if (array.Strings(key, diagnostics) is not { } strings)
            {
                return null;
            }

            var pins = new List<Located<(PackageName Name, T Value)>>();
            foreach (var text in strings)
            {
                var at = text.Value.LastIndexOf('@');
                if (at <= 0 || at == text.Value.Length - 1)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PinInvalid,
                        $"\"{text.Value}\" is not a package and a version.",
                        "an entry is a full package name, an @, and a version or a requirement, as in @owner/name@1.0.0",
                        RegenerateFix,
                        text.Location));
                    continue;
                }

                PackageName? name = null;
                if (PackageName.TryParse(text.Value[..at], out var parsedName, out var nameError))
                {
                    name = parsedName;
                }
                else
                {
                    diagnostics.Add(nameError with { Location = text.Location });
                }

                if (!parser(text.Value[(at + 1)..], out var value, out var valueError))
                {
                    diagnostics.Add(valueError! with { Location = text.Location });
                }
                else if (name is not null)
                {
                    pins.Add(new Located<(PackageName Name, T Value)>((name, value!), text.Location));
                }
            }

            return pins;
        }

        private static List<Entry>? ReadEntries(JsonItem? array, DiagnosticList diagnostics)
        {
            if (array is null)
            {
                return null;
            }

            var entries = new List<Entry>();
            foreach (var item in array.Items)
            {
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(JsonDiagnostics.WrongType("An entry of \"packages\"", JsonKind.Object, item, removable: false));
                }
                else if (ReadEntry(item, diagnostics) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static Entry? ReadEntry(JsonItem item, DiagnosticList diagnostics)
        {
            var reader = new JsonObjectReader(item, diagnostics);
            var nameItem = reader.Required("name", JsonKind.String, "\"name\": \"@owner/name\"");
            var versionItem = reader.Required("version", JsonKind.String, "\"version\": \"1.0.0\"");
            var sourceItem = reader.Required("source", JsonKind.String, "\"source\": \"github.com/owner/repo\"");
            var releaseTagItem = reader.Required("releaseTag", JsonKind.String, "\"releaseTag\": \"name-v1.0.0\"");
            var assetItem = reader.Required("asset", JsonKind.String, "\"asset\": \"name-1.0.0.pmj.tar.gz\"");
            var sha256Item = reader.Required("sha256", JsonKind.String, "\"sha256\": \"<64 hexadecimal digits>\"");
            var verifiedItem = reader.Required("verified", JsonKind.String, "\"verified\": \"checksum\"");
            var dependenciesItem = reader.Required("dependencies", JsonKind.Array, "\"dependencies\": []");
            reader.Finish();

            var name = nameItem.Parse<PackageName>(PackageName.TryParse, diagnostics);
            var version = versionItem.Parse<SemanticVersion>(SemanticVersion.TryParse, diagnostics);
            var source = sourceItem.Parse<RepositoryRef>(RepositoryRef.TryParse, diagnostics);
            var sha256 = sha256Item.Parse<Sha256Digest>(Sha256Digest.TryParse, diagnostics);
            var verified = ReadVerified(verifiedItem, diagnostics);
            var pins = ReadPins<SemanticVersion>(dependenciesItem, "dependencies", SemanticVersion.TryParse, diagnostics);

            if (name is not null && source is not null && sourceItem is not null && !source.BelongsTo(name))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.RepositoryOwnerMismatch,
                    $"The source \"{source}\" does not belong to \"{name}\".",
                    $"a package is fetched from a repository owned by its scope, \"{name.Scope}\"",
                    RegenerateFix,
                    sourceItem.Location));
            }

            if (name is not null && version is not null)
            {
                var tag = ReleaseTag.For(name, version);
                if (releaseTagItem is not null && releaseTagItem.Text != tag)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LockTagMismatch,
                        $"The release tag \"{releaseTagItem.Text}\" is not this package's.",
                        $"the tag of {name} {version} is \"{tag}\"",
                        RegenerateFix,
                        releaseTagItem.Location));
                }

                var asset = AssetName.For(name, version);
                if (assetItem is not null && assetItem.Text != asset)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LockAssetMismatch,
                        $"The asset \"{assetItem.Text}\" is not this package's.",
                        $"the asset of {name} {version} is \"{asset}\"",
                        RegenerateFix,
                        assetItem.Location));
                }
            }

            if (name is null || version is null || source is null || sha256 is null || verified is null || pins is null || nameItem is null)
            {
                return null;
            }

            var package = new LockedPackage(
                name,
                version,
                source,
                sha256,
                verified.Value,
                pins.Select(pin => new LockedDependency(pin.Value.Name, pin.Value.Value)).ToList());
            return new Entry(package, nameItem.Location, pins.Select(pin => pin.Location).ToList());
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
                "a package is verified by checksum or by attestation",
                RegenerateFix,
                item.Location));
            return null;
        }

        private static void CheckConsistency(List<Located<Dependency>> requirements, List<Entry> entries, DiagnosticList diagnostics)
        {
            var named = new HashSet<PackageName>();
            foreach (var requirement in requirements)
            {
                if (!named.Add(requirement.Value.Name))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DependencyDuplicate,
                        $"\"{requirement.Value.Name}\" is asked for twice in \"root\".",
                        "a manifest names a package once, in one of its two tables, so a lockfile written from one does too",
                        RegenerateFix,
                        requirement.Location));
                }
            }

            var byName = new Dictionary<PackageName, LockedPackage>();
            var byBareName = new Dictionary<string, PackageName>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var name = entry.Package.Name;
                if (!byName.TryAdd(name, entry.Package))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LockDuplicatePackage,
                        $"\"{name}\" has two entries.",
                        "a build holds one version of a package",
                        RegenerateFix,
                        entry.Location));
                }
                else if (!byBareName.TryAdd(name.Name, name))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LockNameCollision,
                        $"\"{byBareName[name.Name]}\" and \"{name}\" have the same name.",
                        $"Emojicode imports a package by its bare name, so two packages named \"{name.Name}\" cannot be in one build",
                        RegenerateFix,
                        entry.Location));
                }
            }

            foreach (var entry in entries)
            {
                for (var i = 0; i < entry.Package.Dependencies.Count; i++)
                {
                    var dependency = entry.Package.Dependencies[i];
                    string? reason = null;
                    if (dependency.Name == entry.Package.Name)
                    {
                        reason = "a package cannot depend on itself";
                    }
                    else if (!byName.TryGetValue(dependency.Name, out var held))
                    {
                        reason = "no entry in \"packages\" has that name";
                    }
                    else if (held.Version != dependency.Version)
                    {
                        reason = $"the entry for it is at version {held.Version}";
                    }

                    if (reason is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.LockDanglingDependency,
                            $"\"{entry.Package.Name}\" depends on \"{dependency.Name}@{dependency.Version}\", which the lockfile does not hold.",
                            reason,
                            RegenerateFix,
                            entry.PinLocations[i]));
                    }
                }
            }

            foreach (var requirement in requirements)
            {
                var asked = requirement.Value;
                string? reason = null;
                if (!byName.TryGetValue(asked.Name, out var held))
                {
                    reason = "no entry in \"packages\" has that name";
                }
                else if (!asked.Requirement.IsSatisfiedBy(held.Version))
                {
                    reason = $"the entry for it is at version {held.Version}, which is not on the line {asked.Requirement.Line} at or above {asked.Requirement.Minimum}";
                }

                if (reason is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LockRootUnsatisfied,
                        $"Nothing in the lockfile answers \"{asked.Name}@{asked.Requirement}\".",
                        reason,
                        RegenerateFix,
                        requirement.Location));
                }
            }

            var reached = new HashSet<PackageName>();
            var waiting = new Queue<PackageName>(requirements.Select(requirement => requirement.Value.Name));
            while (waiting.TryDequeue(out var name))
            {
                if (byName.TryGetValue(name, out var package) && reached.Add(name))
                {
                    foreach (var dependency in package.Dependencies)
                    {
                        waiting.Enqueue(dependency.Name);
                    }
                }
            }

            foreach (var entry in entries.Where(entry => !reached.Contains(entry.Package.Name)))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.LockUnreachable,
                    $"Nothing leads to \"{entry.Package.Name}\".",
                    "neither the manifest's requirements nor any package they lead to depends on it",
                    RegenerateFix,
                    entry.Location));
            }
        }

        private static void CheckCircles(List<Entry> entries, DiagnosticList diagnostics)
        {
            var byName = entries.ToDictionary(entry => entry.Package.Name);
            var circles = CycleFinder.Find(entries.ToDictionary(
                entry => entry.Package.Name,
                entry => (IReadOnlyList<PackageName>)entry.Package.Dependencies.Select(dependency => dependency.Name).ToList()));

            foreach (var circle in circles)
            {
                // The circle is closed where its last package depends on its first.
                var last = byName[circle[^1]];
                var closing = 0;
                while (last.Package.Dependencies[closing].Name != circle[0])
                {
                    closing++;
                }

                var steps = Chain.Text(circle.Count + 1, index =>
                {
                    var package = byName[circle[index % circle.Count]].Package;
                    return $"{package.Name}@{package.Version}";
                });
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ResolveCycle,
                    $"\"{circle[0]}\" depends on itself through other packages.",
                    $"Emojicode cannot build packages that need one another in a circle: {steps}",
                    RegenerateFix,
                    last.PinLocations[closing]));
            }
        }

        // A package as it was read, with the places needed to say where a later check failed.
        private sealed record Entry(LockedPackage Package, SourceLocation Location, IReadOnlyList<SourceLocation> PinLocations);
    }
}
