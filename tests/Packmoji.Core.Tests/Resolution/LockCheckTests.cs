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
            diagnostic.Fix.ShouldStartWith("ask for a later version of \"@thatplatypus/crypto\" on the line 1.x in packmoji.json");
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
            diagnostic.Fix.ShouldStartWith("find out which before going on");
            diagnostic.Fix.ShouldContain("must not be used");
            diagnostic.Fix.ShouldNotContain("delete");
        }

        [Fact]
        public async Task A_locked_repository_that_is_not_the_published_one_is_an_error()
        {
            var locked = await Locked();
            var universe = Grapevine().Change(
                "@thatplatypus/crypto", "1.0.0", published => published with { Source = Sample.Repository("github.com/thatplatypus/elsewhere") });

            (await Check(locked, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch).Reason.ShouldContain("github.com/thatplatypus/elsewhere");
        }

        // A lockfile made in code, as an edit or a bad merge of one could leave it. Each of those below
        // holds together as far as the reader can tell, so only what is published can show it up.
        private static LockedPackage Entry(PublishedVersion published, params string[] pins) => new(
            published.Name,
            published.Version,
            published.Source,
            published.Sha256,
            published.Verified,
            pins.Select(pin => new LockedDependency(Sample.Asks(pin).Name, Sample.Asks(pin).Requirement.Minimum)).ToList());

        private static Lockfile Holding(params LockedPackage[] packages) => new(RootRequirements.From(Manifest), packages);

        private static Universe GrapevineAskingForMore() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2", "@thatplatypus/deflate@0.1")
            .Publish("@thatplatypus/crypto", "1.0.0")
            .Publish("@thatplatypus/crypto", "1.2.0")
            .Publish("@thatplatypus/crypto", "1.9.0")
            .Publish("@thatplatypus/crypto", "2.0.0")
            .Publish("@thatplatypus/deflate", "0.1.0")
            .Publish("@attacker/evil", "1.0.0");

        [Fact]
        public async Task A_locked_dependency_below_what_the_published_version_asks_for_is_an_error()
        {
            // Every digest here is the real one. Only what grapevine is said to depend on is wrong.
            var universe = GrapevineAskingForMore();
            var lowered = Holding(
                Entry(universe.Get("@thatplatypus/crypto", "1.0.0")),
                Entry(universe.Get("@thatplatypus/deflate", "0.1.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0"));

            var diagnostic = (await Check(lowered, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Message.ShouldBe("packmoji.lock does not agree with what is published for \"@thatplatypus/grapevine\" 0.3.0.");
            diagnostic.Reason.ShouldStartWith("what is published asks for @thatplatypus/crypto@1.2, and the lockfile gives it @thatplatypus/crypto@1.0.0;");
            diagnostic.Fix.ShouldNotContain("delete");
        }

        [Fact]
        public async Task A_locked_dependency_on_another_line_than_the_published_version_asks_for_is_an_error()
        {
            var universe = GrapevineAskingForMore();
            var raised = Holding(
                Entry(universe.Get("@thatplatypus/crypto", "2.0.0")),
                Entry(universe.Get("@thatplatypus/deflate", "0.1.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@thatplatypus/crypto@2.0.0", "@thatplatypus/deflate@0.1.0"));

            (await Check(raised, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch)
                .Reason.ShouldContain("asks for @thatplatypus/crypto@1.2, and the lockfile gives it @thatplatypus/crypto@2.0.0");
        }

        [Fact]
        public async Task A_locked_dependency_that_the_published_version_does_not_have_is_an_error()
        {
            // A package nothing asks for, hung on one that is asked for, would be fetched and built.
            var universe = GrapevineAskingForMore();
            var added = Holding(
                Entry(universe.Get("@attacker/evil", "1.0.0")),
                Entry(universe.Get("@thatplatypus/crypto", "1.2.0")),
                Entry(universe.Get("@thatplatypus/deflate", "0.1.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@attacker/evil@1.0.0", "@thatplatypus/crypto@1.2.0", "@thatplatypus/deflate@0.1.0"));

            var diagnostic = (await Check(added, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Message.ShouldContain("\"@thatplatypus/grapevine\" 0.3.0");
            diagnostic.Reason.ShouldStartWith("the lockfile gives it a dependency on @attacker/evil@1.0.0, and what is published has none on that package;");
        }

        [Fact]
        public async Task A_dependency_of_the_published_version_that_the_lockfile_leaves_out_is_an_error()
        {
            var universe = GrapevineAskingForMore();
            var dropped = Holding(
                Entry(universe.Get("@thatplatypus/crypto", "1.2.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@thatplatypus/crypto@1.2.0"));

            (await Check(dropped, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch)
                .Reason.ShouldStartWith("what is published asks for @thatplatypus/deflate@0.1, and the lockfile gives it no such dependency;");
        }

        [Fact]
        public async Task A_locked_dependency_above_the_minimum_on_its_line_agrees_with_what_is_published()
        {
            // Something else may have asked for more, and a check chooses no versions, so it cannot
            // tell. A locked version that is newer than anyone asked for is not noticed here.
            var universe = GrapevineAskingForMore();
            var higher = Holding(
                Entry(universe.Get("@thatplatypus/crypto", "1.9.0")),
                Entry(universe.Get("@thatplatypus/deflate", "0.1.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@thatplatypus/crypto@1.9.0", "@thatplatypus/deflate@0.1.0"));

            var result = await Check(higher, universe);

            result.ShouldSucceed();
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_digest_and_a_dependency_that_both_differ_are_one_problem_of_the_one_package()
        {
            var universe = GrapevineAskingForMore();
            var grapevine = Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), "@thatplatypus/crypto@1.2.0") with { Sha256 = Sample.Sha('b') };
            var both = Holding(Entry(universe.Get("@thatplatypus/crypto", "1.2.0")), grapevine);

            var diagnostic = (await Check(both, universe)).ShouldFailWith(DiagnosticCodes.LockMismatch);

            diagnostic.Reason.ShouldStartWith($"the lockfile has the digest {Sample.Sha('b')} and what is published has");
            diagnostic.Reason.ShouldContain(", and what is published asks for @thatplatypus/deflate@0.1, and the lockfile gives it no such dependency;");
        }

        [Fact]
        public async Task Of_many_dependencies_that_differ_three_are_named_and_the_rest_are_said_to_be_there()
        {
            var universe = GrapevineAskingForMore();
            var extras = Enumerable.Range(1, 6).Select(number => $"@thatplatypus/extra{number}@1.0.0").ToArray();
            var many = Holding(
                Entry(universe.Get("@thatplatypus/crypto", "1.2.0")),
                Entry(universe.Get("@thatplatypus/deflate", "0.1.0")),
                Entry(universe.Get("@thatplatypus/grapevine", "0.3.0"), ["@thatplatypus/crypto@1.2.0", "@thatplatypus/deflate@0.1.0", .. extras]));

            var result = await Check(many, universe);

            var diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.Code == DiagnosticCodes.LockMismatch).ShouldBeComplete();
            diagnostic.Reason.ShouldContain("@thatplatypus/extra1@1.0.0");
            diagnostic.Reason.ShouldContain("@thatplatypus/extra3@1.0.0");
            diagnostic.Reason.ShouldNotContain("@thatplatypus/extra4@1.0.0");
            diagnostic.Reason.ShouldContain("and more of its dependencies differ");
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
