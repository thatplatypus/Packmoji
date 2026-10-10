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
        public async Task The_chain_shows_which_version_asked_for_it()
        {
            var universe = new Universe().Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldStartWith("the chain begins at this project's requirement on \"@thatplatypus/grapevine\"");
            diagnostic.Fix.ShouldContain("\"@thatplatypus/grapevine\" has to publish a version that asks for a version of \"@thatplatypus/crypto\" that exists");
        }

        // Grapevine asks for crypto, and crypto 1.0.0 asks for a zlib that was never published.
        // Crypto 1.0.1 asks for one that was, and grapevine 0.3.1 has moved to crypto 1.0.1.
        private static Universe MissingDeepInTheChain() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
            .Publish("@thatplatypus/grapevine", "0.3.1", "@thatplatypus/crypto@1.0.1")
            .Publish("@thatplatypus/crypto", "1.0.0", "@thatplatypus/zlib@9.9")
            .Publish("@thatplatypus/crypto", "1.0.1", "@thatplatypus/zlib@1.0")
            .Publish("@thatplatypus/zlib", "1.0.0");

        [Fact]
        public async Task When_a_package_deep_in_the_chain_asks_for_it_the_fix_is_at_the_head_of_the_chain()
        {
            // The project wrote only the first requirement of the chain, so that is all it can change.
            var diagnostic = (await MissingDeepInTheChain().Resolve(Project.Asking("@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0.0 → @thatplatypus/zlib@9.9");
            diagnostic.Fix.ShouldStartWith("the chain begins at this project's requirement on \"@thatplatypus/grapevine\": ask there, in packmoji.json, for a later version");
            diagnostic.Fix.ShouldContain("\"@thatplatypus/crypto\" has to publish a version that asks for a version of \"@thatplatypus/zlib\" that exists");
        }

        [Fact]
        public async Task Asking_for_a_later_version_at_the_head_of_the_chain_is_the_fix_and_it_works()
        {
            (await MissingDeepInTheChain().Resolve(Project.Asking("@thatplatypus/grapevine@0.3.1"))).ShouldSucceed()
                .Selected().ShouldBe(["@thatplatypus/crypto@1.0.1", "@thatplatypus/grapevine@0.3.1", "@thatplatypus/zlib@1.0.0"]);
        }

        [Fact]
        public async Task Asking_for_more_of_a_package_in_the_middle_of_the_chain_does_not_help()
        {
            // Grapevine 0.3.0 still leads to crypto 1.0.0, and so to what crypto 1.0.0 asks for.
            var result = await MissingDeepInTheChain().Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0.1"));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldContain(DiagnosticCodes.ResolveVersionMissing);
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
