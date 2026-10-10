using Packmoji.Cli.Output;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj add</c>: one more package the project asks for. With no requirement it asks for the
    /// latest version there is, written in full, so that what was added is what was seen.
    /// </summary>
    internal static class AddCommand
    {
        private const string Dependencies = "dependencies";
        private const string DevDependencies = "devDependencies";

        /// <param name="package">The package as it was typed: <c>@scope/name</c>, or <c>@scope/name@requirement</c>.</param>
        /// <param name="repository">Where the package lives, when it was said. Null when it was not.</param>
        /// <param name="json">Whether to answer a tool, with one JSON object that says what changed in the lockfile.</param>
        public static async Task<int> RunAsync(PmjHost host, string package, bool dev, string? repository, bool json, CancellationToken cancellationToken)
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

            // Said before its versions are asked for: nothing is asked of GitHub about a package that may not be depended on.
            if (!project.Allowed.Allows(name))
            {
                return reply.Stop(project.Allowed.Refuses(name, "pmj add was given it"));
            }

            // With no requirement there is still something to do for a package that is asked for
            // already, when it is to be moved, or when pmj is told where to look for it.
            var existing = ManifestEditor.Find(project.Manifest, name, out var wasDev);
            var moves = existing is not null && dev && !wasDev;
            if (existing is not null && requirement is null && repository is null && !moves)
            {
                return reply.Stop(new Diagnostic(
                    DiagnosticCodes.DependencyExists,
                    $"\"{name}\" is already a dependency of this project.",
                    $"{ManifestReader.FileName} asks for it at {existing.Requirement}, under \"{(wasDev ? DevDependencies : Dependencies)}\"",
                    $"to ask for another version, name it: pmj add {name}@<version>; to move to the latest on its line, run pmj update {name}"));
            }

            var told = new Dictionary<PackageName, RepositoryRef>();
            if (repository is not null)
            {
                if (!RepositoryRef.TryParse(repository, out var where, out var notOne))
                {
                    return reply.Stop(notOne);
                }

                if (!where.BelongsTo(name))
                {
                    return reply.Stop(new Diagnostic(
                        DiagnosticCodes.RepositoryOwnerMismatch,
                        $"\"{name}\" cannot live in {where}.",
                        $"a package lives in a repository of the owner its scope names, and the scope of \"{name}\" is \"{name.Scope}\"",
                        $"give a repository of {name.Scope}: {RepositoryOption.Name} github.com/{name.Scope}/<repository>"));
                }

                told.Add(name, where);
            }

            // The repository is where this package is looked for first, and a place to look for
            // whatever it needs: what is released beside a package is what it most often depends on.
            var source = project.Source([.. told.Values], told);
            requirement ??= existing?.Requirement;
            if (requirement is null)
            {
                if (await source.ListVersionsAsync(name, cancellationToken) is not { } listed)
                {
                    return reply.Stop(DirectPackageSource.NotFound(name, null, source.LookedIn(name)));
                }

                if (VersionCatalog.Latest(listed.Versions) is not { } latest)
                {
                    return reply.Stop(new Diagnostic(
                        DiagnosticCodes.VersionNoneReleased,
                        $"No version of \"{name}\" has been released that is not a pre-release.",
                        $"{listed.Repository} has released {listed.Versions.Count} of it, each a pre-release, and pmj takes a pre-release only when it is named",
                        $"to use one, name it: pmj add {name}@{listed.Versions[^1]}"));
                }

                VersionRequirement.TryParse(latest.ToString(), out requirement, out _);
            }

            // A package that is asked for already stays in the table it is in, unless it is being moved.
            var asDev = dev || (existing is not null && wasDev);
            var asked = new Dependency(name, requirement!);
            if (existing is null || existing.Requirement.Text != asked.Requirement.Text || wasDev != asDev)
            {
                var changed = ProjectSession.Checked(ManifestEditor.With(project.Manifest, asked, asDev));
                if (!changed.Succeeded)
                {
                    return reply.Stop(changed.Diagnostics, changed.Omitted);
                }

                var said = existing switch
                {
                    null => $"Added {name} {asked.Requirement} to {(asDev ? DevDependencies : Dependencies)}.",
                    _ when wasDev != asDev => $"Moved {name} to {DevDependencies}, asking for {asked.Requirement}.",
                    _ => $"Changed the requirement on {name} from {existing.Requirement} to {asked.Requirement}.",
                };
                return await project.ResolveAsync(changed.Value, source, said, json, cancellationToken);
            }

            // Nothing to change in the manifest. There is still a lockfile to write when the project
            // has none that answers it, or when pmj has just been told where to look.
            if (project.IsLocked && repository is null)
            {
                return project.Unchanged($"{ManifestReader.FileName} already asks for {name} at {existing.Requirement}.", json);
            }

            return await project.ResolveAsync(project.Manifest, source, said: null, json, cancellationToken);
        }
    }
}
