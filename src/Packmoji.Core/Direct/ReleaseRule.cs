using Packmoji.Core.Archives;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// The rule a release is taken by. A tag says which version of which package a release is, and a
    /// tag is only a name: so the release's file has to be an archive that pmj wrote, and the manifest
    /// in it has to give that package and that version, and has to say that the package lives in the
    /// repository the release was found in.
    /// </summary>
    /// <remarks>
    /// It is in one place because two things take releases: pmj, when it finds packages on GitHub
    /// itself, and whatever keeps a registry. A release that one of them took and the other refused
    /// would be a package that can be had one way and not the other.
    /// </remarks>
    public static class ReleaseRule
    {
        /// <param name="repository">The repository the release was found in.</param>
        /// <param name="archive">The bytes of the release's file.</param>
        public static ReleaseVerdict Judge(PackageName name, SemanticVersion version, RepositoryRef repository, ReadOnlyMemory<byte> archive)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            ArgumentNullException.ThrowIfNull(repository);

            var files = PackageArchive.Read(archive);
            if (!files.Succeeded)
            {
                return ReleaseVerdict.Refused(ReleaseFault.NotAnArchive, files.Diagnostics[0].Reason);
            }

            var read = ManifestReader.Read(files.Value.First(file => file.Path.Value == ManifestReader.FileName).Content);
            if (!read.Succeeded)
            {
                var first = read.Diagnostics[0];
                return ReleaseVerdict.Refused(ReleaseFault.ManifestUnreadable, $"its {ManifestReader.FileName} cannot be read: {first.Message} That is because {first.Reason}");
            }

            var manifest = read.Value;
            if (manifest.Package.Name != name)
            {
                return ReleaseVerdict.Refused(ReleaseFault.OtherPackage, $"the manifest in its archive is that of \"{manifest.Package.Name}\"", manifest);
            }

            if (manifest.Package.Version != version)
            {
                return ReleaseVerdict.Refused(ReleaseFault.OtherVersion, $"the manifest in its archive gives the version {manifest.Package.Version}", manifest);
            }

            if (manifest.Repository != repository)
            {
                return ReleaseVerdict.Refused(
                    ReleaseFault.OtherRepository,
                    $"the manifest in its archive says that the package lives in {manifest.Repository}, and a release counts only where its package says it lives",
                    manifest);
            }

            return ReleaseVerdict.Of(
                new PublishedVersion(
                    name,
                    version,
                    VersionStatus.Active,
                    repository,
                    Sha256Digest.Of(archive.Span),
                    VerificationLevel.Checksum,
                    manifest.Dependencies ?? []),
                manifest);
        }
    }
}
