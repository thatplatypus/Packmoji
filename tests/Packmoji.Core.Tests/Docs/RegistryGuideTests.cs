using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Packmoji.Core.Archives;
using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Docs
{
    /// <summary>Holds docs/registry.md to the code, so that the guide cannot say what the registry's files and its keeper do not do.</summary>
    public sealed class RegistryGuideTests
    {
        private static readonly string Guide =
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "registry.md")).ReplaceLineEndings("\n");

        private static readonly string[] Shown = Regex.Matches(Guide, "```json\n(.*?)```", RegexOptions.Singleline).Select(match => match.Groups[1].Value).ToArray();

        // Every key of a JSON text, each as the guide's tables name it: versions[].quarantined.seen.
        private static IEnumerable<string> Keys(JsonElement element, string under)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray().SelectMany(item => Keys(item, under + "[]"));
            }

            // What a version needs is a table of packages, and a package is no key of the format.
            if (element.ValueKind != JsonValueKind.Object || under.EndsWith("dependencies", StringComparison.Ordinal))
            {
                return [];
            }

            return element.EnumerateObject().SelectMany(member =>
            {
                var key = under.Length == 0 ? member.Name : $"{under}.{member.Name}";
                return Keys(member.Value, key).Prepend(key);
            });
        }

        [Fact]
        public void The_guide_shows_one_file_of_each_kind() => Shown.Length.ShouldBe(2);

        [Fact]
        public void The_package_the_guide_shows_is_what_the_writer_writes_and_holds_a_version_of_each_shape()
        {
            var package = RegistryPackageReader.Read(Encoding.UTF8.GetBytes(Shown[0]), Sample.Name("@thatplatypus/grapevine"), "docs/registry.md").ShouldSucceed();

            RegistryPackageWriter.Write(package).ShouldBe(Shown[0]);
            package.Versions.Count(version => version.Quarantined is null).ShouldBe(1);
            package.Versions.Count(version => version.Quarantined is not null).ShouldBe(1);
        }

        [Fact]
        public void The_refused_releases_the_guide_shows_are_what_the_writer_writes()
        {
            var refused = RefusedReleasesReader.Read(Encoding.UTF8.GetBytes(Shown[1]), Sample.Repository("github.com/thatplatypus/grapevine"), "docs/registry.md").ShouldSucceed();

            RefusedReleasesWriter.Write(refused).ShouldBe(Shown[1]);
            refused.Releases.ShouldHaveSingleItem();
        }

        [Fact]
        public void Every_key_a_file_holds_is_in_the_table_under_it_and_the_tables_name_no_other()
        {
            var keys = Shown.SelectMany(text => Keys(JsonDocument.Parse(text).RootElement, "")).Distinct(StringComparer.Ordinal).ToList();

            // Sixteen of a package's file and seven more of the other, which has a "version" too.
            keys.Count.ShouldBe(23);
            foreach (var key in keys)
            {
                Guide.ShouldContain($"\n| `{key}` | ");
            }

            var named = Regex.Matches(Guide, @"^\| `((?:version|name|versions|repository|releases)(?:\[\])?(?:\.[a-z0-9]+)*)` \| (?:number|string|array|boolean|object) \|", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value)
                .ToList();
            named.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBe(keys.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void The_guide_gives_the_limits_the_code_holds_to()
        {
            Guide.ShouldContain($"**A run downloads at most {RegistryLimits.Default.MaxDownloads} files and {RegistryLimits.Default.MaxBytes / (1024 * 1024)} MiB.**");
            Guide.ShouldContain($"**A file is at most {RegistryPackageReader.MaxBytes / (1024 * 1024)} MiB** ({RegistryPackageReader.MaxBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)");
            Guide.ShouldContain($"an archive that `pmj pack` wrote, of at most {PackageArchive.MaxBytes / (1024 * 1024)} MiB");
        }

        [Fact]
        public void Every_reason_a_release_is_refused_for_is_in_the_guide_with_its_code_and_the_guide_gives_no_other()
        {
            RefusalReasons.All.Count.ShouldBe(9);
            foreach (var (code, words) in RefusalReasons.All)
            {
                // Where a sentence ends with what was found, the guide has the kind of thing it is, between angle brackets.
                var sentence = Regex.Escape(words).Replace(@"\{0}", "`<[a-z ]+>`");
                Regex.IsMatch(Guide, $@"^\| `{Regex.Escape(code)}` \| {sentence} \| [^|\n]+ \|$", RegexOptions.Multiline).ShouldBeTrue($"the guide has no row for: {words}");
            }

            var start = Guide.IndexOf("\n### Why a release is refused\n", StringComparison.Ordinal);
            start.ShouldBeGreaterThan(0);
            var section = Guide[start..Guide.IndexOf("\n## ", start, StringComparison.Ordinal)];
            Regex.Matches(section, @"^\| `[a-z]+\.[a-z-]+` \|", RegexOptions.Multiline).Count.ShouldBe(RefusalReasons.All.Count);
        }

        [Fact]
        public void The_guide_names_the_three_statuses_a_file_may_hold()
        {
            foreach (var status in new[] { RegistryStatuses.Active, RegistryStatuses.Yanked, RegistryStatuses.Quarantined })
            {
                Guide.ShouldContain($"\n| `{status}` | ");
            }
        }

        [Fact]
        public void The_line_the_guide_shows_for_the_list_reads_as_a_list_and_the_guide_names_the_list()
        {
            Guide.ShouldContain($"[`{RepositoryList.FileName}`](../{RepositoryList.FileName})");
            var shown = Regex.Match(Guide, "```\n(github\\.com/[^`]+)```").Groups[1].Value;

            RepositoryList.Read(shown).ShouldSucceed().Repositories.ShouldHaveSingleItem().ShouldBe(Sample.Repository("github.com/thatplatypus/grapevine"));
        }

        [Fact]
        public void The_address_the_guide_gives_for_a_file_ends_with_the_path_of_that_packages_file() =>
            Guide.ShouldContain($"\nhttps://raw.githubusercontent.com/thatplatypus/Packmoji/registry/{RegistryPaths.Package(Sample.Name("@thatplatypus/grapevine"))}\n");

        [Fact]
        public void The_guide_holds_no_em_dash() => Guide.ShouldNotContain(char.ConvertFromUtf32(0x2014));
    }
}
