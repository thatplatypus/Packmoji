using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// A requirement names the exact version a build uses unless something asks for more. So a version
    /// that was never published stops a resolution wherever it is asked for, and the resolver never
    /// moves to a later version by itself.
    /// </summary>
    public sealed class ResolverMissingVersionTests
    {
        [Fact]
        public async Task A_version_that_was_never_published_stops_the_resolution()
        {
            var universe = new Universe().Publish("@thatplatypus/crypto", "1.0.1");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" was never published.");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldBe("ask for a version of \"@thatplatypus/crypto\" that was published, in packmoji.json");
        }

        [Fact]
        public async Task The_chain_shows_which_version_asked_for_it_and_the_fix_names_that_package()
        {
            var universe = new Universe().Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldContain("a later version of \"@thatplatypus/grapevine\"");
            diagnostic.Fix.ShouldContain("@thatplatypus/grapevine 0.3.0");
        }

        [Fact]
        public async Task It_stops_the_resolution_even_when_a_higher_minimum_supersedes_it()
        {
            // Were this allowed, publishing crypto 1.0.0 later could change a build that no one touched.
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.1")
                .Publish("@thatplatypus/crypto", "1.1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Message.ShouldContain("Version 1.0.0");
        }

        [Fact]
        public async Task It_stops_the_resolution_even_when_only_a_superseded_version_asks_for_it()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/d@1.0")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/a@1.0.0 → @thatplatypus/d@1.0");
        }

        [Fact]
        public async Task A_version_that_the_lockfile_holds_and_the_source_no_longer_has_is_gone_and_the_lockfile_is_to_be_kept()
        {
            // The lockfile holds a digest for it, so it was published once. What is replaced is first
            // taken down, and a lockfile deleted meanwhile leaves no digest to catch what comes back.
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");
            var before = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
                .Publish("@thatplatypus/crypto", "1.0.0");
            var locked = (await before.Resolve(manifest)).ShouldSucceed().ToLockfile(manifest);
            var universe = new Universe().Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0");

            var diagnostic = (await universe.Resolve(manifest, locked)).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" is no longer published.");
            diagnostic.Reason.ShouldStartWith("packmoji.lock holds it");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldStartWith("keep packmoji.lock");
            diagnostic.Fix.ShouldNotContain("delete");
        }

        [Fact]
        public async Task A_lockfile_that_holds_the_package_at_another_version_says_nothing_of_this_one()
        {
            var manifest = Project.Asking("@thatplatypus/crypto@1.0");
            var locked = (await new Universe().Publish("@thatplatypus/crypto", "1.0.0").Resolve(manifest)).ShouldSucceed().ToLockfile(manifest);

            var diagnostic = (await new Universe().Resolve(Project.Asking("@thatplatypus/crypto@1.1"), locked))
                .ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" was never published.");
        }

        [Fact]
        public async Task Every_missing_version_is_reported_in_order_of_name_and_then_of_version()
        {
            var universe = new Universe().Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2", "@thatplatypus/deflate@0.1");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0"));

            result.Succeeded.ShouldBeFalse();
            result.Graph.ShouldBeNull();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Message).ShouldBe(
            [
                "Version 1.0.0 of \"@thatplatypus/crypto\" was never published.",
                "Version 1.2.0 of \"@thatplatypus/crypto\" was never published.",
                "Version 0.1.0 of \"@thatplatypus/deflate\" was never published.",
            ]);
        }
    }
}
