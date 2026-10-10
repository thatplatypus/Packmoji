using Packmoji.Cli.Commands;
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
        private readonly PmjHost _host;
        private readonly BuildRequest _request;
        private readonly TextWriter _said;
        private readonly ICompilerDriver _driver;
        private readonly BuiltStore _store;

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
            try
            {
                return await new Builder(host, request, said).BuildAsync(cancellationToken);
            }
            catch (PackageSourceException failure)
            {
                // What could not be fetched, read or kept is a problem to report, as it is for every command.
                return Stopped([failure.Diagnostic]);
            }
        }

        private static BuildOutcome Stopped(IReadOnlyList<Diagnostic> diagnostics, int omitted = 0) => new(diagnostics, omitted, null, []);

        private async Task<BuildOutcome> BuildAsync(CancellationToken cancellationToken)
        {
            var opened = ProjectSession.OpenToFetch(_host);
            if (!opened.Succeeded)
            {
                return Stopped(opened.Diagnostics, opened.Omitted);
            }

            // A build chooses no version. A project that asks for no package has nothing to choose, and needs no lockfile to say so.
            var project = opened.Value;
            var asked = RootRequirements.From(project.Manifest);
            var asksForNothing = asked.Dependencies.Count == 0 && asked.DevDependencies.Count == 0;
            if (!project.IsLocked && !(project.Lockfile is null && asksForNothing))
            {
                return Stopped([CommandDiagnostics.NotInstalled(missing: project.Lockfile is null)]);
            }

            using var held = await ProjectLock.TakeAsync(
                _host.WorkingDirectory,
                () => _host.Error.WriteLine("Waiting for another pmj that is building this project."),
                cancellationToken);

            var warnings = new List<Diagnostic>();
            var omitted = 0;
            if (project.Lockfile is not null)
            {
                var check = await project.FetchLockedAsync([], cancellationToken);
                if (!check.Succeeded)
                {
                    return Stopped(check.Diagnostics, check.OmittedDiagnostics);
                }

                warnings.AddRange(check.Diagnostics);
                omitted = check.OmittedDiagnostics;
            }

            var lockfile = project.Lockfile ?? new Lockfile(asked, []);
            var order = BuildOrder.Of(lockfile);
            if (order.Count == 0 && _request.DependenciesOnly)
            {
                // Nothing is locked now, and something may have been before.
                if (PlacedPackages.Place(_host.WorkingDirectory, []) is { } leftBehind)
                {
                    return Stopped([leftBehind, .. warnings], omitted);
                }

                _said.WriteLine("Nothing to build: the project depends on no package.");
                return new BuildOutcome(warnings, omitted, null, []);
            }

            var identified = await _driver.IdentifyAsync(cancellationToken);
            if (!identified.Succeeded)
            {
                return Stopped([identified.Problem, .. warnings], omitted);
            }

            var compiler = identified.Value;
            var sources = new Dictionary<PackageName, PackageSources>();
            foreach (var package in order)
            {
                var files = await project.FilesOfAsync(package.Name, package.Version, package.Sha256, cancellationToken);
                var manifest = ManifestReader.Read(files.First(file => file.Path.Value == ManifestReader.FileName).Content);
                sources[package.Name] = manifest.Succeeded
                    ? new PackageSources(package, manifest.Value, files)
                    : throw new PackageSourceException(manifest.Diagnostics[0]);
            }

            // Whatever would stop a package being built is found before the first is compiled, and all of it is said.
            var unbuildable = order
                .OrderBy(package => package.Name)
                .Where(package => !sources[package.Name].Manifest.Package.Emojicode.IsSatisfiedBy(compiler.Version))
                .Select(package => TooOld(sources[package.Name], compiler))
                .Concat(order.OrderBy(package => package.Name).SelectMany(package => sources[package.Name].NativeFiles
                    .Where(file => file.Language is null)
                    .Select(file => NotNative(sources[package.Name], file.Path))))
                .ToList();
            if (unbuildable.Count > 0)
            {
                return Stopped([.. unbuildable, .. warnings], omitted) with { Compiler = compiler };
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
                        return Stopped([native.Problem, .. warnings], omitted) with { Compiler = compiler };
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
            if (_driver.Lacks(archiving: toBuild.Count > 0, nativeCode: toBuild.Any(package => sources[package.Name].NativeFiles.Count > 0)) is { Count: > 0 } lacks)
            {
                return Stopped([.. lacks, .. warnings], omitted) with { Compiler = compiler };
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
                return Stopped([.. noEntries, .. warnings], omitted) with { Compiler = compiler };
            }

            // What is compiled is the unpacked files, and only once they are known to be the archive's.
            var unpacked = toBuild.ToDictionary(package => package.Name, package => project.Store.Sources(package.Name, package.Version, package.Sha256, sources[package.Name].Files));
            foreach (var package in toBuild)
            {
                if (await BuildPackageAsync(lockfile, sources[package.Name], unpacked[package.Name], entries[package.Name], compiler, keys, cancellationToken) is { } problem)
                {
                    return Stopped([problem, .. warnings], omitted) with { Compiler = compiler };
                }
            }

            var placed = order.Select(package => new PlacedPackage(package.Name.Name, keys[package.Name], _store.PackageDirectory(package.Name, package.Version, keys[package.Name]))).ToList();
            if (PlacedPackages.Place(_host.WorkingDirectory, placed) is { } unplaced)
            {
                return Stopped([unplaced, .. warnings], omitted) with { Compiler = compiler };
            }

            _said.WriteLine(Summary(toBuild.Count, order.Count - toBuild.Count));
            var built = order
                .OrderBy(package => package.Name)
                .Select(package => new BuiltPackage(
                    package,
                    Path.Combine(_host.WorkingDirectory, PlacedPackages.DirectoryName, package.Name.Name),
                    sources[package.Name].Manifest.Native?.Link ?? [],
                    toBuild.Contains(package)))
                .ToList();
            return new BuildOutcome(warnings, omitted, compiler, built);
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

                var objects = new List<string> { compiled.Value };
                var headers = (package.Manifest.Native?.IncludeDirs ?? []).Select(directory => Local(unpacked, directory.Value)).ToList();
                foreach (var (path, language) in package.NativeFiles)
                {
                    var native = await _driver.CompileNativeAsync(new NativeCompile(package.What, language!.Value, Local(unpacked, path), headers, staged.WorkDirectory, objects.Count - 1), cancellationToken);
                    Relay(name.Name, native.Printed);
                    if (!native.Succeeded)
                    {
                        return native.Problem;
                    }

                    objects.Add(native.Value);
                }

                var archived = await _driver.ArchiveAsync(new ArchiveRequest(package.What, name.Name, objects, staged.PackageDirectory), cancellationToken);
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

        // A path inside a package, as a path on this machine.
        private static string Local(string directory, string path) => Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));

        private static Diagnostic TooOld(PackageSources package, CompilerIdentity compiler) =>
            new(
                DiagnosticCodes.CompilerTooOld,
                $"{package.What} needs a newer Emojicode compiler than the one here.",
                $"its manifest asks for {package.Manifest.Package.Emojicode}, and the compiler at \"{compiler.Path}\" says it is {compiler.Version}",
                "use a compiler that is new enough, naming it with EMOJICODEC if it is not the first on the PATH, or depend on a version of the package that this compiler builds");

        private static Diagnostic NotNative(PackageSources package, string path) =>
            new(
                DiagnosticCodes.NativeUnsupported,
                $"\"{path}\" of {package.What} is neither C nor C++.",
                "its manifest selects it under \"native.sources\", and pmj compiles a file that ends .c as C, and one that ends .cpp, .cc or .cxx as C++",
                "tell its author: \"native.sources\" is for the files to compile, and headers are named by \"native.includeDirs\"");

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
