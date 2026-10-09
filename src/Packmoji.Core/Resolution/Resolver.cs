using Packmoji.Core.Diagnostics;
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
            var selection = Selection.Of(graph, manifest);
            var locked = (existing?.Packages ?? []).ToLookup(package => (package.Name, package.Version));
            var errors = new DiagnosticList();
            var warnings = new DiagnosticList();

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
