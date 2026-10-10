using Packmoji.Core.Diagnostics;
using Packmoji.Core.Resolution;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static class ResolveAssertions
    {
        /// <summary>Holds a resolution to having given a graph. Warnings are allowed, and are for the test to look at.</summary>
        public static ResolvedGraph ShouldSucceed(this ResolveResult result)
        {
            result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message} {diagnostic.Reason}")
                .ShouldBeEmpty();
            result.Succeeded.ShouldBeTrue();
            result.OmittedDiagnostics.ShouldBe(0);
            return result.Graph.ShouldNotBeNull();
        }

        /// <summary>Holds a resolution to having been stopped by exactly one problem, of the given code, and to having said nothing else.</summary>
        public static Diagnostic ShouldFailWith(this ResolveResult result, string code)
        {
            result.Succeeded.ShouldBeFalse();
            result.Graph.ShouldBeNull();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([code]);
            var diagnostic = result.Diagnostics[0].ShouldBeComplete();
            diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
            diagnostic.Location.ShouldBeNull();
            return diagnostic;
        }

        /// <summary>The packages of a graph, each as <c>@owner/name@1.2.3</c>, in the order the graph holds them.</summary>
        public static string[] Selected(this ResolvedGraph graph) =>
            graph.Packages.Select(package => $"{package.Published.Name}@{package.Published.Version}").ToArray();

        /// <summary>What a resolved package depends on, each as <c>@owner/name@1.2.3</c>.</summary>
        public static string[] Pins(this ResolvedGraph graph, string name) =>
            graph.Packages
                .Single(package => package.Published.Name.ToString() == name)
                .Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Version}")
                .ToArray();
    }
}
