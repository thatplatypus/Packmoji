using System.Reflection;
using Packmoji.Cli.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.GitHub;

namespace Packmoji.Cli
{
    /// <summary>
    /// What pmj needs of the machine it runs on: a directory to work in, a place for its cache, a way
    /// to reach releases, a way to start the tools of a build, and somewhere to write. A test gives it
    /// a made-up one of each, and so no command reads the environment or the clock for itself.
    /// </summary>
    public sealed class PmjHost
    {
        private const string SiteVariable = "PACKMOJI_GITHUB";
        private const string ApiVariable = "PACKMOJI_GITHUB_API";

        private static readonly string[] TokenVariables = ["GITHUB_TOKEN", "GH_TOKEN"];

        public required string WorkingDirectory { get; init; }

        /// <summary>The directory pmj keeps its own files in. The cache is inside it.</summary>
        public required string HomeDirectory { get; init; }

        public required IReleaseHost Releases { get; init; }

        public required TextWriter Out { get; init; }

        public required TextWriter Error { get; init; }

        /// <summary>Gives the value of a variable of the environment, or null when it is not set. A build asks it where its tools are.</summary>
        public required Func<string, string?> Variable { get; init; }

        /// <summary>Starts the programs a build needs, and the program it builds.</summary>
        public required IToolRunner Tools { get; init; }

        /// <summary>Whether this is macOS, whose linker is asked for less than any other's.</summary>
        public bool IsMacOS { get; init; } = OperatingSystem.IsMacOS();

        /// <summary>
        /// The directory Emojicode's installer puts its files under when it is told nothing. The
        /// compiler's own packages and its headers are looked for there when the environment does
        /// not say where they are.
        /// </summary>
        public string InstallRoot { get; init; } = "/usr/local";

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
                Releases = GitHub(variable, http),
                Out = output,
                Error = error,
                Variable = variable,
                Tools = new ProcessToolRunner(TokenVariables),
            };
        }

        // GitHub as the environment describes it. What the environment says and pmj cannot use is
        // never passed over for what pmj does when nothing is said: someone who mistypes the address
        // of a GitHub of their own would have their packages looked for, and their token sent, at
        // the other one. The first such thing is kept, and is what a command that needs GitHub is told.
        private static IReleaseHost GitHub(Func<string, string?> variable, HttpClient http)
        {
            Diagnostic? problem = null;
            var site = Address(variable, SiteVariable, ref problem);
            var api = Address(variable, ApiVariable, ref problem);
            if (problem is null && (site is null) != (api is null))
            {
                var (set, unset) = site is null ? (ApiVariable, SiteVariable) : (SiteVariable, ApiVariable);
                problem = new Diagnostic(
                    DiagnosticCodes.ConfigInvalid,
                    $"{set} is set and {unset} is not.",
                    "with one of them alone, pmj would download releases from one GitHub and list versions from another",
                    "set both, or neither: the two are the addresses of one GitHub");
            }

            var token = Token(variable, ref problem);
            return problem is null ? new GitHubReleaseHost(http, site, api, token, $"pmj/{Version}") : new UnusableReleaseHost(problem);
        }

        // Another address for GitHub or for its API: for a test of the native binary, or a GitHub of
        // one's own. A variable that is set to nothing says nothing.
        private static Uri? Address(Func<string, string?> variable, string name, ref Diagnostic? problem)
        {
            var text = variable(name)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            // Asked of the text, for .NET reads "localhost:8123" as an address whose scheme is "localhost", and a path as a file's.
            var web = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var read = Uri.TryCreate(text, UriKind.Absolute, out var address);
            if (web && read && GitHubReleaseHost.CanBeAddress(address!))
            {
                return address;
            }

            // What it holds is not said again: an address that was mistyped may have a password in it.
            problem ??= new Diagnostic(
                DiagnosticCodes.ConfigInvalid,
                $"{name} is not an address pmj can use.",
                !web ? "it does not begin with http:// or https://"
                : !read ? "it cannot be read as an address"
                : "it holds something other than the name of a machine and a path: a name and a password, a query, or a fragment",
                $"set it to an address such as https://github.example.com, or unset it: without {SiteVariable} and {ApiVariable}, pmj speaks to github.com");
            return null;
        }

        // The first of the two that is set to something is the token. One that is set and cannot be
        // used is a problem, and is not passed over for the one after it.
        private static string? Token(Func<string, string?> variable, ref Diagnostic? problem)
        {
            foreach (var name in TokenVariables)
            {
                // Space and line ends around a token are how it came to be in the variable, and no part of it.
                var text = variable(name)?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (GitHubReleaseHost.CanBeToken(text))
                {
                    return text;
                }

                problem ??= new Diagnostic(
                    DiagnosticCodes.ConfigInvalid,
                    $"{name} does not hold a token.",
                    "it holds a character that cannot be sent as part of a token: a space, a line end, or one that is not ASCII",
                    "set it to the token alone, as GitHub gave it, or unset it: a public repository's releases can be listed with none");
                return null;
            }

            return null;
        }
    }
}
