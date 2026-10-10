using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    /// <summary>
    /// A lockfile records what the manifest asked for when it was written. Whether the manifest of
    /// today asks for the same is what decides if versions are chosen again, and it is decided
    /// without the network.
    /// </summary>
    public sealed class RootRequirementsTests
    {
        private static Dependency Asks(string name, string requirement) => new(Sample.Name(name), Sample.Requirement(requirement));

        private static readonly Dependency Crypto = Asks("@thatplatypus/crypto", "1.0");
        private static readonly Dependency Deflate = Asks("@thatplatypus/deflate", "0.1");
        private static readonly Dependency Testkit = Asks("@thatplatypus/testkit", "0.1");

        [Fact]
        public void What_a_manifest_asks_for_is_its_two_tables()
        {
            var manifest = ManifestReader.Read(Fixtures.FullManifest).ShouldSucceed();

            var root = RootRequirements.From(manifest);

            root.Dependencies.ShouldBe([Crypto, Deflate]);
            root.DevDependencies.ShouldBe([Testkit]);
        }

        [Fact]
        public void A_table_the_manifest_does_not_have_is_empty()
        {
            var manifest = ManifestReader.Read(Fixtures.MinimalManifest).ShouldSucceed();

            var root = RootRequirements.From(manifest);

            root.Dependencies.ShouldBeEmpty();
            root.DevDependencies.ShouldBeEmpty();
            root.Matches(new RootRequirements([], [])).ShouldBeTrue();
        }

        [Fact]
        public void The_order_of_a_table_does_not_matter()
        {
            var one = new RootRequirements([Crypto, Deflate], [Testkit]);
            var other = new RootRequirements([Deflate, Crypto], [Testkit]);

            one.Matches(other).ShouldBeTrue();
            other.Matches(one).ShouldBeTrue();
        }

        [Fact]
        public void Nor_does_the_spelling_of_a_requirement()
        {
            var one = new RootRequirements([Asks("@thatplatypus/crypto", "1.0")], []);
            var other = new RootRequirements([Asks("@thatplatypus/crypto", "1.0.0")], []);

            one.Matches(other).ShouldBeTrue();
        }

        [Fact]
        public void Another_minimum_is_another_requirement()
        {
            var one = new RootRequirements([Asks("@thatplatypus/crypto", "1.0")], []);

            one.Matches(new RootRequirements([Asks("@thatplatypus/crypto", "1.1")], [])).ShouldBeFalse();
            one.Matches(new RootRequirements([Asks("@thatplatypus/crypto", "1.0.0-beta.1")], [])).ShouldBeFalse();
        }

        [Fact]
        public void Another_package_or_one_more_or_one_fewer_does_not_match()
        {
            var one = new RootRequirements([Crypto], []);

            one.Matches(new RootRequirements([Deflate], [])).ShouldBeFalse();
            one.Matches(new RootRequirements([Crypto, Deflate], [])).ShouldBeFalse();
            one.Matches(new RootRequirements([], [])).ShouldBeFalse();
            new RootRequirements([Crypto, Deflate], []).Matches(one).ShouldBeFalse();
        }

        [Fact]
        public void A_package_moved_from_one_table_to_the_other_does_not_match()
        {
            var one = new RootRequirements([Crypto, Testkit], []);
            var other = new RootRequirements([Crypto], [Testkit]);

            one.Matches(other).ShouldBeFalse();
            other.Matches(one).ShouldBeFalse();
        }

        [Fact]
        public void A_table_that_names_a_package_twice_matches_only_one_that_does_the_same()
        {
            var twice = new RootRequirements([Crypto, Crypto, Deflate], []);

            twice.Matches(new RootRequirements([Crypto, Deflate, Deflate], [])).ShouldBeFalse();
            twice.Matches(new RootRequirements([Deflate, Crypto, Crypto], [])).ShouldBeTrue();
        }

        [Fact]
        public void A_lockfile_matches_the_manifest_it_was_written_from_and_not_one_that_asks_for_more()
        {
            var locked = LockfileReader.Read(Fixtures.Lockfile).ShouldSucceed().Root;
            var grapevine = Asks("@thatplatypus/grapevine", "0.3.0");

            locked.Matches(new RootRequirements([grapevine], [])).ShouldBeTrue();
            locked.Matches(new RootRequirements([grapevine, Crypto], [])).ShouldBeFalse();
            locked.Matches(new RootRequirements([Asks("@thatplatypus/grapevine", "0.3.1")], [])).ShouldBeFalse();
        }
    }
}
