using Packmoji.Core.Identity;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// Changes what a manifest asks for, and nothing else about it. A manifest names a package once,
    /// in one of its two tables, and every change here keeps it so.
    /// </summary>
    /// <remarks>
    /// The rules that hold across a manifest, such as a package not depending on itself, are the
    /// reader's. What is edited here is written and read again before it is used, so that they are
    /// said once.
    /// </remarks>
    public static class ManifestEditor
    {
        /// <summary>The requirement on a package, and which table it is in. Null when the manifest has none.</summary>
        public static Dependency? Find(Manifest manifest, PackageName name, out bool dev)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(name);
            var development = manifest.DevDependencies?.FirstOrDefault(dependency => dependency.Name == name);
            dev = development is not null;
            return development ?? manifest.Dependencies?.FirstOrDefault(dependency => dependency.Name == name);
        }

        /// <summary>
        /// The manifest asking for a package in the table named. A package already in that table keeps
        /// its place and has its requirement changed. One in the other table is moved.
        /// </summary>
        public static Manifest With(Manifest manifest, Dependency dependency, bool dev)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(dependency);
            var into = dev ? manifest.DevDependencies : manifest.Dependencies;
            var other = dev ? manifest.Dependencies : manifest.DevDependencies;

            var changed = (into ?? []).Any(existing => existing.Name == dependency.Name)
                ? into!.Select(existing => existing.Name == dependency.Name ? dependency : existing).ToList()
                : [.. into ?? [], dependency];
            var rest = Remove(other, dependency.Name);

            return dev
                ? manifest with { Dependencies = rest, DevDependencies = changed }
                : manifest with { Dependencies = changed, DevDependencies = rest };
        }

        /// <summary>The manifest without its requirement on a package, or null when it has none.</summary>
        public static Manifest? Without(Manifest manifest, PackageName name)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(name);
            return Find(manifest, name, out _) is null
                ? null
                : manifest with { Dependencies = Remove(manifest.Dependencies, name), DevDependencies = Remove(manifest.DevDependencies, name) };
        }

        // A table left with nothing in it is a table the manifest does not have, so that removing
        // what was added gives back the file as it was.
        private static IReadOnlyList<Dependency>? Remove(IReadOnlyList<Dependency>? table, PackageName name)
        {
            if (table is null || table.All(dependency => dependency.Name != name))
            {
                return table;
            }

            var rest = table.Where(dependency => dependency.Name != name).ToList();
            return rest.Count == 0 ? null : rest;
        }
    }
}
