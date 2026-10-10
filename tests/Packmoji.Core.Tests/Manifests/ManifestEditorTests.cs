using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    /// <summary>
    /// <c>pmj add</c>, <c>remove</c> and <c>update</c> change what a manifest asks for and nothing
    /// else about it, and what they write is what the writer writes: so the same change gives the
    /// same file whoever makes it.
    /// </summary>
    public sealed class ManifestEditorTests
    {
        private static readonly Manifest Minimal = ManifestReader.Read(Fixtures.MinimalManifest).ShouldSucceed();
        private static readonly Manifest Full = ManifestReader.Read(Fixtures.FullManifest).ShouldSucceed();

        private static string[] Asked(IReadOnlyList<Dependency>? table) => (table ?? []).Select(dependency => $"{dependency.Name}@{dependency.Requirement}").ToArray();

        [Fact]
        public void A_dependency_is_added_to_a_manifest_that_had_none()
        {
            var edited = ManifestEditor.With(Minimal, Sample.Asks("@thatplatypus/deflate@0.1"), dev: false);

            Asked(edited.Dependencies).ShouldBe(["@thatplatypus/deflate@0.1"]);
            edited.DevDependencies.ShouldBeNull();
            edited.Package.ShouldBe(Minimal.Package);
        }

        [Fact]
        public void A_dependency_for_development_goes_in_the_other_table()
        {
            var edited = ManifestEditor.With(Minimal, Sample.Asks("@thatplatypus/testkit@0.1"), dev: true);

            edited.Dependencies.ShouldBeNull();
            Asked(edited.DevDependencies).ShouldBe(["@thatplatypus/testkit@0.1"]);
        }

        [Fact]
        public void A_dependency_is_added_beside_the_ones_that_are_there_and_nothing_else_changes()
        {
            var edited = ManifestEditor.With(Full, Sample.Asks("@thatplatypus/bytes@2.1.3"), dev: false);

            Asked(edited.Dependencies).ShouldBe(["@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1", "@thatplatypus/bytes@2.1.3"]);
            Asked(edited.DevDependencies).ShouldBe(["@thatplatypus/testkit@0.1"]);
            edited.Build.ShouldBe(Full.Build);
            edited.Native.ShouldBe(Full.Native);
            edited.Policy.ShouldBe(Full.Policy);
        }

        [Fact]
        public void A_dependency_that_is_there_has_its_requirement_changed_in_place()
        {
            var edited = ManifestEditor.With(Full, Sample.Asks("@thatplatypus/crypto@1.4"), dev: false);

            Asked(edited.Dependencies).ShouldBe(["@thatplatypus/crypto@1.4", "@thatplatypus/deflate@0.1"]);
        }

        [Fact]
        public void A_dependency_that_is_in_one_table_and_is_asked_for_in_the_other_moves()
        {
            var edited = ManifestEditor.With(Full, Sample.Asks("@thatplatypus/crypto@1.0"), dev: true);

            Asked(edited.Dependencies).ShouldBe(["@thatplatypus/deflate@0.1"]);
            Asked(edited.DevDependencies).ShouldBe(["@thatplatypus/testkit@0.1", "@thatplatypus/crypto@1.0"]);
        }

        [Fact]
        public void Where_a_dependency_is_can_be_asked()
        {
            ManifestEditor.Find(Full, Sample.Name("@thatplatypus/crypto"), out var cryptoIsDev).ShouldBe(Sample.Asks("@thatplatypus/crypto@1.0"));
            ManifestEditor.Find(Full, Sample.Name("@thatplatypus/testkit"), out var testkitIsDev).ShouldBe(Sample.Asks("@thatplatypus/testkit@0.1"));
            ManifestEditor.Find(Full, Sample.Name("@thatplatypus/absent"), out _).ShouldBeNull();

            cryptoIsDev.ShouldBeFalse();
            testkitIsDev.ShouldBeTrue();
        }

        [Fact]
        public void A_dependency_is_removed_from_whichever_table_has_it()
        {
            Asked(ManifestEditor.Without(Full, Sample.Name("@thatplatypus/crypto"))!.Dependencies).ShouldBe(["@thatplatypus/deflate@0.1"]);
            ManifestEditor.Without(Full, Sample.Name("@thatplatypus/testkit"))!.DevDependencies.ShouldBeNull();
        }

        [Fact]
        public void Removing_the_last_dependency_of_a_table_removes_the_table()
        {
            var one = ManifestEditor.With(Minimal, Sample.Asks("@thatplatypus/deflate@0.1"), dev: false);

            var none = ManifestEditor.Without(one, Sample.Name("@thatplatypus/deflate")).ShouldNotBeNull();

            ManifestWriter.Write(none).ShouldBe(Fixtures.MinimalManifest);
        }

        [Fact]
        public void Removing_a_dependency_that_is_not_there_is_nothing()
        {
            ManifestEditor.Without(Full, Sample.Name("@thatplatypus/absent")).ShouldBeNull();
        }

        [Fact]
        public void What_is_written_after_a_change_is_read_back_as_the_change()
        {
            var edited = ManifestEditor.With(Full, Sample.Asks("@thatplatypus/bytes@2.1.3"), dev: false);

            var read = ManifestReader.Read(ManifestWriter.Write(edited)).ShouldSucceed();

            Asked(read.Dependencies).ShouldBe(["@thatplatypus/bytes@2.1.3", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1"]);
        }
    }
}
