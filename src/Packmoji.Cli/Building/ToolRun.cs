namespace Packmoji.Cli.Building
{
    /// <summary>What one run of a tool gave: how it ended, and what it wrote to each of its two streams.</summary>
    public sealed record ToolRun(int Status, string Output, string Error);
}
