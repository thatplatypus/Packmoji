using Packmoji.Cli.Output;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Commands
{
    /// <summary><c>pmj remove</c>: one package fewer that the project asks for, and a lockfile that holds only what is still needed.</summary>
    internal static class RemoveCommand
    {
        public static async Task<int> RunAsync(PmjHost host, string package, CancellationToken cancellationToken)
        {
            var opened = ProjectSession.OpenToFetch(host);
            if (!opened.Succeeded)
            {
                return DiagnosticPrinter.Report(host, opened.Diagnostics, opened.Omitted);
            }

            var project = opened.Value;
            if (!PackageArgument.TryParse(package, out var name, out var requirement, out var invalid))
            {
                return DiagnosticPrinter.Report(host, invalid);
            }

            if (requirement is not null)
            {
                return DiagnosticPrinter.Report(host, CommandDiagnostics.NameAlone("remove", package, name));
            }

            if (ManifestEditor.Without(project.Manifest, name) is not { } without)
            {
                return DiagnosticPrinter.Report(host, CommandDiagnostics.NotADependency(name));
            }

            var changed = ProjectSession.Checked(without);
            return changed.Succeeded
                ? await project.ResolveAsync(changed.Value, project.Source(), $"Removed {name}.", cancellationToken)
                : DiagnosticPrinter.Report(host, changed.Diagnostics, changed.Omitted);
        }
    }
}
