using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class ManifestWriterTests
    {
        private static PackageSection Package(PackageKind kind = PackageKind.Library) => new(
            Sample.Name("@thatplatypus/crypto"),
            Sample.Version("1.0.0"),
            kind,
            Sample.Compiler(">=1.0.0-beta.2"));

        private static Dependency Needs(string name, string requirement) => new(Sample.Name(name), Sample.Requirement(requirement));

        [Fact]
        public void The_smallest_manifest_is_written_in_its_canonical_form() =>
            ManifestWriter.Write(new Manifest(Package())).ShouldBe(Fixtures.MinimalManifest);

        [Fact]
        public void Reading_a_canonical_manifest_and_writing_it_gives_the_same_bytes()
        {
            foreach (var fixture in new[] { Fixtures.MinimalManifest, Fixtures.FullManifest, Fixtures.FlatManifest })
            {
                ManifestWriter.Write(ManifestReader.Read(fixture).ShouldSucceed()).ShouldBe(fixture);
            }
        }

        [Fact]
        public void What_is_written_reads_back_and_writes_the_same_again()
        {
            var manifest = new Manifest(
                Package(PackageKind.App) with { Description = "Says \"hello\" \\ and 🍇", License = Sample.License("MIT OR Apache-2.0") },
                Dependencies: [Needs("@thatplatypus/grapevine", "0.3")],
                Build: new BuildSection(Sample.Path("src/main.🍇"), [Sample.Glob("src/**/*.🍇")]),
                Policy: new PolicySection(true));

            var written = ManifestWriter.Write(manifest);
            var read = ManifestReader.Read(written).ShouldSucceed();

            read.Package.Kind.ShouldBe(PackageKind.App);
            read.Package.Description.ShouldBe("Says \"hello\" \\ and 🍇");
            read.RequireAttestation.ShouldBeTrue();
            ManifestWriter.Write(read).ShouldBe(written);
        }

        [Fact]
        public void Dependencies_are_written_sorted_whatever_order_the_model_has()
        {
            var manifest = new Manifest(
                Package(),
                Dependencies: [Needs("@thatplatypus/zebra", "1.0"), Needs("@a-team/deflate", "0.1"), Needs("@thatplatypus/apple", "2.1.3")]);

            ManifestWriter.Write(manifest).ShouldContain("""
                  "dependencies": {
                    "@a-team/deflate": "0.1",
                    "@thatplatypus/apple": "2.1.3",
                    "@thatplatypus/zebra": "1.0"
                  }
                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_requirement_is_written_as_its_author_spelled_it()
        {
            var manifest = new Manifest(Package(), Dependencies: [Needs("@thatplatypus/deflate", "1.2.0")]);

            ManifestWriter.Write(manifest).ShouldContain("\"@thatplatypus/deflate\": \"1.2.0\"");
        }

        [Fact]
        public void A_key_the_model_does_not_have_is_not_written()
        {
            var written = ManifestWriter.Write(new Manifest(Package(), Build: new BuildSection(Sample.Path("src/lib.🍇"))));

            written.ShouldContain("\"entry\": \"src/lib.🍇\"");
            written.ShouldNotContain("sources");
            written.ShouldNotContain("description");
            written.ShouldNotContain("dependencies");
            written.ShouldNotContain("native");
            written.ShouldNotContain("policy");
        }

        [Fact]
        public void A_section_that_is_present_and_empty_is_written_empty()
        {
            var written = ManifestWriter.Write(new Manifest(
                Package(),
                Dependencies: [],
                DevDependencies: [],
                Build: new BuildSection(),
                Native: new NativeSection(Sources: [], IncludeDirs: [], Link: []),
                Policy: new PolicySection()));

            written.ShouldBe("""
                {
                  "package": {
                    "name": "@thatplatypus/crypto",
                    "version": "1.0.0",
                    "kind": "library",
                    "emojicode": ">=1.0.0-beta.2"
                  },
                  "dependencies": {},
                  "devDependencies": {},
                  "build": {},
                  "native": {
                    "sources": [],
                    "includeDirs": [],
                    "link": []
                  },
                  "policy": {}
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void An_emoji_and_the_compiler_requirement_are_written_as_themselves()
        {
            var written = ManifestWriter.Write(ManifestReader.Read(Fixtures.FullManifest).ShouldSucceed());

            written.ShouldContain("src/lib.🍇");
            written.ShouldContain(">=1.0.0-beta.2");
            written.ShouldNotContain("\\u");
            written.ShouldNotContain("\r");
            written.ShouldEndWith("}\n");
        }
    }
}
