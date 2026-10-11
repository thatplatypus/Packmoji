using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// One file of a release, as the host lists it. A host may say less of a file than this holds, and
    /// what it did not say is not known: that is no reason to refuse its list.
    /// </summary>
    /// <param name="Name">The file's name.</param>
    /// <param name="Size">How many bytes it is. Zero when the host did not say.</param>
    /// <param name="Sha256">The digest the host took of it when it was uploaded. Null when the host did not say.</param>
    /// <param name="IsUploaded">Whether the host says that the whole of it is there.</param>
    public sealed record ReleaseFile(string Name, long Size = 0, Sha256Digest? Sha256 = null, bool IsUploaded = false);
}
