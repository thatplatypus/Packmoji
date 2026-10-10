using System.Security.Cryptography;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Archives;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj pack</c>: one file for a release, the same bytes from the same files wherever and whenever it is packed.</summary>
    public sealed class PackTests
    {
        private const string Library =
            """
            {
              "package": { "name": "@thatplatypus/crypto", "version": "1.2.0", "kind": "library", "emojicode": ">=1.0.0-beta.2", "repository": "github.com/thatplatypus/grapevine" },
              "build": { "entry": "crypto.🍇", "sources": ["*.🍇", "inner/**/*.🍇"] },
              "native": { "sources": ["native/*.cpp"], "includeDirs": ["native/include"] }
            }
            """;

        // Grapevine's own layout: sources beside the manifest, and native code in a directory of its own.
        private static void WriteLibrary(Sandbox sandbox, string directory)
        {
            sandbox.Write(directory + "native/include/deep/shim.h", "// h\n");
            sandbox.Write(directory + "inner/more/sha.🍇", "💭 sha\n");
            sandbox.Write(directory + "native/shim.cpp", "// cpp\n");
            sandbox.Write(directory + "crypto.🍇", "💭 crypto\n");
            sandbox.Write(directory + "packmoji.json", Library);
            sandbox.Write(directory + "LICENSE", "MIT\n");
        }

        private static IReadOnlyList<string> Packed(Sandbox sandbox, string archive)
        {
            var read = PackageArchive.Read(File.ReadAllBytes(sandbox.PathOf(archive)));
            read.Diagnostics.Select(diagnostic => diagnostic.Reason).ShouldBeEmpty();
            return read.Value!.Select(file => file.Path.Value).ToList();
        }

        [Fact]
        public async Task A_new_library_packs_as_it_stands_and_pack_says_what_it_made_and_how_to_release_it()
        {
            using var sandbox = new Sandbox();
            await sandbox.RunAsync("new", "@thatplatypus/greeter", "--lib");

            var run = await sandbox.RunInAsync("greeter", "pack");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            Packed(sandbox, "greeter/target/greeter-0.1.0.pmj.tar.gz").ShouldBe(["README.md", "packmoji.json", "src/lib.🍇"]);
            var bytes = File.ReadAllBytes(sandbox.PathOf("greeter/target/greeter-0.1.0.pmj.tar.gz"));
            run.Output.ShouldBe(
                $"""
                Packed @thatplatypus/greeter 0.1.0: 3 files, {bytes.Length} bytes.
                  target/greeter-0.1.0.pmj.tar.gz
                  sha256 {Convert.ToHexStringLower(SHA256.HashData(bytes))}
                To publish it, release that file under the tag greeter-v0.1.0 in github.com/thatplatypus/greeter.

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task The_same_files_give_the_same_bytes_packed_twice_and_packed_somewhere_else()
        {
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "one/");
            (await sandbox.RunInAsync("one", "pack")).Status.ShouldBe(0);
            var first = File.ReadAllBytes(sandbox.PathOf("one/target/crypto-1.2.0.pmj.tar.gz"));

            // Again where target/ now holds the first archive, and again in another directory whose
            // files were written in another order and at another time.
            (await sandbox.RunInAsync("one", "pack")).Status.ShouldBe(0);
            sandbox.Write("two/packmoji.json", Library);
            sandbox.Write("two/LICENSE", "MIT\n");
            sandbox.Write("two/crypto.🍇", "💭 crypto\n");
            sandbox.Write("two/native/shim.cpp", "// cpp\n");
            sandbox.Write("two/inner/more/sha.🍇", "💭 sha\n");
            sandbox.Write("two/native/include/deep/shim.h", "// h\n");
            File.SetLastWriteTimeUtc(sandbox.PathOf("two/crypto.🍇"), new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            (await sandbox.RunInAsync("two", "pack")).Status.ShouldBe(0);

            File.ReadAllBytes(sandbox.PathOf("one/target/crypto-1.2.0.pmj.tar.gz")).ShouldBe(first);
            File.ReadAllBytes(sandbox.PathOf("two/target/crypto-1.2.0.pmj.tar.gz")).ShouldBe(first);
            sandbox.Files("one/target").ShouldBe(["crypto-1.2.0.pmj.tar.gz"]);
        }

        [Fact]
        public async Task Only_the_manifest_a_readme_a_license_and_what_the_patterns_name_are_packed()
        {
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "");
            sandbox.Write("README.md", "# crypto\n");
            sandbox.Write("notes/todo.txt", "later\n");
            sandbox.Write("tests/check.🍇", "💭 a test\n");
            sandbox.Write("inner/.hidden.🍇", "💭 hidden\n");
            sandbox.Write(".env", "SECRET=1\n");
            sandbox.Write("target/old.🍇", "💭 built\n");
            sandbox.Write("packages/dep/dep.🍇", "💭 someone else's\n");
            sandbox.Write(".git/hooks/pre-push.🍇", "💭 git's\n");
            sandbox.Write("native/notes.md", "notes\n");

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(0);
            Packed(sandbox, "target/crypto-1.2.0.pmj.tar.gz").ShouldBe(
                ["LICENSE", "README.md", "crypto.🍇", "inner/more/sha.🍇", "native/include/deep/shim.h", "native/shim.cpp", "packmoji.json"]);
            run.Output.ShouldContain("in github.com/thatplatypus/grapevine.");
        }

        [Fact]
        public async Task What_pmj_and_git_keep_in_the_directory_is_never_packed_whatever_the_patterns_say()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Library.Replace("\"*.🍇\", \"inner/**/*.🍇\"", "\"**/*.🍇\", \".git/**\""));
            sandbox.Write("crypto.🍇", "💭 crypto\n");
            sandbox.Write("tests/check.🍇", "💭 a test\n");
            sandbox.Write("target/old.🍇", "💭 built\n");
            sandbox.Write("packages/dep/dep.🍇", "💭 someone else's\n");
            sandbox.Write(".git/hooks/pre-push.🍇", "💭 git's\n");
            sandbox.Write("inner/target/kept.🍇", "💭 only the top of the tree is pmj's\n");

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(0);
            Packed(sandbox, "target/crypto-1.2.0.pmj.tar.gz").ShouldBe(["crypto.🍇", "inner/target/kept.🍇", "packmoji.json", "tests/check.🍇"]);
        }

        [Fact]
        public async Task A_symbolic_link_among_the_sources_is_refused_and_nothing_is_written()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "Making a symbolic link there needs a right that a test does not have.");
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "");
            File.CreateSymbolicLink(sandbox.PathOf("linked.🍇"), sandbox.PathOf("crypto.🍇"));

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[pack.unportable]: \"linked.🍇\" cannot be packed.");
            run.Error.ShouldContain("it is a symbolic link");
            Directory.Exists(sandbox.PathOf("target")).ShouldBeFalse();
        }

        [Fact]
        public async Task A_package_whose_entry_file_is_not_there_is_told_that_and_not_that_a_pattern_is_missing()
        {
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "");
            File.Delete(sandbox.PathOf("crypto.🍇"));

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[entry.not-found]: The entry file \"crypto.🍇\" was not found.");
            run.Error.ShouldNotContain("pattern");
            Directory.Exists(sandbox.PathOf("target")).ShouldBeFalse();
        }

        [Fact]
        public async Task An_entry_file_that_is_there_and_that_no_pattern_selects_is_refused_with_the_pattern_as_the_fix()
        {
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "");
            sandbox.Write("packmoji.json", Library.Replace("\"*.🍇\", ", ""));

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[pack.nothing]: The entry file \"crypto.🍇\" is not among the files that are packed.");
            run.Error.ShouldContain("fix: add a pattern that selects it");
            Directory.Exists(sandbox.PathOf("target")).ShouldBeFalse();
        }

        [Fact]
        public async Task An_entry_file_under_what_is_never_packed_is_not_found_whatever_pattern_names_it()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Library.Replace("\"entry\": \"crypto.🍇\"", "\"entry\": \"target/made.🍇\"").Replace("\"*.🍇\", ", "\"**/*.🍇\", "));
            sandbox.Write("target/made.🍇", "💭 made\n");
            sandbox.Write("crypto.🍇", "💭 crypto\n");

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[entry.not-found]: The entry file \"target/made.🍇\" was not found.");
            sandbox.Files("target").ShouldBe(["made.🍇"]);
        }

        [Fact]
        public async Task A_package_too_large_for_one_archive_is_refused_and_nothing_is_left_behind()
        {
            using var sandbox = new Sandbox();
            WriteLibrary(sandbox, "");
            File.WriteAllBytes(sandbox.PathOf("big.🍇"), new byte[PackageArchive.MaxBytes]);

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[archive.invalid]: The package is too large to pack.");
            run.Error.ShouldContain($"{PackageArchive.MaxBytes} bytes");
            sandbox.Files("target").ShouldBeEmpty();
        }

        [Fact]
        public async Task Where_there_is_no_project_pack_says_so()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[project.not-found]: There is no packmoji.json here.");
            run.Error.ShouldContain("pmj new or pmj init");
        }

        [Fact]
        public async Task A_manifest_that_does_not_read_is_reported_with_the_place_of_each_problem()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", "{\n  \"package\": { \"name\": \"crypto\", \"version\": \"1\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }\n}\n");

            var run = await sandbox.RunAsync("pack");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("packmoji.json:2:24: error[name.invalid]: ");
            run.Error.ShouldContain("packmoji.json:2:45: error[version.invalid]: ");
        }
    }
}
