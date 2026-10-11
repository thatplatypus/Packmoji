namespace Packmoji.Core.Registry
{
    /// <summary>What a run made: the registry's next state, and what the run has to say, in the order it was found.</summary>
    public sealed record RegistryRunResult(RegistryState After, IReadOnlyList<RegistryNote> Notes);
}
