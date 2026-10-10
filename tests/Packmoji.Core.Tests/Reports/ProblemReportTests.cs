using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Reports;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Reports
{
    public sealed class ProblemReportTests
    {
        private static readonly Diagnostic Error = new("entry.not-found", "There is no \"src/lib.🍇\".", "the manifest names it", "make the file", new SourceLocation("packmoji.json", 9, 14));

        private static readonly Diagnostic Warning = new("resolve.yanked-locked", "A version was withdrawn.", "its author said so", "move on when you can") { Severity = DiagnosticSeverity.Warning };

        [Fact]
        public void A_problem_is_written_whole_with_its_place_when_it_has_one_and_an_emoji_as_itself()
        {
            ProblemReport.Json([Error, Warning], 3).ShouldBe(
                """
                {
                  "ok": false,
                  "diagnostics": [
                    {
                      "severity": "error",
                      "code": "entry.not-found",
                      "message": "There is no \"src/lib.🍇\".",
                      "reason": "the manifest names it",
                      "fix": "make the file",
                      "location": {
                        "file": "packmoji.json",
                        "line": 9,
                        "column": 14
                      }
                    },
                    {
                      "severity": "warning",
                      "code": "resolve.yanked-locked",
                      "message": "A version was withdrawn.",
                      "reason": "its author said so",
                      "fix": "move on when you can"
                    }
                  ],
                  "omittedDiagnostics": 3
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_warning_alone_does_not_make_the_answer_a_failure()
        {
            ProblemReport.Json([Warning]).ShouldStartWith("{\n  \"ok\": true,");
            ProblemReport.Json([]).ShouldBe("{\n  \"ok\": true,\n  \"diagnostics\": [],\n  \"omittedDiagnostics\": 0\n}\n");
        }

        [Fact]
        public void What_was_verified_is_every_locked_package_in_order_of_name()
        {
            var lockfile = LockfileReader.Read(Fixtures.Lockfile).ShouldSucceed();

            var json = VerifyReport.Json(lockfile, [Error]);

            json.ShouldStartWith("{\n  \"ok\": false,\n  \"diagnostics\": [\n    {\n      \"severity\": \"error\",");
            json.ShouldEndWith(
                """
                  "packages": [
                    {
                      "name": "@thatplatypus/crypto",
                      "version": "1.0.0",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                      "verified": "attestation"
                    },
                    {
                      "name": "@thatplatypus/deflate",
                      "version": "0.1.0",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                      "verified": "attestation"
                    },
                    {
                      "name": "@thatplatypus/grapevine",
                      "version": "0.3.0",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                      "verified": "checksum"
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }
    }
}
