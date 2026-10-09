namespace Packmoji.Core.Manifests
{
    /// <param name="RequireAttestation">Whether every dependency must have a verified build attestation. Null when the manifest does not say.</param>
    public sealed record PolicySection(bool? RequireAttestation = null);
}
