using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// What minimal version selection makes of a graph: for each package, the highest minimum that
    /// any requirement names, so long as every requirement on it is on one compatibility line. Every
    /// requirement that can be reached counts, whichever version made it, so that the answer cannot
    /// depend on the order anything was looked at in.
    /// </summary>
    internal sealed class Selection
    {
        private Selection(IReadOnlyList<Package> packages, IReadOnlyList<Use> used)
        {
            Packages = packages;
            Used = used;
        }

        /// <summary>Every package that a requirement names, in order of full name.</summary>
        public IReadOnlyList<Package> Packages { get; }

        /// <summary>
        /// Whether every package has a selected version that was published. Until then there is no
        /// one graph of selected versions, and so nothing to look for a circle in.
        /// </summary>
        public bool IsComplete => Packages.All(package => package.Selected?.Published is not null);

        /// <summary>
        /// The packages a build would use, in order of full name: those the project asks for, and
        /// those their selected versions lead to. A package that only a superseded version asks for
        /// has a selected version and is not among these, since nothing that is built depends on it.
        /// </summary>
        public IReadOnlyList<Use> Used { get; }

        public static Selection Of(RequirementGraph graph, Manifest manifest)
        {
            var packages = graph.Nodes
                .GroupBy(node => node.Name)
                .Select(group => new Package(group.Key, group.ToList()))
                .OrderBy(package => package.Name)
                .ToList();
            var byName = packages.ToDictionary(package => package.Name);

            var used = new Dictionary<PackageName, Use>();
            var waiting = new Queue<PackageName>((manifest.Dependencies ?? []).Concat(manifest.DevDependencies ?? []).Select(dependency => dependency.Name));
            while (waiting.TryDequeue(out var name))
            {
                if (used.ContainsKey(name) || !byName.TryGetValue(name, out var package) || package.Selected?.Published is not { } published)
                {
                    continue;
                }

                used.Add(name, new Use(package.Selected, published));
                foreach (var dependency in published.Dependencies)
                {
                    waiting.Enqueue(dependency.Name);
                }
            }

            return new Selection(packages, used.Values.OrderBy(use => use.Published.Name).ToList());
        }

        /// <summary>One package of the graph: every version of it that was asked for, and the one selected.</summary>
        internal sealed class Package
        {
            public Package(PackageName name, IReadOnlyList<RequirementGraph.Node> nodes)
            {
                Name = name;
                Nodes = nodes;
                Lines = nodes
                    .GroupBy(node => CompatibilityLine.Of(node.Version))
                    .Select(line => line.First())
                    .OrderBy(node => node.Version)
                    .ToList();
                Selected = Lines.Count == 1 ? nodes.MaxBy(node => node.Version) : null;
            }

            public PackageName Name { get; }

            /// <summary>The versions asked for, in the order they were found.</summary>
            public IReadOnlyList<RequirementGraph.Node> Nodes { get; }

            /// <summary>
            /// The first version found on each compatibility line that is asked for, from the oldest
            /// line to the newest. A package that can be selected for has one.
            /// </summary>
            public IReadOnlyList<RequirementGraph.Node> Lines { get; }

            /// <summary>
            /// The highest minimum asked for. Null when the requirements are on more than one line,
            /// since no version then answers them all.
            /// </summary>
            public RequirementGraph.Node? Selected { get; }
        }

        /// <summary>A selected version that a build would use, and what the source said of it.</summary>
        internal sealed record Use(RequirementGraph.Node Node, PublishedVersion Published);
    }
}
