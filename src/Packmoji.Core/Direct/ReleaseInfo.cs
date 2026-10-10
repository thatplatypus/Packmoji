namespace Packmoji.Core.Direct
{
    /// <summary>One published release of a repository. A draft is not published, and is never one of these.</summary>
    /// <param name="Tag">The release's tag.</param>
    /// <param name="IsPrerelease">Whether the release is marked as a pre-release.</param>
    /// <param name="Assets">The names of the files the release carries.</param>
    public sealed record ReleaseInfo(string Tag, bool IsPrerelease, IReadOnlyList<string> Assets);
}
