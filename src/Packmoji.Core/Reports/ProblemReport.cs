using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;

namespace Packmoji.Core.Reports
{
    /// <summary>What a command that was asked to answer a tool says when it could not do what it was asked.</summary>
    public static class ProblemReport
    {
        /// <summary>One JSON object: <c>ok</c>, which is false when any of the problems is an error, and the problems.</summary>
        public static string Json(IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics = 0)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            ReportJson.WriteOutcome(json, diagnostics, omittedDiagnostics);
            json.WriteEndObject();
            return json.ToString();
        }
    }
}
