using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// The file a registry keeps of the releases of one repository that claim to be a package and
    /// were refused. It is shown to people, and what a release holds is a stranger's text, so what
    /// may stand in it is held as closely as its form.
    /// </summary>
    public sealed class RefusedReleasesTests
    {
        private const string File = "v1/refused/thatplatypus/grapevine.json";

        private static readonly RepositoryRef Grapevine = Sample.Repository("github.com/thatplatypus/grapevine");

        private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        private static ReadResult<RefusedReleases> Read(string text) => RefusedReleasesReader.Read(Encoding.UTF8.GetBytes(text), Grapevine, File);

        private static Diagnostic ShouldFailAtMark(string fixture, string code)
        {
            var marked = Marked.From(fixture);
            var diagnostic = Read(marked.Text).ShouldFailAt(marked, code, File);
            diagnostic.Fix.ShouldBe(RegistryPackageReader.KeeperFix);
            return diagnostic;
        }

        private static Diagnostic ReleaseShouldFail(RegistryFixture release, string code) => ShouldFailAtMark(RegistryFixture.Refused(release).ToString(), code);

        [Fact]
        public void What_was_refused_is_written_in_one_form_in_order_of_tag()
        {
            var refused = new RefusedReleases(
                Grapevine,
                [
                    new RefusedRelease("deflate-v0.1.0", Sample.ShaOf(RegistryFixture.OtherSha), Time("2026-10-12T08:17:03Z"), "archive.invalid", "its file is not an archive as pmj pack writes one"),
                    new RefusedRelease("crypto-v0.2.0", Sample.ShaOf(RegistryFixture.Sha), Time("2026-10-12T10:17:03+02:00"), "release.invalid", "the manifest in its archive gives the version 0.1.0"),
                ]);

            RefusedReleasesWriter.Write(refused).ShouldBe(
                """
                {
                  "version": 1,
                  "repository": "github.com/thatplatypus/grapevine",
                  "releases": [
                    {
                      "tag": "crypto-v0.2.0",
                      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                      "seen": "2026-10-12T08:17:03Z",
                      "code": "release.invalid",
                      "why": "the manifest in its archive gives the version 0.1.0"
                    },
                    {
                      "tag": "deflate-v0.1.0",
                      "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                      "seen": "2026-10-12T08:17:03Z",
                      "code": "archive.invalid",
                      "why": "its file is not an archive as pmj pack writes one"
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void What_is_written_reads_back_as_what_it_was()
        {
            var refused = new RefusedReleases(
                Grapevine,
                [new RefusedRelease("crypto-v0.2.0", Sample.ShaOf(RegistryFixture.Sha), Time("2026-10-12T08:17:03Z"), "release.invalid", "the manifest in its archive is that of \"@thatplatypus/deflate\"")]);
            var written = RefusedReleasesWriter.Write(refused);

            var read = Read(written).ShouldSucceed();

            read.Repository.ShouldBe(Grapevine);
            read.Releases.ShouldHaveSingleItem().ShouldBe(refused.Releases[0]);
            RefusedReleasesWriter.Write(read).ShouldBe(written);
        }

        [Fact]
        public void Releases_are_read_in_order_of_tag_whatever_order_the_file_has_them_in() =>
            Read(RegistryFixture.Refused(RegistryFixture.Refusal("deflate-v0.1.0"), RegistryFixture.Refusal("crypto-v0.2.0"), RegistryFixture.Refusal("crypto-v0.10.0")).ToString())
                .ShouldSucceed().Releases.Select(release => release.Tag).ShouldBe(["crypto-v0.10.0", "crypto-v0.2.0", "deflate-v0.1.0"]);

        [Fact]
        public void A_key_this_pmj_does_not_know_is_passed_over_wherever_it_stands() =>
            Read(RegistryFixture.Refused(RegistryFixture.Refusal().With("asset", "599854636")).With("checked", "\"2026-10-12T09:17:03Z\"").ToString())
                .ShouldSucceed().Releases.ShouldHaveSingleItem().Tag.ShouldBe("crypto-v0.2.0");

        [Theory]
        [InlineData("version")]
        [InlineData("repository")]
        [InlineData("releases")]
        public void A_file_without_one_of_its_keys_is_refused(string key) =>
            ShouldFailAtMark(RegistryFixture.Refused(RegistryFixture.Refusal()).Without(key).Marked(), DiagnosticCodes.KeyMissing).Message.ShouldContain($"\"{key}\"");

        [Theory]
        [InlineData("tag")]
        [InlineData("sha256")]
        [InlineData("seen")]
        [InlineData("code")]
        [InlineData("why")]
        public void A_release_without_one_of_its_keys_is_refused(string key) =>
            ShouldFailAtMark(RegistryFixture.Refused().With("releases", $"[ {RegistryFixture.Refusal().Without(key).Marked()} ]").ToString(), DiagnosticCodes.KeyMissing)
                .Message.ShouldContain($"\"{key}\"");

        [Fact]
        public void A_file_that_does_not_say_it_is_of_the_first_format_is_refused() =>
            ShouldFailAtMark(RegistryFixture.Refused(RegistryFixture.Refusal().Without("why")).With("version", "§2").ToString(), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("v1");

        [Fact]
        public void A_file_of_another_repository_is_refused_where_it_names_it()
        {
            var diagnostic = ShouldFailAtMark(RegistryFixture.Refused(RegistryFixture.Refusal()).With("repository", "§\"github.com/thatplatypus/crypto\"").ToString(), DiagnosticCodes.RegistryInvalid);

            diagnostic.Message.ShouldContain("github.com/thatplatypus/crypto");
            diagnostic.Reason.ShouldContain("github.com/thatplatypus/grapevine");
        }

        [Fact]
        public void A_repository_that_is_no_repository_is_refused() =>
            ShouldFailAtMark(RegistryFixture.Refused(RegistryFixture.Refusal()).With("repository", "§\"thatplatypus/grapevine\"").ToString(), DiagnosticCodes.RepositoryInvalid);

        [Theory]
        [InlineData("§\"v0.2.0\"")]
        [InlineData("§\"crypto-0.2.0\"")]
        [InlineData("§\"crypto-v0.2\"")]
        [InlineData("§\"Crypto-v0.2.0\"")]
        [InlineData("§\"crypto-v0.2.0\\n::error::made up\"")]
        [InlineData("§\"\"")]
        public void A_tag_that_names_no_version_of_a_package_is_refused(string tag) =>
            ReleaseShouldFail(RegistryFixture.Refusal().With("tag", tag), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("crypto-v1.0.0");

        [Fact]
        public void A_tag_that_is_there_twice_is_refused_at_the_second() =>
            ShouldFailAtMark(
                RegistryFixture.Refused(RegistryFixture.Refusal("crypto-v0.2.0"), RegistryFixture.Refusal("crypto-v0.2.0").With("tag", "§\"crypto-v0.2.0\"")).ToString(),
                DiagnosticCodes.RegistryInvalid).Message.ShouldContain("crypto-v0.2.0");

        [Fact]
        public void A_digest_that_is_no_digest_is_refused() =>
            ReleaseShouldFail(RegistryFixture.Refusal().With("sha256", "§\"abc\""), DiagnosticCodes.Sha256Invalid);

        [Fact]
        public void A_time_that_is_not_written_in_UTC_to_the_second_is_refused() =>
            ReleaseShouldFail(RegistryFixture.Refusal().With("seen", "§\"2026-10-12\""), DiagnosticCodes.RegistryInvalid);

        [Theory]
        [InlineData("§\"\"")]
        [InlineData("§\"invalid\"")]
        [InlineData("§\"Release.Invalid\"")]
        [InlineData("§\"release.invalid now\"")]
        [InlineData("§\"release..invalid\"")]
        [InlineData("§\".invalid\"")]
        [InlineData("§\"release.\"")]
        [InlineData("§\"release.invalid-\"")]
        [InlineData("§\"a.b.c\"")]
        public void A_code_that_is_not_written_as_a_code_is_refused(string code) =>
            ReleaseShouldFail(RegistryFixture.Refusal().With("code", code), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("release.invalid");

        // What is refused is refused for what a stranger's file holds, and this file is shown to people
        // and printed by tools. So a reason is one line of text that can be seen, and no more than that.
        [Theory]
        [InlineData("§\"\"")]
        [InlineData("§\"   \"")]
        [InlineData("§\"two\\nlines\"")]
        [InlineData("§\"a bell \\u0007\"")]
        [InlineData("§\"an escape \\u001b[31m\"")]
        [InlineData("§\"turned \\u202e round\"")]
        [InlineData("§\" a space before\"")]
        [InlineData("§\"a space after \"")]
        public void A_reason_that_is_not_one_line_of_text_that_can_be_seen_is_refused(string why) =>
            ReleaseShouldFail(RegistryFixture.Refusal().With("why", why), DiagnosticCodes.RegistryInvalid);

        [Fact]
        public void A_reason_of_more_than_three_hundred_characters_is_refused_and_one_of_three_hundred_reads()
        {
            ReleaseShouldFail(RegistryFixture.Refusal().With("why", $"§\"{new string('a', 301)}\""), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("300");

            Read(RegistryFixture.Refused(RegistryFixture.Refusal().With("why", $"\"{new string('a', 300)}\"")).ToString()).ShouldSucceed();
        }

        [Fact]
        public void A_file_over_four_mebibytes_is_refused_before_any_of_it_is_read() =>
            RefusedReleasesReader.Read(new byte[RegistryPackageReader.MaxBytes + 1], Grapevine, File).Diagnostics.ShouldHaveSingleItem().ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.FileTooLarge);

        [Fact]
        public void A_file_that_is_not_JSON_or_holds_no_object_is_refused()
        {
            Read("not JSON").Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.JsonSyntax);
            ShouldFailAtMark("§[]", DiagnosticCodes.JsonWrongType);
        }

        [Fact]
        public void An_entry_of_the_releases_that_is_not_an_object_is_refused() =>
            ShouldFailAtMark(RegistryFixture.Refused().With("releases", "[ §7 ]").ToString(), DiagnosticCodes.JsonWrongType);
    }
}
