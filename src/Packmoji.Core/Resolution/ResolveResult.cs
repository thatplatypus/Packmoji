using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Resolution
{
    /// <summary>
    /// What a resolution gave: the graph, or what stopped it. A warning does not stop one, so a
    /// result can hold a graph and diagnostics at once, and the graph is there exactly when none of
    /// the diagnostics is an error.
    /// </summary>
    public sealed class ResolveResult
    {
        private ResolveResult(ResolvedGraph? graph, IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics)
        {
            Graph = graph;
            Diagnostics = diagnostics;
            OmittedDiagnostics = omittedDiagnostics;
        }

        public ResolvedGraph? Graph { get; }

        /// <summary>
        /// What was found: every error, and then every warning, so that a warning never takes the room
        /// of an error. At most a hundred are listed.
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>How many more were found than <see cref="Diagnostics"/> lists.</summary>
        public int OmittedDiagnostics { get; }

        [MemberNotNullWhen(true, nameof(Graph))]
        public bool Succeeded => Graph is not null;

        /// <param name="graph">Asked for only when there is no error, so that a graph that could not be whole is never made.</param>
        internal static ResolveResult From(DiagnosticList errors, DiagnosticList warnings, Func<ResolvedGraph> graph)
        {
            var listed = errors.Concat(warnings).Take(DiagnosticList.Limit).ToList();
            var omitted = errors.Omitted + warnings.Omitted + (errors.Count + warnings.Count - listed.Count);
            return new ResolveResult(errors.Count == 0 ? graph() : null, listed, omitted);
        }
    }
}
