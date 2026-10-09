using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// Reads <c>packmoji.json</c>. It is strict, because a format that refuses what it does not know
    /// never has to guess what an old file meant, and it reports every problem it finds in one pass.
    /// </summary>
    public static class ManifestReader
    {
        public const string FileName = "packmoji.json";

        private const string ConflictFix = "resolve the conflict by hand, keeping one side of each marked region";

        public static ReadResult<Manifest> Read(string text, string file = FileName)
        {
            ArgumentNullException.ThrowIfNull(text);
            return Read(Encoding.UTF8.GetBytes(text), file);
        }

        public static ReadResult<Manifest> Read(ReadOnlyMemory<byte> utf8, string file = FileName)
        {
            var diagnostics = new List<Diagnostic>();
            var tree = JsonTreeReader.Read(utf8, file, ConflictFix, diagnostics);
            if (tree is null)
            {
                return ReadResult<Manifest>.Failure(diagnostics);
            }

            if (tree.Kind != JsonKind.Object)
            {
                return ReadResult<Manifest>.Failure([JsonDiagnostics.RootNotObject(tree)]);
            }

            var root = new JsonObjectReader(tree, diagnostics);
            var package = ReadPackage(
                root.Required("package", JsonKind.Object, "\"package\": { \"name\": \"@owner/name\", \"version\": \"0.1.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }"),
                diagnostics);
            var dependencies = ReadDependencies(root.Optional("dependencies", JsonKind.Object), diagnostics);
            var devDependencies = ReadDependencies(root.Optional("devDependencies", JsonKind.Object), diagnostics);
            var build = ReadBuild(root.Optional("build", JsonKind.Object), diagnostics);
            var native = ReadNative(root.Optional("native", JsonKind.Object), diagnostics);
            var policy = ReadPolicy(root.Optional("policy", JsonKind.Object), diagnostics);
            root.Finish();

            CheckAcrossTables(package, dependencies, devDependencies, diagnostics);

            if (diagnostics.Count > 0 || package is null)
            {
                return ReadResult<Manifest>.Failure(diagnostics);
            }

            return ReadResult<Manifest>.Success(new Manifest(
                package,
                dependencies?.Select(dependency => dependency.Value).ToList(),
                devDependencies?.Select(dependency => dependency.Value).ToList(),
                build,
                native,
                policy));
        }

        private static PackageSection? ReadPackage(JsonItem? item, List<Diagnostic> diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            var reader = new JsonObjectReader(item, diagnostics);
            var nameItem = reader.Required("name", JsonKind.String, "\"name\": \"@owner/name\"");
            var versionItem = reader.Required("version", JsonKind.String, "\"version\": \"0.1.0\"");
            var kindItem = reader.Required("kind", JsonKind.String, "\"kind\": \"library\"");
            var emojicodeItem = reader.Required("emojicode", JsonKind.String, "\"emojicode\": \">=1.0.0-beta.2\"");
            var descriptionItem = reader.Optional("description", JsonKind.String);
            var licenseItem = reader.Optional("license", JsonKind.String);
            var repositoryItem = reader.Optional("repository", JsonKind.String);
            reader.Finish();

            var name = nameItem.Parse<PackageName>(PackageName.TryParse, diagnostics);
            var version = versionItem.Parse<SemanticVersion>(SemanticVersion.TryParse, diagnostics);
            var kind = ReadKind(kindItem, diagnostics);
            var emojicode = emojicodeItem.Parse<CompilerRequirement>(CompilerRequirement.TryParse, diagnostics);
            var license = licenseItem.Parse<SpdxExpression>(SpdxExpression.TryParse, diagnostics);
            var repository = repositoryItem.Parse<RepositoryRef>(RepositoryRef.TryParse, diagnostics);

            if (descriptionItem is not null && ManifestRules.CheckDescription(descriptionItem.Text) is { } problem)
            {
                diagnostics.Add(problem with { Location = descriptionItem.Location });
            }

            if (name is not null && repository is not null && repositoryItem is not null && !repository.BelongsTo(name))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.RepositoryOwnerMismatch,
                    $"The repository \"{repository}\" does not belong to \"{name}\".",
                    $"a package's repository must be owned by its scope, \"{name.Scope}\", which is how Packmoji knows who may publish it",
                    $"use a repository owned by \"{name.Scope}\", or put the package under the scope \"{repository.Owner}\"",
                    repositoryItem.Location));
            }

            if (name is null || version is null || kind is null || emojicode is null)
            {
                return null;
            }

            return new PackageSection(name, version, kind.Value, emojicode, descriptionItem?.Text, license, repository);
        }

        private static PackageKind? ReadKind(JsonItem? item, List<Diagnostic> diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            if (PackageKinds.TryParse(item.Text, out var kind))
            {
                return kind;
            }

            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.KindInvalid,
                $"\"{item.Text}\" is not a kind of package.",
                "a package is a library, which others depend on, or an app, which is run",
                "write \"library\" or \"app\"",
                item.Location));
            return null;
        }

        private static List<Located<Dependency>>? ReadDependencies(JsonItem? table, List<Diagnostic> diagnostics)
        {
            if (table is null)
            {
                return null;
            }

            var dependencies = new List<Located<Dependency>>();
            foreach (var member in table.Members)
            {
                PackageName? name = null;
                if (PackageName.TryParse(member.Name, out var parsed, out var nameError))
                {
                    name = parsed;
                }
                else
                {
                    diagnostics.Add(nameError with { Location = member.NameLocation });
                }

                VersionRequirement? requirement = null;
                if (member.Value.Kind == JsonKind.String)
                {
                    requirement = member.Value.Parse<VersionRequirement>(VersionRequirement.TryParse, diagnostics);
                }
                else
                {
                    diagnostics.Add(JsonDiagnostics.WrongType($"The requirement of \"{member.Name}\"", JsonKind.String, member.Value, removable: false));
                }

                if (name is not null && requirement is not null)
                {
                    dependencies.Add(new Located<Dependency>(new Dependency(name, requirement), member.NameLocation));
                }
            }

            return dependencies;
        }

        private static BuildSection? ReadBuild(JsonItem? item, List<Diagnostic> diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            var reader = new JsonObjectReader(item, diagnostics);
            var entryItem = reader.Optional("entry", JsonKind.String);
            var sourcesItem = reader.Optional("sources", JsonKind.Array);
            reader.Finish();

            var entry = entryItem.Parse<RelativePath>(RelativePath.TryParse, diagnostics);
            if (entry is not null && entryItem is not null && !ManifestRules.IsSourceFile(entry.Value))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.EntrySuffix,
                    $"\"{entry}\" is not an Emojicode source file.",
                    "an entry is the package's main file, and its name ends in .emojic or .🍇",
                    "name the file the compiler should be given",
                    entryItem.Location));
            }

            if (sourcesItem is { Items.Count: 0 })
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ListEmpty,
                    "\"sources\" is empty.",
                    "a package with no sources has nothing to build or to pack",
                    "list at least one pattern, or remove \"sources\" to have every Emojicode file under src",
                    sourcesItem.Location));
            }

            return new BuildSection(entry, ReadEach<GlobPattern>(sourcesItem, "sources", GlobPattern.TryParse, diagnostics));
        }

        private static NativeSection? ReadNative(JsonItem? item, List<Diagnostic> diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            var reader = new JsonObjectReader(item, diagnostics);
            var sourcesItem = reader.Optional("sources", JsonKind.Array);
            var includeDirsItem = reader.Optional("includeDirs", JsonKind.Array);
            var linkItem = reader.Optional("link", JsonKind.Array);
            reader.Finish();

            var sources = ReadEach<GlobPattern>(sourcesItem, "sources", GlobPattern.TryParse, diagnostics);
            var includeDirs = ReadEach<RelativePath>(includeDirsItem, "includeDirs", RelativePath.TryParse, diagnostics);

            List<string>? link = null;
            if (linkItem.Strings("link", diagnostics) is { } names)
            {
                link = [];
                foreach (var name in names)
                {
                    if (ManifestRules.CheckLinkName(name.Value) is { } problem)
                    {
                        diagnostics.Add(problem with { Location = name.Location });
                    }
                    else
                    {
                        link.Add(name.Value);
                    }
                }
            }

            return new NativeSection(sources, includeDirs, link);
        }

        private static PolicySection? ReadPolicy(JsonItem? item, List<Diagnostic> diagnostics)
        {
            if (item is null)
            {
                return null;
            }

            var reader = new JsonObjectReader(item, diagnostics);
            var requireAttestation = reader.Optional("requireAttestation", JsonKind.Boolean);
            reader.Finish();

            return new PolicySection(requireAttestation?.IsTrue);
        }

        private static List<T>? ReadEach<T>(JsonItem? array, string key, TryParser<T> parser, List<Diagnostic> diagnostics) where T : class
        {
            if (array.Strings(key, diagnostics) is not { } strings)
            {
                return null;
            }

            var values = new List<T>();
            foreach (var text in strings)
            {
                if (parser(text.Value, out var value, out var error))
                {
                    values.Add(value!);
                }
                else
                {
                    diagnostics.Add(error! with { Location = text.Location });
                }
            }

            return values;
        }

        private static void CheckAcrossTables(
            PackageSection? package,
            List<Located<Dependency>>? dependencies,
            List<Located<Dependency>>? devDependencies,
            List<Diagnostic> diagnostics)
        {
            // The first package seen under each bare name, which is the name Emojicode imports by. The
            // package itself is in the build too, so its own name is taken before any dependency's.
            var byBareName = new Dictionary<string, PackageName>(StringComparer.Ordinal);
            if (package is not null)
            {
                byBareName.Add(package.Name.Name, package.Name);
            }

            foreach (var dependency in (dependencies ?? []).Concat(devDependencies ?? []))
            {
                var name = dependency.Value.Name;
                if (!byBareName.TryGetValue(name.Name, out var first))
                {
                    byBareName.Add(name.Name, name);
                }
                else if (first != name)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DependencyNameCollision,
                        $"\"{first}\" and \"{name}\" have the same name.",
                        $"Emojicode imports a package by its bare name, so two packages named \"{name.Name}\" cannot be in one build",
                        "depend on only one of them",
                        dependency.Location));
                }
                else if (package is not null && name == package.Name)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DependencySelf,
                        $"\"{name}\" depends on itself.",
                        "a package cannot be its own dependency",
                        "remove the entry",
                        dependency.Location));
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DependencyDuplicate,
                        $"\"{name}\" is in both \"dependencies\" and \"devDependencies\".",
                        "a package is either needed to use this one or needed only to develop it",
                        "keep it in \"dependencies\" alone",
                        dependency.Location));
                }
            }
        }
    }
}
