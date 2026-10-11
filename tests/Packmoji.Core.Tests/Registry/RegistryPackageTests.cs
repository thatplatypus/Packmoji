using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// The file a registry keeps of one package. Every pmj that was ever released reads it, so what
    /// is held here is the form to the byte, each rule of reading, and that a key this pmj does not
    /// know stops nothing.
    /// </summary>
    public sealed class RegistryPackageTests
    {
        private const string File = "v1/packages/thatplatypus/crypto.json";

        private static readonly PackageName Crypto = Sample.Name("@thatplatypus/crypto");

        private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        private static RegistryVersion Version(string version, params string[] dependencies) => new(
            Sample.Version(version),
            VersionStatus.Active,
            Sample.Repository("github.com/thatplatypus/grapevine"),
            Sample.ShaOf(RegistryFixture.Sha),
            48213,
            VerificationLevel.Checksum,
            IsImmutable: false,
            Time("2026-10-11T15:02:11Z"),
            Time("2026-10-11T15:17:40Z"),
            dependencies.Select(dependency => dependency.Split('@', StringSplitOptions.RemoveEmptyEntries)).Select(parts => new Dependency(Sample.Name("@" + parts[0]), Sample.Requirement(parts[1]))).ToList());

        private static ReadResult<RegistryPackage> Read(string text) => RegistryPackageReader.Read(Encoding.UTF8.GetBytes(text), Crypto, File);

        private static RegistryPackage ShouldRead(RegistryFixture fixture) => Read(fixture.ToString()).ShouldSucceed();

        private static Diagnostic ShouldFailAtMark(string fixture, string code)
        {
            var marked = Marked.From(fixture);
            var diagnostic = Read(marked.Text).ShouldFailAt(marked, code, File);
            diagnostic.Fix.ShouldBe(RegistryPackageReader.KeeperFix);
            return diagnostic;
        }

        private static Diagnostic VersionShouldFail(RegistryFixture version, string code) => ShouldFailAtMark(RegistryFixture.Package(version).ToString(), code);

        [Fact]
        public void A_package_is_written_in_one_form_with_its_versions_and_what_each_needs_in_order()
        {
            var package = new RegistryPackage(
                Sample.Name("@thatplatypus/grapevine"),
                [
                    Version("0.3.0", "@thatplatypus/deflate@0.1", "@thatplatypus/crypto@1.0") with { IsImmutable = true, Verified = VerificationLevel.Attestation },
                    Version("0.1.0") with
                    {
                        Status = VersionStatus.Quarantined,
                        Quarantined = new RegistryQuarantine(Time("2026-11-02T09:17:12Z"), Sample.ShaOf(RegistryFixture.OtherSha)),
                    },
                    Version("0.2.0") with { Status = VersionStatus.Yanked },
                ]);

            RegistryPackageWriter.Write(package).ShouldBe(
                """
                {
                  "version": 1,
                  "name": "@thatplatypus/grapevine",
                  "versions": [
                    {
                      "version": "0.1.0",
                      "status": "quarantined",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                      "size": 48213,
                      "verified": "checksum",
                      "immutable": false,
                      "published": "2026-10-11T15:02:11Z",
                      "indexed": "2026-10-11T15:17:40Z",
                      "dependencies": {},
                      "quarantined": {
                        "seen": "2026-11-02T09:17:12Z",
                        "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
                      }
                    },
                    {
                      "version": "0.2.0",
                      "status": "yanked",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                      "size": 48213,
                      "verified": "checksum",
                      "immutable": false,
                      "published": "2026-10-11T15:02:11Z",
                      "indexed": "2026-10-11T15:17:40Z",
                      "dependencies": {}
                    },
                    {
                      "version": "0.3.0",
                      "status": "active",
                      "source": "github.com/thatplatypus/grapevine",
                      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                      "size": 48213,
                      "verified": "attestation",
                      "immutable": true,
                      "published": "2026-10-11T15:02:11Z",
                      "indexed": "2026-10-11T15:17:40Z",
                      "dependencies": {
                        "@thatplatypus/crypto": "1.0",
                        "@thatplatypus/deflate": "0.1"
                      }
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_time_is_written_in_UTC_to_the_second_whatever_zone_it_was_given_in()
        {
            var package = new RegistryPackage(Crypto, [Version("1.0.0") with { Published = new DateTimeOffset(2026, 10, 11, 17, 2, 11, 900, TimeSpan.FromHours(2)) }]);

            RegistryPackageWriter.Write(package).ShouldContain("\"published\": \"2026-10-11T15:02:11Z\"");
        }

        [Fact]
        public void What_is_written_reads_back_as_what_it_was()
        {
            var package = new RegistryPackage(
                Crypto,
                [
                    Version("1.0.0-beta.1"),
                    Version("1.0.0", "@thatplatypus/deflate@0.1"),
                    Version("1.2.0") with
                    {
                        Status = VersionStatus.Quarantined,
                        IsImmutable = true,
                        Quarantined = new RegistryQuarantine(Time("2026-11-02T09:17:12Z"), Sample.ShaOf(RegistryFixture.OtherSha)),
                    },
                ]);
            var written = RegistryPackageWriter.Write(package);

            var read = Read(written).ShouldSucceed();

            read.Name.ShouldBe(Crypto);
            read.Versions.Select(version => version.Version.ToString()).ShouldBe(["1.0.0-beta.1", "1.0.0", "1.2.0"]);
            read.Versions[1].Dependencies.ShouldHaveSingleItem().ShouldBe(new Dependency(Sample.Name("@thatplatypus/deflate"), Sample.Requirement("0.1")));
            read.Versions[2].Status.ShouldBe(VersionStatus.Quarantined);
            read.Versions[2].IsImmutable.ShouldBeTrue();
            read.Versions[2].Quarantined.ShouldBe(new RegistryQuarantine(Time("2026-11-02T09:17:12Z"), Sample.ShaOf(RegistryFixture.OtherSha)));
            RegistryPackageWriter.Write(read).ShouldBe(written);
        }

        [Fact]
        public void Every_key_of_a_version_is_read()
        {
            var version = ShouldRead(RegistryFixture.Package(RegistryFixture.Version()
                .With("status", "\"yanked\"")
                .With("verified", "\"attestation\"")
                .With("immutable", "true")
                .With("dependencies", "{ \"@thatplatypus/deflate\": \"0.1.2\" }"))).Versions.ShouldHaveSingleItem();

            version.Version.ShouldBe(Sample.Version("1.0.0"));
            version.Status.ShouldBe(VersionStatus.Yanked);
            version.Source.ShouldBe(Sample.Repository("github.com/thatplatypus/grapevine"));
            version.Sha256.ShouldBe(Sample.ShaOf(RegistryFixture.Sha));
            version.Size.ShouldBe(1024);
            version.Verified.ShouldBe(VerificationLevel.Attestation);
            version.IsImmutable.ShouldBeTrue();
            version.Published.ShouldBe(Time("2026-10-11T15:02:11Z"));
            version.Indexed.ShouldBe(Time("2026-10-11T15:17:40Z"));
            version.Dependencies.ShouldHaveSingleItem().Requirement.Text.ShouldBe("0.1.2");
            version.Quarantined.ShouldBeNull();
        }

        [Fact]
        public void A_package_with_no_version_reads() => ShouldRead(RegistryFixture.Package()).Versions.ShouldBeEmpty();

        [Fact]
        public void Versions_are_read_lowest_first_whatever_order_the_file_has_them_in() =>
            ShouldRead(RegistryFixture.Package(RegistryFixture.Version("1.10.0"), RegistryFixture.Version("1.2.0"), RegistryFixture.Version("1.0.0-rc.1")))
                .Versions.Select(version => version.Version.ToString()).ShouldBe(["1.0.0-rc.1", "1.2.0", "1.10.0"]);

        [Fact]
        public void A_version_is_found_by_its_number()
        {
            var package = ShouldRead(RegistryFixture.Package(RegistryFixture.Version("1.0.0"), RegistryFixture.Version("1.2.0")));

            package.Find(Sample.Version("1.2.0")).ShouldNotBeNull().Version.ShouldBe(Sample.Version("1.2.0"));
            package.Find(Sample.Version("1.1.0")).ShouldBeNull();
        }

        // The registry is read by every pmj that was ever released, and gains keys as it grows. A pmj
        // that refused what it does not know would stop working the day one is added.
        [Fact]
        public void A_key_this_pmj_does_not_know_is_passed_over_wherever_it_stands()
        {
            var quarantined = RegistryFixture.Version()
                .With("status", "\"quarantined\"")
                .With("quarantined", $"{{ \"seen\": \"2026-11-02T09:17:12Z\", \"sha256\": \"{RegistryFixture.OtherSha}\", \"by\": \"a run\" }}")
                .With("license", "\"MIT\"")
                .With("attestation", "{ \"workflow\": \"release.yml\" }");

            var package = ShouldRead(RegistryFixture.Package(quarantined).With("description", "\"hashes\"").With("latest", "null"));

            package.Versions.ShouldHaveSingleItem().Quarantined.ShouldNotBeNull().Sha256.ShouldBe(Sample.ShaOf(RegistryFixture.OtherSha));
        }

        [Theory]
        [InlineData("version")]
        [InlineData("name")]
        [InlineData("versions")]
        public void A_package_without_one_of_its_keys_is_refused(string key) =>
            ShouldFailAtMark(RegistryFixture.Package(RegistryFixture.Version()).Without(key).Marked(), DiagnosticCodes.KeyMissing).Message.ShouldContain($"\"{key}\"");

        [Theory]
        [InlineData("version")]
        [InlineData("status")]
        [InlineData("source")]
        [InlineData("sha256")]
        [InlineData("size")]
        [InlineData("verified")]
        [InlineData("immutable")]
        [InlineData("published")]
        [InlineData("indexed")]
        [InlineData("dependencies")]
        public void A_version_without_one_of_its_keys_is_refused(string key) =>
            ShouldFailAtMark(RegistryFixture.Package(RegistryFixture.Version().Without(key).Marked()).ToString(), DiagnosticCodes.KeyMissing).Message.ShouldContain($"\"{key}\"");

        [Theory]
        [InlineData("version", "§7")]
        [InlineData("status", "§true")]
        [InlineData("source", "§[]")]
        [InlineData("sha256", "§null")]
        [InlineData("size", "§\"1024\"")]
        [InlineData("verified", "§1")]
        [InlineData("immutable", "§\"false\"")]
        [InlineData("published", "§1791730931")]
        [InlineData("indexed", "§{}")]
        [InlineData("dependencies", "§[]")]
        [InlineData("quarantined", "§\"yes\"")]
        public void A_key_of_a_version_that_is_of_the_wrong_kind_is_refused(string key, string value) =>
            VersionShouldFail(RegistryFixture.Version().With(key, value), DiagnosticCodes.JsonWrongType);

        [Theory]
        [InlineData("§2", "2")]
        [InlineData("§0", "0")]
        [InlineData("§1.0", "1.0")]
        public void A_file_that_does_not_say_it_is_of_the_first_format_is_refused_and_nothing_else_is_said_of_it(string version, string shown)
        {
            var diagnostic = ShouldFailAtMark(RegistryFixture.Package(RegistryFixture.Version().Without("sha256")).With("version", version).ToString(), DiagnosticCodes.RegistryInvalid);

            diagnostic.Message.ShouldContain(shown);
            diagnostic.Reason.ShouldContain("v1");
        }

        [Fact]
        public void A_file_of_another_package_is_refused_where_it_gives_its_name()
        {
            var diagnostic = ShouldFailAtMark(RegistryFixture.Package(RegistryFixture.Version()).With("name", "§\"@thatplatypus/deflate\"").ToString(), DiagnosticCodes.RegistryInvalid);

            diagnostic.Message.ShouldContain("@thatplatypus/deflate");
            diagnostic.Reason.ShouldContain("@thatplatypus/crypto");
        }

        [Fact]
        public void A_name_that_is_no_package_name_is_refused() =>
            ShouldFailAtMark(RegistryFixture.Package(RegistryFixture.Version()).With("name", "§\"crypto\"").ToString(), DiagnosticCodes.NameInvalid);

        [Fact]
        public void An_entry_of_the_versions_that_is_not_an_object_is_refused() =>
            ShouldFailAtMark(RegistryFixture.Package("§\"1.0.0\"").ToString(), DiagnosticCodes.JsonWrongType);

        [Fact]
        public void A_version_that_is_no_version_is_refused() =>
            VersionShouldFail(RegistryFixture.Version().With("version", "§\"1.0\""), DiagnosticCodes.VersionInvalid);

        [Fact]
        public void A_version_that_is_there_twice_is_refused_at_the_second()
        {
            var twice = RegistryFixture.Package(RegistryFixture.Version("1.0.0").ToString() + ",\n" + RegistryFixture.Version("1.0.0").With("version", "§\"1.0.0\"").ToString());

            ShouldFailAtMark(twice.ToString(), DiagnosticCodes.RegistryInvalid).Message.ShouldContain("1.0.0");
        }

        [Theory]
        [InlineData("§\"Active\"")]
        [InlineData("§\"deleted\"")]
        [InlineData("§\"\"")]
        public void A_status_that_is_not_one_of_the_three_is_refused(string status) =>
            VersionShouldFail(RegistryFixture.Version().With("status", status), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("\"active\", \"yanked\" or \"quarantined\"");

        [Theory]
        [InlineData("§\"signed\"")]
        [InlineData("§\"Checksum\"")]
        public void A_level_of_verification_that_is_not_one_of_the_two_is_refused(string verified) =>
            VersionShouldFail(RegistryFixture.Version().With("verified", verified), DiagnosticCodes.VerifiedInvalid);

        [Fact]
        public void A_source_that_the_packages_scope_does_not_own_is_refused() =>
            VersionShouldFail(RegistryFixture.Version().With("source", "§\"github.com/someone/crypto\""), DiagnosticCodes.RepositoryOwnerMismatch).Reason.ShouldContain("thatplatypus");

        [Fact]
        public void A_source_that_is_no_repository_is_refused() =>
            VersionShouldFail(RegistryFixture.Version().With("source", "§\"https://github.com/thatplatypus/grapevine\""), DiagnosticCodes.RepositoryInvalid);

        [Fact]
        public void A_digest_that_is_no_digest_is_refused() =>
            VersionShouldFail(RegistryFixture.Version().With("sha256", "§\"abc\""), DiagnosticCodes.Sha256Invalid);

        [Theory]
        [InlineData("§0")]
        [InlineData("§-5")]
        [InlineData("§1.5")]
        [InlineData("§1e3")]
        [InlineData("§16777217")]
        [InlineData("§99999999999999999999")]
        public void A_size_that_no_archive_could_be_is_refused(string size) =>
            VersionShouldFail(RegistryFixture.Version().With("size", size), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("16777216");

        [Theory]
        [InlineData("1")]
        [InlineData("16777216")]
        public void A_size_from_one_byte_to_the_most_an_archive_may_be_reads(string size) =>
            ShouldRead(RegistryFixture.Package(RegistryFixture.Version().With("size", size))).Versions.ShouldHaveSingleItem().Size.ShouldBe(int.Parse(size));

        [Theory]
        [InlineData("published", "§\"2026-10-11\"")]
        [InlineData("published", "§\"2026-10-11T15:02:11\"")]
        [InlineData("published", "§\"2026-10-11T15:02:11+02:00\"")]
        [InlineData("published", "§\"2026-10-11T15:02:11.250Z\"")]
        [InlineData("published", "§\"2026-10-11 15:02:11Z\"")]
        [InlineData("published", "§\"2026-13-41T15:02:11Z\"")]
        [InlineData("indexed", "§\"yesterday\"")]
        [InlineData("indexed", "§\"\"")]
        public void A_time_that_is_not_written_in_UTC_to_the_second_is_refused(string key, string time) =>
            VersionShouldFail(RegistryFixture.Version().With(key, time), DiagnosticCodes.RegistryInvalid).Reason.ShouldContain("2026-10-11T15:02:11Z");

        [Fact]
        public void A_quarantined_version_says_when_its_bytes_were_seen_changed_and_what_they_were()
        {
            var quarantined = RegistryFixture.Version()
                .With("status", "\"quarantined\"")
                .With("quarantined", $"{{ \"seen\": \"2026-11-02T09:17:12Z\", \"sha256\": \"{RegistryFixture.OtherSha}\" }}");

            var version = ShouldRead(RegistryFixture.Package(quarantined)).Versions.ShouldHaveSingleItem();

            version.Status.ShouldBe(VersionStatus.Quarantined);
            version.Quarantined.ShouldBe(new RegistryQuarantine(Time("2026-11-02T09:17:12Z"), Sample.ShaOf(RegistryFixture.OtherSha)));
        }

        [Fact]
        public void A_quarantined_version_that_does_not_say_so_is_refused() =>
            VersionShouldFail(RegistryFixture.Version().With("status", "§\"quarantined\""), DiagnosticCodes.RegistryInvalid).Message.ShouldContain("\"quarantined\"");

        [Theory]
        [InlineData("\"active\"")]
        [InlineData("\"yanked\"")]
        public void A_version_that_is_not_quarantined_and_says_it_was_is_refused(string status) =>
            VersionShouldFail(
                RegistryFixture.Version().With("status", status).With("quarantined", $"§{{ \"seen\": \"2026-11-02T09:17:12Z\", \"sha256\": \"{RegistryFixture.OtherSha}\" }}"),
                DiagnosticCodes.RegistryInvalid);

        [Theory]
        [InlineData("§{ \"sha256\": \"" + RegistryFixture.OtherSha + "\" }", DiagnosticCodes.KeyMissing)]
        [InlineData("§{ \"seen\": \"2026-11-02T09:17:12Z\" }", DiagnosticCodes.KeyMissing)]
        [InlineData("{ \"seen\": §\"then\", \"sha256\": \"" + RegistryFixture.OtherSha + "\" }", DiagnosticCodes.RegistryInvalid)]
        [InlineData("{ \"seen\": \"2026-11-02T09:17:12Z\", \"sha256\": §\"abc\" }", DiagnosticCodes.Sha256Invalid)]
        public void What_a_quarantine_says_is_held_as_the_rest_is(string quarantined, string code) =>
            VersionShouldFail(RegistryFixture.Version().With("status", "\"quarantined\"").With("quarantined", quarantined), code);

        [Theory]
        [InlineData("{ §\"crypto\": \"1.0\" }", DiagnosticCodes.NameInvalid)]
        [InlineData("{ \"@thatplatypus/deflate\": §\"^1.0\" }", DiagnosticCodes.RequirementInvalid)]
        [InlineData("{ \"@thatplatypus/deflate\": §1 }", DiagnosticCodes.JsonWrongType)]
        [InlineData("{ §\"@thatplatypus/crypto\": \"1.0\" }", DiagnosticCodes.DependencySelf)]
        [InlineData("{ \"@thatplatypus/deflate\": \"0.1\", §\"@thatplatypus/deflate\": \"0.2\" }", DiagnosticCodes.JsonDuplicateKey)]
        public void What_a_version_needs_is_held_to_being_packages_and_requirements_and_not_itself(string dependencies, string code) =>
            VersionShouldFail(RegistryFixture.Version().With("dependencies", dependencies), code);

        [Fact]
        public void Every_problem_of_a_file_is_said_and_not_only_the_first()
        {
            var result = Read(RegistryFixture.Package(RegistryFixture.Version().With("size", "0").With("status", "\"gone\"").With("sha256", "\"abc\"")).ToString());

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([DiagnosticCodes.RegistryInvalid, DiagnosticCodes.Sha256Invalid, DiagnosticCodes.RegistryInvalid], ignoreOrder: true);
            result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Fix == RegistryPackageReader.KeeperFix);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("\"a package\"")]
        public void A_file_that_holds_something_other_than_an_object_is_refused(string text) =>
            ShouldFailAtMark("§" + text, DiagnosticCodes.JsonWrongType);

        [Fact]
        public void A_file_that_is_not_JSON_is_refused() =>
            Read("{ \"version\": 1, ").Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.JsonSyntax);

        [Fact]
        public void A_file_over_four_mebibytes_is_refused_before_any_of_it_is_read()
        {
            var result = RegistryPackageReader.Read(new byte[RegistryPackageReader.MaxBytes + 1], Crypto, File);

            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.FileTooLarge);
            diagnostic.Reason.ShouldContain("4194304");
            diagnostic.Fix.ShouldBe(RegistryPackageReader.KeeperFix);
        }

        [Fact]
        public void Two_records_of_a_version_are_the_same_but_for_status_when_only_the_status_and_the_quarantine_differ()
        {
            var version = Version("1.0.0", "@thatplatypus/deflate@0.1");
            var quarantined = Version("1.0.0", "@thatplatypus/deflate@0.1") with
            {
                Status = VersionStatus.Quarantined,
                Quarantined = new RegistryQuarantine(Time("2026-11-02T09:17:12Z"), Sample.ShaOf(RegistryFixture.OtherSha)),
            };

            version.SameButForStatus(quarantined).ShouldBeTrue();
            version.SameButForStatus(version with { Sha256 = Sample.ShaOf(RegistryFixture.OtherSha) }).ShouldBeFalse();
            version.SameButForStatus(version with { Source = Sample.Repository("github.com/thatplatypus/crypto") }).ShouldBeFalse();
            version.SameButForStatus(version with { Size = 1 }).ShouldBeFalse();
            version.SameButForStatus(version with { Verified = VerificationLevel.Attestation }).ShouldBeFalse();
            version.SameButForStatus(version with { IsImmutable = true }).ShouldBeFalse();
            version.SameButForStatus(version with { Published = Time("2026-10-11T15:02:12Z") }).ShouldBeFalse();
            version.SameButForStatus(version with { Indexed = Time("2026-10-11T15:17:41Z") }).ShouldBeFalse();
            version.SameButForStatus(Version("1.0.0")).ShouldBeFalse();
            version.SameButForStatus(Version("1.0.0", "@thatplatypus/deflate@0.2")).ShouldBeFalse();
            version.SameButForStatus(Version("1.0.1", "@thatplatypus/deflate@0.1")).ShouldBeFalse();
        }
    }
}
