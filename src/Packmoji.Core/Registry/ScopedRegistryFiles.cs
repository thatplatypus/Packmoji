using Packmoji.Core.Direct;
using Packmoji.Core.Identity;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// A registry that is asked for the file of no package outside a limit on scopes, whatever
    /// asked. Everything above this refuses a package of another scope before it comes here. This is
    /// the one place a request for a package's file cannot go round, and so the line that keeps the
    /// promise if a line above it is ever wrong: as <see cref="ScopedReleaseHost"/> is for releases.
    /// </summary>
    public sealed class ScopedRegistryFiles(IRegistryFiles inner, ScopeLimit allowed) : IRegistryFiles
    {
        public string Address => inner.Address;

        public Task<ReadOnlyMemory<byte>?> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (RegistryPaths.TryParsePackage(path, out var name) && !allowed.Allows(name))
            {
                throw new PackageSourceException(allowed.Refuses(name, "the registry was about to be asked for it"));
            }

            return inner.ReadAsync(path, maxBytes, cancellationToken);
        }
    }
}
