using System.Security.Cryptography;
using System.Text;
using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Cli.Building
{
    /// <summary>
    /// The driver for <c>emojicodec</c> as it is released, which is decision record 0001 as code. The
    /// compiler is asked only to compile: pmj makes each archive itself. And the compiler's status is
    /// one of three things that say whether it succeeded, because it ends with 0 for some failures.
    /// </summary>
    internal sealed class EmojicodecDriver(PmjHost host) : ICompilerDriver
    {
        private const string CompilerVariable = "EMOJICODEC";
        private const string ArchiverVariable = "AR";
        private const string HeadersVariable = "EMOJICODE_INCLUDE";
        private const string PackagesVariable = "EMOJICODE_PACKAGES_PATH";
        private const string PathVariable = "PATH";

        // The two libraries that the compiler's own package s says a program is linked with. Of the
        // packages that come with the compiler, it is the only one that says any.
        private static readonly string[] OwnLibraries = ["m", "pthread"];

        // The two of the compiler's own packages that every program is linked with.
        private static readonly string[] AlwaysLinked = ["s", "runtime"];

        // A header that every native file of a package includes, and so the one that says whether the headers are there.
        private static readonly string RuntimeHeader = Path.Combine("runtime", "Runtime.h");

        // What the compiler calls the two files it leaves of a package, and what it prints when its
        // own check refuses the code it generated: Options.cpp:122-128, and Grapevine's defect 09.
        private const string Interface = "🏛";
        private const string Report = "documentation.json";
        private const string VerifierRefused = "Detected in:";

        private string? _compiler;

        public async ValueTask<BuildStep<CompilerIdentity>> IdentifyAsync(CancellationToken cancellationToken)
        {
            if (Find(CompilerVariable, "emojicodec") is not { } path)
            {
                return BuildStep<CompilerIdentity>.Failed(NotFound(DiagnosticCodes.CompilerNotFound, "The Emojicode compiler", CompilerVariable, "emojicodec", "install Emojicode"));
            }

            // The compiler has no --version. Its help begins with a banner, and that is where it says which it is.
            if (await host.Tools.RunAsync(path, ["--help"], host.WorkingDirectory, cancellationToken) is not { } help)
            {
                return BuildStep<CompilerIdentity>.Failed(NotRun(DiagnosticCodes.CompilerNotFound, "The Emojicode compiler", path));
            }

            if (CompilerBanner.Version(help.Output) is not { } version)
            {
                return BuildStep<CompilerIdentity>.Failed(new Diagnostic(
                    DiagnosticCodes.CompilerUnknown,
                    "The Emojicode compiler did not say which version it is.",
                    $"\"{path}\" was asked for its help, and what it printed has no line that pmj can read a version from: it looks for \"Emojicode Compiler\" and then a version, as in \"Emojicode Compiler 1.0 beta 2.\"",
                    $"check that it is the Emojicode compiler, or set {CompilerVariable} to the one that is; if it is a compiler newer than this pmj knows of, pmj has to be updated"));
            }

            try
            {
                await using var file = File.OpenRead(path);
                Sha256Digest.TryParse(Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken)), out var sha256, out _);
                _compiler = path;
                return BuildStep<CompilerIdentity>.Of(new CompilerIdentity(path, version, sha256!));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return BuildStep<CompilerIdentity>.Failed(new Diagnostic(
                    DiagnosticCodes.CompilerNotFound,
                    "The Emojicode compiler could not be read.",
                    $"pmj tells one compiler from another by the bytes of its file, and \"{path}\" could not be read: {failure.Message}",
                    "check that the file is yours to read"));
            }
        }

        public async ValueTask<BuildStep<Sha256Digest>> IdentifyNativeAsync(NativeLanguage language, CancellationToken cancellationToken)
        {
            var native = Native(language);
            if (Find(native.Variable, native.Fallback) is not { } path)
            {
                return BuildStep<Sha256Digest>.Failed(NotFound(DiagnosticCodes.ToolNotFound, native.Tool, native.Variable, native.Fallback, native.Install));
            }

            if (await host.Tools.RunAsync(path, ["--version"], host.WorkingDirectory, cancellationToken) is not { } run)
            {
                return BuildStep<Sha256Digest>.Failed(NotRun(DiagnosticCodes.ToolNotFound, native.Tool, path));
            }

            return run.Status == 0
                ? BuildStep<Sha256Digest>.Of(Sha256Digest.Of(Encoding.UTF8.GetBytes(run.Output + run.Error)))
                : BuildStep<Sha256Digest>.Failed(new Diagnostic(
                    DiagnosticCodes.ToolNotFound,
                    $"{native.Tool} did not say which it is.",
                    $"\"{path}\" was asked for its version, as pmj asks so that what one compiler built is never taken for another's: {Ended("it", run)}",
                    $"check that it is a compiler that answers --version, or set {native.Variable} to one that does"));
        }

        public IReadOnlyList<Diagnostic> Lacks(bool archiving, bool nativeCode, bool linking)
        {
            var lacks = new List<Diagnostic>();
            var linker = Native(NativeLanguage.Cpp);
            if (linking && Find(linker.Variable, linker.Fallback) is null)
            {
                lacks.Add(NotFound(DiagnosticCodes.ToolNotFound, linker.Tool, linker.Variable, linker.Fallback, linker.Install));
            }

            if (linking && AlwaysLinked.Select(Stock).FirstOrDefault(archive => !File.Exists(archive)) is { } missing)
            {
                lacks.Add(new Diagnostic(
                    DiagnosticCodes.CompilerIncomplete,
                    "The Emojicode compiler's own packages were not found.",
                    $"a program is linked with them, and \"{missing}\" is not there",
                    $"set {PackagesVariable} to the directory that holds the compiler's own packages, s and runtime among them: it is where Emojicode's installer was told to put them"));
            }

            if (archiving && Find(ArchiverVariable, "ar") is null)
            {
                lacks.Add(NotFound(DiagnosticCodes.ToolNotFound, "The archiver", ArchiverVariable, "ar", "install a C toolchain, which has one"));
            }

            if (nativeCode && !File.Exists(Path.Combine(Headers, RuntimeHeader)))
            {
                lacks.Add(new Diagnostic(
                    DiagnosticCodes.CompilerIncomplete,
                    "The Emojicode compiler's headers were not found.",
                    $"native code is compiled against them, and \"{Path.Combine(Headers, RuntimeHeader)}\" is not there",
                    $"set {HeadersVariable} to the directory that holds runtime/Runtime.h, which is where Emojicode's installer was told to put its headers"));
            }

            return lacks;
        }

        public async ValueTask<BuildStep<string>> CompilePackageAsync(PackageCompile compile, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(compile);
            var made = Path.Combine(compile.WorkDirectory, compile.Name + ".o");
            var declared = Path.Combine(compile.OutputDirectory, Interface);
            var compiled = await CompileAsync(
                compile.What,
                [compile.Entry, "-p", compile.Name, "-c", "-o", made, "-i", declared, "-r"],
                compile.Optimized,
                compile.SearchPaths,
                compile.WorkDirectory,
                [made, declared],
                cancellationToken);

            // The report is written beside the object, and belongs with what others read of the package.
            var report = Path.Combine(compile.WorkDirectory, Report);
            if (compiled.Succeeded && File.Exists(report))
            {
                File.Move(report, Path.Combine(compile.OutputDirectory, Report), overwrite: true);
            }

            return compiled;
        }

        public ValueTask<BuildStep<string>> CompileProgramAsync(ProgramCompile compile, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(compile);
            var made = Path.Combine(compile.WorkDirectory, compile.Name + ".o");
            return CompileAsync(compile.What, [compile.Entry, "-c", "-o", made], compile.Optimized, compile.SearchPaths, compile.WorkDirectory, [made], cancellationToken);
        }

        /// <param name="arguments">What the compiler is given before the flags that a package and a program share.</param>
        /// <param name="made">What the compiler is to write, the object first.</param>
        private async ValueTask<BuildStep<string>> CompileAsync(
            string what,
            List<string> arguments,
            bool optimized,
            IReadOnlyList<string> searchPaths,
            string workDirectory,
            string[] made,
            CancellationToken cancellationToken)
        {
            if (optimized)
            {
                arguments.Add("-O");
            }

            foreach (var searched in searchPaths)
            {
                arguments.Add("-S");
                arguments.Add(searched);
            }

            // Run where there is no ./packages, which the compiler would search without being asked.
            if (await host.Tools.RunAsync(Compiler, arguments, workDirectory, cancellationToken) is not { } run)
            {
                return BuildStep<string>.Failed(NotRun(DiagnosticCodes.CompilerNotFound, "The Emojicode compiler", Compiler));
            }

            var printed = run.Output + run.Error;
            return Refused(run, made) is { } reason
                ? BuildStep<string>.Failed(
                    new Diagnostic(
                        DiagnosticCodes.BuildCompileFailed,
                        $"{what} could not be compiled.",
                        reason,
                        "mend what the compiler names, which is printed above; if the code is a package's and not yours, tell its author"),
                    printed)
                : BuildStep<string>.Of(made[0], printed);
        }

        public async ValueTask<BuildStep<string>> CompileNativeAsync(NativeCompile compile, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(compile);
            var native = Native(compile.Language);
            if (Find(native.Variable, native.Fallback) is not { } compiler)
            {
                return BuildStep<string>.Failed(NotFound(DiagnosticCodes.ToolNotFound, native.Tool, native.Variable, native.Fallback, native.Install));
            }

            // The flags are pmj's and the same for every package. A manifest gives none: a flag from
            // a package would be someone else's option handed to the compiler of whoever builds it.
            var made = Path.Combine(compile.WorkDirectory, $"native-{compile.Number}.o");
            List<string> arguments = [native.Standard, "-O2", "-c", compile.Source, "-I", Headers];
            foreach (var included in compile.IncludeDirectories)
            {
                arguments.Add("-I");
                arguments.Add(included);
            }

            arguments.Add("-o");
            arguments.Add(made);
            if (await host.Tools.RunAsync(compiler, arguments, compile.WorkDirectory, cancellationToken) is not { } run)
            {
                return BuildStep<string>.Failed(NotRun(DiagnosticCodes.ToolNotFound, native.Tool, compiler));
            }

            var printed = run.Output + run.Error;
            return Failed(char.ToLowerInvariant(native.Tool[0]) + native.Tool[1..], run, made) is { } reason
                ? BuildStep<string>.Failed(
                    new Diagnostic(
                        DiagnosticCodes.BuildNativeFailed,
                        $"A native file of {compile.What} could not be compiled.",
                        reason,
                        "mend what the compiler names, which is printed above; if the code is a package's and not yours, tell its author"),
                    printed)
                : BuildStep<string>.Of(made, printed);
        }

        public async ValueTask<BuildStep<string>> ArchiveAsync(ArchiveRequest archive, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(archive);
            if (Find(ArchiverVariable, "ar") is not { } archiver)
            {
                return BuildStep<string>.Failed(NotFound(DiagnosticCodes.ToolNotFound, "The archiver", ArchiverVariable, "ar", "install a C toolchain, which has one"));
            }

            // Always a file that was not there: an archiver given one that is adds to it, and keeps what no longer belongs in it.
            var made = Path.Combine(archive.OutputDirectory, $"lib{archive.Name}.a");
            if (await host.Tools.RunAsync(archiver, ["rcs", made, .. archive.Objects], archive.OutputDirectory, cancellationToken) is not { } run)
            {
                return BuildStep<string>.Failed(NotRun(DiagnosticCodes.ToolNotFound, "The archiver", archiver));
            }

            var printed = run.Output + run.Error;
            return Failed("the archiver", run, made) is { } reason
                ? BuildStep<string>.Failed(
                    new Diagnostic(
                        DiagnosticCodes.BuildArchiveFailed,
                        $"The archive of {archive.What} could not be made.",
                        reason,
                        $"read what the archiver printed, which is above; if it is not an archiver that takes \"rcs\", set {ArchiverVariable} to one that does"),
                    printed)
                : BuildStep<string>.Of(made, printed);
        }

        public async ValueTask<BuildStep<string>> LinkAsync(LinkRequest link, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(link);
            var native = Native(NativeLanguage.Cpp);
            if (Find(native.Variable, native.Fallback) is not { } linker)
            {
                return BuildStep<string>.Failed(NotFound(DiagnosticCodes.ToolNotFound, native.Tool, native.Variable, native.Fallback, native.Install));
            }

            // The compiler's own link names archives in an order that fails as soon as one package
            // imports another, so pmj links: every archive in one group, where their order does not
            // matter and the linker takes only what the program refers to. The linker of macOS reads
            // archives that way without being asked, and refuses the two flags.
            List<string> arguments = [.. link.Objects];
            if (!host.IsMacOS)
            {
                arguments.Add("-Wl,--start-group");
            }

            arguments.AddRange(link.Packages.OrderBy(package => package.Name, StringComparer.Ordinal).Select(package => Path.Combine(package.Directory, $"lib{package.Name}.a")));
            arguments.AddRange(ReservedNames.All.Order(StringComparer.Ordinal).Select(Stock).Where(File.Exists));
            if (!host.IsMacOS)
            {
                arguments.Add("-Wl,--end-group");
            }

            var libraries = link.Libraries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            libraries.AddRange(OwnLibraries.Where(own => !libraries.Contains(own, StringComparer.Ordinal)));
            arguments.AddRange(libraries.Select(library => "-l" + library));
            arguments.Add("-o");
            arguments.Add(link.Program);
            if (await host.Tools.RunAsync(linker, arguments, Path.GetDirectoryName(link.Program)!, cancellationToken) is not { } run)
            {
                return BuildStep<string>.Failed(NotRun(DiagnosticCodes.ToolNotFound, native.Tool, linker));
            }

            var printed = run.Output + run.Error;
            return Failed("the linker", run, link.Program) is { } reason
                ? BuildStep<string>.Failed(
                    new Diagnostic(
                        DiagnosticCodes.BuildLinkFailed,
                        $"{link.What} could not be linked.",
                        reason,
                        "read what the linker printed, which is above; a library it cannot find is one that a package names under \"native.link\", and has to be on this machine"),
                    printed)
                : BuildStep<string>.Of(link.Program, printed);
        }

        private string Compiler => _compiler ?? throw new InvalidOperationException("The compiler is asked which it is before it is asked to compile.");

        // Where the compiler's installer puts its headers and its own packages when it is told nothing: install.sh:8-10.
        private string Headers => Set(HeadersVariable) ?? Path.Combine(host.InstallRoot, "include", "emojicode");

        // The compiler looks for a package where its variable says and then where it was installed,
        // and the variable may name a directory of someone's own packages. So its own packages are in
        // the first of the two that holds s, which every program needs.
        private string StockDirectory
        {
            get
            {
                var installed = Path.Combine(host.InstallRoot, "EmojicodePackages");
                var named = Set(PackagesVariable);
                return named is null || (!File.Exists(Path.Combine(named, "s", "libs.a")) && File.Exists(Path.Combine(installed, "s", "libs.a"))) ? installed : named;
            }
        }

        // The archive of one of the compiler's own packages.
        private string Stock(string package) => Path.Combine(StockDirectory, package, $"lib{package}.a");

        // C++17 is what the compiler's own headers are written in. For C, gnu11 and not c11: native
        // code is there to reach the system, and plain c11 hides the system's own declarations.
        private static (string Tool, string Variable, string Fallback, string Standard, string Install) Native(NativeLanguage language) =>
            language == NativeLanguage.C
                ? ("The C compiler", "CC", "cc", "-std=gnu11", "install a C toolchain")
                : ("The C++ compiler", "CXX", "c++", "-std=c++17", "install a C++ toolchain");

        // When the compiler has succeeded is item 3 of decision record 0001: its status is 0, it did
        // not say that its own check refused the code, and what it was to write is there.
        private static string? Refused(ToolRun run, params string[] made)
        {
            if (run.Status == 0 && (run.Output.Contains(VerifierRefused, StringComparison.Ordinal) || run.Error.Contains(VerifierRefused, StringComparison.Ordinal)))
            {
                return $"the compiler ended as if all were well, and what it printed holds \"{VerifierRefused}\", which is its own check refusing the code it generated";
            }

            return Failed("the compiler", run, made);
        }

        // Why a tool has failed, or null when it has not: it says so by its status, or it says all
        // is well and has not written what it was run to write. A status is believed of no tool.
        private static string? Failed(string tool, ToolRun run, params string[] made)
        {
            if (run.Status != 0)
            {
                return Ended(tool, run);
            }

            return made.FirstOrDefault(file => !File.Exists(file)) is { } missing
                ? $"{tool} ended as if all were well and did not write \"{missing}\"{Said(run, "; it said: ")}"
                : null;
        }

        private static string Ended(string tool, ToolRun run) => $"{tool} ended with status {run.Status}{Said(run, ", and said: ")}";

        // The first thing a tool said, which is nearly always what went wrong. All it said is printed apart from this.
        private static string Said(ToolRun run, string lead)
        {
            var first = (run.Error + "\n" + run.Output).Split('\n', '\r').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
            return first is null ? "" : lead + first;
        }

        // A tool is one program with no arguments: a path, or a name that is looked for in each
        // directory of PATH. A variable that is set to nothing says nothing.
        private string? Find(string variable, string fallback)
        {
            var named = Set(variable) ?? fallback;
            if (named.Contains('/') || named.Contains(Path.DirectorySeparatorChar))
            {
                var given = Path.GetFullPath(named, host.WorkingDirectory);
                return File.Exists(given) ? given : null;
            }

            return (Set(PathVariable) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.GetFullPath(Path.Combine(directory, named), host.WorkingDirectory))
                .FirstOrDefault(File.Exists);
        }

        private string? Set(string variable) => host.Variable(variable) is { Length: > 0 } value ? value : null;

        private Diagnostic NotFound(string code, string tool, string variable, string fallback, string install) =>
            new(
                code,
                $"{tool} was not found.",
                Set(variable) is { } named
                    ? $"{variable} names \"{named}\", and there is no such program"
                    : $"no program called {fallback} is in any directory of {PathVariable}",
                $"{install}, or set {variable} to where it is");

        private static Diagnostic NotRun(string code, string tool, string path) =>
            new(
                code,
                $"{tool} could not be run.",
                $"\"{path}\" is there, and it could not be started",
                "check that it is a program for this machine, and that it is yours to run");
    }
}
