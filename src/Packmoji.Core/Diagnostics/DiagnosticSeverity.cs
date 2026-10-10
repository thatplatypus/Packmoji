namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// Whether a problem stops what was being done. Nearly every problem does. A warning is for the
    /// few that a person should hear of while their work goes on, such as a version already in use
    /// that its author has since withdrawn.
    /// </summary>
    public enum DiagnosticSeverity
    {
        Error,
        Warning,
    }
}
