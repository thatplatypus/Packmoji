using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;
using Packmoji.GitHub;
using Shouldly;
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

        /// <summary>The repository that holds all three of Grapevine's packages.</summary>
        public const string Grapevine = "github.com/thatplatypus/grapevine";

        public string Root { get; }

        /// <summary>The directory pmj is run in, unless a test says another.</summary>
        public string Work { get; }

        /// <summary>The directory pmj keeps its own files in. It is not there until pmj makes it.</summary>
        public string Home { get; }

        public FakeGitHub GitHub { get; } = new();

        /// <summary>Stands in for the standard output of the next run, for a test of what pmj does when it cannot write.</summary>
        public TextWriter? Output { get; set; }

        /// <summary>When set, the next run is told this is how it may be stopped, in place of the test's own way.</summary>
        public CancellationToken? Stop { get; set; }

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

            var status = await PmjCommandLine.RunAsync(args, host, Stop ?? TestContext.Current.CancellationToken);
            return new Run(status, output.ToString() ?? "", error.ToString());
        }

        /// <summary>
        /// The host that pmj would make of an environment with these variables and no others, working
        /// in <see cref="Work"/> and speaking to the made-up GitHub.
        /// </summary>
        public PmjHost Host(params (string Name, string Value)[] variables) =>
            PmjHost.From(
                name => variables.Where(variable => variable.Name == name).Select(variable => variable.Value).FirstOrDefault(),
                Work,
                new HttpClient(GitHub, disposeHandler: false),
                new StringWriter(),
                new StringWriter());

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

        /// <summary>
        /// A machine whose GitHub has Grapevine as it is: three packages released from one repository,
        /// two of which are not in a repository of their own name.
        /// </summary>
        public static Sandbox WithGrapevine()
        {
            var sandbox = new Sandbox();
            sandbox.Release(Grapevine, "@thatplatypus/crypto", "1.0.0");
            sandbox.Release(Grapevine, "@thatplatypus/deflate", "0.1.0");
            sandbox.Release(Grapevine, "@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");
            return sandbox;
        }

        /// <summary>Makes the working directory an application that depends on what is given, each written <c>@owner/name@1.2</c>, or <c>dev:@owner/name@1.2</c>.</summary>
        public void Project(string name, params string[] dependencies) =>
            Write("packmoji.json", TestPackage.Manifest(name, "0.1.0", "app", null, dependencies));

        /// <summary>Makes the project in the working directory say that every package it uses must be attested.</summary>
        public void RequireAttestation() =>
            Write("packmoji.json", ManifestWriter.Write(Manifest() with { Policy = new PolicySection(RequireAttestation: true) }));

        /// <summary>
        /// Releases a version of a library on the made-up GitHub, as <c>pmj pack</c> and a release
        /// would: the tag, and the one file. The repository is written <c>github.com/owner/repo</c>.
        /// </summary>
        /// <returns>The archive that was released.</returns>
        public byte[] Release(string repository, string name, string version, params string[] dependencies)
        {
            var archive = TestPackage.Archive(name, version, repository, dependencies);
            Upload(repository, name, version, archive);
            return archive;
        }

        /// <summary>Releases these bytes as a version of a package, whatever they are.</summary>
        public void Upload(string repository, string name, string version, byte[] archive)
        {
            PackageName.TryParse(name, out var package, out _).ShouldBeTrue();
            SemanticVersion.TryParse(version, out var parsed, out _).ShouldBeTrue();
            GitHub.Upload(repository["github.com/".Length..], ReleaseTag.For(package!, parsed!), AssetName.For(package!, parsed!), archive, parsed!.IsPrerelease);
        }

        public Manifest Manifest()
        {
            var read = ManifestReader.Read(Read("packmoji.json"));
            read.Diagnostics.Select(diagnostic => diagnostic.Message).ShouldBeEmpty();
            return read.Value!;
        }

        public Lockfile Lockfile()
        {
            var read = LockfileReader.Read(Read("packmoji.lock"));
            read.Diagnostics.Select(diagnostic => diagnostic.Message).ShouldBeEmpty();
            return read.Value!;
        }

        /// <summary>What the lockfile holds, each as <c>@owner/name version in github.com/owner/repo</c>.</summary>
        public IReadOnlyList<string> Locked() =>
            Lockfile().Packages.Select(package => $"{package.Name} {package.Version} in {package.Source}").Order(StringComparer.Ordinal).ToList();

        /// <summary>Every file in the cache, each as a path inside it, with a digest cut to its first eight characters so that a test can be read.</summary>
        public IReadOnlyList<string> Cached()
        {
            var cache = Path.Combine(Home, "cache");
            return Directory.Exists(cache)
                ? Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(cache, file).Replace(Path.DirectorySeparatorChar, '/'))
                    .Select(file => System.Text.RegularExpressions.Regex.Replace(file, "[0-9a-f]{64}", match => match.Value[..8]))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];
        }

        /// <summary>Empties the cache, as on a machine that has never fetched anything.</summary>
        public void ForgetCache()
        {
            if (Directory.Exists(Home))
            {
                Unlock(Home);
                Directory.Delete(Home, recursive: true);
            }
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            Unlock(Root);
            Directory.Delete(Root, recursive: true);
        }

        // What pmj keeps in its cache it marks as not to be written, and that would stop it being deleted on some machines.
        private static void Unlock(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }
}
