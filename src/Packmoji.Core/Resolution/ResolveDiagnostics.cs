using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// The text of everything a resolution can report, in one place, so that what stops a resolution
    /// reads as one voice and each problem says how the graph came to it.
    /// </summary>
    internal static class ResolveDiagnostics
    {
        private const string Manifest = ManifestReader.FileName;

        public static Diagnostic VersionMissing(RequirementGraph graph, RequirementGraph.Node node) =>
            new(
                DiagnosticCodes.ResolveVersionMissing,
                $"Version {node.Version} of \"{node.Name}\" was never published.",
                $"a requirement names the exact version a build uses unless something asks for more, and this one is asked for: {graph.ChainTo(node.Via)}",
                node.Via.Asker is { } asker
                    ? $"ask for a later version of \"{asker.Name}\", one that asks for a version of \"{node.Name}\" that was published; {asker.Name} {asker.Version} is published and cannot change, so if there is none, its author has to publish one"
                    : $"ask for a version of \"{node.Name}\" that was published, in {Manifest}");

        public static Diagnostic LineConflict(RequirementGraph graph, Selection.Package package) =>
            new(
                DiagnosticCodes.ResolveLineConflict,
                $"\"{package.Name}\" is asked for on the compatibility lines {Listed(package.Lines.Select(node => CompatibilityLine.Of(node.Version).ToString()))}.",
                "a build holds one version of a package, and no version is on two lines: "
                    + string.Join("; ", package.Lines.Select(node => $"{CompatibilityLine.Of(node.Version)} is asked for by {graph.ChainTo(node.Via)}")),
                $"raise the minimums that lead to the older line, until every requirement on \"{package.Name}\" is on one line");

        private static string Listed(IEnumerable<string> items)
        {
            var all = items.ToList();
            return all.Count == 1 ? all[0] : $"{string.Join(", ", all.Take(all.Count - 1))} and {all[^1]}";
        }
    }
}
