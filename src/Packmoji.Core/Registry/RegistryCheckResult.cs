namespace Packmoji.Core.Registry
{
    /// <summary>What <see cref="RegistryCheck"/> found between two states of a registry.</summary>
    /// <param name="Changes">What changed, one change to a line, in an order that is the same every time. Each line is fit to print.</param>
    /// <param name="Broken">Each thing that a run may not do and that was done. Empty when the change is allowed.</param>
    /// <param name="Summary">The changes counted by kind, as in <c>3 added, 1 refused</c>. Empty when nothing changed, or when the change is not allowed.</param>
    public sealed record RegistryCheckResult(IReadOnlyList<string> Changes, IReadOnlyList<string> Broken, string Summary)
    {
        /// <summary>Whether the second state may follow the first.</summary>
        public bool Allowed => Broken.Count == 0;
    }
}
