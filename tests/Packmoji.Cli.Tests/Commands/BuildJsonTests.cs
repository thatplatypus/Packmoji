using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// <c>pmj build --json</c>: what a tool reads. A tool that compiles a project its own way builds
    /// the packages with this and takes from the answer where each is and what to link.
    /// </summary>
    public sealed class BuildJsonTests
    {
        [Fact]
        public async Task A_tool_is_answered_with_one_object_that_says_where_every_package_is()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(run.Output).ShouldBe(
                """
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "compiler": {
                    "path": "~/tools/emojicodec",
                    "version": "1.0.0-beta.2",
                    "sha256": "COMPILER"
                  },
                  "packagesDirectory": "~/work/packages",
                  "packages": [
                    {
                      "name": "@thatplatypus/crypto",
                      "version": "1.0.0",
                      "bareName": "crypto",
                      "directory": "~/work/packages/crypto",
                      "link": [],
                      "built": true
                    },
                    {
                      "name": "@thatplatypus/deflate",
                      "version": "0.1.0",
                      "bareName": "deflate",
                      "directory": "~/work/packages/deflate",
                      "link": [],
                      "built": true
                    },
                    {
                      "name": "@thatplatypus/grapevine",
                      "version": "0.3.0",
                      "bareName": "grapevine",
                      "directory": "~/work/packages/grapevine",
                      "link": [],
                      "built": true
                    }
                  ]
                }

                """.ReplaceLineEndings("\n").Replace("COMPILER", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(sandbox.ToolsDirectory, "emojicodec"))))[..8]));
        }

        [Fact]
        public async Task Each_directory_it_names_holds_the_interface_the_archive_and_the_compilers_report()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            var packages = answer.RootElement.GetProperty("packages").EnumerateArray().ToList();
            packages.Count.ShouldBe(3);
            foreach (var package in packages)
            {
                var directory = package.GetProperty("directory").GetString()!;
                var name = package.GetProperty("bareName").GetString()!;
                Path.GetDirectoryName(directory).ShouldBe(answer.RootElement.GetProperty("packagesDirectory").GetString());
                File.Exists(Path.Combine(directory, "🏛")).ShouldBeTrue(name);
                File.Exists(Path.Combine(directory, $"lib{name}.a")).ShouldBeTrue(name);
                File.Exists(Path.Combine(directory, "documentation.json")).ShouldBeTrue(name);
            }
        }

        [Fact]
        public async Task A_package_that_was_built_before_is_said_not_to_have_been_built_now()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            await sandbox.RunAsync("build", "--dependencies-only");
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.1.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("packages").EnumerateArray()
                .Select(package => $"{package.GetProperty("bareName").GetString()} {package.GetProperty("version").GetString()} {package.GetProperty("built").GetBoolean()}")
                .ShouldBe(["crypto 1.1.0 True", "deflate 0.1.0 False", "grapevine 0.3.0 True"]);
        }

        [Fact]
        public async Task The_libraries_a_package_is_to_be_linked_with_are_given_as_its_manifest_has_them()
        {
            using var sandbox = new Sandbox();
            sandbox.Upload("github.com/thatplatypus/net", "@thatplatypus/net", "1.0.0", TestPackage.Archive(
                ("packmoji.json", "{ \"package\": { \"name\": \"@thatplatypus/net\", \"version\": \"1.0.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }, \"native\": { \"link\": [\"pthread\", \"m\", \"curl\"] } }\n"),
                ("src/lib.🍇", "💭 net\n")));
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/net@1.0");

            var first = await sandbox.RunAsync("build", "--dependencies-only", "--json");
            var again = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            first.Output.ShouldContain("      \"link\": [\n        \"pthread\",\n        \"m\",\n        \"curl\"\n      ],\n");
            again.Output.ShouldContain("      \"link\": [\n        \"pthread\",\n        \"m\",\n        \"curl\"\n      ],\n      \"built\": false\n");
        }

        [Fact]
        public async Task Nothing_is_written_to_the_output_but_the_object_whatever_the_build_has_to_say()
        {
            using var sandbox = new Sandbox();
            sandbox.ReleaseSource("@thatplatypus/crypto", "1.0.0", "⚠️ Something here is deprecated.\n");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("{\n  \"ok\": true,\n");
            run.Output.ShouldNotContain("Building");
            run.Error.ShouldContain("[crypto] ");
            run.Error.ShouldContain("⚠️  warning: Something here is deprecated.");
        }

        [Fact]
        public async Task A_build_that_a_tool_stopped_is_answered_with_the_problem_in_the_object_and_what_the_tool_printed_apart_from_it()
        {
            using var sandbox = new Sandbox();
            sandbox.ReleaseSource("@thatplatypus/crypto", "1.0.0", "💥 Variable \"nothing\" not defined.\n");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Status.ShouldBe(1);
            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            var problem = answer.RootElement.GetProperty("diagnostics").EnumerateArray().ShouldHaveSingleItem();
            problem.GetProperty("code").GetString().ShouldBe("build.compile-failed");
            problem.GetProperty("message").GetString().ShouldBe("\"@thatplatypus/crypto\" 1.0.0 could not be compiled.");
            answer.RootElement.GetProperty("compiler").GetProperty("version").GetString().ShouldBe("1.0.0-beta.2");
            answer.RootElement.GetProperty("packages").GetArrayLength().ShouldBe(0);
            answer.RootElement.TryGetProperty("project", out _).ShouldBeFalse();

            // The compiler's own words are for a log, and the problem is not said twice.
            run.Error.ShouldContain("[crypto] ");
            run.Error.ShouldNotContain("error[");
        }

        [Fact]
        public async Task A_build_that_could_not_begin_is_answered_with_the_problem_and_names_no_compiler()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            sandbox.Plain(run.Output).ShouldBe(
                """
                {
                  "ok": false,
                  "diagnostics": [
                    {
                      "severity": "error",
                      "code": "lock.out-of-date",
                      "message": "There is no packmoji.lock.",
                      "reason": "this command reads what is locked, and chooses nothing itself",
                      "fix": "run pmj install, which writes packmoji.lock"
                    }
                  ],
                  "omittedDiagnostics": 0,
                  "packagesDirectory": "~/work/packages",
                  "packages": []
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task A_project_that_locks_nothing_is_answered_with_no_package_and_no_compiler()
        {
            using var sandbox = new Sandbox();
            sandbox.Project("@someone/app");

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Status.ShouldBe(0);
            sandbox.Plain(run.Output).ShouldBe(
                """
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "packagesDirectory": "~/work/packages",
                  "packages": []
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task What_GitHub_could_not_be_asked_is_a_problem_in_the_object_like_any_other()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.ForgetCache();
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("build", "--dependencies-only", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            using var answer = System.Text.Json.JsonDocument.Parse(run.Output);
            answer.RootElement.GetProperty("diagnostics").EnumerateArray().ShouldHaveSingleItem().GetProperty("code").GetString().ShouldBe("github.unreachable");
        }
    }
}
