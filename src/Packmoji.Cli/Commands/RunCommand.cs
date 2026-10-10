using Packmoji.Cli.Building;
using Packmoji.Cli.Output;
using Packmoji.Cli.Projects;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj run</c>: builds an application as <c>pmj build</c> does, and runs it. The program is the
    /// one that speaks on the output, so what pmj has to say of the build goes where problems go, and
    /// pmj ends with the status the program ended with.
    /// </summary>
    internal static class RunCommand
    {
        /// <param name="arguments">What the program is given, each as it was written after the two dashes.</param>
        public static async Task<int> RunAsync(PmjHost host, bool release, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            // The manifest is read here for the project's kind, before the build opens the project.
            // A limit on scopes that cannot be read is said before any file is, as it is by the build.
            var allowed = host.Scopes();
            if (!allowed.Succeeded)
            {
                return DiagnosticPrinter.Report(host, allowed.Diagnostics);
            }

            var manifest = ProjectFiles.ReadManifest(host.WorkingDirectory);
            if (!manifest.Succeeded)
            {
                return DiagnosticPrinter.Report(host, manifest.Diagnostics, manifest.Omitted);
            }

            // Said before anything is built: a library builds, and there would still be nothing to run.
            var package = manifest.Value.Package;
            if (package.Kind != PackageKind.App)
            {
                return DiagnosticPrinter.Report(host, new Diagnostic(
                    DiagnosticCodes.RunNotAnApp,
                    $"\"{package.Name}\" is a library, and has no program to run.",
                    $"{ManifestReader.FileName} gives its kind as \"library\", and pmj run builds an application and runs it",
                    "run pmj build to build the library, or run this in an application that depends on it"));
            }

            var outcome = await Builder.RunAsync(host, new BuildRequest(release, DependenciesOnly: false), host.Error, cancellationToken);
            DiagnosticPrinter.Print(host.Error, outcome.Diagnostics.OrderBy(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToList(), outcome.OmittedDiagnostics);
            // A build that was stopped made nothing of the project, and what stopped it has been said.
            if (outcome.Project is not { } built)
            {
                return ExitStatus.Problem;
            }

            // Run where pmj was run, as if the program had been started by hand: what it reads and writes by a relative path is the person's.
            return await host.Tools.RunAttachedAsync(built.Output, arguments, host.WorkingDirectory, cancellationToken)
                ?? DiagnosticPrinter.Report(host, new Diagnostic(
                    DiagnosticCodes.RunFailed,
                    "The program was built and could not be started.",
                    $"\"{built.Output}\" is there, and this machine would not run it",
                    "check that the compiler and the C++ compiler both build for this machine: a program built for another one cannot be run here"));
        }
    }
}
