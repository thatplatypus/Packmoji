using Packmoji.GitHub;
using Xunit;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>
    /// A machine for one test: a directory to work in, a home for the cache, and a GitHub that is made
    /// up. Nothing a test does reaches the network, and nothing it writes outlasts it.
    /// </summary>
    internal sealed class Sandbox : IDisposable
    {
        public Sandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"));
            Work = Directory.CreateDirectory(Path.Combine(Root, "work")).FullName;
            Home = Path.Combine(Root, "home");
        }

        public string Root { get; }

        /// <summary>The directory pmj is run in, unless a test says another.</summary>
        public string Work { get; }

        /// <summary>The directory pmj keeps its own files in. It is not there until pmj makes it.</summary>
        public string Home { get; }

        public FakeGitHub GitHub { get; } = new();

        /// <summary>Stands in for the standard output of the next run, for a test of what pmj does when it cannot write.</summary>
        public TextWriter? Output { get; set; }

        public Task<Run> RunAsync(params string[] args) => RunInAsync("", args);

        /// <param name="directory">The directory to run in, as a path inside <see cref="Work"/>.</param>
        public async Task<Run> RunInAsync(string directory, params string[] args)
        {
            var output = Output ?? new StringWriter();
            var error = new StringWriter();
            var host = new PmjHost
            {
                WorkingDirectory = PathOf(directory),
                HomeDirectory = Home,
                Releases = new GitHubReleaseHost(new HttpClient(GitHub, disposeHandler: false)),
                Out = output,
                Error = error,
            };

            var status = await PmjCommandLine.RunAsync(args, host, TestContext.Current.CancellationToken);
            return new Run(status, output.ToString() ?? "", error.ToString());
        }

        /// <summary>A path inside <see cref="Work"/>, written with <c>/</c>, as a path on this machine.</summary>
        public string PathOf(string path) => path.Length == 0 ? Work : Path.Combine(Work, path.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(path))!);
            File.WriteAllText(PathOf(path), text);
        }

        public string Read(string path) => File.ReadAllText(PathOf(path));

        public bool Has(string path) => File.Exists(PathOf(path));

        /// <summary>Every file under a directory of <see cref="Work"/>, each as a path inside it written with <c>/</c>, in order.</summary>
        public IReadOnlyList<string> Files(string directory = "") =>
            Directory.Exists(PathOf(directory))
                ? Directory.EnumerateFiles(PathOf(directory), "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(PathOf(directory), file).Replace(Path.DirectorySeparatorChar, '/'))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            // What pmj keeps in its cache it marks as not to be written, and that would stop it being deleted on some machines.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
    }
}
