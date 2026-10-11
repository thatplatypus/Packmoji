using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Reports
{
    /// <summary>
    /// What every answer for a tool has in common: whether the command did what it was asked, and the
    /// problems it found. They are written in one place so that a tool reads them one way.
    /// </summary>
    internal static class ReportJson
    {
        /// <summary>Writes <c>ok</c>, <c>diagnostics</c> and <c>omittedDiagnostics</c> into the object that is open.</summary>
        public static void WriteOutcome(CanonicalJsonWriter json, IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics)
        {
            json.WriteBoolean("ok", diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));
            json.WriteStartArray("diagnostics");
            foreach (var diagnostic in diagnostics)
            {
                json.WriteStartObject();
                json.WriteString("severity", diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "error");
                json.WriteString("code", diagnostic.Code);
                json.WriteString("message", diagnostic.Message);
                json.WriteString("reason", diagnostic.Reason);
                json.WriteString("fix", diagnostic.Fix);
                if (diagnostic.Location is { } location)
                {
                    json.WriteStartObject("location");
                    json.WriteString("file", location.File);
                    json.WriteNumber("line", location.Line);
                    json.WriteNumber("column", location.Column);
                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteNumber("omittedDiagnostics", omittedDiagnostics);
        }

        /// <summary>Writes what identifies a locked package into the object that is open. They are a lockfile's own keys and spellings.</summary>
        public static void WritePackage(CanonicalJsonWriter json, LockedPackage package)
        {
            json.WriteString("name", package.Name.ToString());
            json.WriteString("version", package.Version.ToString());
            json.WriteString("source", package.Source.ToString());
            json.WriteString("sha256", package.Sha256.Hex);
            json.WriteString("verified", VerificationLevels.Name(package.Verified));
        }

        /// <summary>
        /// Writes <c>packages</c> into the object that is open: every package a lockfile holds, in
        /// order, each with what it depends on. Every answer that lists locked packages lists them
        /// with this, so that a tool reads a package one way.
        /// </summary>
        public static void WritePackages(CanonicalJsonWriter json, Lockfile lockfile)
        {
            json.WriteStartArray("packages");
            foreach (var package in InOrder(lockfile))
            {
                json.WriteStartObject();
                WritePackage(json, package);
                json.WriteStartArray("dependencies");
                foreach (var dependency in package.Dependencies.OrderBy(dependency => dependency.Name).ThenBy(dependency => dependency.Version))
                {
                    json.WriteStartObject();
                    json.WriteString("name", dependency.Name.ToString());
                    json.WriteString("version", dependency.Version.ToString());
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        public static IEnumerable<LockedPackage> InOrder(Lockfile lockfile) =>
            lockfile.Packages.OrderBy(package => package.Name).ThenBy(package => package.Version);
    }
}
