namespace Packmoji.Core.Resolution
{
    /// <summary>Where a published version stands. A version is never deleted, so this is all that can change about one.</summary>
    public enum VersionStatus
    {
        Active,

        /// <summary>Withdrawn by its author. What already uses it goes on using it, and nothing new may start to.</summary>
        Yanked,

        /// <summary>Its bytes changed after it was published. It must not be used at all.</summary>
        Quarantined,
    }
}
