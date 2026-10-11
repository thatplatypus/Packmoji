using Packmoji.Core.Registry;

namespace Packmoji.Indexer
{
    /// <summary>
    /// <c>run</c>: brings the registry in a directory to its next state, from the releases of the
    /// listed repositories. Nothing is asked of GitHub, and nothing is written, until the list and the
    /// registry as it stands have both been read.
    /// </summary>
    internal static class RunCommand
    {
        public static async Task<int> RunAsync(IndexerHost host, string registry, string repositories, CancellationToken cancellationToken)
        {
            if (host.Problem is { } problem)
            {
                host.Error.WriteLine(problem);
                return ExitStatus.Failure;
            }

            string text;
            try
            {
                text = File.ReadAllText(repositories);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return Stop(host, "The list of repositories cannot be read.", [$"{repositories} is not there, or cannot be opened."]);
            }

            var list = RepositoryList.Read(text, repositories);
            if (!list.Succeeded)
            {
                return Stop(host, "The list of repositories cannot be read.", list.Diagnostics.Select(Said.Of));
            }

            var before = RegistryDirectory.Read(registry);
            if (before.State is null)
            {
                return Stop(host, "The registry cannot be read.", before.Problems);
            }

            var result = await RegistryRun.RunAsync(before.State, list.Value.Repositories, host.Releases, host.Clock(), RegistryLimits.Default, cancellationToken);
            RegistryDirectory.Write(registry, before.State, result.After);

            foreach (var note in result.Notes)
            {
                host.Out.WriteLine(note.Text);
            }

            var packages = result.After.Packages;
            host.Out.WriteLine(
                $"The registry holds {Said.Count(packages.Count, "package")}, {Said.Count(packages.Sum(package => package.Versions.Count), "version")} and {Said.Count(result.After.Refused.Sum(file => file.Releases.Count), "refused release")}.");
            return ExitStatus.Success;
        }

        private static int Stop(IndexerHost host, string what, IEnumerable<string> problems)
        {
            host.Error.WriteLine(what);
            foreach (var problem in problems)
            {
                host.Error.WriteLine("  " + problem);
            }

            return ExitStatus.Failure;
        }
    }
}
