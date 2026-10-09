namespace Packmoji.Core.Lockfiles
{
    /// <summary>How far a locked package was verified when it was locked.</summary>
    public enum VerificationLevel
    {
        /// <summary>The archive's bytes matched the digest recorded when the version was first seen.</summary>
        Checksum,

        /// <summary>As well as that, the archive has a build attestation signed by a workflow in the package's own repository.</summary>
        Attestation,
    }
}
