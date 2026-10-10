using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// A limit on scopes holds for what a project comes to need as for what it asks for: a package
    /// that is allowed may depend on one that is not. Such a package stops the resolution, and
    /// nothing is asked of the source about it.
    /// </summary>
    public sealed class ResolverScopeTests
    {
        private static ScopeLimit Only(string scopes)
        {
            ScopeLimit.TryParse(scopes, out var limit, out _).ShouldBeTrue();
            return limit!;
        }

        [Fact]
        public async Task A_package_outside_the_list_that_an_allowed_package_depends_on_stops_the_resolution_and_is_never_asked_for()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@someone/thing@1.0")
                .Publish("@someone/thing", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"), allowed: Only("thatplatypus"))).ShouldFailWith(DiagnosticCodes.ScopeNotAllowed);

            diagnostic.Message.ShouldBe("\"@someone/thing\" is outside the scopes pmj is limited to here.");
            diagnostic.Reason.ShouldBe("\"@thatplatypus/grapevine\" 0.3.0 depends on it (@thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @someone/thing@1.0), and PACKMOJI_SCOPES allows only thatplatypus");
            universe.Asked.ShouldNotContain(asked => asked.Contains("@someone/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_package_the_project_itself_asks_for_is_refused_as_one_the_manifest_asks_for()
        {
            var universe = new Universe().Publish("@someone/thing", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@someone/thing@1.0"), allowed: Only("thatplatypus,emojicode"))).ShouldFailWith(DiagnosticCodes.ScopeNotAllowed);

            diagnostic.Reason.ShouldBe("packmoji.json asks for it, and PACKMOJI_SCOPES allows only emojicode and thatplatypus");
            universe.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task Each_package_outside_the_list_is_named_once_in_order_of_name_and_what_lies_behind_it_is_not_looked_at()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@someone/zebra@1.0", "@other/apple@2.0")
                .Publish("@thatplatypus/b", "1.0.0", "@someone/zebra@1.0")
                .Publish("@someone/zebra", "1.0.0", "@hidden/behind@1.0")
                .Publish("@other/apple", "2.0.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"), allowed: Only("thatplatypus"));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}").ShouldBe(
            [
                "scope.not-allowed \"@other/apple\" is outside the scopes pmj is limited to here.",
                "scope.not-allowed \"@someone/zebra\" is outside the scopes pmj is limited to here.",
            ]);
            universe.Asked.ShouldAllBe(asked => asked.StartsWith("@thatplatypus/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_package_outside_the_list_that_is_asked_for_at_two_versions_is_refused_once()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@someone/zebra@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@someone/zebra@2.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"), allowed: Only("thatplatypus"));

            // It is the package that may not be depended on, and not one version of it.
            var refused = result.Diagnostics.ShouldHaveSingleItem();
            refused.Code.ShouldBe(DiagnosticCodes.ScopeNotAllowed);
            refused.Reason.ShouldStartWith("\"@thatplatypus/a\" 1.0.0 depends on it (");
        }

        [Fact]
        public async Task What_is_inside_the_list_resolves_as_it_does_with_no_limit()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@emojicode/extras@1.0")
                .Publish("@emojicode/extras", "1.0.0");

            var limited = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"), allowed: Only("thatplatypus,emojicode"));
            var free = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"));

            limited.Succeeded.ShouldBeTrue();
            limited.Graph!.Packages.Select(package => package.Published.Name.ToString()).ShouldBe(free.Graph!.Packages.Select(package => package.Published.Name.ToString()));
        }

        [Fact]
        public async Task With_no_limit_a_package_of_any_scope_is_asked_for_as_ever()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@someone/thing@1.0")
                .Publish("@someone/thing", "1.0.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"), allowed: ScopeLimit.None)).Succeeded.ShouldBeTrue();
        }
    }
}
