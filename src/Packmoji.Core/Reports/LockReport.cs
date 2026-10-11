using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Reports
{
    /// <summary>What a command that locks packages says to a tool: <c>add</c>, <c>remove</c>, <c>install</c> and <c>update</c>.</summary>
    public static class LockReport
    {
        /// <summary>
        /// One JSON object: <c>ok</c> and the problems, whether the project's files were written, what
        /// the lockfile holds that it did not hold before, and every package it holds now, each as
        /// <c>tree --json</c> gives a package. A tool that restores a project reads the last, and
        /// need not run pmj a second time to learn what was locked.
        /// </summary>
        /// <param name="diagnostics">Warnings alone: a command that met an error has nothing of this to say.</param>
        /// <param name="written">False when nothing had to be written, and when the command was asked only what it would do.</param>
        /// <param name="before">The lockfile the project had. Null when it had none.</param>
        /// <param name="after">The lockfile it has now, or would have.</param>
        public static string Json(IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics, bool written, Lockfile? before, Lockfile after)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            ArgumentNullException.ThrowIfNull(after);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            ReportJson.WriteOutcome(json, diagnostics, omittedDiagnostics);
            json.WriteBoolean("written", written);
            json.WriteStartArray("changes");
            foreach (var change in LockChanges.Of(before, after))
            {
                json.WriteStartObject();
                json.WriteString("change", change.Kind switch { LockChangeKind.Added => "added", LockChangeKind.Removed => "removed", _ => "moved" });
                json.WriteString("name", change.Name.ToString());
                if (change.From is { } from)
                {
                    json.WriteString("from", from.ToString());
                }

                if (change.To is { } to)
                {
                    json.WriteString("to", to.ToString());
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            ReportJson.WritePackages(json, after);
            json.WriteEndObject();
            return json.ToString();
        }
    }
}
