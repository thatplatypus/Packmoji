using Packmoji.Core.Diagnostics;
using Packmoji.Core.Graphs;
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

        public static Diagnostic RootDuplicate(PackageName project, PackageName name) =>
            new(
                DiagnosticCodes.DependencyDuplicate,
                $"\"{name}\" is asked for twice by \"{project}\".",
                "a manifest names a package once, in one of its two tables",
                $"keep one requirement on \"{name}\" in {Manifest}");

        public static Diagnostic VersionMissing(RequirementGraph graph, RequirementGraph.Node node) =>
            new(
                DiagnosticCodes.ResolveVersionMissing,
                $"Version {node.Version} of \"{node.Name}\" was never published.",
                $"a requirement names the exact version a build uses unless something asks for more, and this one is asked for: {graph.ChainTo(node.Via)}",
                node.Via.Asker is { } asker
                    ? $"ask for a later version of \"{asker.Name}\", one that asks for a version of \"{node.Name}\" that was published; {asker.Name} {asker.Version} is published and cannot change, so if there is none, its author has to publish one"
                    : $"ask for a version of \"{node.Name}\" that was published, in {Manifest}");

        public static Diagnostic LockedVersionMissing(LockedPackage locked) =>
            new(
                DiagnosticCodes.ResolveVersionMissing,
                $"Version {locked.Version} of \"{locked.Name}\" is not published.",
                $"{Lock} holds it, and a version that is not published cannot be fetched",
                LockfileReader.RegenerateFix);

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

        public static Diagnostic LockedQuarantined(LockedPackage locked) =>
            Quarantined(locked.Name, locked.Version, $"{Lock} holds it");

        public static Diagnostic LineConflict(RequirementGraph graph, Selection.Package package) =>
            new(
                DiagnosticCodes.ResolveLineConflict,
                $"\"{package.Name}\" is asked for on the compatibility lines {Listed(package.Lines.Select(node => CompatibilityLine.Of(node.Version).ToString()))}.",
                "a build holds one version of a package, and no version is on two lines: "
                    + string.Join("; ", package.Lines.Select(node => $"{CompatibilityLine.Of(node.Version)} is asked for by {graph.ChainTo(node.Via)}")),
                $"raise the minimums that lead to the older line, until every requirement on \"{package.Name}\" is on one line");

        /// <param name="packages">The packages of one bare name, in order of full name. One alone shares its name with the project.</param>
        public static Diagnostic NameCollision(RequirementGraph graph, IReadOnlyList<Selection.Package> packages)
        {
            var bare = packages[0].Name.Name;
            var names = Listed(packages.Select(package => $"\"{package.Name}\""));
            var chains = string.Join("; ", packages.Select(package => graph.ChainTo(package.Nodes[0].Via)));
            return bare == graph.Project.Name
                ? new Diagnostic(
                    DiagnosticCodes.ResolveNameCollision,
                    $"{names} {(packages.Count == 1 ? "has" : "have")} the same name as this project, \"{graph.Project}\".",
                    $"Emojicode imports a package by its bare name, so a project named \"{bare}\" cannot be built with another package of that name: {chains}",
                    "stop depending on what brings it in, or give this project another name")
                : new Diagnostic(
                    DiagnosticCodes.ResolveNameCollision,
                    $"{names} have the same name.",
                    $"Emojicode imports a package by its bare name, so two packages named \"{bare}\" cannot be in one build: {chains}",
                    "depend on only one of them");
        }

        public static Diagnostic CycleThroughProject(RequirementGraph graph, RequirementGraph.Requirement requirement) =>
            new(
                DiagnosticCodes.ResolveCycle,
                $"\"{graph.Project}\" depends on itself.",
                $"a package cannot be built from something that needs the package itself: {graph.ChainTo(requirement)}",
                requirement.Asker is { } asker
                    ? $"stop depending on \"{asker.Name}\", or use a version of it that does not depend on \"{graph.Project}\""
                    : $"remove \"{graph.Project}\" from its own {Manifest}");

        /// <param name="circle">The selected versions of a circle, each depending on the next and the last on the first.</param>
        public static Diagnostic Cycle(IReadOnlyList<PublishedVersion> circle)
        {
            var first = circle[0];
            var closing = circle[^1].Dependencies
                .Where(dependency => dependency.Name == first.Name)
                .OrderBy(dependency => dependency.Requirement.Minimum)
                .ThenBy(dependency => dependency.Requirement.Text, StringComparer.Ordinal)
                .First();
            var steps = Chain.Text(circle.Count + 1, index => index < circle.Count
                ? $"{circle[index].Name}@{circle[index].Version}"
                : $"{first.Name}@{closing.Requirement.Text}");
            return circle.Count == 1
                ? new Diagnostic(
                    DiagnosticCodes.ResolveCycle,
                    $"\"{first.Name}\" depends on itself.",
                    $"Emojicode cannot build a package that needs itself: {steps}",
                    $"ask in {Manifest} for a version of it that does not, if there is one, and tell its author if there is not")
                : new Diagnostic(
                    DiagnosticCodes.ResolveCycle,
                    $"\"{first.Name}\" depends on itself through other packages.",
                    $"Emojicode cannot build packages that need one another in a circle: {steps}",
                    $"ask in {Manifest} for versions of them that do not need one another, if there are any, and tell their authors if there are not");
        }

        public static Diagnostic GraphTooLarge(RequirementGraph graph, RequirementGraph.Requirement last) =>
            new(
                DiagnosticCodes.ResolveGraphTooLarge,
                $"What \"{graph.Project}\" depends on is more than {RequirementGraph.MaxNodes} versions.",
                $"no real project is near that, so pmj stopped looking; the last requirement it followed was {graph.ChainTo(last)}",
                "look at what that chain brings in, and at where pmj gets its package information from");

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
