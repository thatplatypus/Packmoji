using System.Text;
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
        public static Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, IPackageSource source, CancellationToken cancellationToken) =>
            ResolveAsync(manifest, existing, source, ScopeLimit.None, cancellationToken);

        /// <param name="manifest">The project's manifest, as <see cref="ManifestReader"/> gives one.</param>
        /// <param name="existing">The lockfile the project already has, or null when it has none.</param>
        /// <param name="allowed">
        /// The scopes that may be depended on. A package of another scope stops the resolution
        /// wherever a requirement leads to it, and the source is never asked for it.
        /// </param>
        public static async Task<ResolveResult> ResolveAsync(Manifest manifest, Lockfile? existing, IPackageSource source, ScopeLimit allowed, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allowed);

            var graph = await RequirementGraph.BuildAsync(manifest, source, allowed, cancellationToken);
            if (graph.Overflow is { } overflow)
            {
                // Nothing else can be said of a graph that was not looked through to its end.
                return ResolveResult.Stopped(ResolveDiagnostics.GraphTooLarge(graph, overflow));
            }

            // Nor of one that holds a package that may not be depended on. What lies behind such a
            // package was never looked at, so whatever else seems wrong with the graph might not be.
            // Each is named once: it is the package that is refused, at whatever version.
            var refused = new DiagnosticList();
            foreach (var node in graph.Nodes.Where(node => node.Refused).OrderBy(node => node.Name).ThenBy(node => node.Version).DistinctBy(node => node.Name))
            {
                refused.Add(ResolveDiagnostics.ScopeNotAllowed(graph, node, allowed));
            }

            if (refused.Count > 0)
            {
                return ResolveResult.From(refused, new DiagnosticList(), null);
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
                errors.Add(() => locked.Contains((node.Name, node.Version))
                    ? ResolveDiagnostics.VersionGone(graph, node)
                    : (source as IExplainsMissing)?.Missing(node.Name, node.Version, graph.ChainTo(node.Via)) ?? ResolveDiagnostics.VersionMissing(graph, node));
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

            // A version that needs the project is a circle only if it is built. One that is passed
            // over for a later version makes none, as it makes none among packages.
            var built = selection.Used.Select(use => use.Node).ToHashSet();
            foreach (var requirement in graph.OnProject.Where(requirement => requirement.Asker is null || built.Contains(requirement.Asker)))
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

            if (errors.Count > 0)
            {
                return ResolveResult.From(errors, warnings, null);
            }

            // The lockfile's reader refuses a file over its limit. A graph can be far inside the limit
            // on versions and still make a lockfile over that one, because what versions depend on
            // grows with the square of their number: and a lockfile that cannot be read back is
            // worse than none, so such a graph is refused here, where something can be said of it.
            var resolved = Resolved(selection);
            if (Encoding.UTF8.GetByteCount(LockfileWriter.Write(resolved.ToLockfile(manifest))) > LockfileReader.MaxBytes)
            {
                return ResolveResult.Stopped(ResolveDiagnostics.LockfileTooLarge(graph));
            }

            return ResolveResult.From(errors, warnings, resolved);
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
