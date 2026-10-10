using CsCheck;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// When a manifest has not changed, nothing is resolved. What was locked may still have been
    /// yanked or quarantined since, or may no longer be what is published, and this is the check for
    /// that: one question to the source for each locked package, and no choosing of versions.
    /// </summary>
    public sealed class LockCheckTests
    {
        private static readonly Manifest Manifest = Project.Asking("@thatplatypus/grapevine@0.3");

        private static Universe Grapevine() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1")
            .Publish("@thatplatypus/crypto", "1.0.0")
            .Publish("@thatplatypus/deflate", "0.1.0");

        private static async Task<Lockfile> Locked() => (await Grapevine().Resolve(Manifest)).ShouldSucceed().ToLockfile(Manifest);

        private static Task<ResolveResult> Check(Lockfile lockfile, IPackageSource source) =>
            LockCheck.CheckAsync(lockfile, source, TestContext.Current.CancellationToken);

        [Fact]
        public async Task What_is_locked_and_published_as_it_was_says_nothing_and_the_graph_is_the_lockfile_again()
        {
            var locked = await Locked();
            var universe = Grapevine();

            var result = await Check(locked, universe);

            result.Diagnostics.ShouldBeEmpty();
            LockfileWriter.Write(result.ShouldSucceed().ToLockfile(Manifest)).ShouldBe(LockfileWriter.Write(locked));
            universe.Asked.ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
        }

        [Fact]
        public async Task A_locked_version_that_has_been_yanked_is_a_warning_and_goes_on_being_used()
        {
            var locked = await Locked();

            var result = await Check(locked, Grapevine().Yank("@thatplatypus/crypto", "1.0.0"));

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            var warning = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            warning.Code.ShouldBe(DiagnosticCodes.ResolveYankedLocked);
            warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
            warning.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" has been yanked.");
        }

        [Fact]
        public async Task A_locked_version_that_is_quarantined_is_an_error_though_it_was_locked()
        {
            var locked = await Locked();

            var diagnostic = (await Check(locked, Grapevine().Quarantine("@thatplatypus/crypto", "1.0.0"))).ShouldFailWith(DiagnosticCodes.ResolveQuarantined);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" is quarantined.");
            diagnostic.Reason.ShouldContain("must not be used");
            diagnostic.Reason.ShouldEndWith("packmoji.lock holds it");
        }

        [Fact]
        public async Task A_locked_version_that_is_gone_is_an_error_and_the_lockfile_is_to_be_kept()
        {
            // What is replaced is first taken down. A lockfile deleted while it is down leaves no digest
            // to catch what comes back, so the one thing this must not say is to delete the lockfile.
            var locked = await Locked();
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1")
                .Publish("@thatplatypus/deflate", "0.1.0");

            var diagnostic = (await Check(locked, universe)).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" is no longer published.");
            diagnostic.Reason.ShouldStartWith("packmoji.lock holds it");
            diagnostic.Fix.ShouldStartWith("keep packmoji.lock");
            diagnostic.Fix.ShouldNotContain("delete");
            diagnostic.Fix.ShouldContain("a later version of \"@thatplatypus/crypto\"");
        }

        [Fact]
        public async Task A_locked_digest_that_is_not_the_published_one_is_an_error()
        {
            var locked = await Locked();
            var universe = Grapevine().Change("@thatplatypus/crypto", "1.0.0", published => published with { Sha256 = Sample.Sha('b') });

            var diagnostic = (await Check(locked, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Message.ShouldBe("packmoji.lock does not agree with what is published for \"@thatplatypus/crypto\" 1.0.0.");
            diagnostic.Reason.ShouldContain($"what is published has {Sample.Sha('b')}");
        }

        [Fact]
        public async Task A_locked_repository_that_is_not_the_published_one_is_an_error()
        {
            var locked = await Locked();
            var universe = Grapevine().Change(
                "@thatplatypus/crypto", "1.0.0", published => published with { Source = Sample.Repository("github.com/thatplatypus/elsewhere") });

            (await Check(locked, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch).Reason.ShouldContain("github.com/thatplatypus/elsewhere");
        }

        [Fact]
        public async Task How_far_a_version_is_verified_now_is_not_compared_and_the_graph_keeps_what_was_locked()
        {
            var locked = await Locked();
            var universe = Grapevine().Change("@thatplatypus/crypto", "1.0.0", published => published with { Verified = VerificationLevel.Attestation });

            var result = await Check(locked, universe);

            result.Diagnostics.ShouldBeEmpty();
            var packages = result.ShouldSucceed().Packages;
            packages.Count.ShouldBe(3);
            packages.ShouldAllBe(package => package.Published.Verified == VerificationLevel.Checksum);
        }

        [Fact]
        public async Task Errors_come_before_warnings_and_each_kind_is_in_the_order_of_the_lockfile()
        {
            var locked = await Locked();
            var universe = Grapevine()
                .Yank("@thatplatypus/crypto", "1.0.0")
                .Change("@thatplatypus/deflate", "0.1.0", published => published with { Sha256 = Sample.Sha('b') })
                .Quarantine("@thatplatypus/grapevine", "0.3.0");

            var result = await Check(locked, universe);

            result.Succeeded.ShouldBeFalse();
            result.Graph.ShouldBeNull();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Code).ShouldBe(
            [
                DiagnosticCodes.LockMismatch,
                DiagnosticCodes.ResolveQuarantined,
                DiagnosticCodes.ResolveYankedLocked,
            ]);
        }

        [Fact]
        public async Task A_version_that_is_quarantined_and_also_changed_is_reported_for_both()
        {
            var locked = await Locked();
            var universe = Grapevine()
                .Quarantine("@thatplatypus/crypto", "1.0.0")
                .Change("@thatplatypus/crypto", "1.0.0", published => published with { Sha256 = Sample.Sha('b') });

            var result = await Check(locked, universe);

            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([DiagnosticCodes.ResolveQuarantined, DiagnosticCodes.LockMismatch]);
        }

        [Fact]
        public async Task A_lockfile_of_no_packages_checks_to_a_graph_of_none_and_asks_nothing()
        {
            var universe = Grapevine();

            var result = await Check(new Lockfile(new RootRequirements([], []), []), universe);

            result.ShouldSucceed().Packages.ShouldBeEmpty();
            universe.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task More_than_a_hundred_problems_are_counted_and_the_first_hundred_listed()
        {
            var packages = Enumerable.Range(100, 150)
                .Select(number => new LockedPackage(
                    Sample.Name($"@thatplatypus/p{number}"),
                    Sample.Version("1.0.0"),
                    Sample.Repository($"github.com/thatplatypus/p{number}"),
                    Sample.Sha('a'),
                    VerificationLevel.Checksum,
                    []))
                .ToList();

            var result = await Check(new Lockfile(new RootRequirements([], []), packages), new Universe());

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Count.ShouldBe(100);
            result.OmittedDiagnostics.ShouldBe(50);
            result.Diagnostics[0].Message.ShouldContain("\"@thatplatypus/p100\"");
        }

        [Fact]
        public async Task An_answer_for_another_version_than_was_asked_is_a_fault_in_the_source_and_is_thrown()
        {
            var locked = await Locked();
            var source = new AnsweringSource((_, _) => Sample.Published("@thatplatypus/crypto", "9.9.9"));

            var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Check(locked, source));

            thrown.Message.ShouldContain("@thatplatypus/crypto@1.0.0");
            thrown.Message.ShouldContain("@thatplatypus/crypto@9.9.9");
        }

        [Fact]
        public async Task A_check_that_was_cancelled_asks_nothing_more()
        {
            var locked = await Locked();
            var universe = Grapevine();
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => LockCheck.CheckAsync(locked, universe, cancelled.Token));

            universe.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_lockfile_and_a_source_must_be_given()
        {
            await Should.ThrowAsync<ArgumentNullException>(() => Check(null!, new Universe()));
            await Should.ThrowAsync<ArgumentNullException>(() => Check(new Lockfile(new RootRequirements([], []), []), null!));
        }

        [Fact]
        public async Task Whatever_the_graph_a_lockfile_that_was_just_written_checks_clean_and_comes_back_the_same()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var checkedCount = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var manifest = project.Manifest();
                    var resolved = await Resolver.ResolveAsync(manifest, null, project.Universe(), cancellation);
                    if (resolved.Graph is not { Packages.Count: > 0 } graph)
                    {
                        return;
                    }

                    Interlocked.Increment(ref checkedCount);
                    var locked = graph.ToLockfile(manifest);

                    var result = await LockCheck.CheckAsync(locked, project.Universe(), cancellation);

                    result.Diagnostics.ShouldBeEmpty();
                    LockfileWriter.Write(result.ShouldSucceed().ToLockfile(manifest)).ShouldBe(LockfileWriter.Write(locked));
                },
                iter: 500,
                print: project => project.Describe());

            checkedCount.ShouldBeGreaterThan(100);
        }
    }
}
