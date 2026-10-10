using Packmoji.Core.Diagnostics;
using Packmoji.Core.Reports;

namespace Packmoji.Cli.Output
{
    /// <summary>
    /// Prints a problem the way every pmj problem is printed: its place when it has one, whether it
    /// stops the work, its code, and then what failed, why, and what to do next.
    /// </summary>
    internal static class DiagnosticPrinter
    {
        public static void Print(TextWriter writer, Diagnostic diagnostic)
        {
            var place = diagnostic.Location is { } location ? $"{location.File}:{location.Line}:{location.Column}: " : "";
            var severity = diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "error";
            writer.WriteLine($"{place}{severity}[{diagnostic.Code}]: {diagnostic.Message}");
            writer.WriteLine($"  why: {diagnostic.Reason}");
            writer.WriteLine($"  fix: {diagnostic.Fix}");
        }

        /// <summary>Prints each problem, and says how many more there were than are listed.</summary>
        public static void Print(TextWriter writer, IReadOnlyList<Diagnostic> diagnostics, int omitted = 0)
        {
            foreach (var diagnostic in diagnostics)
            {
                Print(writer, diagnostic);
            }

            if (omitted > 0)
            {
                writer.WriteLine($"and {omitted} more that are not listed.");
            }
        }

        /// <summary>Prints what stopped a command where problems go, and gives the status a command ends with when it has reported one.</summary>
        /// <param name="json">Whether the command was asked to answer a tool. Its answer is then one object on the output, with the problems in it, and nothing is printed for a person.</param>
        public static int Report(PmjHost host, IReadOnlyList<Diagnostic> diagnostics, int omitted = 0, bool json = false)
        {
            if (json)
            {
                host.Out.Write(ProblemReport.Json(diagnostics, omitted));
            }
            else
            {
                Print(host.Error, diagnostics, omitted);
            }

            return ExitStatus.Problem;
        }

        public static int Report(PmjHost host, Diagnostic diagnostic) => Report(host, [diagnostic]);
    }
}
