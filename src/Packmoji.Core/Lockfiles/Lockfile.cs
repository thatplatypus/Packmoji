namespace Packmoji.Core.Lockfiles
{
    /// <summary>What a <c>packmoji.lock</c> says: what was asked for, and the exact packages that answer it.</summary>
    public sealed record Lockfile(RootRequirements Root, IReadOnlyList<LockedPackage> Packages)
    {
        public const int FormatVersion = 1;
    }
}
