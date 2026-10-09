using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class ManifestReaderTests
    {
        private const string File = ManifestReader.FileName;

        // A valid manifest in which any required value can be swapped, with room for more keys in
        // "package" and for more sections. Each of those two begins with its own comma.
        private static string Manifest(
            string name = "\"@thatplatypus/crypto\"",
            string version = "\"1.0.0\"",
            string kind = "\"library\"",
            string emojicode = "\">=1.0.0-beta.2\"",
            string more = "",
            string sections = "") => $$"""
            {
              "package": {
                "name": {{name}},
                "version": {{version}},
                "kind": {{kind}},
                "emojicode": {{emojicode}}{{more}}
              }{{sections}}
            }
            """;

        private static Diagnostic ShouldFailAtMark(string fixture, string code)
        {
            var marked = Marked.From(fixture);
            return ManifestReader.Read(marked.Text).ShouldFailAt(marked, code, File);
        }

        [Fact]
        public void The_smallest_manifest_is_read_and_what_it_leaves_out_stays_out()
        {
            var manifest = ManifestReader.Read(Fixtures.MinimalManifest).ShouldSucceed();

            manifest.Package.Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));
            manifest.Package.Version.ShouldBe(Sample.Version("1.0.0"));
            manifest.Package.Kind.ShouldBe(PackageKind.Library);
            manifest.Package.Emojicode.ShouldBe(Sample.Compiler(">=1.0.0-beta.2"));
            manifest.Package.Description.ShouldBeNull();
            manifest.Package.License.ShouldBeNull();
            manifest.Package.Repository.ShouldBeNull();
            manifest.Dependencies.ShouldBeNull();
            manifest.DevDependencies.ShouldBeNull();
            manifest.Build.ShouldBeNull();
            manifest.Native.ShouldBeNull();
            manifest.Policy.ShouldBeNull();
        }

        [Fact]
        public void What_a_manifest_leaves_out_has_its_default_where_it_is_used()
        {
            var manifest = ManifestReader.Read(Fixtures.MinimalManifest).ShouldSucceed();

            manifest.Repository.ShouldBe(Sample.Repository("github.com/thatplatypus/crypto"));
            manifest.Sources.Select(pattern => pattern.Value).ShouldBe(["src/**/*.emojic", "src/**/*.🍇"]);
            manifest.RequireAttestation.ShouldBeFalse();
        }

        [Fact]
        public void Every_key_of_a_full_manifest_is_read()
        {
            var manifest = ManifestReader.Read(Fixtures.FullManifest).ShouldSucceed();

            manifest.Package.Name.ShouldBe(Sample.Name("@thatplatypus/grapevine"));
            manifest.Package.Version.ShouldBe(Sample.Version("0.3.0"));
            manifest.Package.Description.ShouldBe("HTTP framework for Emojicode");
            manifest.Package.License.ShouldBe(Sample.License("MIT"));
            manifest.Package.Repository.ShouldBe(Sample.Repository("github.com/thatplatypus/grapevine"));
            manifest.Repository.ShouldBe(Sample.Repository("github.com/thatplatypus/grapevine"));

            manifest.Dependencies.ShouldNotBeNull().Select(dependency => $"{dependency.Name}@{dependency.Requirement}")
                .ShouldBe(["@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1"]);
            manifest.DevDependencies.ShouldNotBeNull().Select(dependency => $"{dependency.Name}@{dependency.Requirement}")
                .ShouldBe(["@thatplatypus/testkit@0.1"]);

            manifest.Build.ShouldNotBeNull().Entry.ShouldBe(Sample.Path("src/lib.🍇"));
            manifest.Build.Sources.ShouldNotBeNull().Select(pattern => pattern.Value).ShouldBe(["src/**/*.emojic", "src/**/*.🍇"]);

            manifest.Native.ShouldNotBeNull().Sources.ShouldNotBeNull().Select(pattern => pattern.Value).ShouldBe(["native/*.cpp"]);
            manifest.Native.IncludeDirs.ShouldNotBeNull().Select(path => path.Value).ShouldBe(["native/include"]);
            manifest.Native.Link.ShouldBe(["pthread"]);

            manifest.Policy.ShouldNotBeNull().RequireAttestation.ShouldBe(false);
        }

        [Fact]
        public void A_package_laid_out_flat_as_grapevine_is_reads()
        {
            var manifest = ManifestReader.Read(Fixtures.FlatManifest).ShouldSucceed();

            manifest.Build.ShouldNotBeNull().Entry.ShouldBe(Sample.Path("grapevine.🍇"));
            manifest.Sources.Select(pattern => pattern.Value).ShouldBe(["*.🍇"]);
            manifest.Native.ShouldNotBeNull().IncludeDirs.ShouldBeNull();
        }

        [Fact]
        public void An_app_is_a_kind_of_package()
        {
            ManifestReader.Read(Manifest(kind: "\"app\"")).ShouldSucceed().Package.Kind.ShouldBe(PackageKind.App);
        }

        [Fact]
        public void A_section_that_is_present_and_empty_is_empty_and_not_absent()
        {
            var manifest = ManifestReader.Read(Manifest(sections: """
                ,
                  "dependencies": {},
                  "devDependencies": {},
                  "build": {},
                  "native": { "sources": [], "includeDirs": [], "link": [] },
                  "policy": {}
                """)).ShouldSucceed();

            manifest.Dependencies.ShouldNotBeNull().ShouldBeEmpty();
            manifest.DevDependencies.ShouldNotBeNull().ShouldBeEmpty();
            manifest.Build.ShouldNotBeNull().Entry.ShouldBeNull();
            manifest.Build.Sources.ShouldBeNull();
            manifest.Native.ShouldNotBeNull().Sources.ShouldNotBeNull().ShouldBeEmpty();
            manifest.Native.IncludeDirs.ShouldNotBeNull().ShouldBeEmpty();
            manifest.Native.Link.ShouldNotBeNull().ShouldBeEmpty();
            manifest.Policy.ShouldNotBeNull().RequireAttestation.ShouldBeNull();
        }

        [Fact]
        public void The_order_of_keys_does_not_matter()
        {
            var manifest = ManifestReader.Read("""
                {
                  "policy": { "requireAttestation": true },
                  "package": {
                    "emojicode": ">=1.0.0-beta.2",
                    "kind": "app",
                    "version": "2.0.0",
                    "name": "@thatplatypus/todo"
                  }
                }
                """).ShouldSucceed();

            manifest.Package.Name.ShouldBe(Sample.Name("@thatplatypus/todo"));
            manifest.RequireAttestation.ShouldBeTrue();
        }

        [Fact]
        public void A_manifest_with_carriage_returns_reads_and_keeps_its_line_numbers()
        {
            ManifestReader.Read(Fixtures.FullManifest.ReplaceLineEndings("\r\n")).ShouldSucceed();

            var marked = Marked.From(Manifest(sections: ",\n  §\"extras\": {}"));
            ManifestReader.Read(marked.Text.ReplaceLineEndings("\r\n")).ShouldFailAt(marked, DiagnosticCodes.KeyUnknown, File);
        }

        [Fact]
        public void A_manifest_given_as_bytes_with_a_byte_order_mark_reads()
        {
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Fixtures.FullManifest)];

            ManifestReader.Read(bytes).ShouldSucceed().Build.ShouldNotBeNull().Entry.ShouldBe(Sample.Path("src/lib.🍇"));
        }

        [Fact]
        public void A_diagnostic_names_the_file_it_was_told()
        {
            var result = ManifestReader.Read("nope", "packages/crypto/packmoji.json");

            result.Diagnostics.ShouldHaveSingleItem().Location.ShouldNotBeNull().File.ShouldBe("packages/crypto/packmoji.json");
        }

        [Fact]
        public void A_file_that_is_not_json_is_one_syntax_error() =>
            ShouldFailAtMark("""{ "package": §oops }""", DiagnosticCodes.JsonSyntax);

        [Theory]
        [InlineData("")]
        [InlineData("\n")]
        public void An_empty_file_is_a_syntax_error_and_not_an_exception(string text)
        {
            var result = ManifestReader.Read(text);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.JsonSyntax);
        }

        [Fact]
        public void Conflict_markers_in_a_manifest_are_to_be_resolved_by_hand()
        {
            var result = ManifestReader.Read("""
                {
                <<<<<<< HEAD
                  "package": {}
                =======
                  "package": {}
                >>>>>>> other
                }
                """);

            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonSyntax);
            diagnostic.Reason.ShouldContain("merge conflict");
            diagnostic.Fix.ShouldContain("by hand");
        }

        [Theory]
        [InlineData("§[]", "an array")]
        [InlineData("§\"packmoji\"", "a string")]
        [InlineData("§null", "null")]
        [InlineData("§7", "a number")]
        public void A_root_that_is_not_an_object_is_refused(string fixture, string found) =>
            ShouldFailAtMark(fixture, DiagnosticCodes.JsonWrongType).Reason.ShouldContain(found);

        [Fact]
        public void A_key_given_twice_is_refused() =>
            ShouldFailAtMark(Manifest(more: ",\n    §\"name\": \"@thatplatypus/other\""), DiagnosticCodes.JsonDuplicateKey);

        [Fact]
        public void A_number_where_a_string_belongs_names_both_kinds()
        {
            var diagnostic = ShouldFailAtMark(Manifest(version: "§1"), DiagnosticCodes.JsonWrongType);

            diagnostic.Message.ShouldBe("\"version\" must be a string.");
            diagnostic.Reason.ShouldContain("a number");
        }

        [Fact]
        public void Null_for_an_optional_key_says_the_key_can_be_removed() =>
            ShouldFailAtMark(Manifest(more: ", \"description\": §null"), DiagnosticCodes.JsonWrongType).Fix.ShouldStartWith("remove it");

        [Fact]
        public void A_section_of_the_wrong_kind_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"dependencies\": §[]"), DiagnosticCodes.JsonWrongType);

        [Fact]
        public void A_requirement_that_is_not_a_string_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"dependencies\": { \"@thatplatypus/deflate\": §1 }"), DiagnosticCodes.JsonWrongType)
                .Message.ShouldBe("The requirement of \"@thatplatypus/deflate\" must be a string.");

        [Fact]
        public void A_policy_flag_that_is_not_true_or_false_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"policy\": { \"requireAttestation\": §\"yes\" }"), DiagnosticCodes.JsonWrongType);

        [Fact]
        public void An_unknown_section_is_refused_and_the_known_ones_are_listed() =>
            ShouldFailAtMark(Manifest(sections: ",\n  §\"extras\": {}"), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"package\", \"dependencies\", \"devDependencies\", \"build\", \"native\", \"policy\"");

        [Fact]
        public void An_unknown_key_of_package_is_refused() =>
            ShouldFailAtMark(Manifest(more: ",\n    §\"author\": \"someone\""), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"name\", \"version\", \"kind\", \"emojicode\", \"description\", \"license\", \"repository\"");

        [Fact]
        public void An_unknown_key_of_build_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { §\"output\": \"x\" }"), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"entry\", \"sources\"");

        [Fact]
        public void An_unknown_key_of_native_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"native\": { §\"include-dirs\": [] }"), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"sources\", \"includeDirs\", \"link\"");

        [Fact]
        public void An_unknown_key_of_policy_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"policy\": { §\"require-attestation\": true }"), DiagnosticCodes.KeyUnknown)
                .Reason.ShouldContain("\"requireAttestation\"");

        [Fact]
        public void A_manifest_without_package_is_refused_at_its_root()
        {
            var diagnostic = ShouldFailAtMark("""§{ "dependencies": {} }""", DiagnosticCodes.KeyMissing);

            diagnostic.Message.ShouldContain("\"package\"");
            diagnostic.Fix.ShouldContain("\"name\"");
        }

        [Fact]
        public void A_package_without_a_required_key_is_refused_at_package()
        {
            var diagnostic = ShouldFailAtMark(
                """{ "package": §{ "name": "@thatplatypus/crypto", "version": "1.0.0", "kind": "library" } }""",
                DiagnosticCodes.KeyMissing);

            diagnostic.Message.ShouldContain("\"emojicode\"");
            diagnostic.Fix.ShouldContain("\">=1.0.0-beta.2\"");
        }

        [Fact]
        public void A_name_in_capitals_is_refused() =>
            ShouldFailAtMark(Manifest(name: "§\"@Thatplatypus/Crypto\""), DiagnosticCodes.NameInvalid).Fix.ShouldContain("\"@thatplatypus/crypto\"");

        [Fact]
        public void A_dependency_with_a_hyphen_in_its_name_is_refused_at_its_key() =>
            ShouldFailAtMark(Manifest(sections: ", \"dependencies\": { §\"@thatplatypus/emoji-crypto\": \"1.0\" }"), DiagnosticCodes.NameInvalid);

        [Fact]
        public void A_reserved_name_is_refused() =>
            ShouldFailAtMark(Manifest(name: "§\"@thatplatypus/json\""), DiagnosticCodes.NameReserved);

        [Fact]
        public void A_version_of_two_numbers_is_refused() =>
            ShouldFailAtMark(Manifest(version: "§\"1.0\""), DiagnosticCodes.VersionInvalid);

        [Fact]
        public void A_version_with_build_metadata_is_refused() =>
            ShouldFailAtMark(Manifest(version: "§\"1.0.0+build\""), DiagnosticCodes.VersionBuildMetadata);

        [Fact]
        public void A_requirement_with_an_operator_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"dependencies\": { \"@thatplatypus/deflate\": §\"^0.1\" }"), DiagnosticCodes.RequirementInvalid);

        [Fact]
        public void A_compiler_requirement_without_its_operator_is_refused() =>
            ShouldFailAtMark(Manifest(emojicode: "§\"1.0.0\""), DiagnosticCodes.CompilerInvalid);

        [Fact]
        public void A_kind_that_is_neither_library_nor_app_is_refused() =>
            ShouldFailAtMark(Manifest(kind: "§\"plugin\""), DiagnosticCodes.KindInvalid).Fix.ShouldContain("\"library\"");

        [Fact]
        public void A_description_of_two_lines_is_refused() =>
            ShouldFailAtMark(Manifest(more: ", \"description\": §\"two\\nlines\""), DiagnosticCodes.DescriptionInvalid);

        [Fact]
        public void A_license_that_is_not_an_expression_is_refused() =>
            ShouldFailAtMark(Manifest(more: ", \"license\": §\"see LICENSE\""), DiagnosticCodes.LicenseInvalid);

        [Fact]
        public void A_repository_with_a_scheme_is_refused() =>
            ShouldFailAtMark(Manifest(more: ", \"repository\": §\"https://github.com/thatplatypus/grapevine\""), DiagnosticCodes.RepositoryInvalid);

        [Fact]
        public void A_repository_under_another_owner_is_refused()
        {
            var diagnostic = ShouldFailAtMark(Manifest(more: ", \"repository\": §\"github.com/someone/grapevine\""), DiagnosticCodes.RepositoryOwnerMismatch);

            diagnostic.Reason.ShouldContain("\"thatplatypus\"");
        }

        [Fact]
        public void A_package_that_depends_on_itself_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"dependencies\": { §\"@thatplatypus/crypto\": \"1.0\" }"), DiagnosticCodes.DependencySelf);

        [Fact]
        public void A_package_in_both_tables_is_refused_where_it_is_given_second() =>
            ShouldFailAtMark(
                Manifest(sections: ", \"dependencies\": { \"@thatplatypus/deflate\": \"0.1\" }, \"devDependencies\": { §\"@thatplatypus/deflate\": \"0.1\" }"),
                DiagnosticCodes.DependencyDuplicate);

        [Fact]
        public void Two_dependencies_with_one_bare_name_are_refused()
        {
            var diagnostic = ShouldFailAtMark(
                Manifest(sections: ", \"dependencies\": { \"@thatplatypus/deflate\": \"0.1\", §\"@someone/deflate\": \"2.0\" }"),
                DiagnosticCodes.DependencyNameCollision);

            diagnostic.Message.ShouldContain("\"@thatplatypus/deflate\"");
            diagnostic.Message.ShouldContain("\"@someone/deflate\"");
        }

        [Fact]
        public void A_dependency_with_the_packages_own_bare_name_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"devDependencies\": { §\"@someone/crypto\": \"2.0\" }"), DiagnosticCodes.DependencyNameCollision);

        [Fact]
        public void An_entry_that_leaves_the_package_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"entry\": §\"../shared/bytes.🍇\" }"), DiagnosticCodes.PathInvalid);

        [Fact]
        public void An_entry_that_is_not_a_source_file_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"entry\": §\"src/lib.txt\" }"), DiagnosticCodes.EntrySuffix);

        [Fact]
        public void An_include_directory_with_a_backslash_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"native\": { \"includeDirs\": [§\"native\\\\include\"] }"), DiagnosticCodes.PathInvalid)
                .Fix.ShouldContain("\"native/include\"");

        [Fact]
        public void A_pattern_with_a_character_class_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"sources\": [§\"src/[ab].🍇\"] }"), DiagnosticCodes.GlobInvalid);

        [Fact]
        public void A_native_pattern_that_leaves_the_package_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"native\": { \"sources\": [§\"../native/*.cpp\"] }"), DiagnosticCodes.GlobInvalid);

        [Fact]
        public void A_linker_flag_in_link_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"native\": { \"link\": [\"pthread\", §\"-Wl,--whole-archive\"] }"), DiagnosticCodes.LinkInvalid);

        [Fact]
        public void Sources_that_are_present_and_empty_are_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"sources\": §[] }"), DiagnosticCodes.ListEmpty);

        [Fact]
        public void A_pattern_given_twice_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"sources\": [\"*.🍇\", §\"*.🍇\"] }"), DiagnosticCodes.ListDuplicate);

        [Fact]
        public void An_entry_of_sources_that_is_not_a_string_is_refused() =>
            ShouldFailAtMark(Manifest(sections: ", \"build\": { \"sources\": [§7] }"), DiagnosticCodes.JsonWrongType)
                .Message.ShouldBe("An entry of \"sources\" must be a string.");

        [Fact]
        public void Every_problem_is_reported_in_one_read()
        {
            var result = ManifestReader.Read(Manifest(
                name: "\"@Thatplatypus/Crypto\"",
                version: "\"1.0\"",
                more: ", \"license\": \"see LICENSE\"",
                sections: ", \"dependencies\": { \"@thatplatypus/deflate\": \"^0.1\" }, \"extras\": {}"));

            result.Succeeded.ShouldBeFalse();
            result.Value.ShouldBeNull();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
            [
                DiagnosticCodes.NameInvalid,
                DiagnosticCodes.VersionInvalid,
                DiagnosticCodes.LicenseInvalid,
                DiagnosticCodes.RequirementInvalid,
                DiagnosticCodes.KeyUnknown,
            ]);
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostic.ShouldBeComplete().Location.ShouldNotBeNull().File.ShouldBe(File);
            }
        }
    }
}
