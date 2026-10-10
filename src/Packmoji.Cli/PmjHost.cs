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
        public static PmjHost FromEnvironment(TextWriter output, TextWriter error)
        {
            var home = Environment.GetEnvironmentVariable("PACKMOJI_HOME");
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            return new PmjHost
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                HomeDirectory = string.IsNullOrEmpty(home) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".packmoji") : home,
                Releases = new GitHubReleaseHost(new HttpClient(), Address("PACKMOJI_GITHUB"), Address("PACKMOJI_GITHUB_API"), token, $"pmj/{Version}"),
                Out = output,
                Error = error,
            };
        }

        // Another address for GitHub or for its API: for a test of the native binary, or a GitHub of one's own.
        private static Uri? Address(string variable) =>
            Uri.TryCreate(Environment.GetEnvironmentVariable(variable), UriKind.Absolute, out var address) ? address : null;
    }
}
