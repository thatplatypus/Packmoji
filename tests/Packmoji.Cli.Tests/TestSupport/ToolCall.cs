namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>One run of a made-up tool: which tool its file said it was, where the file is, and what it was given.</summary>
    internal sealed record ToolCall(string Tool, string Program, IReadOnlyList<string> Arguments, string WorkingDirectory);
}
