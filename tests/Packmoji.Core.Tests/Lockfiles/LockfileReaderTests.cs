using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    public sealed class LockfileReaderTests
    {
        private const string File = LockfileReader.FileName;
        private const string Sha = "\"9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08\"";

        // A lockfile around the entries given, with a root that asks for crypto unless told otherwise.
        private static string Lock(string packages, string root = "\"@thatplatypus/crypto@1.0\"", string version = "1", string more = "") => $$"""
            {
              "version": {{version}},
              "root": {
                "dependencies": [{{root}}],
                "devDependencies": []
              },
              "packages": [
            {{packages}}
              ]{{more}}
            }
            """;

        // One entry of "packages": crypto 1.0.0 unless told otherwise.
        private static string Entry(
            string name = "\"@thatplatypus/crypto\"",
            string version = "\"1.0.0\"",
            string source = "\"github.com/thatplatypus/grapevine\"",
            string releaseTag = "\"crypto-v1.0.0\"",
            string asset = "\"crypto-1.0.0.pmj.tar.gz\"",
            string sha256 = Sha,
            string verified = "\"attestation\"",
            string dependencies = "",
            string more = "") => $$"""
                {
                  "name": {{name}},
                  "version": {{version}},
                  "source": {{source}},
                  "releaseTag": {{releaseTag}},
                  "asset": {{asset}},
                  "sha256": {{sha256}},
                  "verified": {{verified}},
                  "dependencies": [{{dependencies}}]{{more}}
                }
            """;

        private static string Deflate(string name = "\"@thatplatypus/deflate\"") =>
            Entry(name: name, version: "\"0.1.0\"", releaseTag: "\"deflate-v0.1.0\"", asset: "\"deflate-0.1.0.pmj.tar.gz\"");

        private static Diagnostic ShouldFailAtMark(string fixture, string code)
        {
            var marked = Marked.From(fixture);
            return LockfileReader.Read(marked.Text).ShouldFailAt(marked, code, File);
        }

        [Fact]
        public void The_helpers_of_this_class_make_a_lockfile_that_reads() =>
            LockfileReader.Read(Lock(Entry())).ShouldSucceed().Packages.ShouldHaveSingleItem().Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));

        [Fact]
        public void A_lockfile_with_nothing_locked_reads()
        {
            var lockfile = LockfileReader.Read(Fixtures.EmptyLockfile).ShouldSucceed();

            lockfile.Root.Dependencies.ShouldBeEmpty();
            lockfile.Root.DevDependencies.ShouldBeEmpty();
            lockfile.Packages.ShouldBeEmpty();
        }

        [Fact]
        public void Every_key_of_a_lockfile_is_read()
        {
            var lockfile = LockfileReader.Read(Fixtures.Lockfile).ShouldSucceed();

            var requirement = lockfile.Root.Dependencies.ShouldHaveSingleItem();
            requirement.Name.ShouldBe(Sample.Name("@thatplatypus/grapevine"));
            requirement.Requirement.ShouldBe(Sample.Requirement("0.3"));
            requirement.Requirement.Text.ShouldBe("0.3");
            lockfile.Root.DevDependencies.ShouldBeEmpty();

            lockfile.Packages.Select(package => package.Name.ToString())
                .ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/grapevine"]);

            var grapevine = lockfile.Packages[2];
            grapevine.Version.ShouldBe(Sample.Version("0.3.0"));
            grapevine.Source.ShouldBe(Sample.Repository("github.com/thatplatypus/grapevine"));
            grapevine.Sha256.Hex.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            grapevine.Verified.ShouldBe(VerificationLevel.Checksum);
            grapevine.ReleaseTag.ShouldBe("grapevine-v0.3.0");
            grapevine.Asset.ShouldBe("grapevine-0.3.0.pmj.tar.gz");
            grapevine.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Version}")
                .ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0"]);

            lockfile.Packages[0].Verified.ShouldBe(VerificationLevel.Attestation);
        }

        [Fact]
        public void The_order_of_packages_and_of_keys_does_not_matter()
        {
            var lockfile = LockfileReader.Read("""
                {
                  "packages": [
                    {
                      "dependencies": [],
                      "verified": "checksum",
                      "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                      "asset": "crypto-1.0.0.pmj.tar.gz",
                      "releaseTag": "crypto-v1.0.0",
                      "source": "github.com/thatplatypus/grapevine",
                      "version": "1.0.0",
                      "name": "@thatplatypus/crypto"
                    }
                  ],
                  "root": { "devDependencies": ["@thatplatypus/crypto@1.0"], "dependencies": [] },
                  "version": 1
                }
                """).ShouldSucceed();

            lockfile.Root.DevDependencies.ShouldHaveSingleItem().Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));
            lockfile.Packages.ShouldHaveSingleItem().Verified.ShouldBe(VerificationLevel.Checksum);
        }

        [Fact]
        public void A_lockfile_with_carriage_returns_reads() =>
            LockfileReader.Read(Fixtures.Lockfile.ReplaceLineEndings("\r\n")).ShouldSucceed().Packages.Count.ShouldBe(3);

        [Theory]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("oops")]
        public void A_file_that_is_not_a_json_object_is_refused_and_not_thrown_on(string text)
        {
            var result = LockfileReader.Read(text);

            result.Succeeded.ShouldBeFalse();
            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBeOneOf(DiagnosticCodes.JsonSyntax, DiagnosticCodes.JsonWrongType);
            diagnostic.Fix.ShouldBe(LockfileReader.RegenerateFix);
        }

        [Fact]
        public void Conflict_markers_are_named_and_the_fix_is_to_write_the_lockfile_again()
        {
            var result = LockfileReader.Read("""
                {
                  "version": 1,
                <<<<<<< HEAD
                  "root": { "dependencies": ["@thatplatypus/crypto@1.0"], "devDependencies": [] },
                =======
                  "root": { "dependencies": ["@thatplatypus/crypto@1.1"], "devDependencies": [] },
                >>>>>>> other
                  "packages": []
                }
                """);

            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonSyntax);
            diagnostic.Reason.ShouldContain("merge conflict");
            diagnostic.Fix.ShouldBe(LockfileReader.RegenerateFix);
            diagnostic.Fix.ShouldContain("pmj install");
        }

        [Fact]
        public void A_lockfile_from_a_newer_pmj_is_refused_and_the_fix_is_to_upgrade()
        {
            var diagnostic = ShouldFailAtMark(Lock(Entry(), version: "§2", more: ",\n  \"somethingNew\": {}"), DiagnosticCodes.LockUnsupportedVersion);

            diagnostic.Message.ShouldContain("2");
            diagnostic.Fix.ShouldContain("upgrade");
        }

        [Theory]
        [InlineData("§0")]
        [InlineData("§1.0")]
        [InlineData("§-1")]
        public void Any_other_version_that_is_not_one_is_refused_and_the_fix_is_to_write_it_again(string version) =>
            ShouldFailAtMark(Lock(Entry(), version: version), DiagnosticCodes.LockUnsupportedVersion).Fix.ShouldBe(LockfileReader.RegenerateFix);

        [Fact]
        public void A_version_that_is_a_string_is_the_wrong_kind() =>
            ShouldFailAtMark(Lock(Entry(), version: "§\"1\""), DiagnosticCodes.JsonWrongType);

        [Fact]
        public void An_unknown_key_at_the_top_is_refused() =>
            ShouldFailAtMark(Lock(Entry(), more: ",\n  §\"generated\": \"today\""), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"version\", \"root\", \"packages\"");

        [Fact]
        public void An_unknown_key_of_a_package_is_refused() =>
            ShouldFailAtMark(Lock(Entry(more: ",\n      §\"yanked\": false")), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"name\", \"version\", \"source\", \"releaseTag\", \"asset\", \"sha256\", \"verified\", \"dependencies\"");

        [Fact]
        public void A_lockfile_without_packages_is_refused() =>
            ShouldFailAtMark("""§{ "version": 1, "root": { "dependencies": [], "devDependencies": [] } }""", DiagnosticCodes.KeyMissing)
                .Message.ShouldContain("\"packages\"");

        [Fact]
        public void A_root_without_one_of_its_two_lists_is_refused() =>
            ShouldFailAtMark("""{ "version": 1, "root": §{ "dependencies": [] }, "packages": [] }""", DiagnosticCodes.KeyMissing)
                .Message.ShouldContain("\"devDependencies\"");

        [Fact]
        public void An_entry_of_packages_that_is_not_an_object_is_refused() =>
            ShouldFailAtMark(Lock("    §\"@thatplatypus/crypto@1.0.0\"", root: ""), DiagnosticCodes.JsonWrongType)
                .Message.ShouldBe("An entry of \"packages\" must be an object.");

        [Fact]
        public void A_package_name_that_is_not_one_is_refused() =>
            ShouldFailAtMark(Lock(Entry(name: "§\"crypto\""), root: ""), DiagnosticCodes.NameInvalid);

        [Fact]
        public void A_version_that_is_not_one_is_refused() =>
            ShouldFailAtMark(Lock(Entry(version: "§\"1.0\""), root: ""), DiagnosticCodes.VersionInvalid);

        [Fact]
        public void A_source_that_is_not_a_repository_is_refused() =>
            ShouldFailAtMark(Lock(Entry(source: "§\"https://github.com/thatplatypus/grapevine\"")), DiagnosticCodes.RepositoryInvalid);

        [Fact]
        public void A_source_under_another_owner_is_refused() =>
            ShouldFailAtMark(Lock(Entry(source: "§\"github.com/someone/grapevine\"")), DiagnosticCodes.RepositoryOwnerMismatch);

        [Fact]
        public void A_release_tag_that_is_not_the_packages_own_is_refused() =>
            ShouldFailAtMark(Lock(Entry(releaseTag: "§\"v1.0.0\"")), DiagnosticCodes.LockTagMismatch).Reason.ShouldContain("\"crypto-v1.0.0\"");

        [Fact]
        public void An_asset_that_is_not_the_packages_own_is_refused() =>
            ShouldFailAtMark(Lock(Entry(asset: "§\"crypto.tar.gz\"")), DiagnosticCodes.LockAssetMismatch).Reason.ShouldContain("\"crypto-1.0.0.pmj.tar.gz\"");

        [Theory]
        [InlineData("§\"abc123\"")]
        [InlineData("§\"9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08\"")]
        public void A_digest_that_is_not_one_is_refused(string sha256) =>
            ShouldFailAtMark(Lock(Entry(sha256: sha256)), DiagnosticCodes.Sha256Invalid);

        [Fact]
        public void A_verification_level_that_is_not_one_is_refused() =>
            ShouldFailAtMark(Lock(Entry(verified: "§\"signed\"")), DiagnosticCodes.VerifiedInvalid).Reason.ShouldContain("attestation");

        [Fact]
        public void A_dependency_without_a_version_is_not_a_pin() =>
            ShouldFailAtMark(Lock(Entry(dependencies: "§\"@thatplatypus/deflate\"")), DiagnosticCodes.PinInvalid);

        [Fact]
        public void A_dependency_with_a_requirement_where_a_version_belongs_is_refused() =>
            ShouldFailAtMark(Lock(Entry(dependencies: "§\"@thatplatypus/deflate@0.1\"")), DiagnosticCodes.VersionInvalid);

        [Fact]
        public void A_root_entry_without_a_requirement_is_not_a_pin() =>
            ShouldFailAtMark(Lock(Entry(), root: "§\"@thatplatypus/crypto\""), DiagnosticCodes.PinInvalid);

        [Fact]
        public void A_root_entry_with_an_operator_is_refused() =>
            ShouldFailAtMark(Lock(Entry(), root: "§\"@thatplatypus/crypto@^1.0\""), DiagnosticCodes.RequirementInvalid);

        [Fact]
        public void A_root_entry_given_twice_is_refused() =>
            ShouldFailAtMark(Lock(Entry(), root: "\"@thatplatypus/crypto@1.0\", §\"@thatplatypus/crypto@1.0\""), DiagnosticCodes.ListDuplicate);

        [Fact]
        public void A_package_with_two_entries_is_refused_at_the_second() =>
            ShouldFailAtMark(Lock(Entry() + ",\n" + Entry(name: "§\"@thatplatypus/crypto\"")), DiagnosticCodes.LockDuplicatePackage);

        [Fact]
        public void Two_packages_with_one_bare_name_are_refused_at_the_second()
        {
            var diagnostic = ShouldFailAtMark(
                Lock(
                    Entry() + ",\n" + Entry(name: "§\"@someone/crypto\"", source: "\"github.com/someone/crypto\""),
                    root: "\"@thatplatypus/crypto@1.0\", \"@someone/crypto@1.0\""),
                DiagnosticCodes.LockNameCollision);

            diagnostic.Message.ShouldContain("\"@thatplatypus/crypto\"");
            diagnostic.Message.ShouldContain("\"@someone/crypto\"");
        }

        [Fact]
        public void A_dependency_on_a_package_the_lockfile_does_not_hold_is_dangling() =>
            ShouldFailAtMark(Lock(Entry(dependencies: "§\"@thatplatypus/deflate@0.1.0\"")), DiagnosticCodes.LockDanglingDependency)
                .Reason.ShouldContain("no entry");

        [Fact]
        public void A_dependency_on_another_version_than_the_one_held_is_dangling() =>
            ShouldFailAtMark(Lock(Entry(dependencies: "§\"@thatplatypus/deflate@0.1.5\"") + ",\n" + Deflate()), DiagnosticCodes.LockDanglingDependency)
                .Reason.ShouldContain("0.1.0");

        [Fact]
        public void A_package_that_depends_on_itself_is_dangling() =>
            ShouldFailAtMark(Lock(Entry(dependencies: "§\"@thatplatypus/crypto@1.0.0\"")), DiagnosticCodes.LockDanglingDependency)
                .Reason.ShouldContain("itself");

        [Fact]
        public void A_root_entry_that_nothing_in_packages_answers_is_unsatisfied() =>
            ShouldFailAtMark(Lock(Entry(), root: "\"@thatplatypus/crypto@1.0\", §\"@thatplatypus/testkit@0.1\""), DiagnosticCodes.LockRootUnsatisfied);

        [Theory]
        [InlineData("§\"@thatplatypus/crypto@1.1\"")]
        [InlineData("§\"@thatplatypus/crypto@2.0\"")]
        [InlineData("§\"@thatplatypus/crypto@0.9\"")]
        public void A_root_entry_the_held_version_does_not_satisfy_is_unsatisfied(string root) =>
            ShouldFailAtMark(Lock(Entry(), root: root), DiagnosticCodes.LockRootUnsatisfied).Reason.ShouldContain("1.0.0");

        [Fact]
        public void A_package_nothing_leads_to_is_unreachable() =>
            ShouldFailAtMark(Lock(Entry() + ",\n" + Deflate("§\"@thatplatypus/deflate\"")), DiagnosticCodes.LockUnreachable);

        [Fact]
        public void A_package_reached_only_through_another_is_reachable() =>
            LockfileReader.Read(Lock(Entry(dependencies: "\"@thatplatypus/deflate@0.1.0\"") + ",\n" + Deflate())).ShouldSucceed().Packages.Count.ShouldBe(2);

        [Fact]
        public void Every_problem_of_the_first_pass_is_reported_together_and_each_says_to_write_the_file_again()
        {
            var result = LockfileReader.Read(Lock(Entry(version: "\"1.0\"", sha256: "\"abc\"", verified: "\"signed\"")));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.Code)
                .ShouldBe([DiagnosticCodes.VersionInvalid, DiagnosticCodes.Sha256Invalid, DiagnosticCodes.VerifiedInvalid]);
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostic.ShouldBeComplete().Fix.ShouldBe(LockfileReader.RegenerateFix);
                diagnostic.Location.ShouldNotBeNull().File.ShouldBe(File);
            }
        }
    }
}
