using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// A published version never changes. So a lockfile that records one digest for a version, where
    /// the source records another, is evidence that something was replaced, and a version said to be
    /// in a repository that its scope does not own is not that package at all.
    /// </summary>
    public sealed class ResolverLockfileTests
    {
        private static Universe Grapevine() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
            .Publish("@thatplatypus/crypto", "1.0.0")
            .Publish("@thatplatypus/crypto", "1.1.0");

        private static readonly Manifest Manifest = Project.Asking("@thatplatypus/grapevine@0.3");

        private static async Task<Lockfile> Locked() => (await Grapevine().Resolve(Manifest)).ShouldSucceed().ToLockfile(Manifest);

        [Fact]
        public async Task A_lockfile_that_agrees_with_what_is_published_says_nothing()
        {
            var result = await Grapevine().Resolve(Manifest, await Locked());

            result.ShouldSucceed();
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_locked_digest_that_is_not_the_published_one_stops_the_resolution()
        {
            var universe = Grapevine().Change("@thatplatypus/crypto", "1.0.0", published => published with { Sha256 = Sample.Sha('b') });

            var diagnostic = (await universe.Resolve(Manifest, await Locked())).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Message.ShouldBe("packmoji.lock does not agree with what is published for \"@thatplatypus/crypto\" 1.0.0.");
            diagnostic.Reason.ShouldContain($"the lockfile has the digest {Sample.DigestOf("@thatplatypus/crypto@1.0.0")}");
            diagnostic.Reason.ShouldContain($"what is published has {Sample.Sha('b')}");
            diagnostic.Fix.ShouldContain("must not be used");
            diagnostic.Fix.ShouldContain("restore it");
        }

        [Fact]
        public async Task A_locked_repository_that_is_not_the_published_one_stops_it_as_well()
        {
            var universe = Grapevine().Change(
                "@thatplatypus/crypto", "1.0.0", published => published with { Source = Sample.Repository("github.com/thatplatypus/elsewhere") });

            var diagnostic = (await universe.Resolve(Manifest, await Locked())).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Reason.ShouldContain("the lockfile has the repository github.com/thatplatypus/crypto");
            diagnostic.Reason.ShouldContain("what is published is in github.com/thatplatypus/elsewhere");
            diagnostic.Reason.ShouldNotContain("digest");
        }

        [Fact]
        public async Task A_lockfile_that_holds_the_package_at_another_version_is_not_compared()
        {
            var locked = await Locked();
            var universe = Grapevine().Change("@thatplatypus/crypto", "1.0.0", published => published with { Sha256 = Sample.Sha('b') });

            var result = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1"), locked);

            result.ShouldSucceed();
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task How_far_a_version_was_verified_may_differ_from_what_was_locked()
        {
            var universe = Grapevine().Change("@thatplatypus/crypto", "1.0.0", published => published with { Verified = VerificationLevel.Attestation });

            var result = await universe.Resolve(Manifest, await Locked());

            result.Diagnostics.ShouldBeEmpty();
            result.ShouldSucceed().Packages.Single(package => package.Published.Name.Name == "crypto").Published.Verified.ShouldBe(VerificationLevel.Attestation);
        }

        [Fact]
        public async Task A_selected_version_in_a_repository_its_scope_does_not_own_stops_the_resolution()
        {
            var universe = Grapevine().Change(
                "@thatplatypus/crypto", "1.0.0", published => published with { Source = Sample.Repository("github.com/someone-else/crypto") });

            var diagnostic = (await universe.Resolve(Manifest)).ShouldFailWith(DiagnosticCodes.RepositoryOwnerMismatch);

            diagnostic.Message.ShouldBe("The repository \"github.com/someone-else/crypto\" does not belong to \"@thatplatypus/crypto\".");
            diagnostic.Reason.ShouldContain("owned by its scope, \"thatplatypus\"");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
        }

        [Fact]
        public async Task A_version_that_is_not_selected_may_be_anywhere()
        {
            var universe = Grapevine().Change(
                "@thatplatypus/crypto", "1.0.0", published => published with { Source = Sample.Repository("github.com/someone-else/crypto") });

            (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1"))).ShouldSucceed();
        }

        [Fact]
        public async Task What_stops_a_resolution_is_listed_in_one_order_whatever_was_found_first()
        {
            // By the order of the design: never published, yanked, quarantined, two lines, and then
            // the repository and the lockfile. Within one of those, by name.
            var manifest = Project.Asking(
                "@thatplatypus/a@1.0", "@thatplatypus/b@1.0", "@thatplatypus/c@1.0", "@thatplatypus/d@1.0", "@thatplatypus/e@1.0",
                "@thatplatypus/f@1.0", "@thatplatypus/g@1.0", "@thatplatypus/h@1.0");
            Universe Published() => new Universe()
                .Publish("@thatplatypus/a", "1.0.0")
                .Publish("@thatplatypus/b", "1.0.0")
                .Publish("@thatplatypus/c", "1.0.0", "@thatplatypus/h@2.0")
                .Publish("@thatplatypus/d", "1.0.0")
                .Publish("@thatplatypus/e", "1.0.0")
                .Publish("@thatplatypus/f", "1.0.0")
                .Publish("@thatplatypus/h", "1.0.0")
                .Publish("@thatplatypus/h", "2.0.0");
            var locked = new Lockfile(
                RootRequirements.From(manifest),
                [new LockedPackage(Sample.Name("@thatplatypus/a"), Sample.Version("1.0.0"), Sample.Repository("github.com/thatplatypus/a"), Sample.Sha('c'), VerificationLevel.Checksum, [])]);
            var universe = Published()
                .Change("@thatplatypus/b", "1.0.0", published => published with { Source = Sample.Repository("github.com/someone-else/b") })
                .Quarantine("@thatplatypus/d", "1.0.0")
                .Yank("@thatplatypus/e", "1.0.0")
                .Yank("@thatplatypus/f", "1.0.0");

            var result = await universe.Resolve(manifest, locked);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Code).ShouldBe(
            [
                DiagnosticCodes.ResolveVersionMissing,
                DiagnosticCodes.ResolveYanked,
                DiagnosticCodes.ResolveYanked,
                DiagnosticCodes.ResolveQuarantined,
                DiagnosticCodes.ResolveLineConflict,
                DiagnosticCodes.RepositoryOwnerMismatch,
                DiagnosticCodes.LockMismatch,
            ]);
            result.Diagnostics[0].Message.ShouldContain("\"@thatplatypus/g\"");
            result.Diagnostics[1].Message.ShouldContain("\"@thatplatypus/e\"");
            result.Diagnostics[2].Message.ShouldContain("\"@thatplatypus/f\"");
        }
    }
}
