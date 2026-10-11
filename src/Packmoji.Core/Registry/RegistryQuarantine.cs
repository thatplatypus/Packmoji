using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Registry
{
    /// <summary>What was seen of a version's bytes when they were found to be other than the ones first recorded.</summary>
    /// <param name="Seen">When they were found changed.</param>
    /// <param name="Sha256">The digest of what the release then held.</param>
    public sealed record RegistryQuarantine(DateTimeOffset Seen, Sha256Digest Sha256);
}
