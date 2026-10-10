using System.Diagnostics.CodeAnalysis;
using Packmoji.Cli.Cache;
using Packmoji.Cli.Output;
using Packmoji.Cli.Projects;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Reports;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// A project that a command is at work on: its manifest, its lockfile if it has one, and the
    /// cache. The commands that change what a project asks for differ only in what they change, so
    /// what follows a change is here once: resolve, fetch, and write the two files.
    /// </summary>
    internal sealed class ProjectSession
    {
        private ProjectSession(PmjHost host, Manifest manifest, Lockfile? lockfile)
        {
            Host = host;
            Manifest = manifest;
            Lockfile = lockfile;
            Store = new FileAssetStore(host.HomeDirectory);
        }

        public PmjHost Host { get; }

        public Manifest Manifest { get; }

        public Lockfile? Lockfile { get; }

        public FileAssetStore Store { get; }

        /// <summary>Whether there is a lockfile and it still answers what the manifest asks for. When it does, nothing needs to be chosen.</summary>
        [MemberNotNullWhen(true, nameof(Lockfile))]
        public bool IsLocked => Lockfile is not null && Lockfile.Root.Matches(RootRequirements.From(Manifest));

        /// <summary>The project in the host's working directory, or why it cannot be worked on.</summary>
        public static Outcome<ProjectSession> Open(PmjHost host)
        {
            var manifest = ProjectFiles.ReadManifest(host.WorkingDirectory);
            if (!manifest.Succeeded)
            {
                return Outcome<ProjectSession>.Failed(manifest.Diagnostics, manifest.Omitted);
            }

            var lockfile = ProjectFiles.ReadLockfile(host.WorkingDirectory);
            return lockfile is { Succeeded: false }
                ? Outcome<ProjectSession>.Failed(lockfile.Diagnostics, lockfile.Omitted)
                : Outcome<ProjectSession>.Of(new ProjectSession(host, manifest.Value, lockfile?.Value));
        }

        /// <summary>
        /// The project, for a command that locks or fetches packages. A project that requires
        /// attestation is not opened for that: see <see cref="CommandDiagnostics.AttestationRequired"/>.
        /// </summary>
        public static Outcome<ProjectSession> OpenToFetch(PmjHost host)
        {
            var opened = Open(host);
            return opened.Succeeded && opened.Value.Manifest.RequireAttestation
                ? Outcome<ProjectSession>.Failed(CommandDiagnostics.AttestationRequired())
                : opened;
        }

        /// <summary>Where this project's packages are found. One is made for a command and used for all of it, because it learns as it goes.</summary>
        /// <param name="alsoLookIn">Repositories that were named as places to look in as well.</param>
        /// <param name="told">Where a package lives, for a package someone has said it of.</param>
        public DirectPackageSource Source(IReadOnlyList<RepositoryRef> alsoLookIn, IReadOnlyDictionary<PackageName, RepositoryRef>? told = null) =>
            new(Host.Releases, Store, Manifest, Lockfile, told, alsoLookIn);

        /// <summary>
        /// A changed manifest as its file will read. The rules that hold across a manifest are the
        /// reader's, so a change is held to them by writing it and reading it back.
        /// </summary>
        public static Outcome<Manifest> Checked(Manifest changed)
        {
            var read = ManifestReader.Read(ManifestWriter.Write(changed));

            // A place in a file that has not been written is no help to anyone.
            return read.Succeeded
                ? Outcome<Manifest>.Of(read.Value)
                : Outcome<Manifest>.Failed(read.Diagnostics.Select(diagnostic => diagnostic with { Location = null }).ToList(), read.OmittedDiagnostics);
        }

        /// <summary>
        /// Installs what the lockfile holds, choosing nothing and writing nothing: each locked package
        /// is held to what is published, and one whose locked bytes are in the cache is not asked of
        /// GitHub at all.
        /// </summary>
        /// <param name="json">Whether to answer a tool, with one JSON object.</param>
        public async Task<int> InstallLockedAsync(IReadOnlyList<RepositoryRef> alsoLookIn, bool json, CancellationToken cancellationToken)
        {
            var check = await FetchLockedAsync(alsoLookIn, cancellationToken);
            if (!check.Succeeded)
            {
                return DiagnosticPrinter.Report(Host, check.Diagnostics, check.OmittedDiagnostics, json);
            }

            var said = check.Graph.Packages.Count == 0
                ? "Nothing to install: the project depends on no package."
                : $"Installed what {LockfileReader.FileName} holds: {Count(check.Graph.Packages.Count)}.";
            return Done(json, check.Diagnostics, check.OmittedDiagnostics, written: false, Lockfile!, said + Environment.NewLine);
        }

        /// <summary>
        /// Says that a command found nothing to change: to a person in the command's own words, and
        /// to a tool as a lockfile that holds what it held. Only for a project whose lockfile
        /// answers its manifest.
        /// </summary>
        public int Unchanged(string said, bool json) => Done(json, [], 0, written: false, Lockfile!, said + Environment.NewLine);

        /// <summary>
        /// Holds each locked package to what is published and fetches what the cache does not hold,
        /// and says nothing itself: what it found is for the command to say. A build begins with this,
        /// as an install is nothing else.
        /// </summary>
        public async Task<ResolveResult> FetchLockedAsync(IReadOnlyList<RepositoryRef> alsoLookIn, CancellationToken cancellationToken)
        {
            var check = await LockCheck.CheckAsync(Lockfile!, Source(alsoLookIn), cancellationToken);
            if (check.Succeeded)
            {
                await FetchAsync(check.Graph, cancellationToken);
            }

            return check;
        }

        /// <summary>
        /// Resolves a manifest, and if nothing stops the resolution fetches what it chose and writes
        /// the lockfile, and the manifest too when it was changed. Nothing is written unless all of
        /// it can be.
        /// </summary>
        /// <param name="source">The source the command has been using, so that what it learned is not asked again.</param>
        /// <param name="said">What the command did to the manifest, said first when it has been done. Null when it changed nothing.</param>
        /// <param name="json">Whether to answer a tool, with one JSON object.</param>
        public async Task<int> ResolveAsync(Manifest manifest, DirectPackageSource source, string? said, bool json, CancellationToken cancellationToken)
        {
            var result = await DirectResolver.ResolveAsync(manifest, Lockfile, source, cancellationToken);
            if (!result.Succeeded)
            {
                return DiagnosticPrinter.Report(Host, result.Diagnostics, result.OmittedDiagnostics, json);
            }

            // Fetched before anything is written, so that a project is never left locked to what could not be had.
            await FetchAsync(result.Graph, cancellationToken);

            // The manifest and the lockfile, or neither: a manifest that asks for what no lockfile answers is half of a command.
            var locked = result.Graph.ToLockfile(manifest);
            (string Name, string Text)[] lockfile = [(LockfileReader.FileName, LockfileWriter.Write(locked))];
            (string Name, string Text)[] files = said is null ? lockfile : [(ManifestReader.FileName, ManifestWriter.Write(manifest)), .. lockfile];
            if (ProjectFiles.Write(Host.WorkingDirectory, files) is { } problem)
            {
                // What was only a warning is said with what stopped the command, so that nothing found on the way is lost.
                return DiagnosticPrinter.Report(Host, [problem, .. result.Diagnostics], result.OmittedDiagnostics, json);
            }

            return Done(json, result.Diagnostics, result.OmittedDiagnostics, written: true, locked, (said is null ? "" : said + Environment.NewLine) + Summary(Lockfile, locked));
        }

        /// <summary>
        /// Resolves a manifest and says what would follow, and writes nothing to the project. What it
        /// has to download to find out stays in the cache, where it does no harm.
        /// </summary>
        /// <param name="json">Whether to answer a tool, with one JSON object.</param>
        public async Task<int> PreviewAsync(Manifest manifest, DirectPackageSource source, string? said, bool json, CancellationToken cancellationToken)
        {
            var result = await DirectResolver.ResolveAsync(manifest, Lockfile, source, cancellationToken);
            if (!result.Succeeded)
            {
                return DiagnosticPrinter.Report(Host, result.Diagnostics, result.OmittedDiagnostics, json);
            }

            var would = result.Graph.ToLockfile(manifest);
            return Done(
                json,
                result.Diagnostics,
                result.OmittedDiagnostics,
                written: false,
                would,
                (said is null ? "" : said + Environment.NewLine) + Summary(Lockfile, would, done: false) + "Nothing was written." + Environment.NewLine);
        }

        // What a command did. A tool is given one object, with the warnings in it. A person is
        // given the warnings where problems go, and then the command's own words.
        private int Done(bool json, IReadOnlyList<Diagnostic> warnings, int omitted, bool written, Lockfile after, string said)
        {
            if (json)
            {
                Host.Out.Write(LockReport.Json(warnings, omitted, written, Lockfile, after));
            }
            else
            {
                DiagnosticPrinter.Print(Host.Error, warnings, omitted);
                Host.Out.Write(said);
            }

            return ExitStatus.Success;
        }

        /// <summary>What a new lockfile holds, said against the one before it. Each line ends with the host's line ending.</summary>
        /// <param name="done">Whether the new lockfile has been written, as against only worked out.</param>
        public static string Summary(Lockfile? before, Lockfile after, bool done = true)
        {
            var changes = LockChanges.Between(before, after);
            var count = Count(after.Packages.Count);
            var lines = changes.Count > 0
                ? [$"{LockfileReader.FileName} {(done ? "now holds" : "would hold")} {count}:", .. changes.Select(change => "  " + change)]
                : new List<string>
                {
                    before is null
                        ? $"{LockfileReader.FileName} {(done ? "holds" : "would hold")} no package: the project depends on none."
                        : $"{LockfileReader.FileName} {(done ? "holds" : "would hold")} the same {count} as before.",
                };
            return string.Concat(lines.Select(line => line + Environment.NewLine));
        }

        private static string Count(int packages) => packages switch { 0 => "no package", 1 => "1 package", _ => $"{packages} packages" };

        // Every archive of a graph is in the cache by now: the source keeps what it downloads, and
        // what it did not download it took from the cache. Here each is unpacked beside itself.
        private async Task FetchAsync(ResolvedGraph graph, CancellationToken cancellationToken)
        {
            foreach (var package in graph.Packages)
            {
                var published = package.Published;
                if (Directory.Exists(Store.DirectoryPath(published.Name, published.Version, published.Sha256)))
                {
                    continue;
                }

                Store.Unpack(published.Name, published.Version, published.Sha256, await FilesOfAsync(published.Name, published.Version, published.Sha256, cancellationToken));
            }
        }

        /// <summary>
        /// The files of a package whose archive has been fetched, read from the archive itself, which
        /// is held to its digest as it is read. A build takes a package's manifest from here, and
        /// holds the unpacked files to these before it compiles them.
        /// </summary>
        public async Task<IReadOnlyList<ArchiveFile>> FilesOfAsync(PackageName name, SemanticVersion version, Sha256Digest digest, CancellationToken cancellationToken)
        {
            if (await Store.FindAsync(name, version, digest, cancellationToken) is not { } archive)
            {
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.CacheUnusable,
                    $"The archive of \"{name}\" {version} is gone from the cache.",
                    "it was there a moment ago, so something else is changing the cache while pmj uses it",
                    "run the command again"));
            }

            var files = PackageArchive.Read(archive);
            return files.Succeeded ? files.Value : throw new PackageSourceException(files.Diagnostics[0]);
        }
    }
}
