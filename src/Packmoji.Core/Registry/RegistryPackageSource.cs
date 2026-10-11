using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
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
        // What was read of each package. Null is a package the registry does not have.
        private readonly Dictionary<PackageName, RegistryPackage?> _read = [];

        public async ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            return (await PackageAsync(name, cancellationToken))?.Find(version) is { } held
                ? new PublishedVersion(name, version, held.Status, held.Source, held.Sha256, held.Verified, held.Dependencies)
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
        /// Of a version this source did not give: when the registry does not have the package at all,
        /// that is what there is to say. A registry knows every version of a package it does have, so
        /// of a version that is missing among them the resolver's own word stands: it was never published.
        /// </summary>
        Diagnostic? IExplainsMissing.Missing(PackageName name, SemanticVersion version, string chain) =>
            _read.TryGetValue(name, out var package) && package is null ? NotFound(name, files.Address, chain) : null;

        /// <summary>Says that a registry does not have a package: where the registry is, and how to look on GitHub itself.</summary>
        /// <param name="askedFor">How a resolution came to ask for the package, when one did.</param>
        public static Diagnostic NotFound(PackageName name, string address, string? askedFor = null)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(address);
            return new Diagnostic(
                DiagnosticCodes.PackageNotFound,
                $"\"{name}\" is not in the registry.",
                $"the registry at {address} has no file of it{(askedFor is null ? "" : $"; it is asked for: {askedFor}")}",
                "if its repository is on the registry's list, a release is in the registry within the hour; to ask GitHub itself, run the command again with --direct");
        }

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
