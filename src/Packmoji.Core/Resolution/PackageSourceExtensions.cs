using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Resolution
{
    internal static class PackageSourceExtensions
    {
        /// <summary>
        /// Asks a source for one version, and holds it to answering for what was asked. An answer for
        /// another package or another version is a fault in the source, and no diagnostic could tell a
        /// person what to do about it, so it is thrown.
        /// </summary>
        public static async ValueTask<PublishedVersion?> AskAsync(this IPackageSource source, PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var published = await source.FindAsync(name, version, cancellationToken);
            if (published is not null && (published.Name != name || published.Version != version))
            {
                throw new InvalidOperationException(
                    $"The package source was asked for {name}@{version} and answered with {published.Name}@{published.Version}.");
            }

            return published;
        }
    }
}
