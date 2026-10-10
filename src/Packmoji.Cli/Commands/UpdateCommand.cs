using Packmoji.Cli.Output;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj update</c>: raises what the project asks for to the latest version on each requirement's
    /// own line. A build never moves to a newer version by itself, so this is how it is moved. Going
    /// to another line is a decision, and is made with <c>pmj add</c>.
    /// </summary>
    internal static class UpdateCommand
    {
        /// <param name="packages">The packages to raise, each as it was typed. Every dependency when none is named.</param>
        /// <param name="dryRun">Whether to say what would change and write nothing.</param>
        /// <param name="repositories">Repositories to look in as well, each as it was typed.</param>
        /// <param name="json">Whether to answer a tool, with one JSON object that says what changed in the lockfile, or would.</param>
        public static async Task<int> RunAsync(PmjHost host, IReadOnlyList<string> packages, bool dryRun, IReadOnlyList<string> repositories, bool json, CancellationToken cancellationToken)
        {
            var reply = new Reply(host, json);
            var opened = ProjectSession.OpenToFetch(host);
            if (!opened.Succeeded)
            {
                return reply.Stop(opened.Diagnostics, opened.Omitted);
            }

            var project = opened.Value;
            var named = new List<PackageName>();
            var refused = new List<Diagnostic>();
            foreach (var given in packages)
            {
                if (!PackageArgument.TryParse(given, out var name, out var requirement, out var invalid))
                {
                    refused.Add(invalid);
                }
                else if (requirement is not null)
                {
                    refused.Add(CommandDiagnostics.NameAlone("update", given, name));
                }
                else if (ManifestEditor.Find(project.Manifest, name, out _) is null)
                {
                    refused.Add(CommandDiagnostics.NotADependency(name));
                }
                else
                {
                    named.Add(name);
                }
            }

            var alsoLookIn = RepositoryOption.Read(repositories);
            refused.AddRange(alsoLookIn.Diagnostics);
            if (refused.Count > 0)
            {
                return reply.Stop(refused);
            }

            var every = (project.Manifest.Dependencies ?? []).Concat(project.Manifest.DevDependencies ?? []).Select(dependency => dependency.Name);
            var source = project.Source(alsoLookIn.Value!);
            var manifest = project.Manifest;
            var raised = new List<string>();
            foreach (var name in (named.Count > 0 ? named : every).Distinct().Order())
            {
                var asked = ManifestEditor.Find(manifest, name, out var dev)!;
                if (await source.ListVersionsAsync(name, cancellationToken) is not { } listed)
                {
                    return reply.Stop(DirectPackageSource.NotFound(name, null, source.LookedIn(name)));
                }

                if (VersionCatalog.LatestOnLine(listed.Versions, asked.Requirement) is { } latest && VersionRequirement.TryParse(latest.ToString(), out var higher, out _))
                {
                    manifest = ManifestEditor.With(manifest, new Dependency(name, higher), dev);
                    raised.Add($"  {name} from {asked.Requirement} to {higher}");
                }
            }

            if (raised.Count == 0 && project.IsLocked)
            {
                return project.Unchanged("Every requirement already asks for the latest version on its line.", json);
            }

            var changed = ProjectSession.Checked(manifest);
            if (!changed.Succeeded)
            {
                return reply.Stop(changed.Diagnostics, changed.Omitted);
            }

            // With nothing raised there is still a lockfile to bring up to the manifest, and nothing to say of the manifest.
            var said = raised.Count == 0
                ? null
                : string.Join(
                    Environment.NewLine,
                    [$"{(dryRun ? "Would raise" : "Raised")} {raised.Count} requirement{(raised.Count == 1 ? "" : "s")} in {ManifestReader.FileName}:", .. raised]);
            return dryRun
                ? await project.PreviewAsync(changed.Value, source, said, json, cancellationToken)
                : await project.ResolveAsync(changed.Value, source, said, json, cancellationToken);
        }
    }
}
