using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// A quarantined version is one whose bytes changed after it was published. That is what a
    /// tampered release looks like, so nothing uses it, whether or not it was locked before.
    /// </summary>
    public sealed class ResolverQuarantineTests
    {
        private static Universe Grapevine() => new Universe()
            .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
            .Publish("@thatplatypus/crypto", "1.0.0")
            .Publish("@thatplatypus/crypto", "1.1.0");

        [Fact]
        public async Task A_selected_version_that_is_quarantined_stops_the_resolution_and_says_why_it_matters()
        {
            var universe = Grapevine().Quarantine("@thatplatypus/crypto", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldFailWith(DiagnosticCodes.ResolveQuarantined);

            diagnostic.Message.ShouldBe("Version 1.0.0 of \"@thatplatypus/crypto\" is quarantined.");
            diagnostic.Reason.ShouldContain("its bytes changed after it was published");
            diagnostic.Reason.ShouldContain("must not be used");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            diagnostic.Fix.ShouldContain("on the line 1.x");
        }

        [Fact]
        public async Task It_stops_the_resolution_though_the_lockfile_holds_it()
        {
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");
            var locked = (await Grapevine().Resolve(manifest)).ShouldSucceed().ToLockfile(manifest);
            var universe = Grapevine().Quarantine("@thatplatypus/crypto", "1.0.0");

            (await universe.Resolve(manifest, locked)).ShouldFailWith(DiagnosticCodes.ResolveQuarantined);
        }

        [Fact]
        public async Task A_quarantined_version_that_is_not_the_one_selected_stops_nothing()
        {
            var universe = Grapevine().Quarantine("@thatplatypus/crypto", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.1"));

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.1.0", "@thatplatypus/grapevine@0.3.0"]);
            result.Diagnostics.ShouldBeEmpty();
        }
    }
}
