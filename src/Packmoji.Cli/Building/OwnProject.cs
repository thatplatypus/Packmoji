using Packmoji.Cli.Projects;
using Packmoji.Core.Archives;
using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Packing;

namespace Packmoji.Cli.Building
{
    /// <summary>The project itself, as a build that is to compile it finds it in its directory.</summary>
    /// <param name="NativeFiles">
    /// The files its manifest gives to be compiled as native code, in order of their paths. Each has
    /// its language, or null when its name ends as neither C nor C++ does.
    /// </param>
    internal sealed record OwnProject(Manifest Manifest, string Directory, IReadOnlyList<(string Path, NativeLanguage? Language)> NativeFiles)
    {
        /// <summary>The project as a problem names it.</summary>
        public string What => $"\"{Manifest.Package.Name}\" {Manifest.Package.Version}";

        public static OwnProject Read(string directory, Manifest manifest)
        {
            // What pmj writes into a project, and the packages kept beside it, are no part of it: the same as when it is packed.
            var patterns = manifest.Native?.Sources ?? [];
            var native = patterns.Count == 0
                ? []
                : FileTree.List(directory, ProjectFiles.NotTheProject)
                    .Select(entry => entry.Path)
                    .Where(path => patterns.Any(pattern => GlobMatcher.IsMatch(pattern, path)))
                    .Order(PathOrder.Instance)
                    .Select(path => (path, NativeSource.LanguageOf(path)))
                    .ToList();
            return new OwnProject(manifest, directory, native);
        }

        /// <summary>The project's main file, or why the compiler cannot be given one.</summary>
        public Diagnostic? NoEntry(out RelativePath? entry)
        {
            bool IsThere(string path) => File.Exists(Path.Combine(Directory, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!EntryConvention.TryResolve(Manifest, IsThere, out entry, out var unresolved))
            {
                return unresolved;
            }

            return IsThere(entry.Value)
                ? null
                : new Diagnostic(
                    DiagnosticCodes.EntryNotFound,
                    $"The entry file \"{entry}\" was not found.",
                    "the manifest names it under \"build\", and the project has no such file",
                    "create it, or set \"entry\" under \"build\" to the project's main file");
        }
    }
}
