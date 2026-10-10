using Packmoji.Core.Archives;
using Packmoji.Core.Building;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Packing;

namespace Packmoji.Cli.Building
{
    /// <summary>A locked package as its archive gives it: its own manifest, and its files.</summary>
    internal sealed record PackageSources(LockedPackage Locked, Manifest Manifest, IReadOnlyList<ArchiveFile> Files)
    {
        /// <summary>The package as a problem names it.</summary>
        public string What => $"\"{Locked.Name}\" {Locked.Version}";

        /// <summary>
        /// The files its manifest gives to be compiled as native code, in the order its archive holds
        /// them, which is the order of their paths. Each has its language, or null when its name ends
        /// as neither C nor C++ does.
        /// </summary>
        public IReadOnlyList<(string Path, NativeLanguage? Language)> NativeFiles { get; } =
            Files
                .Select(file => file.Path.Value)
                .Where(path => (Manifest.Native?.Sources ?? []).Any(pattern => GlobMatcher.IsMatch(pattern, path)))
                .Select(path => (path, NativeSource.LanguageOf(path)))
                .ToList();
    }
}
