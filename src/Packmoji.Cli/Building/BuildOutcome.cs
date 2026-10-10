using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Cli.Building
{
    /// <summary>What a build came to: the problems it met, and when it met none that stopped it, what it built.</summary>
    /// <param name="Compiler">The compiler that was used. Null when none was needed, or when the build was stopped before one was found.</param>
    /// <param name="Packages">Every locked package, in order of name. Empty when the build was stopped.</param>
    internal sealed record BuildOutcome(
        IReadOnlyList<Diagnostic> Diagnostics,
        int OmittedDiagnostics,
        CompilerIdentity? Compiler,
        IReadOnlyList<BuiltPackage> Packages)
    {
        public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }
}
