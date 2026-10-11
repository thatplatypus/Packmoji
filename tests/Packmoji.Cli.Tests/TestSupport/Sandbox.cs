using Packmoji.Cli.Building;
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
        // The variables that say where the tools of a build are, which a machine with real tools has set as it needs them.
        private static readonly string[] ToolVariables = ["PATH", "EMOJICODEC", "EMOJICODE_PACKAGES_PATH", "EMOJICODE_INCLUDE", "CXX", "CC", "AR"];

        private readonly bool _realTools;

        public Sandbox()
            : this(realTools: false)
        {
        }

        private Sandbox(bool realTools)
        {
            _realTools = realTools;
            Root = Path.Combine(Path.GetTempPath(), "pmj-tests", Guid.NewGuid().ToString("N"));
            Work = Directory.CreateDirectory(Path.Combine(Root, "work")).FullName;
            Home = Path.Combine(Root, "home");
            Tools = new FakeTools(Variable) { BuiltInPackages = Path.Combine(InstallRoot, "EmojicodePackages") };
            if (realTools)
            {
                foreach (var name in ToolVariables)
                {
                    if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                    {
                        Variables[name] = value;
                    }
                }
            }
            else
            {
                InstallTools();
            }
        }

        /// <summary>
        /// A machine whose compiler and other tools are the real ones of the machine the tests run
        /// on, found as its environment says. Its GitHub is still made up, and its files are its own.
        /// </summary>
        public static Sandbox WithRealTools() => new(realTools: true);

        /// <summary>The repository that holds all three of Grapevine's packages.</summary>
        public const string Grapevine = "github.com/thatplatypus/grapevine";

        public string Root { get; }

        /// <summary>The directory pmj is run in, unless a test says another.</summary>
        public string Work { get; }

        /// <summary>The directory pmj keeps its own files in. It is not there until pmj makes it.</summary>
        public string Home { get; }

        public FakeGitHub GitHub { get; } = new();

        /// <summary>The compiler and the other tools of a build, made up. They are on this machine's <c>PATH</c> from the start.</summary>
        public FakeTools Tools { get; }

        /// <summary>The environment pmj is run in. A test changes it as a person would theirs.</summary>
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

        /// <summary>Every variable pmj asked its environment for, in the order it asked.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Whether this machine is a Mac: pmj is told so, and the made-up linker behaves as the one of macOS does.</summary>
        public bool MacOS { get; set; }

        /// <summary>
        /// Where Emojicode's installer would have put its files on this machine had it been told
        /// nothing. Nothing is there: this machine's Emojicode is where its environment says, until a
        /// test moves it.
        /// </summary>
        public string InstallRoot => _realTools ? "/usr/local" : Path.Combine(Root, "usr-local");

        /// <summary>Where the made-up tools are: the one directory on this machine's <c>PATH</c>.</summary>
        public string ToolsDirectory => Path.Combine(Root, "tools");

        /// <summary>Where the compiler's own packages are, as <c>EMOJICODE_PACKAGES_PATH</c> says.</summary>
        public string StockDirectory => Path.Combine(Root, "stock");

        /// <summary>Where the compiler's headers are, as <c>EMOJICODE_INCLUDE</c> says.</summary>
        public string IncludeDirectory => Path.Combine(Root, "include");

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
            Tools.MacLinker = MacOS;
            Tools.ProgramOutput = output;
            var host = new PmjHost
            {
                WorkingDirectory = PathOf(directory),
                HomeDirectory = Home,
                Releases = new GitHubReleaseHost(new HttpClient(GitHub, disposeHandler: false)),
                Out = output,
                Error = error,
                Variable = Variable,
                Tools = _realTools ? new ProcessToolRunner() : Tools,
                IsMacOS = _realTools ? OperatingSystem.IsMacOS() : MacOS,
                InstallRoot = InstallRoot,
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

        /// <summary>
        /// Puts a made-up tool on this machine, as a file whose first line says which tool it is.
        /// What follows that line is what the tool says of itself: the compiler's banner, or what a C
        /// or C++ compiler prints for <c>--version</c>.
        /// </summary>
        /// <param name="path">Where to put it, as a path inside <see cref="Root"/>.</param>
        /// <returns>The file's full path.</returns>
        public string Tool(string path, string tool, params string[] says)
        {
            var file = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, [$"made-up {tool}", .. says]);
            return file;
        }

        // A machine with Emojicode on it as its installer leaves it, but for where: the compiler and a
        // C and C++ toolchain on the PATH, the six stock packages, and the headers.
        private void InstallTools()
        {
            Tool("tools/emojicodec", "emojicodec", FakeTools.Banner);
            Tool("tools/c++", "c++", "c++ (Made Up) 11.4.0");
            Tool("tools/cc", "cc", "cc (Made Up) 11.4.0");
            Tool("tools/ar", "ar");
            foreach (var stock in new[] { "s", "runtime", "files", "sockets", "json", "testtube" })
            {
                var directory = Directory.CreateDirectory(Path.Combine(StockDirectory, stock)).FullName;
                File.WriteAllLines(Path.Combine(directory, $"lib{stock}.a"), ["made-up archive", $"member {stock}.o", "  made-up object", $"  package {stock}"]);
                if (stock != "runtime")
                {
                    File.WriteAllText(Path.Combine(directory, "🏛"), $"💭 made-up interface of {stock}\n");
                }
            }

            foreach (var header in new[] { "runtime/Runtime.h", "s/String.h", "s/Data.h" })
            {
                var file = Path.Combine(IncludeDirectory, header.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "// made up\n");
            }

            Variables["PATH"] = ToolsDirectory;
            Variables["EMOJICODE_PACKAGES_PATH"] = StockDirectory;
            Variables["EMOJICODE_INCLUDE"] = IncludeDirectory;
        }

        private string? Variable(string name)
        {
            Asked.Add(name);
            return Variables.GetValueOrDefault(name);
        }

        /// <summary>
        /// Puts the sample projects on this machine as a checkout of the repository would, in
        /// <c>hello-pkg</c> under <see cref="Work"/>, and releases its two libraries on the made-up
        /// GitHub as their author would: each packed by <c>pmj pack</c>, and the file released.
        /// </summary>
        public async Task ReleaseTheSampleAsync()
        {
            var sample = Path.Combine(AppContext.BaseDirectory, "samples", "hello-pkg");
            foreach (var file in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(Work, "hello-pkg", Path.GetRelativePath(sample, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }

            foreach (var library in new[] { "words", "greeter" })
            {
                var packed = await RunInAsync($"hello-pkg/{library}", "pack");
                packed.Error.ShouldBeEmpty();
                packed.Status.ShouldBe(0);
                var archive = Directory.GetFiles(PathOf($"hello-pkg/{library}/target"), "*.pmj.tar.gz").ShouldHaveSingleItem();
                Upload("github.com/thatplatypus/packmoji", $"@thatplatypus/hello_{library}", "0.1.0", File.ReadAllBytes(archive));
            }
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

        /// <summary>Makes the working directory an application that depends on what is given, and installs it: the two files, and nothing built.</summary>
        public async Task InstallAsync(string name, params string[] dependencies)
        {
            Project(name, dependencies);
            var run = await RunAsync("install");
            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        /// <summary>
        /// Releases a version of a library whose one source file holds these lines, after an import
        /// of each package it depends on. A line can be a mark that the made-up compiler acts on.
        /// </summary>
        public void ReleaseSource(string name, string version, string source, params string[] dependencies) =>
            Upload(
                "github.com/" + name[1..],
                name,
                version,
                TestPackage.Archive(
                    ("packmoji.json", TestPackage.Manifest(name, version, "library", null, dependencies)),
                    ("src/lib.🍇", TestPackage.Source(name, version, dependencies) + source)));

        /// <summary>The keys a package has been built under, each cut to its first eight characters, in order.</summary>
        /// <param name="name">The package's bare name. Its owner is thatplatypus.</param>
        public IReadOnlyList<string> Keys(string name) =>
            Built().Where(file => file.StartsWith($"thatplatypus/{name}/", StringComparison.Ordinal)).Select(file => file.Split('/')[3]).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The one key a package has been built under, cut to its first eight characters.</summary>
        public string Key(string name) => Keys(name).ShouldHaveSingleItem();

        /// <summary>A file of a built package that pmj keeps, when the package has been built under one key.</summary>
        public string BuiltFile(string name, string file) =>
            Directory.GetFiles(Path.Combine(Home, "built", "thatplatypus", name), file, SearchOption.AllDirectories).ShouldHaveSingleItem();

        /// <summary>Every file on this machine that is neither in a project nor among pmj's own, each with its length: what no command should ever change.</summary>
        public IReadOnlyList<string> Elsewhere() =>
            Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(Work + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !file.StartsWith(Home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(file => $"{Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/')} {new FileInfo(file).Length}")
                .Order(StringComparer.Ordinal)
                .ToList();

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
        public IReadOnlyList<string> Cached() => Kept("cache");

        /// <summary>Every file of every built package pmj keeps, each as a path inside <c>built</c>, with a key cut to its first eight characters.</summary>
        public IReadOnlyList<string> Built() => Kept("built");

        /// <summary>
        /// A text with everything that differs from run to run taken out of it, so that a test can
        /// say what it expects: this machine's own directory becomes <c>~</c>, a digest or a key its
        /// first eight characters, and the name of a directory that is only on its way to its place
        /// loses what was added to make it one of a kind.
        /// </summary>
        public string Plain(string text) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(PlainText.Slashed(text).Replace(PlainText.Slashed(Root), "~"), "[0-9a-f]{64}", match => match.Value[..8]),
                "\\.tmp-[0-9a-f]{32}",
                ".tmp");

        /// <summary>What a made-up tool was given, as one line: the tool, then its arguments, made plain by <see cref="Plain"/>.</summary>
        public string Plain(ToolCall call) => Plain(string.Join(' ', [call.Tool, .. call.Arguments]));

        private IReadOnlyList<string> Kept(string directory)
        {
            var kept = Path.Combine(Home, directory);
            return Directory.Exists(kept)
                ? Directory.EnumerateFiles(kept, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(kept, file).Replace(Path.DirectorySeparatorChar, '/'))
                    .Select(file => System.Text.RegularExpressions.Regex.Replace(file, "[0-9a-f]{64}", match => match.Value[..8]))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];
        }

        /// <summary>Empties the cache, as on a machine that has never fetched anything. Nothing of pmj's is left on it.</summary>
        public void ForgetCache() => Forget(Home);

        /// <summary>Throws away what was downloaded and keeps what was built from it, as someone who deletes <c>cache</c> does.</summary>
        public void ForgetDownloads() => Forget(Path.Combine(Home, "cache"));

        /// <summary>Throws away every built package pmj keeps, as someone who deletes <c>built</c> does.</summary>
        public void ForgetBuilt() => Forget(Path.Combine(Home, "built"));

        /// <summary>
        /// Deletes a directory that pmj wrote, as someone who deletes it does. What pmj keeps it marks
        /// as not to be written, and on Windows that stops a file being deleted until the mark is taken away.
        /// </summary>
        public static void Forget(string directory)
        {
            if (Directory.Exists(directory))
            {
                Unlock(directory);
                Directory.Delete(directory, recursive: true);
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
