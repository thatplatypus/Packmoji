namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// One problem, in the three parts every Packmoji error has: what failed, why, and what to do next.
    /// Its text is always fit to print, whatever it was made from: a diagnostic repeats what a file
    /// said, and <see cref="Printable"/> is applied to each part however the part is set.
    /// </summary>
    /// <param name="Code">A stable identifier from <see cref="DiagnosticCodes"/>. Tools match on this and never on the text.</param>
    /// <param name="Message">What failed.</param>
    /// <param name="Reason">Why it failed.</param>
    /// <param name="Fix">What to do next.</param>
    /// <param name="Location">Where in a file. Null when the problem is not at a place in a file.</param>
    public sealed record Diagnostic(string Code, string Message, string Reason, string Fix, SourceLocation? Location = null)
    {
        private readonly string _message = Printable.Text(Message);
        private readonly string _reason = Printable.Text(Reason);
        private readonly string _fix = Printable.Text(Fix);

        public string Message
        {
            get => _message;
            init => _message = Printable.Text(value);
        }

        public string Reason
        {
            get => _reason;
            init => _reason = Printable.Text(value);
        }

        public string Fix
        {
            get => _fix;
            init => _fix = Printable.Text(value);
        }
    }
}
