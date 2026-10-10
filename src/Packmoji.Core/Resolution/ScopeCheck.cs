using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// Holds what a project asks for, and what its lockfile holds, to a limit on scopes, before
    /// anything is done with either file. The resolver holds what a project comes to need. This holds
    /// what is already written down.
    /// </summary>
    public static class ScopeCheck
    {
        /// <summary>
        /// The problem for each package of a scope that is not allowed, each once and in order of
        /// name: those the manifest asks for, and those that only the lockfile holds. No more than a
        /// hundred are listed and the rest are counted, since both files may be a stranger's and a
        /// lockfile has room for thousands of packages.
        /// </summary>
        /// <param name="lockfile">The project's lockfile, or null when it has none.</param>
        /// <param name="omitted">How many more there are than the hundred that are listed.</param>
        public static IReadOnlyList<Diagnostic> Outside(ScopeLimit allowed, Manifest manifest, Lockfile? lockfile, out int omitted)
        {
            ArgumentNullException.ThrowIfNull(allowed);
            ArgumentNullException.ThrowIfNull(manifest);
            var met = new SortedDictionary<PackageName, string>();
            foreach (var locked in lockfile?.Packages ?? [])
            {
                met[locked.Name] = $"{LockfileReader.FileName} holds it";
            }

            // Said of the manifest when both name it: the manifest is what a person changes.
            foreach (var asked in (manifest.Dependencies ?? []).Concat(manifest.DevDependencies ?? []))
            {
                met[asked.Name] = $"{ManifestReader.FileName} asks for it";
            }

            var refused = new DiagnosticList();
            foreach (var (name, where) in met.Where(package => !allowed.Allows(package.Key)))
            {
                refused.Add(() => allowed.Refuses(name, where));
            }

            omitted = refused.Omitted;
            return refused;
        }
    }
}
