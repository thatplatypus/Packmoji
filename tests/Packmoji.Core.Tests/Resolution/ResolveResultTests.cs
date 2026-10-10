using Packmoji.Core.Diagnostics;
using Packmoji.Core.Resolution;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    public sealed class ResolveResultTests
    {
        private static readonly Diagnostic Warning = new("resolve.yanked-locked", "A version was withdrawn.", "its author said so", "move on when you can") { Severity = DiagnosticSeverity.Warning };

        private static readonly Diagnostic Error = new("package.not-found", "No release was found.", "pmj looked", "say where it is");

        private static ResolveResult WithWarning()
        {
            var warnings = new DiagnosticList();
            warnings.Add(Warning);
            return ResolveResult.From(new DiagnosticList(), warnings, new ResolvedGraph([]));
        }

        [Fact]
        public void More_errors_take_the_graph_away_and_are_listed_before_the_warnings_that_were_there()
        {
            var result = WithWarning();
            result.Succeeded.ShouldBeTrue();

            var stopped = result.WithErrors([Error]);

            stopped.Succeeded.ShouldBeFalse();
            stopped.Graph.ShouldBeNull();
            stopped.Diagnostics.ShouldBe([Error, Warning]);
            stopped.OmittedDiagnostics.ShouldBe(0);
        }

        [Fact]
        public void No_more_errors_is_the_same_result()
        {
            var result = WithWarning();

            result.WithErrors([]).ShouldBeSameAs(result);
        }
    }
}
