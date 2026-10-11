using System.Reflection;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.GitHub;

namespace Packmoji.Indexer
{
    /// <summary>
    /// What the indexer needs of the machine it runs on: a way to reach releases, a clock, and
    /// somewhere to write. A test gives it a made-up one of each, so no command reads the environment
    /// or the clock for itself.
    /// </summary>
    public sealed class IndexerHost
    {
        private const string SiteVariable = "PACKMOJI_GITHUB";
        private const string ApiVariable = "PACKMOJI_GITHUB_API";

        private static readonly string[] TokenVariables = ["GITHUB_TOKEN", "GH_TOKEN"];

        public required IReleaseHost Releases { get; init; }

        /// <summary>
        /// Why GitHub cannot be asked as the environment describes it, or null when it can. What the
        /// environment says and cannot be used is never passed over for what is done when it says nothing.
        /// </summary>
        public string? Problem { get; init; }

        public required TextWriter Out { get; init; }

        public required TextWriter Error { get; init; }

        /// <summary>Gives the moment, which a run records of whatever it sees for the first time.</summary>
        public required Func<DateTimeOffset> Clock { get; init; }

        public static string Version { get; } =
            typeof(IndexerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        public static IndexerHost FromEnvironment(TextWriter output, TextWriter error) =>
            From(Environment.GetEnvironmentVariable, new HttpClient(), output, error);

        /// <summary>The host that an environment describes: which GitHub is asked, and a token for its API, which raises its limit.</summary>
        /// <param name="variable">Gives the value of an environment variable, or null when it is not set.</param>
        public static IndexerHost From(Func<string, string?> variable, HttpClient http, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(variable);
            string? problem = null;
            var site = Address(variable, SiteVariable, ref problem);
            var api = Address(variable, ApiVariable, ref problem);
            if (problem is null && (site is null) != (api is null))
            {
                var (set, unset) = site is null ? (ApiVariable, SiteVariable) : (SiteVariable, ApiVariable);
                problem = $"{set} is set and {unset} is not: the two are the addresses of one GitHub.";
            }

            var token = Token(variable, ref problem);
            return new IndexerHost
            {
                Releases = problem is null ? new GitHubReleaseHost(http, site, api, token, $"packmoji-indexer/{Version}") : new NoGitHub(),
                Problem = problem,
                Out = output,
                Error = error,
                Clock = () => DateTimeOffset.UtcNow,
            };
        }

        // Another address for GitHub or for its API, for a test. A variable that is set to nothing
        // says nothing. What one holds is never said again: an address may have a password in it.
        private static Uri? Address(Func<string, string?> variable, string name, ref string? problem)
        {
            var text = variable(name)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var web = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (web && Uri.TryCreate(text, UriKind.Absolute, out var address) && GitHubReleaseHost.CanBeAddress(address))
            {
                return address;
            }

            problem ??= $"{name} is not an address that can be used: it has to begin with http:// or https://, and hold no name, password, query or fragment.";
            return null;
        }

        // The first of the two that is set to something is the token, and space around it is no part of it.
        private static string? Token(Func<string, string?> variable, ref string? problem)
        {
            foreach (var name in TokenVariables)
            {
                var text = variable(name)?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (GitHubReleaseHost.CanBeToken(text))
                {
                    return text;
                }

                problem ??= $"{name} does not hold a token: it has a character that cannot be sent as part of one.";
                return null;
            }

            return null;
        }

        // Stands where GitHub would when the environment could not be used. A command says the problem and asks nothing, so this is never asked.
        private sealed class NoGitHub : IReleaseHost
        {
            public Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("GitHub was asked though the environment could not be used.");

            public Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("GitHub was asked though the environment could not be used.");
        }
    }
}
