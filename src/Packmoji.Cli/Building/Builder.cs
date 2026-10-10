using Packmoji.Cli.Commands;
using Packmoji.Cli.Projects;
using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Building
{
    /// <summary>
    /// One build, from the two files of a project to what is in its directories at the end. It knows
    /// the order of things and where each is kept, and nothing of the compiler: no flag and no name
    /// of a file the compiler writes. Those are the driver's.
    /// </summary>
    internal sealed class Builder
    {
        // The directory under target that holds what is made on the way to what is built.
        private const string Intermediate = "obj";

        private readonly PmjHost _host;
        private readonly BuildRequest _request;
        private readonly TextWriter _said;
        private readonly ICompilerDriver _driver;
        private readonly BuiltStore _store;

        // What a check of the lockfile warned of, which is said whatever becomes of the build.
        private readonly List<Diagnostic> _warnings = [];
        private int _omitted;
        private CompilerIdentity? _compiler;

        private Builder(PmjHost host, BuildRequest request, TextWriter said)
        {
            _host = host;
            _request = request;
            _said = said;
            _driver = new EmojicodecDriver(host);
            _store = new BuiltStore(host.HomeDirectory);
        }

        /// <param name="said">Where what the build does is said, a line at a time, as it does it.</param>
        public static async Task<BuildOutcome> RunAsync(PmjHost host, BuildRequest request, TextWriter said, CancellationToken cancellationToken)
        {
            var builder = new Builder(host, request, said);
            try
            {
                return await builder.BuildAsync(cancellationToken);
            }
            catch (PackageSourceException failure)
            {
                // What could not be fetched, read or kept is a problem to report, as it is for every command.
                return builder.Stopped(failure.Diagnostic);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return builder.Stopped(ProjectFiles.Unreadable(host.WorkingDirectory, "built in", failure));
            }
        }

        private BuildOutcome Stopped(params IEnumerable<Diagnostic> problems) => new([.. problems, .. _warnings], _omitted, _compiler, []);

        private string Mode => _request.Release ? "release" : "debug";

        // The places in the project that this build writes into: where its packages go, where its
        // lock is, and when the project itself is built, the directories of what is made on the way
        // and of what is made. What is at the very place of a thing that is built needs no asking
        // after: it is taken away, and a link that is taken away takes nothing with it.
        private IEnumerable<string> WrittenInto()
        {
            string[] always = [ProjectFiles.Packages, ProjectFiles.Target, ProjectLock.Place];
            return _request.DependenciesOnly ? always : [.. always, $"{ProjectFiles.Target}/{Intermediate}", $"{ProjectFiles.Target}/{Mode}"];
        }

        private async Task<BuildOutcome> BuildAsync(CancellationToken cancellationToken)
        {
            var opened = ProjectSession.OpenToFetch(_host);
            if (!opened.Succeeded)
            {
                _omitted = opened.Omitted;
                return Stopped(opened.Diagnostics);
            }

            // A build chooses no version. A project that asks for no package has nothing to choose, and needs no lockfile to say so.
            var project = opened.Value;
            var asked = RootRequirements.From(project.Manifest);
            var asksForNothing = asked.Dependencies.Count == 0 && asked.DevDependencies.Count == 0;
            if (!project.IsLocked && !(project.Lockfile is null && asksForNothing))
            {
                return Stopped(CommandDiagnostics.NotInstalled(missing: project.Lockfile is null));
            }

            // Asked before the first thing is written, which is the lock.
            if (ProjectFiles.Linked(_host.WorkingDirectory, WrittenInto()) is { } linked)
            {
                return Stopped(linked);
            }

            using var held = await ProjectLock.TakeAsync(
                _host.WorkingDirectory,
                () => _host.Error.WriteLine("Waiting for another pmj that is building this project."),
                cancellationToken);

            if (project.Lockfile is not null)
            {
                var check = await project.FetchLockedAsync([], cancellationToken);
                _omitted = check.OmittedDiagnostics;
                if (!check.Succeeded)
                {
                    return Stopped(check.Diagnostics);
                }

                _warnings.AddRange(check.Diagnostics);
            }

            var lockfile = project.Lockfile ?? new Lockfile(asked, []);
            var order = BuildOrder.Of(lockfile);
            var own = _request.DependenciesOnly ? null : OwnProject.Read(_host.WorkingDirectory, project.Manifest);
            if (order.Count == 0 && own is null)
            {
                // Nothing is locked now, and something may have been before.
                if (PlacedPackages.Place(_host.WorkingDirectory, []) is { } leftBehind)
                {
                    return Stopped(leftBehind);
                }

                _said.WriteLine("Nothing to build: the project depends on no package.");
                return new BuildOutcome(_warnings, _omitted, null, []);
            }

            var identified = await _driver.IdentifyAsync(cancellationToken);
            if (!identified.Succeeded)
            {
                return Stopped(identified.Problem);
            }

            var compiler = _compiler = identified.Value;
            var sources = new Dictionary<PackageName, PackageSources>();
            foreach (var package in order)
            {
                var files = await project.FilesOfAsync(package.Name, package.Version, package.Sha256, cancellationToken);
                var manifest = ManifestReader.Read(files.First(file => file.Path.Value == ManifestReader.FileName).Content);
                sources[package.Name] = manifest.Succeeded
                    ? new PackageSources(package, manifest.Value, files)
                    : throw new PackageSourceException(manifest.Diagnostics[0]);
            }

            // Whatever would stop something being built is found before anything is compiled, and all of it is said.
            RelativePath? ownEntry = null;
            var inOrderOfName = order.OrderBy(package => package.Name).Select(package => sources[package.Name]).ToList();
            var unbuildable = inOrderOfName
                .Where(package => !package.Manifest.Package.Emojicode.IsSatisfiedBy(compiler.Version))
                .Select(package => TooOld(package.What, package.Manifest, compiler, yours: false))
                .Concat(inOrderOfName.SelectMany(package => package.NativeFiles.Where(file => file.Language is null).Select(file => NotNative(package.What, file.Path, yours: false))))
                .ToList();
            if (own is not null)
            {
                if (!own.Manifest.Package.Emojicode.IsSatisfiedBy(compiler.Version))
                {
                    unbuildable.Add(TooOld(own.What, own.Manifest, compiler, yours: true));
                }

                unbuildable.AddRange(own.NativeFiles.Where(file => file.Language is null).Select(file => NotNative(own.What, file.Path, yours: true)));
                if (own.NoEntry(out ownEntry) is { } noEntry)
                {
                    unbuildable.Add(noEntry);
                }
            }

            if (unbuildable.Count > 0)
            {
                return Stopped(unbuildable);
            }

            var nativeCompilers = new Dictionary<NativeLanguage, Sha256Digest>();
            var keys = new Dictionary<PackageName, Sha256Digest>();
            foreach (var package in order)
            {
                // A package's key holds what compiles its native code, and a package that has none needs no such compiler to be found.
                var languages = sources[package.Name].NativeFiles.Select(file => file.Language!.Value).Distinct().Order().ToList();
                foreach (var language in languages.Where(language => !nativeCompilers.ContainsKey(language)))
                {
                    var native = await _driver.IdentifyNativeAsync(language, cancellationToken);
                    if (!native.Succeeded)
                    {
                        return Stopped(native.Problem);
                    }

                    nativeCompilers[language] = native.Value;
                }

                keys[package.Name] = BuildKey.Of(
                    compiler.Sha256,
                    _request.Release,
                    package.Sha256,
                    package.Dependencies.ToDictionary(dependency => dependency.Name, dependency => keys[dependency.Name]),
                    languages.ToDictionary(language => language, language => nativeCompilers[language]));
            }

            var toBuild = order.Where(package => !_store.Holds(package.Name, package.Version, keys[package.Name])).ToList();
            // The project's own native files are compiled by the same tools as a package's, and a tool that is missing for them is as well said now.
            var lacks = _driver.Lacks(
                archiving: toBuild.Count > 0 || own is { Manifest.Package.Kind: PackageKind.Library },
                native: [.. toBuild.SelectMany(package => sources[package.Name].NativeFiles).Concat(own?.NativeFiles ?? []).Select(file => file.Language!.Value)],
                linking: own is { Manifest.Package.Kind: PackageKind.App });
            if (lacks.Count > 0)
            {
                return Stopped(lacks);
            }

            var entries = new Dictionary<PackageName, RelativePath>();
            var noEntries = new List<Diagnostic>();
            foreach (var package in toBuild)
            {
                if (NoEntry(sources[package.Name], out var entry) is { } noEntry)
                {
                    noEntries.Add(noEntry);
                }
                else
                {
                    entries[package.Name] = entry!;
                }
            }

            if (noEntries.Count > 0)
            {
                return Stopped(noEntries);
            }

            // What is compiled is the unpacked files, and only once they are known to be the archive's.
            var unpacked = toBuild.ToDictionary(package => package.Name, package => project.Store.Sources(package.Name, package.Version, package.Sha256, sources[package.Name].Files));
            foreach (var package in toBuild)
            {
                if (await BuildPackageAsync(lockfile, sources[package.Name], unpacked[package.Name], entries[package.Name], compiler, keys, cancellationToken) is { } problem)
                {
                    return Stopped(problem);
                }
            }

            var placed = order.Select(package => new PlacedPackage(package.Name.Name, _store.PackageDirectory(package.Name, package.Version, keys[package.Name]))).ToList();
            if (PlacedPackages.Place(_host.WorkingDirectory, placed) is { } unplaced)
            {
                return Stopped(unplaced);
            }

            if (order.Count > 0)
            {
                _said.WriteLine(Summary(toBuild.Count, order.Count - toBuild.Count));
            }

            var packagesDirectory = Path.Combine(_host.WorkingDirectory, PlacedPackages.DirectoryName);
            var built = inOrderOfName
                .Select(package => new BuiltPackage(
                    package.Locked.Name,
                    package.Locked.Version,
                    Path.Combine(packagesDirectory, package.Locked.Name.Name),
                    package.Manifest.Native?.Link ?? [],
                    toBuild.Contains(package.Locked)))
                .ToList();
            if (own is null)
            {
                return new BuildOutcome(_warnings, _omitted, compiler, built);
            }

            var made = await BuildProjectAsync(own, ownEntry!, built, cancellationToken);
            return made.Succeeded
                ? new BuildOutcome(_warnings, _omitted, compiler, built, made.Value)
                : Stopped(made.Problem);
        }

        // Builds one package into what pmj keeps. Null when it is kept, and the problem when it is not.
        /// <param name="unpacked">The directory of the package's unpacked files.</param>
        /// <param name="entry">Its main file.</param>
        private async Task<Diagnostic?> BuildPackageAsync(
            Lockfile lockfile,
            PackageSources package,
            string unpacked,
            RelativePath entry,
            CompilerIdentity compiler,
            Dictionary<PackageName, Sha256Digest> keys,
            CancellationToken cancellationToken)
        {
            var (name, version) = (package.Locked.Name, package.Locked.Version);
            _said.WriteLine($"Building {name} {version}");
            var staged = _store.Begin(name, version, keys[name]);
            var kept = false;
            try
            {
                var needs = BuildOrder.Needs(lockfile, package.Locked);
                var compiled = await _driver.CompilePackageAsync(
                    new PackageCompile(
                        package.What,
                        name.Name,
                        Local(unpacked, entry.Value),
                        needs.Select(needed => _store.EntryDirectory(needed.Name, needed.Version, keys[needed.Name])).ToList(),
                        staged.PackageDirectory,
                        staged.WorkDirectory,
                        _request.Release),
                    cancellationToken);
                Relay(name.Name, compiled.Printed);
                if (!compiled.Succeeded)
                {
                    return compiled.Problem;
                }

                var objects = await CompileNativeAsync(
                    package.What,
                    name.Name,
                    compiled.Value,
                    package.NativeFiles.Select(file => (Local(unpacked, file.Path), file.Language!.Value)).ToList(),
                    (package.Manifest.Native?.IncludeDirs ?? []).Select(directory => Local(unpacked, directory.Value)).ToList(),
                    staged.WorkDirectory,
                    cancellationToken);
                if (!objects.Succeeded)
                {
                    return objects.Problem;
                }

                var archived = await _driver.ArchiveAsync(new ArchiveRequest(package.What, name.Name, objects.Value, staged.PackageDirectory), cancellationToken);
                Relay(name.Name, archived.Printed);
                if (!archived.Succeeded)
                {
                    return archived.Problem;
                }

                _store.Keep(staged, new BuildStamp(
                    keys[name],
                    name,
                    version,
                    package.Locked.Sha256,
                    compiler.Version,
                    compiler.Sha256,
                    _request.Release,
                    package.Locked.Dependencies.ToDictionary(dependency => dependency.Name, dependency => keys[dependency.Name]),
                    package.Manifest.Native?.Link ?? []));
                kept = true;
                return null;
            }
            finally
            {
                if (!kept)
                {
                    BuiltStore.Discard(staged);
                }
            }
        }

        // Builds the project itself into its own directory: an application to a program, and a library to what a package is built to.
        private async Task<BuildStep<BuiltProject>> BuildProjectAsync(OwnProject own, RelativePath entry, IReadOnlyList<BuiltPackage> packages, CancellationToken cancellationToken)
        {
            var target = Path.Combine(_host.WorkingDirectory, ProjectFiles.Target);

            // What is made on the way is kept apart from what is made, so that no project's name is in the way of it.
            var work = Path.Combine(target, Intermediate, Mode);
            var product = Path.Combine(target, Mode, own.Manifest.Package.Name.Name);

            // Nothing of an earlier build stays: not an object that is no longer made, and not a program that would be taken for this one's.
            Clear(work);
            Clear(product);
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(Path.GetDirectoryName(product)!);

            var made = false;
            try
            {
                var built = await MakeProjectAsync(own, entry, packages, work, product, cancellationToken);
                made = built.Succeeded;
                return built;
            }
            finally
            {
                // Nor is anything left half made by this one: an interface with no archive beside
                // it would be taken for a package, and a file a linker did not finish for a program.
                if (!made)
                {
                    Discard(product);
                }
            }
        }

        // Compiles the project and makes of it what its kind is, at a place that has been cleared for it.
        private async Task<BuildStep<BuiltProject>> MakeProjectAsync(OwnProject own, RelativePath entry, IReadOnlyList<BuiltPackage> packages, string work, string product, CancellationToken cancellationToken)
        {
            var package = own.Manifest.Package;
            var name = package.Name.Name;
            _said.WriteLine($"Building {package.Name} {package.Version}");
            IReadOnlyList<string> search = packages.Count > 0 ? [Path.Combine(_host.WorkingDirectory, PlacedPackages.DirectoryName)] : [];
            var main = Local(_host.WorkingDirectory, entry.Value);
            var isLibrary = package.Kind == PackageKind.Library;
            if (isLibrary)
            {
                Directory.CreateDirectory(product);
            }

            var compiled = isLibrary
                ? await _driver.CompilePackageAsync(new PackageCompile(own.What, name, main, search, product, work, _request.Release), cancellationToken)
                : await _driver.CompileProgramAsync(new ProgramCompile(own.What, name, main, search, work, _request.Release), cancellationToken);
            Relay(name, compiled.Printed);
            if (!compiled.Succeeded)
            {
                return BuildStep<BuiltProject>.Failed(compiled.Problem);
            }

            var objects = await CompileNativeAsync(
                own.What,
                name,
                compiled.Value,
                own.NativeFiles.Select(file => (Local(_host.WorkingDirectory, file.Path), file.Language!.Value)).ToList(),
                (own.Manifest.Native?.IncludeDirs ?? []).Select(directory => Local(_host.WorkingDirectory, directory.Value)).ToList(),
                work,
                cancellationToken);
            if (!objects.Succeeded)
            {
                return BuildStep<BuiltProject>.Failed(objects.Problem);
            }

            var finished = isLibrary
                ? await _driver.ArchiveAsync(new ArchiveRequest(own.What, name, objects.Value, product), cancellationToken)
                : await _driver.LinkAsync(
                    new LinkRequest(
                        own.What,
                        objects.Value,
                        packages.Select(built => new LinkedPackage(built.Name.Name, built.Directory)).ToList(),
                        [.. own.Manifest.Native?.Link ?? [], .. packages.SelectMany(built => built.Link)],
                        product),
                    cancellationToken);
            Relay(name, finished.Printed);
            if (!finished.Succeeded)
            {
                return BuildStep<BuiltProject>.Failed(finished.Problem);
            }

            var shown = Path.GetRelativePath(_host.WorkingDirectory, product).Replace(Path.DirectorySeparatorChar, '/');
            _said.WriteLine(isLibrary ? $"Built the library {shown}/." : $"Built the application {shown}.");
            return BuildStep<BuiltProject>.Of(new BuiltProject(package.Name, package.Version, package.Kind, product));
        }

        // Compiles native files, each to an object of its own, and gives every object that is to be archived or linked: the one of the Emojicode code first.
        private async Task<BuildStep<List<string>>> CompileNativeAsync(
            string what,
            string name,
            string first,
            IReadOnlyList<(string Path, NativeLanguage Language)> files,
            IReadOnlyList<string> headers,
            string workDirectory,
            CancellationToken cancellationToken)
        {
            var objects = new List<string> { first };
            foreach (var (path, language) in files)
            {
                var native = await _driver.CompileNativeAsync(new NativeCompile(what, language, path, headers, workDirectory, objects.Count - 1), cancellationToken);
                Relay(name, native.Printed);
                if (!native.Succeeded)
                {
                    return BuildStep<List<string>>.Failed(native.Problem);
                }

                objects.Add(native.Value);
            }

            return BuildStep<List<string>>.Of(objects);
        }

        // Takes away what is at a path, whatever it is: a project that was a library and is now an
        // application leaves a folder where its program goes.
        private static void Clear(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // Takes away what a build that did not end well had begun.
        private static void Discard(string path)
        {
            try
            {
                Clear(path);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // The problem that is reported is the one that led here, and the next build clears what this one could not.
            }
        }

        // A path inside a package, as a path on this machine.
        private static string Local(string directory, string path) => Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));

        private static Diagnostic TooOld(string what, Manifest manifest, CompilerIdentity compiler, bool yours) =>
            new(
                DiagnosticCodes.CompilerTooOld,
                $"{what} needs a newer Emojicode compiler than the one here.",
                $"its manifest asks for {manifest.Package.Emojicode}, and the compiler at \"{compiler.Path}\" says it is {compiler.Version}",
                "use a compiler that is new enough, naming it with EMOJICODEC if it is not the first on the PATH, or "
                    + (yours ? $"lower \"emojicode\" in {ManifestReader.FileName} if the project builds with this one" : "depend on a version of the package that this compiler builds"));

        private static Diagnostic NotNative(string what, string path, bool yours) =>
            new(
                DiagnosticCodes.NativeUnsupported,
                $"\"{path}\" of {what} is neither C nor C++.",
                "its manifest selects it under \"native.sources\", and pmj compiles a file that ends .c as C, and one that ends .cpp, .cc or .cxx as C++",
                yours
                    ? $"narrow \"native.sources\" in {ManifestReader.FileName} to the files to compile; headers are named by \"native.includeDirs\""
                    : "tell its author: \"native.sources\" is for the files to compile, and headers are named by \"native.includeDirs\"");

        // The one file the compiler is given for a package. What was published has to hold it: pmj
        // pack sees to that, and an archive need not have been made by pmj pack.
        private static Diagnostic? NoEntry(PackageSources package, out RelativePath? entry)
        {
            const string Fix = "tell its author: what is published has to hold the package's one main file, and pmj pack sees to that";
            bool Published(string path) => package.Files.Any(file => file.Path.Value == path);
            if (!EntryConvention.TryResolve(package.Manifest, Published, out entry, out var unresolved))
            {
                var two = unresolved.Code == DiagnosticCodes.EntryAmbiguous;
                return new Diagnostic(unresolved.Code, $"{package.What} has {(two ? "two possible entry files" : "no entry file")}.", unresolved.Reason, Fix);
            }

            return Published(entry.Value)
                ? null
                : new Diagnostic(DiagnosticCodes.EntryNotFound, $"{package.What} has no entry file.", $"its manifest names \"{entry}\" as its entry, and what was published holds no such file", Fix);
        }

        // What a tool printed is the tool's word and not pmj's, so it goes where problems go, behind the name of what was being built.
        private void Relay(string name, string printed)
        {
            foreach (var line in ToolOutput.Lines(printed))
            {
                _host.Error.WriteLine($"[{name}] {line}");
            }
        }

        private static string Summary(int compiled, int before)
        {
            var place = PlacedPackages.DirectoryName + "/";
            return (compiled, before) switch
            {
                (_, 0) => $"Built {Count(compiled)} into {place}.",
                (0, 1) => $"1 package was built before, and is in {place}.",
                (0, _) => $"{before} packages were built before, and are in {place}.",
                (_, 1) => $"Built {Count(compiled)} into {place}, and 1 was built before.",
                _ => $"Built {Count(compiled)} into {place}, and {before} were built before.",
            };
        }

        private static string Count(int packages) => packages == 1 ? "1 package" : $"{packages} packages";
    }
}
