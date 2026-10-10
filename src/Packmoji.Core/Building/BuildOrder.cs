using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// The order a lockfile's packages are built in. The compiler reads the interface of everything
    /// a package imports, and of everything those import in their turn, so a package is built after
    /// all it depends on, and has to be shown all of it.
    /// </summary>
    /// <remarks>
    /// Every walk here is a loop. A lockfile may hold ten thousand packages in one chain, and a walk
    /// that called itself for each would run out of stack long before that.
    /// </remarks>
    public static class BuildOrder
    {
        /// <summary>
        /// Every package of a lockfile, each after all it depends on. Among those that could be built
        /// next the first by full name is, so that the order is the same however the lockfile was written.
        /// </summary>
        public static IReadOnlyList<LockedPackage> Of(Lockfile lockfile)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            var held = Held(lockfile);
            var waitingFor = lockfile.Packages.ToDictionary(package => package.Name, package => package.Dependencies.Count);
            var neededBy = lockfile.Packages.ToDictionary(package => package.Name, _ => new List<PackageName>());
            foreach (var package in lockfile.Packages)
            {
                foreach (var needed in package.Dependencies)
                {
                    Find(held, needed.Name, package);
                    neededBy[needed.Name].Add(package.Name);
                }
            }

            var ready = new SortedSet<PackageName>(waitingFor.Where(entry => entry.Value == 0).Select(entry => entry.Key));
            var order = new List<LockedPackage>(lockfile.Packages.Count);
            while (ready.Min is { } next)
            {
                ready.Remove(next);
                order.Add(held[next]);
                foreach (var dependent in neededBy[next])
                {
                    if (--waitingFor[dependent] == 0)
                    {
                        ready.Add(dependent);
                    }
                }
            }

            // A reader refuses a lockfile with a circle in it, so this one was made in code.
            return order.Count == lockfile.Packages.Count
                ? order
                : throw new InvalidOperationException("The lockfile holds packages that depend on one another in a circle, which no lockfile that was read does.");
        }

        /// <summary>Everything a package depends on, directly or through another package, in order of full name.</summary>
        public static IReadOnlyList<LockedPackage> Needs(Lockfile lockfile, LockedPackage package)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            ArgumentNullException.ThrowIfNull(package);
            var held = Held(lockfile);
            var needed = new SortedDictionary<PackageName, LockedPackage>();
            var toLookAt = new Stack<LockedPackage>([package]);
            while (toLookAt.TryPop(out var current))
            {
                foreach (var dependency in current.Dependencies)
                {
                    var found = Find(held, dependency.Name, current);
                    if (needed.TryAdd(found.Name, found))
                    {
                        toLookAt.Push(found);
                    }
                }
            }

            return [.. needed.Values];
        }

        private static Dictionary<PackageName, LockedPackage> Held(Lockfile lockfile) => lockfile.Packages.ToDictionary(package => package.Name);

        // A reader refuses a lockfile that names a package it does not hold, so this one was made in code.
        private static LockedPackage Find(Dictionary<PackageName, LockedPackage> held, PackageName name, LockedPackage askedBy) =>
            held.TryGetValue(name, out var found)
                ? found
                : throw new InvalidOperationException($"The lockfile does not hold \"{name}\", which \"{askedBy.Name}\" depends on, and every lockfile that was read holds what it names.");
    }
}
