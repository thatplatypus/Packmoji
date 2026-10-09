using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// Finds a package's main file, the one file the compiler is given. A manifest may name it; one
    /// that does not is taken to follow the convention. The disk is reached only through the function
    /// passed in, so that the rule can be tested, and used, without one.
    /// </summary>
    public static class EntryConvention
    {
        /// <summary>The files looked for when a manifest names no entry, in the order they are preferred.</summary>
        public static IReadOnlyList<string> Candidates(PackageKind kind)
        {
            var stem = kind == PackageKind.App ? "src/main" : "src/lib";
            return ManifestRules.SourceSuffixes.Select(suffix => stem + suffix).ToList();
        }

        /// <param name="fileExists">Says whether a path, relative to the package's directory, is a file.</param>
        public static bool TryResolve(
            Manifest manifest,
            Func<string, bool> fileExists,
            [NotNullWhen(true)] out RelativePath? entry,
            [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(fileExists);
            error = null;

            entry = manifest.Build?.Entry;
            if (entry is not null)
            {
                return true;
            }

            var candidates = Candidates(manifest.Package.Kind);
            var present = candidates.Where(fileExists).ToList();
            if (present.Count == 1)
            {
                entry = RelativePath.Known(present[0]);
                return true;
            }

            var named = string.Join(" and ", candidates.Select(candidate => $"\"{candidate}\""));
            error = present.Count == 0
                ? new Diagnostic(
                    DiagnosticCodes.EntryNotFound,
                    "The package has no entry file.",
                    $"the manifest names none, and neither of {named} exists",
                    "create one of them, or set \"entry\" under \"build\" to the package's main file")
                : new Diagnostic(
                    DiagnosticCodes.EntryAmbiguous,
                    "The package has two possible entry files.",
                    $"the manifest names none, and both of {named} exist",
                    "set \"entry\" under \"build\" to the one the compiler should be given");
            return false;
        }
    }
}
