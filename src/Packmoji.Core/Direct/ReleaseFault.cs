namespace Packmoji.Core.Direct
{
    /// <summary>Why a release is not the version of the package that its tag says it is.</summary>
    public enum ReleaseFault
    {
        /// <summary>Its file is not an archive as <c>pmj pack</c> writes one.</summary>
        NotAnArchive,

        /// <summary>The manifest in its archive cannot be read.</summary>
        ManifestUnreadable,

        /// <summary>The manifest in its archive is that of another package.</summary>
        OtherPackage,

        /// <summary>The manifest in its archive gives another version.</summary>
        OtherVersion,

        /// <summary>The manifest in its archive says that the package lives in another repository.</summary>
        OtherRepository,
    }
}
