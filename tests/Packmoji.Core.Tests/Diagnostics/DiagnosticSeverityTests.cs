using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    public sealed class DiagnosticSeverityTests
    {
        [Fact]
        public void A_diagnostic_is_an_error_unless_it_is_said_to_be_a_warning()
        {
            var diagnostic = new Diagnostic("x.y", "m", "r", "f");

            diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
            (diagnostic with { Severity = DiagnosticSeverity.Warning }).Severity.ShouldBe(DiagnosticSeverity.Warning);
        }

        [Fact]
        public void A_warning_and_an_error_of_the_same_text_are_not_the_same_diagnostic()
        {
            var error = new Diagnostic("x.y", "m", "r", "f");

            (error with { Severity = DiagnosticSeverity.Warning }).ShouldNotBe(error);
            (error with { Severity = DiagnosticSeverity.Error }).ShouldBe(error);
        }

        [Fact]
        public void Everything_a_reader_reports_is_an_error()
        {
            var manifest = ManifestReader.Read("{ \"package\": { \"name\": \"Crypto\" }, \"oops\": 1 }");
            var lockfile = LockfileReader.Read("{ \"version\": 1, \"root\": 7, \"packages\": [7] }");

            manifest.Diagnostics.Count.ShouldBeGreaterThan(1);
            lockfile.Diagnostics.Count.ShouldBeGreaterThan(1);
            manifest.Diagnostics.Concat(lockfile.Diagnostics).ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }
}
