using System.Reflection;
using System.Text.RegularExpressions;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Docs
{
    /// <summary>Holds docs/manifest.md to the code, so that the reference cannot say what the readers do not do.</summary>
    public sealed class ManifestReferenceTests
    {
        private static readonly string Reference =
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "manifest.md")).ReplaceLineEndings("\n");

        [Fact]
        public void Every_json_example_reads_as_the_file_it_shows()
        {
            var examples = Regex.Matches(Reference, "```json\n(.*?)\n```", RegexOptions.Singleline)
                .Select(match => match.Groups[1].Value)
                .ToList();

            examples.Count.ShouldBeGreaterThanOrEqualTo(3);
            foreach (var example in examples)
            {
                if (example.Contains("\"packages\"", StringComparison.Ordinal))
                {
                    LockfileReader.Read(example).ShouldSucceed();
                }
                else
                {
                    ManifestReader.Read(example).ShouldSucceed();
                }
            }
        }

        [Fact]
        public void Every_diagnostic_code_is_listed()
        {
            var codes = typeof(DiagnosticCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (string)field.GetRawConstantValue()!);

            foreach (var code in codes)
            {
                Reference.ShouldContain($"| `{code}` |");
            }
        }

        [Fact]
        public void The_reference_holds_no_em_dash() => Reference.ShouldNotContain(char.ConvertFromUtf32(0x2014));
    }
}
