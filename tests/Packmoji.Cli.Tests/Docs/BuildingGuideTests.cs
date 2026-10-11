using System.Text.RegularExpressions;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Manifests;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Docs
{
    /// <summary>
    /// Holds docs/building.md to what a build does, so that the guide cannot name a flag pmj does
    /// not give, a file a build does not write, or leave out a problem a build can report.
    /// </summary>
    public sealed class BuildingGuideTests
    {
        private static readonly string Guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "building.md")).ReplaceLineEndings("\n");

        // An application with C and C++ of its own, that depends on a package with C++ and on one that depends on another.
        private static async Task<Sandbox> WithEverythingABuildDoesAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Upload("github.com/thatplatypus/net", "@thatplatypus/net", "1.0.0", TestPackage.Archive(
                ("packmoji.json", "{ \"package\": { \"name\": \"@thatplatypus/net\", \"version\": \"1.0.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }, \"native\": { \"sources\": [\"native/*.cpp\"], \"link\": [\"curl\"] } }\n"),
                ("src/lib.🍇", "💭 net\n"),
                ("native/net.cpp", "// net\n")));
            sandbox.Write("packmoji.json", "{ \"package\": { \"name\": \"@you/site\", \"version\": \"0.1.0\", \"kind\": \"app\", \"emojicode\": \">=1.0.0-beta.2\" }, \"dependencies\": { \"@thatplatypus/grapevine\": \"0.3\", \"@thatplatypus/net\": \"1.0\" }, \"native\": { \"sources\": [\"native/*\"] } }\n");
            sandbox.Write("src/main.🍇", "📦 grapevine 🏠\n🏁 🍇\n🍉\n");
            sandbox.Write("native/a.c", "// c\n");
            sandbox.Write("native/b.cpp", "// c++\n");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            return sandbox;
        }

        [Fact]
        public async Task Every_flag_pmj_gives_a_tool_is_in_the_guide()
        {
            using var sandbox = await WithEverythingABuildDoesAsync();

            (await sandbox.RunAsync("build", "--release")).Status.ShouldBe(0);

            // A library is named after -l, and that is the one flag that is not the same every time.
            var flags = sandbox.Tools.Calls
                .SelectMany(call => call.Arguments)
                .Where(argument => argument.StartsWith('-') || argument == "rcs")
                .Select(argument => argument.StartsWith("-l", StringComparison.Ordinal) && argument is not ("-lm" or "-lpthread") ? "-l<library>" : argument)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            flags.ShouldBe(["--help", "--version", "-I", "-O", "-O2", "-S", "-Wl,--end-group", "-Wl,--start-group", "-c", "-i", "-l<library>", "-lm", "-lpthread", "-o", "-p", "-r", "-std=c++17", "-std=gnu11", "rcs"]);
            foreach (var flag in flags)
            {
                Regex.IsMatch(Guide, $"(^|[ `]){Regex.Escape(flag)}($|[ `.,])", RegexOptions.Multiline).ShouldBeTrue($"the guide does not show {flag}");
            }
        }

        [Fact]
        public async Task What_the_guide_shows_in_a_packages_folder_is_what_a_build_puts_there()
        {
            using var sandbox = await WithEverythingABuildDoesAsync();

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            var shown = Regex.Matches(Guide, "^packages/crypto/(.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToList();
            shown.ShouldBe(sandbox.Files("packages/crypto"));
        }

        [Fact]
        public async Task Where_the_guide_says_a_program_and_a_library_are_built_to_is_where_they_are()
        {
            using var sandbox = await WithEverythingABuildDoesAsync();

            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            (await sandbox.RunAsync("build", "--release")).Status.ShouldBe(0);

            Guide.ShouldContain("target/debug/<name>         an application: the program");
            Guide.ShouldContain("target/release/             the same, built with --release");
            Guide.ShouldContain("target/obj/                 what is made on the way");
            sandbox.Has("target/debug/site").ShouldBeTrue();
            sandbox.Has("target/release/site").ShouldBeTrue();
            sandbox.Files("target").ShouldAllBe(file => file == ".pmj-lock" || file.StartsWith("debug/", StringComparison.Ordinal) || file.StartsWith("release/", StringComparison.Ordinal) || file.StartsWith("obj/", StringComparison.Ordinal));
        }

        [Fact]
        public void Every_problem_a_build_can_report_is_in_the_guide_with_what_to_do_about_it()
        {
            string[] codes =
            [
                "lock.out-of-date", "cache.mismatch", "entry.not-found",
                "compiler.not-found", "compiler.unknown", "compiler.too-old", "compiler.incomplete",
                "tool.not-found", "native.unsupported",
                "build.compile-failed", "build.native-failed", "build.archive-failed", "build.link-failed",
                "built.unusable", "packages.foreign", "project.unreadable", "run.not-an-app", "run.failed", "scope.not-allowed",
            ];

            foreach (var code in codes)
            {
                Regex.IsMatch(Guide, $@"^\| `{Regex.Escape(code)}` \| .+ \| .+ \|$", RegexOptions.Multiline).ShouldBeTrue(code);
            }
        }

        [Fact]
        public void The_manifest_in_the_guide_reads()
        {
            var manifest = Regex.Matches(Guide, "```json\n(.*?)\n```", RegexOptions.Singleline).ShouldHaveSingleItem().Groups[1].Value;

            ManifestReader.Read(manifest).Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ShouldBeEmpty();
        }

        [Fact]
        public void The_decisions_the_guide_points_to_are_there()
        {
            var records = Regex.Matches(Guide, @"\(decisions/([0-9a-z-]+\.md)\)").Select(match => match.Groups[1].Value).ToList();

            records.ShouldBe(["0001-emojicodec-integration.md", "0009-built-packages.md", "0010-how-a-build-is-run.md"]);
        }
    }
}
