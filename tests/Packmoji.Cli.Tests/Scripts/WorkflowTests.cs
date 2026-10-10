using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Scripts
{
    /// <summary>
    /// The workflows GitHub runs for this repository, held to a few rules that a reading of them
    /// would otherwise have to hold: what they run is named by its commit, and only the one job
    /// that makes a release may write to the repository, and only for a tag.
    /// </summary>
    public sealed class WorkflowTests
    {
        private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "workflows");

        private static string Workflow(string name) => File.ReadAllText(Path.Combine(Directory, name)).ReplaceLineEndings("\n");

        // The lines of one job: from its name at two spaces in, to the next line that is no deeper.
        private static string Job(string workflow, string name)
        {
            var lines = workflow.Split('\n');
            var start = Array.IndexOf(lines, $"  {name}:");
            start.ShouldBeGreaterThanOrEqualTo(0, $"there is no job {name}");
            var end = Array.FindIndex(lines, start + 1, line => Regex.IsMatch(line, "^ {0,2}[a-z]"));
            return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
        }

        [Theory]
        [InlineData("ci.yml")]
        [InlineData("release.yml")]
        public void Every_action_a_workflow_uses_is_named_by_its_commit_and_not_by_a_tag_that_can_move(string name)
        {
            var used = Regex.Matches(Workflow(name), @"^\s*(?:- )?uses:\s*(.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value).ToList();

            used.ShouldNotBeEmpty();
            foreach (var action in used)
            {
                Regex.IsMatch(action, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40} # v[0-9]+$").ShouldBeTrue($"{name} uses {action}");
            }
        }

        // A run is started by nothing else: a branch that is pushed is checked when a pull request is opened for it.
        [Fact]
        public void The_checks_run_for_a_pull_request_to_main_for_main_itself_and_by_hand()
        {
            Workflow("ci.yml").ShouldContain("\non:\n  pull_request:\n    branches: [ \"main\" ]\n  push:\n    branches: [ \"main\" ]\n  workflow_dispatch:\n\n");
        }

        [Fact]
        public void A_release_is_made_for_a_pushed_tag_and_the_workflow_run_by_hand_releases_nothing()
        {
            var release = Workflow("release.yml");

            release.ShouldContain("\non:\n  push:\n    tags: [ \"v*\" ]\n  workflow_dispatch:\n\n");
            Job(release, "release").ShouldContain("    if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')\n");
            Regex.Matches(release, @"gh release ").Count.ShouldBe(1);
            Job(release, "release").ShouldContain("gh release create");
        }

        [Fact]
        public void Only_the_job_that_makes_the_release_may_write_to_the_repository()
        {
            var release = Workflow("release.yml");

            release.ShouldContain("\npermissions:\n  contents: read\n");
            Regex.Matches(release, @"^\s*[a-z-]+: write$", RegexOptions.Multiline).Select(match => match.Value.Trim()).ShouldBe(["contents: write"]);
            Job(release, "release").ShouldContain("    permissions:\n      contents: write\n");
            Workflow("ci.yml").ShouldNotContain(": write");
        }

        [Fact]
        public void A_release_builds_the_three_programs_the_script_names_and_holds_the_Linux_one_to_its_c_library()
        {
            var programs = Job(Workflow("release.yml"), "programs");

            Regex.Matches(programs, @"rid: (\S+)").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ShouldBe(["linux-x64", "osx-arm64", "win-x64"]);
            programs.ShouldContain("scripts/aot-smoke.sh ${{ matrix.rid }}");
            programs.ShouldContain("scripts/glibc-floor.sh ");
            Job(Workflow("release.yml"), "assets").ShouldContain("scripts/release-assets.sh built assets");
        }

        [Fact]
        public void Every_pull_request_is_held_to_Windows_as_it_is_to_the_others()
        {
            var ci = Workflow("ci.yml");

            var tests = Job(ci, "tests");
            Regex.Matches(tests, @"os: (\S+)").Select(match => match.Groups[1].Value).ShouldBe(["ubuntu-latest", "windows-latest"]);
            tests.ShouldContain("check: scripts/check.sh --coverage\n");
            tests.ShouldContain("check: scripts/check.sh\n");
            tests.ShouldContain("      run: ${{ matrix.check }}\n");

            var native = Job(ci, "aot");
            Regex.Matches(native, @"rid: (\S+)").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ShouldBe(["linux-x64", "osx-arm64", "win-x64"]);
            native.ShouldContain("          - os: windows-latest\n            rid: win-x64\n");
            native.ShouldContain("scripts/aot-smoke.sh ${{ matrix.rid }}");
        }

        // A script is run by bash on every machine. Without this, Windows would give it to PowerShell.
        [Theory]
        [InlineData("ci.yml", "tests")]
        [InlineData("ci.yml", "aot")]
        [InlineData("release.yml", "programs")]
        public void A_job_that_runs_on_Windows_too_runs_its_scripts_with_bash(string workflow, string job)
        {
            Job(Workflow(workflow), job).ShouldContain("    defaults:\n      run:\n        shell: bash\n");
        }

        [Fact]
        public void The_Linux_program_is_built_and_held_to_its_c_library_in_ci_exactly_as_in_a_release()
        {
            var built = new Regex(@"          - os: (\S+)\n            rid: linux-x64\n");
            var held = new Regex(@"      if: runner\.os == 'Linux'\n      run: (scripts/glibc-floor\.sh .+)\n");
            var ci = Job(Workflow("ci.yml"), "aot");
            var release = Job(Workflow("release.yml"), "programs");

            built.Match(release).Groups[1].Value.ShouldBe("ubuntu-22.04");
            built.Match(ci).Groups[1].Value.ShouldBe("ubuntu-22.04");
            held.Match(release).Groups[1].Value.ShouldBe("scripts/glibc-floor.sh src/Packmoji.Cli/bin/Release/net10.0/${{ matrix.rid }}/publish/pmj 2.35");
            held.Match(ci).Groups[1].Value.ShouldBe(held.Match(release).Groups[1].Value);
        }

        [Fact]
        public void The_notes_of_a_release_and_the_guide_promise_the_c_library_the_program_is_held_to()
        {
            var release = Workflow("release.yml");
            var held = Regex.Match(release, @"scripts/glibc-floor\.sh .+ ([0-9.]+)\n").Groups[1].Value;

            held.ShouldNotBeEmpty();
            release.ShouldContain($"glibc {held} or newer");
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "cli.md")).ShouldContain($"glibc {held} or newer");
        }

        [Fact]
        public void A_tag_that_is_not_the_version_stops_a_release_before_anything_is_built()
        {
            var programs = Job(Workflow("release.yml"), "programs");

            programs.IndexOf("scripts/release-tag.sh", StringComparison.Ordinal).ShouldBeGreaterThan(0);
            programs.IndexOf("scripts/release-tag.sh", StringComparison.Ordinal).ShouldBeLessThan(programs.IndexOf("scripts/aot-smoke.sh", StringComparison.Ordinal));
        }
    }
}
