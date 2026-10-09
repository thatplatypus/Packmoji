namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// One problem, in the three parts every Packmoji error has: what failed, why, and what to do next.
    /// </summary>
    /// <param name="Code">A stable identifier from <see cref="DiagnosticCodes"/>. Tools match on this and never on the text.</param>
    /// <param name="Message">What failed.</param>
    /// <param name="Reason">Why it failed.</param>
    /// <param name="Fix">What to do next.</param>
    /// <param name="Location">Where in a file. Null when the problem is not at a place in a file.</param>
    public sealed record Diagnostic(string Code, string Message, string Reason, string Fix, SourceLocation? Location = null);
}
