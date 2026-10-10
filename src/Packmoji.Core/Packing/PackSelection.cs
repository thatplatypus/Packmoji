using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Packing
{
    /// <summary>
    /// Chooses the files of a package that its archive holds: the manifest, a readme and a license,
    /// and what the manifest's own patterns name. Nothing else in the directory is published, so a
    /// package can keep its tests and its notes beside its sources.
    /// </summary>
    public static class PackSelection
    {
        private const string Fix = "rename it, or change the manifest's patterns so that they do not select it";

        /// <param name="tree">Every file in the package's directory. What pmj itself writes there is the caller's to leave out.</param>
        /// <returns>The paths to pack, in the order the archive holds them, or every reason they cannot be packed.</returns>
        public static ReadResult<IReadOnlyList<RelativePath>> Select(Manifest manifest, IReadOnlyList<TreeEntry> tree)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(tree);

            var patterns = manifest.Sources.Concat(manifest.Native?.Sources ?? []).ToList();
            var headers = (manifest.Native?.IncludeDirs ?? []).Select(directory => GlobPattern.Known(directory.Value + "/**")).ToList();
            var selected = tree
                .Where(entry => IsAlwaysPacked(entry.Path) || patterns.Concat(headers).Any(pattern => GlobMatcher.IsMatch(pattern, entry.Path)))
                .ToList();

            var diagnostics = new DiagnosticList();
            if (selected.Count > PackageArchive.MaxFiles)
            {
                diagnostics.Add(TooLarge($"it would hold more than {PackageArchive.MaxFiles} files"));
            }
            else if (ArchiveRules.TooLarge(selected.Select(entry => (entry.Path, entry.Size)).ToList()) is { } tooLarge)
            {
                diagnostics.Add(TooLarge(tooLarge));
            }

            if (diagnostics.Count > 0)
            {
                return ReadResult<IReadOnlyList<RelativePath>>.Failure(diagnostics);
            }

            var paths = new List<RelativePath>();
            foreach (var entry in selected)
            {
                if (entry.IsLink)
                {
                    diagnostics.Add(Unportable(entry.Path, "it is a symbolic link, and an archive holds only files"));
                }
                else if (RelativePath.TryParse(entry.Path, out var path, out var error))
                {
                    paths.Add(path);
                }
                else
                {
                    diagnostics.Add(Unportable(entry.Path, error.Reason));
                }
            }

            foreach (var problem in ArchiveRules.NameProblems(paths.Select(path => path.Value).ToList()))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.PackUnportable, "The package cannot be packed as it is.", problem, Fix));
            }

            var names = tree.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
            if (!EntryConvention.TryResolve(manifest, names.Contains, out var entryFile, out var noEntry))
            {
                diagnostics.Add(noEntry);
            }
            else if (!names.Contains(entryFile.Value))
            {
                // A file that is not there is another matter than one that no pattern selects, and
                // a pattern would not help it.
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.EntryNotFound,
                    $"The entry file \"{entryFile}\" was not found.",
                    "the manifest names it under \"build\", and there is no such file among those that can be packed",
                    "create it, or set \"entry\" under \"build\" to the package's main file"));
            }
            else if (!selected.Any(entry => entry.Path == entryFile.Value))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PackNothing,
                    $"The entry file \"{entryFile}\" is not among the files that are packed.",
                    "none of the manifest's patterns selects it, so the package would be published without its main file",
                    "add a pattern that selects it to \"sources\" under \"build\""));
            }

            return diagnostics.Count > 0
                ? ReadResult<IReadOnlyList<RelativePath>>.Failure(diagnostics)
                : ReadResult<IReadOnlyList<RelativePath>>.Success(paths.OrderBy(path => path.Value, PathOrder.Instance).ToList());
        }

        // The manifest, and at the package's root anything that begins README or LICENSE.
        private static bool IsAlwaysPacked(string path) =>
            path == ManifestReader.FileName
            || (!path.Contains('/') && (path.StartsWith("README", StringComparison.Ordinal) || path.StartsWith("LICENSE", StringComparison.Ordinal)));

        private static Diagnostic Unportable(string path, string reason) =>
            new(DiagnosticCodes.PackUnportable, $"\"{path}\" cannot be packed.", reason, Fix);

        private static Diagnostic TooLarge(string reason) =>
            new(
                DiagnosticCodes.ArchiveInvalid,
                "The package is too large to pack.",
                reason,
                "narrow the manifest's patterns so that they select only what the package needs to be built");
    }
}
