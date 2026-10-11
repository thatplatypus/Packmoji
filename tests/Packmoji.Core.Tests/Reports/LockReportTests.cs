using Packmoji.Core.Diagnostics;
using Packmoji.Core.Reports;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Reports
{
    /// <summary>
    /// What add, remove, install and update say to a tool. Another program reads this, so its shape
    /// is held to the last key.
    /// </summary>
    public sealed class LockReportTests
    {
        private static string Sha(string package) => Sample.DigestOf(package).Hex;

        [Fact]
        public void A_tool_is_told_whether_the_files_were_written_what_changed_and_every_package_that_is_held_now()
        {
            var before = Locked.Of("@thatplatypus/deflate 0.1.0", "@thatplatypus/grapevine 0.3.0 > @thatplatypus/deflate 0.1.0");
            var after = Locked.Of("@thatplatypus/grapevine 0.3.1 > @thatplatypus/crypto 1.0.0", "@thatplatypus/crypto 1.0.0");

            LockReport.Json([], 0, written: true, before, after).ShouldBe(
                $$"""
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "written": true,
                  "changes": [
                    {
                      "change": "added",
                      "name": "@thatplatypus/crypto",
                      "to": "1.0.0"
                    },
                    {
                      "change": "removed",
                      "name": "@thatplatypus/deflate",
                      "from": "0.1.0"
                    },
                    {
                      "change": "moved",
                      "name": "@thatplatypus/grapevine",
                      "from": "0.3.0",
                      "to": "0.3.1"
                    }
                  ],
                  "packages": [
                    {
                      "name": "@thatplatypus/crypto",
                      "version": "1.0.0",
                      "source": "github.com/thatplatypus/crypto",
                      "sha256": "{{Sha("@thatplatypus/crypto@1.0.0")}}",
                      "verified": "checksum",
                      "dependencies": []
                    },
                    {
                      "name": "@thatplatypus/grapevine",
                      "version": "0.3.1",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "{{Sha("@thatplatypus/grapevine@0.3.1")}}",
                      "verified": "checksum",
                      "dependencies": [
                        {
                          "name": "@thatplatypus/crypto",
                          "version": "1.0.0"
                        }
                      ]
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void What_a_package_depends_on_is_listed_in_order_of_name_however_the_lockfile_has_it()
        {
            // Neither in order nor in the reverse of it, so that only putting them in order gives the order.
            var after = Locked.Of(
                "@thatplatypus/grapevine 0.3.0 > @thatplatypus/deflate 0.1.0, @thatplatypus/crypto 1.0.0, @thatplatypus/zeta 2.0.0",
                "@thatplatypus/crypto 1.0.0",
                "@thatplatypus/deflate 0.1.0",
                "@thatplatypus/zeta 2.0.0");

            var json = LockReport.Json([], 0, written: true, before: null, after);

            var dependencies = json[json.IndexOf("\"dependencies\": [\n", json.IndexOf("\"name\": \"@thatplatypus/grapevine\",\n      \"version\"", StringComparison.Ordinal), StringComparison.Ordinal)..];
            int At(string name) => dependencies.IndexOf(name, StringComparison.Ordinal);
            At("@thatplatypus/crypto").ShouldBeLessThan(At("@thatplatypus/deflate"));
            At("@thatplatypus/deflate").ShouldBeLessThan(At("@thatplatypus/zeta"));
        }

        [Fact]
        public void A_command_that_wrote_nothing_says_so_and_still_says_what_would_be_held()
        {
            var after = Locked.Of("@thatplatypus/crypto 1.0.0");

            var json = LockReport.Json([], 0, written: false, before: null, after);

            json.ShouldContain("\n  \"written\": false,\n");
            json.ShouldContain("\"change\": \"added\"");
            json.ShouldContain("\"name\": \"@thatplatypus/crypto\"");
        }

        [Fact]
        public void A_lockfile_that_holds_the_same_as_before_has_no_change_and_its_packages_are_still_given()
        {
            var held = Locked.Of("@thatplatypus/crypto 1.0.0");

            var json = LockReport.Json([], 0, written: false, held, held);

            json.ShouldContain("\n  \"changes\": [],\n");
            json.ShouldContain("\"name\": \"@thatplatypus/crypto\"");
        }

        [Fact]
        public void A_project_that_depends_on_nothing_has_no_change_and_no_package()
        {
            LockReport.Json([], 0, written: true, before: null, Locked.Of()).ShouldBe(
                """
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "written": true,
                  "changes": [],
                  "packages": []
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_warning_is_in_the_answer_beside_what_was_done_and_the_answer_is_still_ok()
        {
            var warning = new Diagnostic("version.yanked", "A version was withdrawn.", "its author took it back", "move to another") { Severity = DiagnosticSeverity.Warning };

            var json = LockReport.Json([warning], 2, written: true, before: null, Locked.Of("@thatplatypus/crypto 1.0.0"));

            json.ShouldStartWith("{\n  \"ok\": true,\n  \"diagnostics\": [\n    {\n      \"severity\": \"warning\",\n      \"code\": \"version.yanked\",");
            json.ShouldContain("\n  \"omittedDiagnostics\": 2,\n  \"written\": true,\n");
        }
    }
}
