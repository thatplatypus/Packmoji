using System.Text;
using Xunit;

namespace Packmoji.Indexer.Tests
{
    /// <summary>
    /// A place for one test to run the indexer in: a directory of its own, a GitHub that is made up, a
    /// clock that says what it is told, and what the indexer printed. Nothing a test writes outlasts it.
    /// </summary>
    internal sealed class Desk : IDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 17, 40, TimeSpan.Zero);

        private readonly StringWriter _out = new();
        private readonly StringWriter _error = new();

        public Desk()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pmj-indexer-tests", Guid.NewGuid().ToString("N"))).FullName;
            Registry = Path.Combine(Root, "registry");
            List = Path.Combine(Root, "registry-repositories.txt");
            File.WriteAllText(List, MadeUpGitHub.Grapevine + "\n");
        }

        public string Root { get; }

        /// <summary>The directory the registry is kept in. It is not made until something is written to it.</summary>
        public string Registry { get; }

        /// <summary>The list of repositories, which holds Grapevine's until a test writes another.</summary>
        public string List { get; }

        public MadeUpGitHub GitHub { get; } = new();

        public DateTimeOffset Clock { get; set; } = Now;

        /// <summary>What the last command wrote to its output.</summary>
        public string Out { get; private set; } = "";

        /// <summary>What the last command wrote where problems go.</summary>
        public string Error { get; private set; } = "";

        public IndexerHost Host(string? problem = null) => new()
        {
            Releases = GitHub,
            Problem = problem,
            Out = _out,
            Error = _error,
            Clock = () => Clock,
        };

        public async Task<int> RunAsync(params string[] args)
        {
            _out.GetStringBuilder().Clear();
            _error.GetStringBuilder().Clear();
            var status = await IndexerCommandLine.RunAsync(args, Host(), TestContext.Current.CancellationToken);
            Out = _out.ToString().ReplaceLineEndings("\n");
            Error = _error.ToString().ReplaceLineEndings("\n");
            return status;
        }

        /// <summary>Runs <c>run</c> on this desk's registry and list.</summary>
        public Task<int> IndexAsync() => RunAsync("run", "--registry", Registry, "--repositories", List);

        /// <summary>The files under a directory, each as its path from there with forward slashes, in order.</summary>
        public static string[] Files(string directory) =>
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                : [];

        /// <summary>Writes a file under a directory, making what is above it.</summary>
        public static void Put(string directory, string path, string text)
        {
            var file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string Read(string directory, string path) => File.ReadAllText(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>A copy of a directory and everything under it, in a new place on this desk.</summary>
        public string Copy(string directory, string named)
        {
            var copy = Path.Combine(Root, named);
            foreach (var path in Files(directory))
            {
                Put(copy, path, Read(directory, path));
            }

            return copy;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // What could not be deleted is in the machine's own place for what may be deleted.
            }
        }
    }
}
