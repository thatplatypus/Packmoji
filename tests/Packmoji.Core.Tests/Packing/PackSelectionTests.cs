using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Packing;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Packing
{
    /// <summary>
    /// What goes into a package's archive is what its manifest says and nothing else, so that a
    /// directory can hold tests, notes and build output without any of them being published.
    /// </summary>
    public sealed class PackSelectionTests
    {
        private static readonly Manifest Conventional = ManifestReader.Read("""
            {
              "package": { "name": "@thatplatypus/crypto", "version": "1.0.0", "kind": "library", "emojicode": ">=1.0.0-beta.2" }
            }
            """).ShouldSucceed();

        private static readonly Manifest Flat = ManifestReader.Read(Fixtures.FlatManifest).ShouldSucceed();

        private static TreeEntry[] Tree(params string[] paths) => paths.Select(path => new TreeEntry(path, 10, IsLink: false)).ToArray();

        private static string[] Selected(Manifest manifest, params string[] paths) =>
            PackSelection.Select(manifest, Tree(paths)).ShouldSucceed().Select(path => path.Value).ToArray();

        private static Diagnostic Refused(Manifest manifest, TreeEntry[] tree, string code)
        {
            var result = PackSelection.Select(manifest, tree);

            result.Succeeded.ShouldBeFalse();
            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(code);
            return diagnostic;
        }

        [Fact]
        public void A_package_that_says_nothing_packs_its_manifest_and_the_sources_under_src()
        {
            Selected(Conventional, "packmoji.json", "src/lib.🍇", "src/inner/hash.🍇", "src/old.emojic", "src/notes.txt", "tests/main.🍇", "build.sh")
                .ShouldBe(["packmoji.json", "src/inner/hash.🍇", "src/lib.🍇", "src/old.emojic"]);
        }

        [Fact]
        public void A_readme_and_a_license_at_the_root_go_in_whatever_follows_the_word()
        {
            Selected(Conventional, "packmoji.json", "src/lib.🍇", "README.md", "README", "LICENSE", "LICENSE-MIT.txt", "docs/README.md", "readme.md", "CHANGELOG.md")
                .ShouldBe(["LICENSE", "LICENSE-MIT.txt", "README", "README.md", "packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public void A_directory_whose_name_begins_with_either_word_is_not_a_readme_or_a_license()
        {
            Selected(Conventional, "packmoji.json", "src/lib.🍇", "LICENSES/MIT.txt", "README-images/logo.png", "LICENSE")
                .ShouldBe(["LICENSE", "packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public void A_package_laid_out_flat_as_grapevine_is_packs_what_its_patterns_name()
        {
            Selected(
                Flat,
                "packmoji.json", "grapevine.🍇", "app.🍇", "headers.🍇", "README.md", "build.sh",
                "native/net.cpp", "native/net.h", "native/old.c", "tests/unit/main.🍇", "tests/host/main.🍇")
                .ShouldBe(["README.md", "app.🍇", "grapevine.🍇", "headers.🍇", "native/net.cpp", "packmoji.json"]);
        }

        [Fact]
        public void Everything_under_a_directory_of_headers_goes_in()
        {
            var manifest = ManifestReader.Read(Fixtures.FullManifest).ShouldSucceed();

            Selected(manifest, "packmoji.json", "src/lib.🍇", "native/shim.cpp", "native/include/net.h", "native/include/deep/more.hpp", "native/include/.DS_Store", "native/private.h")
                .ShouldBe(["native/include/deep/more.hpp", "native/include/net.h", "native/shim.cpp", "packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public void A_file_that_begins_with_a_dot_stays_out_unless_a_pattern_writes_the_dot()
        {
            Selected(Conventional, "packmoji.json", "src/lib.🍇", "src/.hidden.🍇", "src/.cache/x.🍇", ".gitignore", ".DS_Store")
                .ShouldBe(["packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public void The_files_are_in_the_order_the_archive_holds_them()
        {
            var fullwidth = "src/" + char.ConvertFromUtf32(0xFF21) + ".🍇";

            Selected(Conventional, "src/🍇.🍇", fullwidth, "src/lib.🍇", "packmoji.json")
                .ShouldBe(["packmoji.json", "src/lib.🍇", fullwidth, "src/🍇.🍇"]);
        }

        [Fact]
        public void What_is_selected_can_be_written_as_an_archive()
        {
            var selected = PackSelection.Select(Flat, Tree("packmoji.json", "grapevine.🍇", "native/net.cpp", "README.md")).ShouldSucceed();

            PackageArchive.Check(selected.Select(path => new ArchiveFile(path, new byte[10])).ToList()).ShouldBeEmpty();
        }

        [Fact]
        public void A_file_that_nothing_selects_may_have_any_name()
        {
            Selected(Conventional, "packmoji.json", "src/lib.🍇", "notes/-odd name .txt", "con", "tests/a:b", "Makefile", "MAKEFILE")
                .ShouldBe(["packmoji.json", "src/lib.🍇"]);
        }

        [Fact]
        public void The_entry_file_has_to_be_among_what_is_packed()
        {
            var manifest = ManifestReader.Read("""
                {
                  "package": { "name": "@thatplatypus/crypto", "version": "1.0.0", "kind": "library", "emojicode": ">=1.0.0-beta.2" },
                  "build": { "entry": "crypto.🍇" }
                }
                """).ShouldSucceed();

            var diagnostic = Refused(manifest, Tree("packmoji.json", "crypto.🍇", "src/helper.🍇"), DiagnosticCodes.PackNothing);

            diagnostic.Message.ShouldContain("\"crypto.🍇\"");
            diagnostic.Fix.ShouldContain("\"sources\"");
        }

        [Fact]
        public void A_package_with_no_entry_file_is_told_so_as_the_manifest_rules_tell_it()
        {
            Refused(Conventional, Tree("packmoji.json", "src/helper.🍇"), DiagnosticCodes.EntryNotFound);
            Refused(Conventional, Tree("packmoji.json", "src/lib.🍇", "src/lib.emojic"), DiagnosticCodes.EntryAmbiguous);
        }

        [Fact]
        public void A_selected_file_that_is_a_link_is_refused()
        {
            TreeEntry[] tree = [.. Tree("packmoji.json", "src/lib.🍇"), new TreeEntry("src/shared.🍇", 10, IsLink: true)];

            var diagnostic = Refused(Conventional, tree, DiagnosticCodes.PackUnportable);

            diagnostic.Message.ShouldContain("\"src/shared.🍇\"");
            diagnostic.Reason.ShouldContain("link");
        }

        [Theory]
        [InlineData("src/trailing /x.🍇", "space")]
        [InlineData("src/-flag.🍇", "option")]
        [InlineData("src/a:b.🍇", "colon")]
        public void A_selected_file_whose_name_a_manifest_could_not_write_is_refused(string path, string why)
        {
            var diagnostic = Refused(Conventional, Tree("packmoji.json", "src/lib.🍇", path), DiagnosticCodes.PackUnportable);

            diagnostic.Message.ShouldContain($"\"{path}\"");
            diagnostic.Reason.ShouldContain(why);
        }

        [Fact]
        public void Two_selected_files_that_one_disk_could_not_hold_are_refused()
        {
            Refused(Conventional, Tree("packmoji.json", "src/lib.🍇", "src/Hash.🍇", "src/hash.🍇"), DiagnosticCodes.PackUnportable).Reason.ShouldContain("but for case");
            Refused(Conventional, Tree("packmoji.json", "src/lib.🍇", "src/aux.🍇"), DiagnosticCodes.PackUnportable).Reason.ShouldContain("Windows");
        }

        [Fact]
        public void Every_file_that_cannot_be_packed_is_named_and_not_only_the_first()
        {
            var result = PackSelection.Select(Conventional, [.. Tree("packmoji.json", "src/lib.🍇", "src/nul.🍇", "src/-x.🍇"), new TreeEntry("src/l.🍇", 1, IsLink: true)]);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Count.ShouldBe(3);
            result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Code == DiagnosticCodes.PackUnportable);
        }

        [Fact]
        public void More_files_or_more_bytes_than_an_archive_may_hold_are_refused()
        {
            var many = Enumerable.Range(0, PackageArchive.MaxFiles).Select(number => new TreeEntry($"src/f{number}.🍇", 1, false)).ToArray();
            TreeEntry[] large = [new("packmoji.json", 100, false), new("src/lib.🍇", PackageArchive.MaxUnpackedBytes, false)];

            Refused(Conventional, [.. Tree("packmoji.json", "src/lib.🍇"), .. many], DiagnosticCodes.ArchiveInvalid).Reason.ShouldContain($"{PackageArchive.MaxFiles} files");
            Refused(Conventional, large, DiagnosticCodes.ArchiveInvalid).Reason.ShouldContain($"{PackageArchive.MaxUnpackedBytes} bytes");
        }
    }
}
