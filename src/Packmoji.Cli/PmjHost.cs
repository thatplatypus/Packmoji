using System.Reflection;
using Packmoji.Core.Direct;
using Packmoji.GitHub;

namespace Packmoji.Cli
{
    /// <summary>
    /// What pmj needs of the machine it runs on: a directory to work in, a place for its cache, a way
    /// to reach releases, and somewhere to write. A test gives it a made-up one of each, and so no
    /// command reads the environment or the clock for itself.
    /// </summary>
    public sealed class PmjHost
    {
        public required string WorkingDirectory { get; init; }

        /// <summary>The directory pmj keeps its own files in. The cache is inside it.</summary>
        public required string HomeDirectory { get; init; }

        public required IReleaseHost Releases { get; init; }

        public required TextWriter Out { get; init; }

        public required TextWriter Error { get; init; }

        /// <summary>The version of pmj, as <c>pmj --version</c> gives it.</summary>
        public static string Version { get; } =
            typeof(PmjHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        /// <summary>The host that the real machine is, as its environment describes it.</summary>
        public static PmjHost FromEnvironment(TextWriter output, TextWriter error) =>
            From(Environment.GetEnvironmentVariable, Directory.GetCurrentDirectory(), new HttpClient(), output, error);

        /// <summary>The host that an environment describes: where the cache is kept, which GitHub is spoken to, and a token for its API.</summary>
        /// <param name="variable">Gives the value of an environment variable, or null when it is not set.</param>
        public static PmjHost From(Func<string, string?> variable, string workingDirectory, HttpClient http, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(variable);
            var home = variable("PACKMOJI_HOME");
            return new PmjHost
            {
                WorkingDirectory = workingDirectory,
                HomeDirectory = string.IsNullOrEmpty(home) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".packmoji") : home,
                Releases = new GitHubReleaseHost(http, Address(variable("PACKMOJI_GITHUB")), Address(variable("PACKMOJI_GITHUB_API")), Set(variable("GITHUB_TOKEN")) ?? Set(variable("GH_TOKEN")), $"pmj/{Version}"),
                Out = output,
                Error = error,
            };
        }

        // A variable that is set to nothing says nothing, and does not hide the one after it.
        private static string? Set(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        // Another address for GitHub or for its API: for a test of the native binary, or a GitHub of one's own.
        private static Uri? Address(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var address) ? address : null;
    }
}
