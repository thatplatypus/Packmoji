using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Registry
{
    /// <summary>One release that claimed to be a version of a package, and was not taken as one.</summary>
    /// <param name="Tag">The release's tag, which names the package and the version it claimed to be.</param>
    /// <param name="Sha256">The host's digest of the file that was refused. While the release holds that file, it is not looked at again.</param>
    /// <param name="Seen">When it was refused.</param>
    /// <param name="Code">The code of the problem, as a diagnostic of pmj's has one.</param>
    /// <param name="Why">Why it was refused, in one line that is fit to show: it repeats nothing of the file but what was read from it and found well formed.</param>
    public sealed record RefusedRelease(string Tag, Sha256Digest Sha256, DateTimeOffset Seen, string Code, string Why);
}
