using System.Reflection;
using System.Text.RegularExpressions;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Docs
{
    /// <summary>
    /// Holds docs/cli.md and docs/publishing.md to pmj itself, so that neither can show a command pmj
    /// does not have, or an answer pmj does not give.
    /// </summary>
    public sealed class GuideTests
    {
        private static string Guide(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", name)).ReplaceLineEndings("\n");

        // A digest in an example is any digest: what is shown is the shape.
        private static string Shape(string text) => Regex.Replace(text.ReplaceLineEndings("\n"), "[0-9a-f]{64}", new string('0', 64));

        private static void ShouldShow(string guide, Run run)
        {
            run.Error.ShouldBeEmpty();
            Shape(guide).ShouldContain(Shape(run.Output).TrimEnd('\n'));
        }

        [Fact]
        public void Every_command_and_every_option_pmj_has_is_in_the_guide()
        {
            using var sandbox = new Sandbox();
            var guide = Guide("cli.md");
            var root = PmjCommandLine.Build(sandbox.Host());

            root.Subcommands.Select(command => command.Name).ShouldBe(["new", "init", "add", "remove", "install", "update", "tree", "pack", "verify"]);
            foreach (var command in root.Subcommands)
            {
                guide.ShouldContain($"| `pmj {command.Name}");

                // The line that shows how the command is written has to show everything it takes.
                var written = guide.Split('\n').Where(line => line == $"pmj {command.Name}" || line.StartsWith($"pmj {command.Name} ", StringComparison.Ordinal)).ToList();
                written.ShouldNotBeEmpty();
                foreach (var option in command.Options)
                {
                    written.ShouldContain(line => line.Contains(option.Name, StringComparison.Ordinal), $"pmj {command.Name} takes {option.Name}");
                }
            }

            foreach (var option in root.Options.Where(option => option.Name is not ("--help" or "--version")))
            {
                guide.ShouldContain($"`{option.Name}`");
            }
        }

        [Fact]
        public void Every_way_pmj_ends_is_in_the_guide_with_its_number()
        {
            var statuses = typeof(ExitStatus).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (int)field.GetRawConstantValue()!).ToList();

            statuses.Order().ShouldBe([0, 1, 2, 70, 130]);
            foreach (var status in statuses)
            {
                Guide("cli.md").ShouldContain($"| `{status}` |");
            }
        }

        [Fact]
        public void Every_variable_pmj_reads_from_its_environment_is_in_the_guide()
        {
            var read = new List<string>();
            PmjHost.From(
                name =>
                {
                    read.Add(name);
                    return null;
                },
                ".",
                new HttpClient(),
                new StringWriter(),
                new StringWriter());

            read.Order(StringComparer.Ordinal).ShouldBe(["GH_TOKEN", "GITHUB_TOKEN", "PACKMOJI_GITHUB", "PACKMOJI_GITHUB_API", "PACKMOJI_HOME"]);
            foreach (var variable in read)
            {
                Guide("cli.md").ShouldContain($"`{variable}`");
            }
        }

        [Fact]
        public async Task The_first_project_in_the_guide_is_what_pmj_prints()
        {
            using var sandbox = Sandbox.WithGrapevine();
            var guide = Guide("cli.md");

            ShouldShow(guide, await sandbox.RunAsync("new", "@you/site"));
            ShouldShow(guide, await sandbox.RunInAsync("site", "add", "@thatplatypus/grapevine"));
            ShouldShow(guide, await sandbox.RunInAsync("site", "tree"));
            ShouldShow(guide, await sandbox.RunInAsync("site", "tree", "--json"));
        }

        [Fact]
        public async Task The_dry_run_in_the_guide_is_what_update_prints()
        {
            using var sandbox = new Sandbox();
            foreach (var version in new[] { "1.0.0", "1.2.0", "1.10.0", "2.0.0" })
            {
                sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", version);
            }

            sandbox.Project("@you/site", "@thatplatypus/crypto@1.0");
            await sandbox.RunAsync("install");

            ShouldShow(Guide("cli.md"), await sandbox.RunAsync("update", "--dry-run"));
        }

        [Fact]
        public async Task The_package_that_has_to_be_pointed_to_is_shown_as_pmj_reports_it_and_as_pmj_then_adds_it()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@you/site");
            var guide = Guide("cli.md");

            var lost = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            lost.Status.ShouldBe(1);
            guide.ShouldContain(lost.Error.ReplaceLineEndings("\n").TrimEnd('\n'));
            ShouldShow(guide, await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", "github.com/thatplatypus/grapevine"));
        }

        [Fact]
        public void Every_manifest_in_the_publishing_guide_reads()
        {
            var manifests = Regex.Matches(Guide("publishing.md"), "```json\n(.*?)\n```", RegexOptions.Singleline).Select(match => match.Groups[1].Value).ToList();

            manifests.Count.ShouldBe(2);
            foreach (var manifest in manifests)
            {
                ManifestReader.Read(manifest).Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ShouldBeEmpty();
            }
        }

        [Fact]
        public async Task What_the_publishing_guide_shows_of_pack_is_what_pack_prints_for_the_manifest_it_shows()
        {
            using var sandbox = new Sandbox();
            var guide = Guide("publishing.md");
            sandbox.Write("packmoji.json", Regex.Matches(guide, "```json\n(.*?)\n```", RegexOptions.Singleline)[1].Groups[1].Value);
            sandbox.Write("crypto.🍇", "🌍 🐇 🔐 🍇 🍉\n");
            sandbox.Write("README.md", "# crypto\n");

            ShouldShow(guide, await sandbox.RunAsync("pack"));
        }

        [Fact]
        public void The_release_the_publishing_guide_makes_has_the_tag_and_the_file_pmj_looks_for()
        {
            PackageName.TryParse("@thatplatypus/crypto", out var crypto, out _).ShouldBeTrue();
            SemanticVersion.TryParse("0.1.0", out var version, out _).ShouldBeTrue();
            var tag = ReleaseTag.For(crypto!, version!);
            var guide = Guide("publishing.md");

            guide.ShouldContain($"git tag {tag}\n");
            guide.ShouldContain($"git push origin {tag}\n");
            guide.ShouldContain($"gh release create {tag} target/{AssetName.For(crypto!, version!)} ");
        }

        [Theory]
        [InlineData("cli.md")]
        [InlineData("publishing.md")]
        public void A_guide_holds_no_em_dash(string name) => Guide(name).ShouldNotContain(char.ConvertFromUtf32(0x2014));
    }
}
