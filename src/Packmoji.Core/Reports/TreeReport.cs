using System.Text;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Reports
{
    /// <summary>
    /// What <c>pmj tree</c> says: the graph a lockfile holds, drawn for a person and listed for a tool.
    /// </summary>
    public static class TreeReport
    {
        /// <summary>
        /// How many levels the drawing goes down. Every line is indented by its depth, so a chain of
        /// thousands would otherwise take the square of its length to draw.
        /// </summary>
        public const int MaxDepth = 64;

        /// <summary>
        /// The graph as a drawing: the project, and under each package what it depends on, in order of
        /// name. A package is drawn with what it depends on once, where it is first met, so that the
        /// drawing has a line for each requirement and not one for each path through the graph.
        /// </summary>
        public static string Text(Manifest manifest, Lockfile lockfile)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(lockfile);
            var locked = lockfile.Packages.ToDictionary(package => package.Name);
            var text = new StringBuilder().Append(manifest.Package.Name).Append(' ').Append(manifest.Package.Version).Append('\n');

            var drawn = new HashSet<PackageName>();
            var repeated = false;
            var cut = false;
            var levels = new Stack<Level>();
            levels.Push(new Level([.. Named(lockfile.Root.Dependencies, dev: false), .. Named(lockfile.Root.DevDependencies, dev: true)], ""));
            while (levels.TryPeek(out var level))
            {
                if (level.Next == level.Items.Count)
                {
                    levels.Pop();
                    continue;
                }

                var (name, dev) = level.Items[level.Next++];
                var last = level.Next == level.Items.Count;
                if (!locked.TryGetValue(name, out var package))
                {
                    throw new ArgumentException($"The lockfile depends on \"{name}\" and holds no such package.", nameof(lockfile));
                }

                // Decided in this order, so that a package cut off here for depth is still drawn in
                // full if it is met again higher up.
                var hasMore = package.Dependencies.Count > 0;
                var tooDeep = hasMore && levels.Count == MaxDepth;
                var again = hasMore && !tooDeep && !drawn.Add(name);
                repeated |= again;
                cut |= tooDeep;

                text.Append(level.Prefix).Append(last ? "└── " : "├── ").Append(name).Append(' ').Append(package.Version);
                text.Append(dev ? " (dev)" : "").Append(again ? " (*)" : "").Append(tooDeep ? " (...)" : "").Append('\n');
                if (hasMore && !tooDeep && !again)
                {
                    var below = package.Dependencies.Select(dependency => dependency.Name).Order().Select(dependency => (dependency, false)).ToList();
                    levels.Push(new Level(below, level.Prefix + (last ? "    " : "│   ")));
                }
            }

            if (repeated)
            {
                text.Append("\n(*) is shown with what it depends on where it first appears.\n");
            }

            if (cut)
            {
                text.Append($"\n(...) depends on more, below the {MaxDepth} levels shown here. pmj tree --json gives all of it.\n");
            }

            return text.ToString();
        }

        /// <summary>
        /// The graph as one JSON object: the project, what it asks for with the version that answers
        /// each requirement, and every locked package once with what it depends on.
        /// </summary>
        public static string Json(Manifest manifest, Lockfile lockfile)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(lockfile);
            var locked = lockfile.Packages.ToDictionary(package => package.Name);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            ReportJson.WriteOutcome(json, [], 0);

            json.WriteStartObject("project");
            json.WriteString("name", manifest.Package.Name.ToString());
            json.WriteString("version", manifest.Package.Version.ToString());
            json.WriteEndObject();

            WriteAsked(json, "dependencies", lockfile.Root.Dependencies, locked);
            WriteAsked(json, "devDependencies", lockfile.Root.DevDependencies, locked);

            json.WriteStartArray("packages");
            foreach (var package in ReportJson.InOrder(lockfile))
            {
                json.WriteStartObject();
                ReportJson.WritePackage(json, package);
                json.WriteStartArray("dependencies");
                foreach (var dependency in package.Dependencies.OrderBy(dependency => dependency.Name).ThenBy(dependency => dependency.Version))
                {
                    json.WriteStartObject();
                    json.WriteString("name", dependency.Name.ToString());
                    json.WriteString("version", dependency.Version.ToString());
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return json.ToString();
        }

        private static void WriteAsked(CanonicalJsonWriter json, string table, IReadOnlyList<Dependency> asked, Dictionary<PackageName, LockedPackage> locked)
        {
            json.WriteStartArray(table);
            foreach (var dependency in asked.OrderBy(dependency => dependency.Name))
            {
                if (!locked.TryGetValue(dependency.Name, out var package))
                {
                    throw new ArgumentException($"The lockfile depends on \"{dependency.Name}\" and holds no such package.", nameof(locked));
                }

                json.WriteStartObject();
                json.WriteString("name", dependency.Name.ToString());
                json.WriteString("requirement", dependency.Requirement.Text);
                json.WriteString("version", package.Version.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        private static IEnumerable<(PackageName Name, bool Dev)> Named(IReadOnlyList<Dependency> asked, bool dev) =>
            asked.Select(dependency => dependency.Name).Order().Select(name => (name, dev));

        // One package's dependencies, and how far down them the drawing has come.
        private sealed class Level(IReadOnlyList<(PackageName Name, bool Dev)> items, string prefix)
        {
            public IReadOnlyList<(PackageName Name, bool Dev)> Items { get; } = items;

            public string Prefix { get; } = prefix;

            public int Next { get; set; }
        }
    }
}
