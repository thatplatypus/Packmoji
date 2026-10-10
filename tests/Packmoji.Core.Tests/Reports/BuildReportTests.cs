using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Reports;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Reports
{
    /// <summary>
    /// What <c>pmj build --json</c> says. A tool that compiles a project its own way reads this to
    /// learn where each package is and what to link, so the shape is held here to the last key.
    /// </summary>
    public sealed class BuildReportTests
    {
        private static readonly CompilerIdentity Compiler = new("/usr/local/bin/emojicodec", Sample.Version("1.0.0-beta.2"), Sample.Sha('c'));

        private static readonly BuiltPackage Grapevine = new(Sample.Name("@thatplatypus/grapevine"), Sample.Version("0.1.0"), "/work/site/packages/grapevine", ["pthread", "m"], Compiled: true);

        private static readonly BuiltPackage Crypto = new(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), "/work/site/packages/crypto", [], Compiled: false);

        [Fact]
        public void A_build_that_did_all_of_it_says_which_compiler_where_each_package_is_and_what_was_made_of_the_project()
        {
            var project = new BuiltProject(Sample.Name("@you/site"), Sample.Version("0.1.0"), PackageKind.App, "/work/site/target/debug/site");

            BuildReport.Json([], 0, Compiler, "/work/site/packages", [Crypto, Grapevine], project).ShouldBe(
                $$"""
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "compiler": {
                    "path": "/usr/local/bin/emojicodec",
                    "version": "1.0.0-beta.2",
                    "sha256": "{{new string('c', 64)}}"
                  },
                  "packagesDirectory": "/work/site/packages",
                  "packages": [
                    {
                      "name": "@thatplatypus/crypto",
                      "version": "1.0.0",
                      "bareName": "crypto",
                      "directory": "/work/site/packages/crypto",
                      "link": [],
                      "built": false
                    },
                    {
                      "name": "@thatplatypus/grapevine",
                      "version": "0.1.0",
                      "bareName": "grapevine",
                      "directory": "/work/site/packages/grapevine",
                      "link": [
                        "pthread",
                        "m"
                      ],
                      "built": true
                    }
                  ],
                  "project": {
                    "name": "@you/site",
                    "version": "0.1.0",
                    "kind": "app",
                    "output": "/work/site/target/debug/site"
                  }
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_library_is_said_to_be_one()
        {
            var project = new BuiltProject(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), PackageKind.Library, "/work/crypto/target/debug/crypto");

            BuildReport.Json([], 0, Compiler, "/work/crypto/packages", [], project).ShouldContain("\"kind\": \"library\",\n    \"output\": \"/work/crypto/target/debug/crypto\"");
        }

        [Fact]
        public void A_build_of_the_packages_alone_says_nothing_of_the_project()
        {
            var json = BuildReport.Json([], 0, Compiler, "/work/site/packages", [Crypto], null);

            json.ShouldNotContain("\"project\"");
            json.ShouldEndWith("      \"built\": false\n    }\n  ]\n}\n");
        }

        [Fact]
        public void A_build_that_needed_no_compiler_names_none()
        {
            BuildReport.Json([], 0, null, "/work/site/packages", [], null).ShouldBe(
                """
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "packagesDirectory": "/work/site/packages",
                  "packages": []
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_build_that_was_stopped_says_why_and_is_not_ok_and_a_warning_alone_leaves_it_ok()
        {
            var stopped = new Diagnostic("build.compile-failed", "\"@thatplatypus/crypto\" 1.0.0 could not be compiled.", "the compiler ended with status 1", "mend it");
            var warned = new Diagnostic("resolve.yanked-locked", "A version was withdrawn.", "its author said so", "move on when you can") { Severity = DiagnosticSeverity.Warning };

            var failed = BuildReport.Json([stopped, warned], 2, Compiler, "/work/site/packages", [], null);

            failed.ShouldStartWith("{\n  \"ok\": false,\n  \"diagnostics\": [\n    {\n      \"severity\": \"error\",\n      \"code\": \"build.compile-failed\",\n");
            failed.ShouldContain("  \"omittedDiagnostics\": 2,\n  \"compiler\": {\n");
            BuildReport.Json([warned], 0, Compiler, "/work/site/packages", [Crypto], null).ShouldStartWith("{\n  \"ok\": true,\n  \"diagnostics\": [\n    {\n      \"severity\": \"warning\",\n");
        }

        [Fact]
        public void A_path_with_an_emoji_or_a_backslash_in_it_is_written_as_json_writes_it() =>
            BuildReport.Json([], 0, null, "C:\\work\\🍇\\packages", [], null).ShouldContain("  \"packagesDirectory\": \"C:\\\\work\\\\🍇\\\\packages\",\n");
    }
}
