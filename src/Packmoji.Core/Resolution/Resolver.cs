using Packmoji.Core.Diagnostics;
using Packmoji.Core.Graphs;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// Turns what a manifest asks for into the exact packages a build uses, by minimal version
    /// selection: every requirement is a minimum, and the version used of each package is the highest
    /// minimum anyone asked for, never anything newer. So a resolution does not change because
    /// something new was published, and it needs no list of what versions exist.
    /// </summary>
    /// <remarks>
    /// It reads no file, opens no connection and asks no clock. What it needs to know of published
    /// packages it asks of the <see cref="IPackageSource"/> it is given.
    /// </remarks>
    public static class Resolver
    {
        /// <param name="manifest">The project's manifest, as <see cref="ManifestReader"/> gives one.</param>
        /// <param name="existing">The lockfile the project already has, or null when it has none.</param>
        public static async Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, IPackageSource source, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(source);

            var graph = await RequirementGraph.BuildAsync(manifest, source, cancellationToken);
            if (graph.Overflow is { } overflow)
            {
                // Nothing else can be said of a graph that was not looked through to its end.
                return ResolveResult.Stopped(ResolveDiagnostics.GraphTooLarge(graph, overflow));
            }

            var selection = Selection.Of(graph, manifest);
            var locked = (existing?.Packages ?? []).ToLookup(package => (package.Name, package.Version));
            var errors = new DiagnosticList();
            var warnings = new DiagnosticList();

            // What stops a resolution is reported in one order, whatever was found first: the order
            // of the passes below, and within each of them by name.
            var asked = (manifest.Dependencies ?? []).Concat(manifest.DevDependencies ?? []).Select(dependency => dependency.Name).ToList();
            foreach (var name in asked.GroupBy(name => name).Where(named => named.Count() > 1).Select(named => named.Key).Order())
            {
                errors.Add(() => ResolveDiagnostics.RootDuplicate(graph.Project, name));
            }

            foreach (var node in graph.Nodes.Where(node => node.Published is null).OrderBy(node => node.Name).ThenBy(node => node.Version))
            {
                errors.Add(() => ResolveDiagnostics.VersionMissing(graph, node));
            }

            foreach (var (node, published) in selection.Used.Where(use => use.Published.Status == VersionStatus.Yanked))
            {
                // What the lockfile already holds goes on being used: a yank must not break a build
                // that worked yesterday. Nothing else may start to use the version.
                if (locked.Contains((published.Name, published.Version)))
                {
                    warnings.Add(() => ResolveDiagnostics.YankedLocked(published.Name, published.Version));
                }
                else
                {
                    errors.Add(() => ResolveDiagnostics.Yanked(graph, node));
                }
            }

            foreach (var (node, _) in selection.Used.Where(use => use.Published.Status == VersionStatus.Quarantined))
            {
                errors.Add(() => ResolveDiagnostics.Quarantined(graph, node));
            }

            foreach (var package in selection.Packages.Where(package => package.Selected is null))
            {
                errors.Add(() => ResolveDiagnostics.LineConflict(graph, package));
            }

            foreach (var named in selection.Packages.GroupBy(package => package.Name.Name, StringComparer.Ordinal))
            {
                if (named.Count() > 1 || named.Key == graph.Project.Name)
                {
                    errors.Add(() => ResolveDiagnostics.NameCollision(graph, named.ToList()));
                }
            }

            foreach (var requirement in graph.OnProject)
            {
                errors.Add(() => ResolveDiagnostics.CycleThroughProject(graph, requirement));
            }

            if (selection.IsComplete)
            {
                var used = selection.Used.ToDictionary(use => use.Published.Name, use => use.Published);
                var circles = CycleFinder.Find(used.ToDictionary(
                    use => use.Key,
                    use => (IReadOnlyList<PackageName>)use.Value.Dependencies.Select(dependency => dependency.Name).ToList()));
                foreach (var circle in circles)
                {
                    errors.Add(() => ResolveDiagnostics.Cycle(circle.Select(name => used[name]).ToList()));
                }
            }

            foreach (var (node, published) in selection.Used.Where(use => !use.Published.Source.BelongsTo(use.Published.Name)))
            {
                errors.Add(() => ResolveDiagnostics.OwnerMismatch(graph, node, published));
            }

            foreach (var (_, published) in selection.Used)
            {
                foreach (var held in locked[(published.Name, published.Version)].Where(held => held.Sha256 != published.Sha256 || held.Source != published.Source))
                {
                    errors.Add(() => ResolveDiagnostics.LockMismatch(held, published));
                }
            }

            return ResolveResult.From(errors, warnings, () => Resolved(selection));
        }

        private static ResolvedGraph Resolved(Selection selection)
        {
            var versions = selection.Used.ToDictionary(use => use.Published.Name, use => use.Published.Version);
            return new ResolvedGraph(selection.Used
                .Select(use => new ResolvedPackage(
                    use.Published,
                    use.Published.Dependencies
                        .Select(dependency => dependency.Name)
                        .Distinct()
                        .Order()
                        .Select(name => new LockedDependency(name, versions[name]))
                        .ToList()))
                .ToList());
        }
    }
}
