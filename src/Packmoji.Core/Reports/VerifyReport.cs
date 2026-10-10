using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Reports
{
    /// <summary>What <c>pmj verify</c> says to a tool.</summary>
    public static class VerifyReport
    {
        /// <summary>
        /// One JSON object: <c>ok</c>, the problems found, and every package that was held to the
        /// lockfile. A package is named in a problem's text when it is not what the lockfile holds.
        /// </summary>
        public static string Json(Lockfile lockfile, IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics = 0)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            ArgumentNullException.ThrowIfNull(diagnostics);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            ReportJson.WriteOutcome(json, diagnostics, omittedDiagnostics);
            json.WriteStartArray("packages");
            foreach (var package in ReportJson.InOrder(lockfile))
            {
                json.WriteStartObject();
                ReportJson.WritePackage(json, package);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return json.ToString();
        }
    }
}
