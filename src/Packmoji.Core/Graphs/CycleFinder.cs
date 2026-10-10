using Packmoji.Core.Identity;

namespace Packmoji.Core.Graphs
{
    /// <summary>
    /// Finds the circles in what packages depend on. Emojicode cannot build packages that import one
    /// another in a circle, so one is an error wherever a graph of packages is met: in a resolution,
    /// and in a lockfile.
    /// </summary>
    /// <remarks>
    /// A graph can hold more circles than could ever be listed, so what is reported is one for each
    /// group of packages that all lead to one another: the shortest circle through the group's first
    /// package by name. Every step is a loop and none a recursion, because a graph can be thousands
    /// of packages deep, and the time taken grows only with the size of the graph.
    /// </remarks>
    internal static class CycleFinder
    {
        /// <param name="dependencies">Each package of the graph, with the packages it depends on. A name that is not itself a package of the graph is passed over.</param>
        /// <returns>
        /// The circles, in the order of their first packages. Each begins at its first package by
        /// name and does not repeat it at the end: the last package depends on the first.
        /// </returns>
        public static IReadOnlyList<IReadOnlyList<PackageName>> Find(IReadOnlyDictionary<PackageName, IReadOnlyList<PackageName>> dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);

            // Packages are numbered in the order of their names, so that the lower of two numbers is
            // always the package that sorts first.
            var names = dependencies.Keys.Order().ToArray();
            var numbers = new Dictionary<PackageName, int>(names.Length);
            for (var number = 0; number < names.Length; number++)
            {
                numbers.Add(names[number], number);
            }

            var edges = new int[names.Length][];
            for (var number = 0; number < names.Length; number++)
            {
                edges[number] = dependencies[names[number]]
                    .Where(numbers.ContainsKey)
                    .Select(name => numbers[name])
                    .Distinct()
                    .Order()
                    .ToArray();
            }

            var groups = Groups(edges);
            var circles = new List<IReadOnlyList<PackageName>>();
            var reported = new HashSet<int>();
            var parents = new int[names.Length];
            for (var first = 0; first < names.Length; first++)
            {
                if (reported.Add(groups[first]) && ShortestCircle(first, edges, groups, parents) is { } circle)
                {
                    circles.Add(circle.Select(number => names[number]).ToList());
                }
            }

            return circles;
        }

        /// <summary>
        /// Gives every package the number of its group, where a group is the packages that all lead
        /// to one another. This is Tarjan's algorithm, with the stack of calls kept by hand.
        /// </summary>
        private static int[] Groups(int[][] edges)
        {
            const int unvisited = -1;
            var count = edges.Length;
            var order = new int[count];
            var lowest = new int[count];
            var groups = new int[count];
            var open = new bool[count];
            Array.Fill(order, unvisited);

            var path = new Stack<int>();
            var calls = new Stack<(int Package, int NextEdge)>();
            var visited = 0;
            var found = 0;

            for (var root = 0; root < count; root++)
            {
                if (order[root] != unvisited)
                {
                    continue;
                }

                calls.Push((root, 0));
                while (calls.TryPop(out var call))
                {
                    var (package, nextEdge) = call;
                    if (nextEdge == 0)
                    {
                        order[package] = lowest[package] = visited++;
                        path.Push(package);
                        open[package] = true;
                    }
                    else
                    {
                        // Back from the dependency that was last gone into.
                        lowest[package] = Math.Min(lowest[package], lowest[edges[package][nextEdge - 1]]);
                    }

                    var wentDeeper = false;
                    for (var edge = nextEdge; edge < edges[package].Length; edge++)
                    {
                        var dependency = edges[package][edge];
                        if (order[dependency] == unvisited)
                        {
                            calls.Push((package, edge + 1));
                            calls.Push((dependency, 0));
                            wentDeeper = true;
                            break;
                        }

                        if (open[dependency])
                        {
                            lowest[package] = Math.Min(lowest[package], order[dependency]);
                        }
                    }

                    if (wentDeeper || lowest[package] != order[package])
                    {
                        continue;
                    }

                    int member;
                    do
                    {
                        member = path.Pop();
                        open[member] = false;
                        groups[member] = found;
                    }
                    while (member != package);

                    found++;
                }
            }

            return groups;
        }

        /// <summary>
        /// The shortest way from a package back to itself without leaving its group, and of two equally
        /// short the one that sorts first. Null when there is none, which is so for a package that is
        /// a group by itself and does not depend on itself.
        /// </summary>
        private static List<int>? ShortestCircle(int first, int[][] edges, int[] groups, int[] parents)
        {
            // Looked through level by level, and each package's dependencies in order: so the first
            // way back that is found is the shortest, and the first by name among the shortest.
            var seen = new HashSet<int> { first };
            var waiting = new Queue<int>();
            waiting.Enqueue(first);
            while (waiting.TryDequeue(out var package))
            {
                foreach (var dependency in edges[package])
                {
                    if (dependency == first)
                    {
                        var circle = new List<int>();
                        for (var step = package; step != first; step = parents[step])
                        {
                            circle.Add(step);
                        }

                        circle.Add(first);
                        circle.Reverse();
                        return circle;
                    }

                    if (groups[dependency] == groups[first] && seen.Add(dependency))
                    {
                        parents[dependency] = package;
                        waiting.Enqueue(dependency);
                    }
                }
            }

            return null;
        }
    }
}
