using Packmoji.Core.Graphs;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// Everything a project's requirements lead to: each version that some requirement names as its
    /// minimum, found by asking the package source for one after another until nothing new turns up.
    /// </summary>
    /// <remarks>
    /// Versions are looked at nearest first, and each one's requirements in order of name. So the
    /// requirement that first led to a version is the last step of the shortest chain to it, and of
    /// two chains equally short the one that sorts first; and nothing here depends on the order of a
    /// manifest's tables, of a version's dependencies, or of anything the source does.
    /// </remarks>
    internal sealed class RequirementGraph
    {
        /// <summary>
        /// The most versions a graph may hold. No real graph is near it, and a source that answers
        /// wrongly could otherwise keep a resolution going for ever.
        /// </summary>
        public const int MaxNodes = 10_000;

        private readonly Dictionary<(PackageName Name, SemanticVersion Version), Node> _byVersion = [];
        private readonly List<Node> _nodes = [];

        private RequirementGraph(PackageName project)
        {
            Project = project;
        }

        public PackageName Project { get; }

        /// <summary>Every version that a requirement names, in the order they were found.</summary>
        public IReadOnlyList<Node> Nodes => _nodes;

        public static async Task<RequirementGraph> BuildAsync(Manifest manifest, IPackageSource source, CancellationToken cancellationToken)
        {
            var graph = new RequirementGraph(manifest.Package.Name);
            var waiting = new Queue<Node>();
            graph.Follow(null, (manifest.Dependencies ?? []).Concat(manifest.DevDependencies ?? []), waiting);

            while (waiting.TryDequeue(out var node))
            {
                if (await source.AskAsync(node.Name, node.Version, cancellationToken) is { } published)
                {
                    node.Published = published;
                    graph.Follow(node, published.Dependencies, waiting);
                }
            }

            return graph;
        }

        /// <summary>
        /// How the graph came to a requirement: the project, each version that asked in turn, and
        /// then the package asked for, with the requirement as it was written.
        /// </summary>
        public string ChainTo(Requirement requirement)
        {
            var askers = new List<Node>();
            for (var asker = requirement.Asker; asker is not null; asker = asker.Via.Asker)
            {
                askers.Add(asker);
            }

            askers.Reverse();
            var steps = askers.Count + 2;
            return Chain.Text(steps, index =>
                index == 0 ? Project.ToString()
                : index == steps - 1 ? $"{requirement.Asked.Name}@{requirement.Asked.Requirement.Text}"
                : $"{askers[index - 1].Name}@{askers[index - 1].Version}");
        }

        private void Follow(Node? asker, IEnumerable<Dependency> asked, Queue<Node> waiting)
        {
            var inOrder = asked
                .OrderBy(dependency => dependency.Name)
                .ThenBy(dependency => dependency.Requirement.Minimum)
                .ThenBy(dependency => dependency.Requirement.Text, StringComparer.Ordinal);

            foreach (var dependency in inOrder)
            {
                var key = (dependency.Name, dependency.Requirement.Minimum);
                if (_byVersion.ContainsKey(key))
                {
                    continue;
                }

                var node = new Node(new Requirement(asker, dependency));
                _byVersion.Add(key, node);
                _nodes.Add(node);
                waiting.Enqueue(node);
            }
        }

        /// <summary>One version of one package, which some requirement names as its minimum.</summary>
        internal sealed class Node
        {
            public Node(Requirement via)
            {
                Via = via;
            }

            /// <summary>The requirement that first led here, which is the last step of the chain that is shown for this version.</summary>
            public Requirement Via { get; }

            public PackageName Name => Via.Asked.Name;

            public SemanticVersion Version => Via.Asked.Requirement.Minimum;

            /// <summary>What the source said of this version. Null when it was never published.</summary>
            public PublishedVersion? Published { get; set; }
        }

        /// <summary>One line of a dependency table, and who wrote it.</summary>
        internal sealed class Requirement
        {
            public Requirement(Node? asker, Dependency asked)
            {
                Asker = asker;
                Asked = asked;
            }

            /// <summary>The version whose manifest asks. Null when the project itself asks.</summary>
            public Node? Asker { get; }

            public Dependency Asked { get; }
        }
    }
}
