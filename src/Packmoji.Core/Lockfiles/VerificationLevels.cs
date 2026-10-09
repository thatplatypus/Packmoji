namespace Packmoji.Core.Lockfiles
{
    /// <summary>How a verification level is spelled in a lockfile, in the one place the reader and the writer both use.</summary>
    internal static class VerificationLevels
    {
        private const string Checksum = "checksum";
        private const string Attestation = "attestation";

        public static string Name(VerificationLevel level) => level == VerificationLevel.Attestation ? Attestation : Checksum;

        public static bool TryParse(string text, out VerificationLevel level)
        {
            level = text == Attestation ? VerificationLevel.Attestation : VerificationLevel.Checksum;
            return text is Checksum or Attestation;
        }
    }
}
