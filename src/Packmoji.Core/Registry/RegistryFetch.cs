using Packmoji.Core.Archives;
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
    /// Fetches the archive of a version that a registry said to use. A registry holds no bytes: it
    /// says where a version's release is and which bytes it is. So the bytes come from the release,
    /// and are held to what the registry said of them before they are kept.
    /// </summary>
    /// <remarks>
    /// What is held is the digest, and that the manifest in the archive gives the package, the
    /// version, the repository and the dependencies that the registry gave. A registry that is
    /// wrong, or that someone has changed, can then name a digest for a version and do no more: it
    /// cannot have a resolution choose by one set of dependencies and a build use another.
    ///
    /// Bytes that the cache already holds are held to the record in the same way. With a full
    /// cache nothing is downloaded, and a record that says other than the bytes do would otherwise
    /// be written into a lockfile before anything had looked.
    /// </remarks>
    public static class RegistryFetch
    {
        private const string TellTheKeeper = "tell whoever keeps the registry: what it holds of this version was not made from the version's own archive";

        /// <summary>
        /// Makes sure the archive of a version is in the cache, and is what the registry said of the
        /// version. One that is there under its digest is not downloaded again, and is held to the
        /// record all the same.
        /// </summary>
        /// <param name="published">The version, as the registry gave it.</param>
        public static async Task FetchAsync(PublishedVersion published, IReleaseHost releases, IAssetStore store, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(published);
            ArgumentNullException.ThrowIfNull(releases);
            ArgumentNullException.ThrowIfNull(store);
            var (name, version) = (published.Name, published.Version);
            if (await store.FindAsync(name, version, published.Sha256, cancellationToken) is { } kept && Sha256Digest.Of(kept.Span) == published.Sha256)
            {
                Hold(published, kept);
                return;
            }

            var tag = ReleaseTag.For(name, version);
            if (await releases.DownloadAsync(published.Source, tag, AssetName.For(name, version), PackageArchive.MaxBytes, cancellationToken) is not { } archive)
            {
                throw Mismatch(
                    name,
                    version,
                    $"the release {tag} in {published.Source}, which the registry recorded, is no longer there",
                    $"use another version of it: pmj update, or pmj add {name}@<version>");
            }

            // Held before anything else is done with the bytes, so that what took a release's place is never read as an archive and never kept.
            var digest = Sha256Digest.Of(archive.Span);
            if (digest != published.Sha256)
            {
                throw Mismatch(
                    name,
                    version,
                    $"the registry recorded the SHA-256 {published.Sha256} when it first saw the release {tag} in {published.Source}, and the file there now has {digest}",
                    "do not use this version. Tell its author, and whoever keeps the registry: a version's bytes never change, and the registry quarantines one whose bytes did");
            }

            Hold(published, archive);
            await store.KeepAsync(name, version, digest, archive, cancellationToken);
        }

        // Bytes of the recorded digest, held to what else the registry said of the version: that
        // they are that package at that version, living where it said, and needing what it said.
        private static void Hold(PublishedVersion published, ReadOnlyMemory<byte> archive)
        {
            var (name, version) = (published.Name, published.Version);
            var verdict = ReleaseRule.Judge(name, version, published.Source, archive);
            if (!verdict.Taken)
            {
                throw Mismatch(name, version, verdict.Reason, TellTheKeeper);
            }

            var recorded = Needs(published.Dependencies);
            var says = Needs(verdict.Published.Dependencies);
            if (!recorded.SequenceEqual(says, StringComparer.Ordinal))
            {
                throw Mismatch(name, version, $"the registry says it needs {Listed(recorded)}, and the manifest in its archive says {Listed(says)}", TellTheKeeper);
            }
        }

        private static List<string> Needs(IReadOnlyList<Dependency> dependencies) =>
            dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal).ToList();

        private static string Listed(List<string> needs) => needs.Count == 0 ? "nothing" : string.Join(", ", needs);

        private static PackageSourceException Mismatch(PackageName name, SemanticVersion version, string reason, string fix) =>
            new(new Diagnostic(
                DiagnosticCodes.RegistryMismatch,
                $"What GitHub serves for \"{name}\" {version} is not what the registry recorded.",
                reason,
                fix));
    }
}
