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
    /// A yanked version was withdrawn by its author. A project that had already locked it goes on
    /// using it and is told; nothing may start to use it, and the resolver never moves to a later
    /// version by itself, so the project is told which minimum to raise.
    /// </summary>
    public sealed class ResolverYankedTests
    {
        private static Universe Grapevine() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
            .Publish("@thatplatypus/crypto", "1.0.0")
            .Publish("@thatplatypus/crypto", "1.0.1")
            .Publish("@thatplatypus/crypto", "1.1.0");

        private static async Task<Lockfile> Locked(Universe universe, Manifest manifest) =>
            (await universe.Resolve(manifest)).ShouldSucceed().ToLockfile(manifest);

        [Fact]
        public async Task A_selected_version_that_is_yanked_stops_a_project_that_has_no_lockfile()
        {
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveYanked);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" has been yanked.");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldBe("ask for a later version of \"@thatplatypus/crypto\" in packmoji.json");
        }

        [Fact]
        public async Task The_resolver_does_not_move_to_a_later_version_by_itself()
        {
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0"));

            result.Graph.ShouldBeNull();
            universe.Asked.ShouldBe(["@thatplatypus/crypto@1.0.0"]);
        }

        [Fact]
        public async Task The_fix_says_where_it_is_asked_for_and_that_the_project_may_ask_for_more()
        {
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldFailWith(DiagnosticCodes.ResolveYanked);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldContain("in packmoji.json");
            diagnostic.Fix.ShouldContain("on the line 1.x");
            diagnostic.Fix.ShouldContain("@thatplatypus/grapevine 0.3.0");
        }

        [Fact]
        public async Task Asking_for_more_in_the_project_is_the_fix_and_it_works()
        {
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0.1"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.0.1", "@thatplatypus/grapevine@0.3.0"]);
        }

        [Fact]
        public async Task A_yanked_version_that_the_lockfile_holds_goes_on_being_used_with_a_warning()
        {
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");
            var locked = await Locked(Grapevine(), manifest);
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var result = await universe.Resolve(manifest, locked);

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/grapevine@0.3.0"]);
            var warning = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            warning.Code.ShouldBe(DiagnosticCodes.ResolveYankedLocked);
            warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
            warning.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" has been yanked.");
            warning.Reason.ShouldContain("packmoji.lock already holds it");
            warning.Fix.ShouldContain("on the line 1.x");
        }

        [Fact]
        public async Task A_resolved_package_says_that_it_is_yanked()
        {
            var manifest = Project.Asking("@thatplatypus/crypto@1.0");
            var locked = await Locked(Grapevine(), manifest);
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var graph = (await universe.Resolve(manifest, locked)).ShouldSucceed();

            graph.Packages.ShouldHaveSingleItem().Published.Status.ShouldBe(VersionStatus.Yanked);
        }

        [Fact]
        public async Task A_lockfile_that_holds_the_package_at_another_version_does_not_excuse_it()
        {
            // The manifest now asks for 1.0.1, which is yanked, and the lockfile holds 1.0.0.
            var locked = await Locked(Grapevine(), Project.Asking("@thatplatypus/crypto@1.0"));
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.1");

            (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0.1"), locked)).ShouldFailWith(DiagnosticCodes.ResolveYanked);
        }

        [Fact]
        public async Task A_yanked_version_that_is_not_the_one_selected_stops_nothing()
        {
            var universe = Grapevine().Yank("@thatplatypus/crypto", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1"));

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.1.0", "@thatplatypus/grapevine@0.3.0"]);
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_yanked_version_of_a_package_that_is_not_in_the_build_stops_nothing()
        {
            // Only a 1.0.0 asks for d, and a 1.0.0 is superseded: nothing that is built needs d.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/d@1.0")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1")
                .Publish("@thatplatypus/d", "1.0.0")
                .Yank("@thatplatypus/d", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"));

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/a@1.1.0", "@thatplatypus/b@1.0.0"]);
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task Errors_are_listed_before_warnings_so_that_a_warning_never_takes_the_room_of_an_error()
        {
            var manifest = Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");
            var before = new Universe().Publish("@thatplatypus/crypto", "1.0.0").Publish("@thatplatypus/deflate", "0.1.0");
            var locked = await Locked(before, manifest);
            var universe = new Universe()
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/deflate", "0.1.0")
                .Publish("@thatplatypus/zlib", "1.0.0")
                .Yank("@thatplatypus/crypto", "1.0.0")
                .Yank("@thatplatypus/zlib", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1", "@thatplatypus/zlib@1.0"), locked);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity)).ShouldBe(
            [
                (DiagnosticCodes.ResolveYanked, DiagnosticSeverity.Error),
                (DiagnosticCodes.ResolveYankedLocked, DiagnosticSeverity.Warning),
            ]);
            result.Diagnostics[0].Message.ShouldContain("\"@thatplatypus/zlib\"");
            result.Diagnostics[1].Message.ShouldContain("\"@thatplatypus/crypto\"");
        }

        [Fact]
        public async Task Whatever_the_graph_what_was_locked_before_a_yank_goes_on_resolving_and_nothing_else_does()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var tried = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var manifest = project.Manifest();
                    var before = await Resolver.ResolveAsync(manifest, null, project.Universe(), cancellation);
                    if (!before.Succeeded || before.Graph.Packages.Count == 0)
                    {
                        return;
                    }

                    Interlocked.Increment(ref tried);
                    var locked = before.Graph.ToLockfile(manifest);
                    var universe = project.Universe();
                    foreach (var package in before.Graph.Packages)
                    {
                        universe.Yank(package.Published.Name.ToString(), package.Published.Version.ToString());
                    }

                    var withLockfile = await Resolver.ResolveAsync(manifest, locked, universe, cancellation);
                    var withoutLockfile = await Resolver.ResolveAsync(manifest, null, universe, cancellation);

                    LockfileWriter.Write(withLockfile.ShouldSucceed().ToLockfile(manifest)).ShouldBe(LockfileWriter.Write(locked));
                    withLockfile.Diagnostics.Count.ShouldBe(before.Graph.Packages.Count);
                    withLockfile.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Code == DiagnosticCodes.ResolveYankedLocked && diagnostic.Severity == DiagnosticSeverity.Warning);
                    withoutLockfile.Succeeded.ShouldBeFalse();
                    withoutLockfile.Diagnostics.Count(diagnostic => diagnostic.Code == DiagnosticCodes.ResolveYanked).ShouldBe(before.Graph.Packages.Count);
                },
                iter: 500,
                print: project => project.Describe());

            tried.ShouldBeGreaterThan(50);
        }
    }
}
