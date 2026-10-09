using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class EntryConventionTests
    {
        private static Manifest Manifest(string kind, string build = "") => ManifestReader.Read($$"""
            {
              "package": {
                "name": "@thatplatypus/crypto",
                "version": "1.0.0",
                "kind": "{{kind}}",
                "emojicode": ">=1.0.0-beta.2"
              }{{build}}
            }
            """).ShouldSucceed();

        private static Func<string, bool> Files(params string[] present) => path => present.Contains(path, StringComparer.Ordinal);

        [Theory]
        [InlineData("library", "src/lib.emojic")]
        [InlineData("library", "src/lib.🍇")]
        [InlineData("app", "src/main.emojic")]
        [InlineData("app", "src/main.🍇")]
        public void The_one_conventional_file_that_exists_is_the_entry(string kind, string file)
        {
            EntryConvention.TryResolve(Manifest(kind), Files(file, "src/other.🍇"), out var entry, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            entry.ShouldBe(Sample.Path(file));
        }

        [Fact]
        public void An_entry_the_manifest_names_is_the_entry_and_nothing_is_looked_for()
        {
            var manifest = Manifest("library", ", \"build\": { \"entry\": \"grapevine.🍇\" }");

            EntryConvention.TryResolve(manifest, _ => throw new InvalidOperationException("no file should be looked for"), out var entry, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            entry.ShouldBe(Sample.Path("grapevine.🍇"));
        }

        [Theory]
        [InlineData("library", "src/lib.emojic", "src/lib.🍇")]
        [InlineData("app", "src/main.emojic", "src/main.🍇")]
        public void With_neither_file_there_is_no_entry_and_both_are_named(string kind, string first, string second)
        {
            EntryConvention.TryResolve(Manifest(kind), Files(), out var entry, out var error).ShouldBeFalse();

            entry.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.EntryNotFound);
            error!.Reason.ShouldContain($"\"{first}\"");
            error.Reason.ShouldContain($"\"{second}\"");
            error.Fix.ShouldContain("\"entry\"");
        }

        [Theory]
        [InlineData("library", "src/lib.emojic", "src/lib.🍇")]
        [InlineData("app", "src/main.emojic", "src/main.🍇")]
        public void With_both_files_the_entry_is_ambiguous(string kind, string first, string second)
        {
            EntryConvention.TryResolve(Manifest(kind), Files(first, second), out var entry, out var error).ShouldBeFalse();

            entry.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.EntryAmbiguous);
            error!.Reason.ShouldContain($"\"{first}\"");
            error.Reason.ShouldContain($"\"{second}\"");
            error.Fix.ShouldContain("\"entry\"");
        }

        [Fact]
        public void An_app_does_not_take_a_librarys_file_nor_a_library_an_apps()
        {
            EntryConvention.TryResolve(Manifest("app"), Files("src/lib.🍇"), out _, out var appError).ShouldBeFalse();
            EntryConvention.TryResolve(Manifest("library"), Files("src/main.🍇"), out _, out var libraryError).ShouldBeFalse();

            appError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.EntryNotFound);
            libraryError.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.EntryNotFound);
        }

        [Fact]
        public void The_candidates_are_the_plain_suffix_first_and_the_grapes_second()
        {
            EntryConvention.Candidates(PackageKind.Library).ShouldBe(["src/lib.emojic", "src/lib.🍇"]);
            EntryConvention.Candidates(PackageKind.App).ShouldBe(["src/main.emojic", "src/main.🍇"]);
        }
    }
}
