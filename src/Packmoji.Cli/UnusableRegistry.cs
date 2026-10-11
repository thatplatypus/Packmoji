using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Registry;

namespace Packmoji.Cli
{
    /// <summary>
    /// A registry, to a pmj that was told something about one that it cannot use. Nothing is asked
    /// of anyone: whatever first asks the registry for a file is given the problem instead. A
    /// command that asks a registry nothing never meets it, and so is not stopped by it.
    /// </summary>
    internal sealed class UnusableRegistry(Diagnostic problem) : IRegistryFiles
    {
        /// <summary>What was said of a registry that cannot be used.</summary>
        public Diagnostic Problem => problem;

        // There is none to give: what was said could not be used, and may hold what should not be shown.
        public string Address => "";

        public Task<ReadOnlyMemory<byte>?> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromException<ReadOnlyMemory<byte>?>(new PackageSourceException(problem));
    }
}
