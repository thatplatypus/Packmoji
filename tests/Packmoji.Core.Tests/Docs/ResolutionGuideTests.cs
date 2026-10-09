using System.Reflection;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Graphs;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Docs
{
    /// <summary>Holds docs/resolution.md to the code, so that the guide cannot say what the resolver does not do.</summary>
    public sealed class ResolutionGuideTests
    {
        private static readonly string Guide =
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "resolution.md")).ReplaceLineEndings("\n");

        [Fact]
        public void Every_code_a_resolution_or_a_check_can_raise_is_explained()
        {
            var codes = typeof(DiagnosticCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (string)field.GetRawConstantValue()!)
                .Where(code => code.StartsWith("resolve.", StringComparison.Ordinal))
                .Append(DiagnosticCodes.LockMismatch)
                .Append(DiagnosticCodes.RepositoryOwnerMismatch)
                .Append(DiagnosticCodes.DependencyDuplicate)
                .ToList();

            codes.Count.ShouldBe(11);
            foreach (var code in codes)
            {
                Guide.ShouldContain($"| `{code}` |");
            }
        }

        [Fact]
        public async Task The_worked_example_resolves_to_the_versions_the_guide_says_it_does()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2", "@thatplatypus/deflate@0.1")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.2.0")
                .Publish("@thatplatypus/crypto", "1.9.0")
                .Publish("@thatplatypus/deflate", "0.1.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0"))).ShouldSucceed();

            graph.Packages.Count.ShouldBe(3);
            foreach (var package in graph.Packages)
            {
                Guide.ShouldContain($"| `{package.Published.Name}` | `{package.Published.Version}` |");
            }
        }

        [Fact]
        public void A_chain_is_written_as_the_guide_shows_one()
        {
            Guide.ShouldContain(Chain.Text(["@thatplatypus/app", "@thatplatypus/grapevine@0.3.0", "@thatplatypus/crypto@1.0"]));
            Guide.ShouldContain("(12 more)");
        }

        [Fact]
        public void The_guide_holds_no_em_dash() => Guide.ShouldNotContain(char.ConvertFromUtf32(0x2014));
    }
}
