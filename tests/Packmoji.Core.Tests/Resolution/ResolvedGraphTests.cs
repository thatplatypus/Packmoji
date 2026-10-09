using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    public sealed class ResolvedGraphTests
    {
        // Grapevine as the fixtures have it, so that what a resolution writes can be held to Fixtures.Lockfile.
        private static Universe Grapevine()
        {
            var repository = Sample.Repository("github.com/thatplatypus/grapevine");
            return new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/deflate@0.1", "@thatplatypus/crypto@1.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/deflate", "0.1.0")
                .Change("@thatplatypus/grapevine", "0.3.0", published => published with
                {
                    Source = repository,
                    Sha256 = Sample.ShaOf("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
                })
                .Change("@thatplatypus/crypto", "1.0.0", published => published with
                {
                    Source = repository,
                    Sha256 = Sample.ShaOf("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"),
                    Verified = VerificationLevel.Attestation,
                })
                .Change("@thatplatypus/deflate", "0.1.0", published => published with
                {
                    Source = repository,
                    Sha256 = Sample.ShaOf("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"),
                    Verified = VerificationLevel.Attestation,
                });
        }

        [Fact]
        public async Task The_packages_of_a_resolution_are_in_order_of_their_full_names()
        {
            var universe = new Universe()
                .Publish("@zebra/a", "1.0.0")
                .Publish("@apple/z", "1.0.0", "@mango/m@1.0")
                .Publish("@mango/m", "1.0.0");

            var graph = (await universe.Resolve(Project.Asking("@zebra/a@1.0", "@apple/z@1.0"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@apple/z@1.0.0", "@mango/m@1.0.0", "@zebra/a@1.0.0"]);
        }

        [Fact]
        public async Task A_resolved_package_carries_what_the_source_said_of_it()
        {
            var universe = Grapevine();

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            var crypto = graph.Packages.Single(package => package.Published.Name.Name == "crypto");
            crypto.Published.ShouldBeSameAs(universe.Get("@thatplatypus/crypto", "1.0.0"));
            crypto.Published.Verified.ShouldBe(VerificationLevel.Attestation);
            crypto.Published.Status.ShouldBe(VersionStatus.Active);
        }

        [Fact]
        public async Task The_lockfile_of_a_resolution_is_the_one_the_reference_shows()
        {
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");

            var graph = (await Grapevine().Resolve(manifest)).ShouldSucceed();

            LockfileWriter.Write(graph.ToLockfile(manifest)).ShouldBe(Fixtures.Lockfile);
        }

        [Fact]
        public async Task The_lockfile_of_a_resolution_records_what_the_manifest_asked_for_in_its_two_tables()
        {
            var universe = new Universe().Publish("@thatplatypus/crypto", "1.0.0").Publish("@thatplatypus/testkit", "0.1.0");
            var manifest = Project.Named(Project.Name, ["@thatplatypus/crypto@1.0"], ["@thatplatypus/testkit@0.1"]);

            var lockfile = (await universe.Resolve(manifest)).ShouldSucceed().ToLockfile(manifest);

            lockfile.Root.Matches(RootRequirements.From(manifest)).ShouldBeTrue();
            lockfile.Root.Dependencies.ShouldBe([Sample.Asks("@thatplatypus/crypto@1.0")]);
            lockfile.Root.DevDependencies.ShouldBe([Sample.Asks("@thatplatypus/testkit@0.1")]);
            lockfile.Packages.Select(package => package.Name.Name).ShouldBe(["crypto", "testkit"]);
        }

        [Fact]
        public async Task The_lockfile_of_a_resolution_is_read_back_with_nothing_to_say_and_is_written_the_same_again()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/d@1.0")
                .Publish("@thatplatypus/a", "1.1.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1", "@thatplatypus/c@1.2.0-rc.1")
                .Publish("@thatplatypus/c", "1.0.0")
                .Publish("@thatplatypus/c", "1.2.0-rc.1")
                .Publish("@thatplatypus/d", "1.0.0");
            var manifest = Project.Named(Project.Name, ["@thatplatypus/a@1.0"], ["@thatplatypus/b@1.0"]);

            var written = LockfileWriter.Write((await universe.Resolve(manifest)).ShouldSucceed().ToLockfile(manifest));

            var read = LockfileReader.Read(written).ShouldSucceed();
            LockfileWriter.Write(read).ShouldBe(written);
            read.Packages.Select(package => $"{package.Name}@{package.Version}")
                .ShouldBe(["@thatplatypus/a@1.1.0", "@thatplatypus/b@1.0.0", "@thatplatypus/c@1.2.0-rc.1"]);
        }

        [Fact]
        public async Task A_dependency_that_a_version_names_twice_is_one_dependency_of_the_package()
        {
            // No manifest can say this, and a source is not held to what a manifest can say.
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/crypto@1.2")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.2.0");
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");

            var graph = (await universe.Resolve(manifest)).ShouldSucceed();

            graph.Pins("@thatplatypus/grapevine").ShouldBe(["@thatplatypus/crypto@1.2.0"]);
            LockfileReader.Read(LockfileWriter.Write(graph.ToLockfile(manifest))).ShouldSucceed();
        }
    }
}
