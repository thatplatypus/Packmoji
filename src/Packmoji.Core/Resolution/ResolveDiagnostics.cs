using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
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
        private const string Lock = LockfileReader.FileName;

        public static Diagnostic VersionMissing(RequirementGraph graph, RequirementGraph.Node node) =>
            new(
                DiagnosticCodes.ResolveVersionMissing,
                $"Version {node.Version} of \"{node.Name}\" was never published.",
                $"a requirement names the exact version a build uses unless something asks for more, and this one is asked for: {graph.ChainTo(node.Via)}",
                node.Via.Asker is { } asker
                    ? $"ask for a later version of \"{asker.Name}\", one that asks for a version of \"{node.Name}\" that was published; {asker.Name} {asker.Version} is published and cannot change, so if there is none, its author has to publish one"
                    : $"ask for a version of \"{node.Name}\" that was published, in {Manifest}");

        public static Diagnostic Yanked(RequirementGraph graph, RequirementGraph.Node node) =>
            new(
                DiagnosticCodes.ResolveYanked,
                $"Version {node.Version} of \"{node.Name}\" has been yanked.",
                $"its author withdrew it, so nothing may start to use it, and it is the version this build would use: {graph.ChainTo(node.Via)}",
                node.Via.Asker is { } asker
                    ? $"ask for a later version of \"{node.Name}\" in {Manifest}, on the line {CompatibilityLine.Of(node.Version)}, which takes the place of what {asker.Name} {asker.Version} asks for"
                    : $"ask for a later version of \"{node.Name}\" in {Manifest}");

        public static Diagnostic YankedLocked(PackageName name, SemanticVersion version) =>
            new(
                DiagnosticCodes.ResolveYankedLocked,
                $"Version {version} of \"{name}\" has been yanked.",
                $"its author withdrew it; {Lock} already holds it, so this build goes on using it",
                $"move to a later version when you can, by asking for one on the line {CompatibilityLine.Of(version)} in {Manifest}")
            {
                Severity = DiagnosticSeverity.Warning,
            };

        public static Diagnostic Quarantined(RequirementGraph graph, RequirementGraph.Node node) =>
            Quarantined(node.Name, node.Version, $"it is the version this build would use: {graph.ChainTo(node.Via)}");

        public static Diagnostic LineConflict(RequirementGraph graph, Selection.Package package) =>
            new(
                DiagnosticCodes.ResolveLineConflict,
                $"\"{package.Name}\" is asked for on the compatibility lines {Listed(package.Lines.Select(node => CompatibilityLine.Of(node.Version).ToString()))}.",
                "a build holds one version of a package, and no version is on two lines: "
                    + string.Join("; ", package.Lines.Select(node => $"{CompatibilityLine.Of(node.Version)} is asked for by {graph.ChainTo(node.Via)}")),
                $"raise the minimums that lead to the older line, until every requirement on \"{package.Name}\" is on one line");

        public static Diagnostic OwnerMismatch(RequirementGraph graph, RequirementGraph.Node node, PublishedVersion published) =>
            new(
                DiagnosticCodes.RepositoryOwnerMismatch,
                $"The repository \"{published.Source}\" does not belong to \"{node.Name}\".",
                $"a package is fetched from a repository owned by its scope, \"{node.Name.Scope}\", and version {node.Version} is said to be in this one; it is asked for: {graph.ChainTo(node.Via)}",
                "do not build with it: what told pmj where this version lives is wrong");

        public static Diagnostic LockMismatch(LockedPackage locked, PublishedVersion published)
        {
            var differences = new List<string>();
            if (locked.Sha256 != published.Sha256)
            {
                differences.Add($"the lockfile has the digest {locked.Sha256} and what is published has {published.Sha256}");
            }

            if (locked.Source != published.Source)
            {
                differences.Add($"the lockfile has the repository {locked.Source} and what is published is in {published.Source}");
            }

            return new Diagnostic(
                DiagnosticCodes.LockMismatch,
                $"{Lock} does not agree with what is published for \"{published.Name}\" {published.Version}.",
                $"{string.Join(", and ", differences)}; a published version never changes, so one of the two is wrong",
                $"find out which before going on: if {Lock} is as it was committed, the published version has been replaced and must not be used, and if the lockfile was edited, restore it");
        }

        private static Diagnostic Quarantined(PackageName name, SemanticVersion version, string where) =>
            new(
                DiagnosticCodes.ResolveQuarantined,
                $"Version {version} of \"{name}\" is quarantined.",
                $"its bytes changed after it was published, which is what a tampered release looks like, so it must not be used, and {where}",
                $"ask for another version of \"{name}\" on the line {CompatibilityLine.Of(version)} in {Manifest}, and tell its author");

        private static string Listed(IEnumerable<string> items)
        {
            var all = items.ToList();
            return all.Count == 1 ? all[0] : $"{string.Join(", ", all.Take(all.Count - 1))} and {all[^1]}";
        }
    }
}
