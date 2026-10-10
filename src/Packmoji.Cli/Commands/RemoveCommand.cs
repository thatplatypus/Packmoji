using Packmoji.Cli.Output;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Commands
{
    /// <summary><c>pmj remove</c>: one package fewer that the project asks for, and a lockfile that holds only what is still needed.</summary>
    internal static class RemoveCommand
    {
        /// <param name="repositories">Repositories to look in as well, each as it was typed.</param>
        /// <param name="json">Whether to answer a tool, with one JSON object that says what changed in the lockfile.</param>
        public static async Task<int> RunAsync(PmjHost host, string package, IReadOnlyList<string> repositories, bool json, CancellationToken cancellationToken)
        {
            var reply = new Reply(host, json);
            var opened = ProjectSession.OpenToFetch(host);
            if (!opened.Succeeded)
            {
                return reply.Stop(opened.Diagnostics, opened.Omitted);
            }

            var project = opened.Value;
            if (!PackageArgument.TryParse(package, out var name, out var requirement, out var invalid))
            {
                return reply.Stop(invalid);
            }

            if (requirement is not null)
            {
                return reply.Stop(CommandDiagnostics.NameAlone("remove", package, name));
            }

            if (ManifestEditor.Without(project.Manifest, name) is not { } without)
            {
                return reply.Stop(CommandDiagnostics.NotADependency(name));
            }

            var alsoLookIn = RepositoryOption.Read(repositories);
            if (!alsoLookIn.Succeeded)
            {
                return reply.Stop(alsoLookIn.Diagnostics);
            }

            var changed = ProjectSession.Checked(without);
            return changed.Succeeded
                ? await project.ResolveAsync(changed.Value, project.Source(alsoLookIn.Value), $"Removed {name}.", json, cancellationToken)
                : reply.Stop(changed.Diagnostics, changed.Omitted);
        }
    }
}
