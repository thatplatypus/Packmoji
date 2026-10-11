namespace Packmoji.Core.Registry
{
    /// <summary>What kind of thing a run has to say.</summary>
    public enum RegistryNoteKind
    {
        /// <summary>A version was indexed.</summary>
        Added,

        /// <summary>A claim to be a version was refused.</summary>
        Refused,

        /// <summary>A version's bytes were found changed, and it was quarantined.</summary>
        Quarantined,

        /// <summary>A release that had been refused is gone.</summary>
        NoLongerRefused,

        /// <summary>The release of an indexed version is no longer there.</summary>
        Gone,

        /// <summary>A claim was not looked at, because the run had reached its limit.</summary>
        Left,

        /// <summary>A list or a file could not be had.</summary>
        Unreachable,
    }
}
