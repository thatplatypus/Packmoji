namespace Packmoji.Core.Reports
{
    /// <summary>What became of a package between one lockfile and the next.</summary>
    public enum LockChangeKind
    {
        /// <summary>The new lockfile holds it, and the one before did not.</summary>
        Added,

        /// <summary>The one before held it, and the new lockfile does not.</summary>
        Removed,

        /// <summary>Both hold it, at different versions. The new one may be the lower: a requirement can be brought down.</summary>
        Moved,
    }
}
