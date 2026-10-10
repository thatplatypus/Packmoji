using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Reports
{
    /// <summary>What a new lockfile holds that the one before it did not, said as a person would want it after a command.</summary>
    public static class LockChanges
    {
        /// <summary>
        /// A line for each package that was added, removed or moved to another version, in order of
        /// name: <c>+ name version</c>, <c>- name version</c> and <c>~ name old to new</c>. None when
        /// both hold the same versions.
        /// </summary>
        /// <param name="before">Null when there was no lockfile, and then everything is added.</param>
        public static IReadOnlyList<string> Between(Lockfile? before, Lockfile after) => Of(before, after).Select(change => change.Line).ToList();

        /// <summary>The same changes as data, for a tool, in the same order.</summary>
        /// <param name="before">Null when there was no lockfile, and then everything is added.</param>
        public static IReadOnlyList<LockChange> Of(Lockfile? before, Lockfile after)
        {
            ArgumentNullException.ThrowIfNull(after);
            var was = (before?.Packages ?? []).ToDictionary(package => package.Name, package => package.Version);
            var now = after.Packages.ToDictionary(package => package.Name, package => package.Version);
            var changes = new List<LockChange>();
            foreach (var name in was.Keys.Union(now.Keys).Order())
            {
                var had = was.TryGetValue(name, out var old);
                var has = now.TryGetValue(name, out var current);
                if (had && has && old != current)
                {
                    changes.Add(new LockChange(LockChangeKind.Moved, name, old, current));
                }
                else if (has && !had)
                {
                    changes.Add(new LockChange(LockChangeKind.Added, name, null, current));
                }
                else if (had && !has)
                {
                    changes.Add(new LockChange(LockChangeKind.Removed, name, old, null));
                }
            }

            return changes;
        }
    }
}
