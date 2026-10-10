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
        public static IReadOnlyList<string> Between(Lockfile? before, Lockfile after)
        {
            ArgumentNullException.ThrowIfNull(after);
            var was = (before?.Packages ?? []).ToDictionary(package => package.Name, package => package.Version);
            var now = after.Packages.ToDictionary(package => package.Name, package => package.Version);

            var lines = new List<string>();
            foreach (var name in was.Keys.Union(now.Keys).Order())
            {
                var had = was.TryGetValue(name, out var old);
                var has = now.TryGetValue(name, out var current);
                if (had && has && old != current)
                {
                    lines.Add($"~ {name} {old} to {current}");
                }
                else if (has && !had)
                {
                    lines.Add($"+ {name} {current}");
                }
                else if (had && !has)
                {
                    lines.Add($"- {name} {old}");
                }
            }

            return lines;
        }
    }
}
