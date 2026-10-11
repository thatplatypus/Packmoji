using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// A package source with a registry behind it. Every version asked about is answered from its
    /// package's file, which says where the version lives, which bytes it is, what it needs and where
    /// it stands: nothing is downloaded to find any of that out, and nobody has to be told where a
    /// package is kept.
    /// </summary>
    /// <remarks>
    /// A package's file is read once for a command, however many of its versions are asked about. A
    /// file that could not be read is not remembered: whatever asks next asks again, and is told again.
    /// </remarks>
    public sealed class RegistryPackageSource(IRegistryFiles files) : IPackageSource, IExplainsMissing
    {
        private const string AskGitHubItself = "to ask GitHub itself, run the command again with --direct";

        // What was read of each package. Null is a package the registry does not have.
        private readonly Dictionary<PackageName, RegistryPackage?> _read = [];

        public async ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);

            // How far a version is verified is what pmj itself does with it, which is to hold its
            // bytes to a digest. What a registry says of an attestation is for a pmj that can check
            // one: a lockfile that said so today would say what nobody did.
            return (await PackageAsync(name, cancellationToken))?.Find(version) is { } held
                ? new PublishedVersion(name, version, held.Status, held.Source, held.Sha256, VerificationLevel.Checksum, held.Dependencies)
                : null;
        }

        /// <summary>
        /// The versions of a package that something may begin to use, lowest first: those that have
        /// been neither withdrawn nor quarantined. Null when the registry does not have the package.
        /// </summary>
        public async Task<IReadOnlyList<SemanticVersion>?> ListVersionsAsync(PackageName name, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            return (await PackageAsync(name, cancellationToken))?.Versions
                .Where(version => version.Status == VersionStatus.Active)
                .Select(version => version.Version)
                .ToList();
        }

        /// <summary>
        /// Of a version this source did not give. That a registry does not hold it is all a registry
        /// can say: a release may be newer than the last run, or may have been refused, so the
        /// resolver's word, that the version was never published, would be more than is known.
        /// </summary>
        Diagnostic? IExplainsMissing.Missing(PackageName name, SemanticVersion version, string chain) => Absent(name, version, chain, locked: false);

        /// <summary>
        /// Of a version a lockfile holds and this source did not give. A registry removes no
        /// version, so nothing has gone: the lockfile was written without this registry, or edited.
        /// </summary>
        Diagnostic? IExplainsMissing.MissingThoughLocked(PackageName name, SemanticVersion version, string? chain) => Absent(name, version, chain, locked: true);

        /// <summary>
        /// Says that a registry does not have a package: which file was asked for and is not there,
        /// and how to look on GitHub itself.
        /// </summary>
        /// <remarks>
        /// An address with no registry behind it answers of every file as a registry does of a
        /// package it does not have. Which of the two it is cannot be told from here. So the file is
        /// named in full, where a mistake in the address can be seen, and the first thing said to do
        /// is to check the address.
        /// </remarks>
        public static Diagnostic NotFound(PackageName name, string address)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(address);
            return NotFound(name, address, "");
        }

        /// <param name="asked">How the package came to be asked about, to follow the reason, or nothing.</param>
        private static Diagnostic NotFound(PackageName name, string address, string asked) =>
            new(
                DiagnosticCodes.PackageNotFound,
                $"\"{name}\" is not in the registry.",
                $"{address}/{RegistryPaths.Package(name)} is not there{asked}",
                $"check that {address} is the address a registry's v1 is under, for one that is not answers the same way; if it is, and the package's repository is on the registry's list, a release is in the registry within a day; {AskGitHubItself}");

        // A package that was never read is one this source was never asked about, and it has nothing to say of it.
        private Diagnostic? Absent(PackageName name, SemanticVersion version, string? chain, bool locked)
        {
            if (!_read.TryGetValue(name, out var package))
            {
                return null;
            }

            if (package is null)
            {
                return NotFound(name, files.Address, Asked(locked ? $"{LockfileReader.FileName} holds {version} of it" : null, chain));
            }

            var holds = package.Versions.Count switch
            {
                0 => "no version of it",
                1 => "1 version of it, and this is not one of them",
                var count => $"{count} versions of it, and this is not one of them",
            };

            // Which of a repository's releases were refused is in a file of its own, and a package's
            // versions say which repository that is.
            var refused = package.Versions.Count == 0 ? "" : $", unless the registry refused it, which {files.Address}/{RegistryPaths.Refused(package.Versions[^1].Source)} says";
            return new Diagnostic(
                DiagnosticCodes.ResolveVersionMissing,
                $"Version {version} of \"{name}\" is not in the registry.",
                $"{files.Address}/{RegistryPaths.Package(name)} holds {holds}{Asked(locked ? $"{LockfileReader.FileName} holds it" : null, chain)}",
                locked
                    ? $"keep {LockfileReader.FileName}: to use what it holds, ask GitHub itself, by running the command again with --direct; to use what the registry holds, ask for one of its versions in {ManifestReader.FileName}"
                    : $"ask for a version the registry holds; a release is in the registry within a day of being made{refused}; {AskGitHubItself}");
        }

        // How a version came to be asked about, to follow a reason: what holds it, and what leads to it. There is always one of the two.
        private static string Asked(string? held, string? chain) =>
            "; " + string.Join(", and ", new[] { held, chain is null ? null : $"it is asked for: {chain}" }.Where(part => part is not null));

        private async Task<RegistryPackage?> PackageAsync(PackageName name, CancellationToken cancellationToken)
        {
            if (_read.TryGetValue(name, out var read))
            {
                return read;
            }

            var path = RegistryPaths.Package(name);
            if (await files.ReadAsync(path, RegistryPackageReader.MaxBytes, cancellationToken) is not { } bytes)
            {
                return _read[name] = null;
            }

            var result = RegistryPackageReader.Read(bytes, name, path);
            if (!result.Succeeded)
            {
                // What the file holds decides which bytes a version is, so a file that does not read
                // is never read in part, and never passed over for looking somewhere else.
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.RegistryInvalid,
                    $"The registry's file of \"{name}\" cannot be read.",
                    $"{files.Address}/{path} does not read: {result.Diagnostics[0].Message}",
                    "tell whoever keeps the registry; to ask GitHub itself in the meantime, run the command again with --direct"));
            }

            return _read[name] = result.Value;
        }
    }
}
