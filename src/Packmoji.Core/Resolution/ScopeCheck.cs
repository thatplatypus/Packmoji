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
        /// <remarks>
        /// A lockfile is held only while it answers the manifest. One that no longer does is used for
        /// nothing: a command that needs a lockfile says it is out of date, and a command that
        /// resolves writes another from the manifest, which is held here, through a resolver that
        /// holds the limit too. Were it held, a project whose manifest had been mended could be got
        /// out of its old lockfile by no command at all.
        /// </remarks>
        /// <param name="lockfile">The project's lockfile, or null when it has none.</param>
        /// <param name="omitted">How many more there are than the hundred that are listed.</param>
        public static IReadOnlyList<Diagnostic> Outside(ScopeLimit allowed, Manifest manifest, Lockfile? lockfile, out int omitted)
        {
            ArgumentNullException.ThrowIfNull(allowed);
            ArgumentNullException.ThrowIfNull(manifest);
            var met = new SortedDictionary<PackageName, string>();
            var inForce = lockfile is not null && lockfile.Root.Matches(RootRequirements.From(manifest)) ? lockfile : null;
            foreach (var locked in inForce?.Packages ?? [])
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
