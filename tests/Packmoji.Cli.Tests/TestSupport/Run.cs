namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>What one run of pmj gave: how it ended, and what it wrote to its two streams.</summary>
    internal sealed record Run(int Status, string Output, string Error);
}
