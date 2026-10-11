namespace Packmoji.Core.Direct
{
    /// <summary>One published release of a repository. A draft is not published, and is never one of these.</summary>
    /// <param name="Tag">The release's tag.</param>
    /// <param name="IsPrerelease">Whether the release is marked as a pre-release.</param>
    /// <param name="Files">The files the release carries.</param>
    public sealed record ReleaseInfo(string Tag, bool IsPrerelease, IReadOnlyList<ReleaseFile> Files)
    {
        /// <summary>When the release was published. Null when the host did not say.</summary>
        public DateTimeOffset? Published { get; init; }

        /// <summary>Whether the host says that the release and its files can no longer be changed.</summary>
        public bool IsImmutable { get; init; }

        /// <summary>The names of the files the release carries.</summary>
        public IEnumerable<string> Assets => Files.Select(file => file.Name);
    }
}
