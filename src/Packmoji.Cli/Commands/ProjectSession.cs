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
        /// <param name="told">Where a package lives, for a package someone has said it of.</param>
        public DirectPackageSource Source(IReadOnlyDictionary<PackageName, RepositoryRef>? told = null) =>
            new(Host.Releases, Store, Manifest, Lockfile, told);

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
        public async Task<int> InstallLockedAsync(CancellationToken cancellationToken)
        {
            var check = await LockCheck.CheckAsync(Lockfile!, Source(), cancellationToken);
            DiagnosticPrinter.Print(Host.Error, check.Diagnostics, check.OmittedDiagnostics);
            if (!check.Succeeded)
            {
                return ExitStatus.Problem;
            }

            await FetchAsync(check.Graph, cancellationToken);
            Host.Out.WriteLine(check.Graph.Packages.Count == 0
                ? "Nothing to install: the project depends on no package."
                : $"Installed what {LockfileReader.FileName} holds: {Count(check.Graph.Packages.Count)}.");
            return ExitStatus.Success;
        }

        /// <summary>
        /// Resolves a manifest, and if nothing stops the resolution fetches what it chose and writes
        /// the lockfile, and the manifest too when it was changed. Nothing is written unless all of
        /// it can be.
        /// </summary>
        /// <param name="source">The source the command has been using, so that what it learned is not asked again.</param>
        /// <param name="said">What the command did to the manifest, said first when it has been done. Null when it changed nothing.</param>
        public async Task<int> ResolveAsync(Manifest manifest, DirectPackageSource source, string? said, CancellationToken cancellationToken)
        {
            var result = await DirectResolver.ResolveAsync(manifest, Lockfile, source, cancellationToken);
            DiagnosticPrinter.Print(Host.Error, result.Diagnostics, result.OmittedDiagnostics);
            if (!result.Succeeded)
            {
                return ExitStatus.Problem;
            }

            // Fetched before anything is written, so that a project is never left locked to what could not be had.
            await FetchAsync(result.Graph, cancellationToken);

            if (said is not null && ProjectFiles.Write(Host.WorkingDirectory, ManifestReader.FileName, ManifestWriter.Write(manifest)) is { } manifestProblem)
            {
                return DiagnosticPrinter.Report(Host, manifestProblem);
            }

            var locked = result.Graph.ToLockfile(manifest);
            if (ProjectFiles.Write(Host.WorkingDirectory, LockfileReader.FileName, LockfileWriter.Write(locked)) is { } lockProblem)
            {
                return DiagnosticPrinter.Report(Host, lockProblem);
            }

            if (said is not null)
            {
                Host.Out.WriteLine(said);
            }

            Host.Out.Write(Summary(Lockfile, locked));
            return ExitStatus.Success;
        }

        /// <summary>What a new lockfile holds, said against the one before it. Each line ends with the host's line ending.</summary>
        public static string Summary(Lockfile? before, Lockfile after)
        {
            var changes = LockChanges.Between(before, after);
            var lines = changes.Count > 0
                ? [$"{LockfileReader.FileName} now holds {Count(after.Packages.Count)}:", .. changes.Select(change => "  " + change)]
                : new List<string>
                {
                    before is null
                        ? $"{LockfileReader.FileName} holds no package: the project depends on none."
                        : $"{LockfileReader.FileName} holds the same {Count(after.Packages.Count)} as before.",
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

                if (await Store.FindAsync(published.Name, published.Version, published.Sha256, cancellationToken) is not { } archive)
                {
                    throw new PackageSourceException(new Diagnostic(
                        DiagnosticCodes.CacheUnusable,
                        $"The archive of \"{published.Name}\" {published.Version} is gone from the cache.",
                        "it was there a moment ago, so something else is changing the cache while pmj uses it",
                        "run the command again"));
                }

                var files = PackageArchive.Read(archive);
                if (!files.Succeeded)
                {
                    throw new PackageSourceException(files.Diagnostics[0]);
                }

                Store.Unpack(published.Name, published.Version, published.Sha256, files.Value);
            }
        }
    }
}
