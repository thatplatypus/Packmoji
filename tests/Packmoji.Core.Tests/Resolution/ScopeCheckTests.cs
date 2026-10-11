using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// What a project asks for, and what its lockfile holds, held to a limit on scopes before
    /// anything is done with either. Both files may have been written by a stranger, so what this
    /// says of them is bounded however much they name.
    /// </summary>
    public sealed class ScopeCheckTests
    {
        private static ScopeLimit Only(string scopes)
        {
            ScopeLimit.TryParse(scopes, out var limit, out _).ShouldBeTrue();
            return limit!;
        }

        // A lockfile that answers a manifest: the manifest's own requirements, and the packages given.
        private static Lockfile Answering(Manifest manifest, params string[] packages) =>
            new(RootRequirements.From(manifest), Locked.Of(packages).Packages);

        [Fact]
        public void Each_package_outside_the_list_is_named_once_in_order_of_name_and_the_manifest_is_what_is_said_to_ask_for_it()
        {
            var manifest = Project.Named(Project.Name, ["@thatplatypus/grapevine@0.3", "@someone/thing@1.0"], ["@another/apple@2.0"]);
            var lockfile = Answering(manifest, "@thatplatypus/grapevine 0.3.0 > @zeta/deep 1.0.0", "@someone/thing 1.0.0", "@another/apple 2.0.0", "@zeta/deep 1.0.0");

            var refused = ScopeCheck.Outside(Only("thatplatypus"), manifest, lockfile, out var omitted);

            omitted.ShouldBe(0);
            refused.Select(problem => $"{problem.Code} {problem.Message} {problem.Reason}").ShouldBe(
            [
                "scope.not-allowed \"@another/apple\" is outside the scopes pmj is limited to here. packmoji.json asks for it, and PACKMOJI_SCOPES allows only thatplatypus",
                "scope.not-allowed \"@someone/thing\" is outside the scopes pmj is limited to here. packmoji.json asks for it, and PACKMOJI_SCOPES allows only thatplatypus",
                "scope.not-allowed \"@zeta/deep\" is outside the scopes pmj is limited to here. packmoji.lock holds it, and PACKMOJI_SCOPES allows only thatplatypus",
            ]);
        }

        [Fact]
        public void No_more_than_a_hundred_are_listed_however_many_a_strangers_lockfile_names_and_the_rest_are_counted()
        {
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3");
            var packages = Enumerable.Range(0, 105).Select(index => $"@evil/pkg{index:000} 1.0.0").Append("@thatplatypus/grapevine 0.3.0").ToArray();

            var refused = ScopeCheck.Outside(Only("thatplatypus"), manifest, Answering(manifest, packages), out var omitted);

            refused.Count.ShouldBe(100);
            omitted.ShouldBe(5);
            refused[0].Message.ShouldStartWith("\"@evil/pkg000\"");
            refused[99].Message.ShouldStartWith("\"@evil/pkg099\"");
        }

        [Fact]
        public void A_lockfile_that_no_longer_answers_the_manifest_is_not_held_since_nothing_of_it_is_used()
        {
            // The manifest was mended and the lockfile is from before: it still holds what is no longer asked for.
            var before = Project.Asking("@thatplatypus/grapevine@0.3", "@someone/thing@1.0");
            var stale = Answering(before, "@thatplatypus/grapevine 0.3.0", "@someone/thing 1.0.0");
            var mended = Project.Asking("@thatplatypus/grapevine@0.3");

            ScopeCheck.Outside(Only("thatplatypus"), mended, stale, out _).ShouldBeEmpty();
        }

        [Fact]
        public void A_manifest_is_held_whatever_becomes_of_its_lockfile()
        {
            var asking = Project.Asking("@thatplatypus/grapevine@0.3", "@someone/thing@1.0");
            var stale = Answering(Project.Asking("@thatplatypus/grapevine@0.3"), "@thatplatypus/grapevine 0.3.0", "@old/gone 1.0.0");

            var refused = ScopeCheck.Outside(Only("thatplatypus"), asking, stale, out _);

            refused.ShouldHaveSingleItem().Message.ShouldStartWith("\"@someone/thing\"");
        }

        [Fact]
        public void With_no_limit_and_with_everything_inside_one_nothing_is_refused()
        {
            var manifest = Project.Asking("@thatplatypus/grapevine@0.3", "@someone/thing@1.0");
            var lockfile = Answering(manifest, "@thatplatypus/grapevine 0.3.0", "@someone/thing 1.0.0");

            ScopeCheck.Outside(ScopeLimit.None, manifest, lockfile, out var free).ShouldBeEmpty();
            ScopeCheck.Outside(Only("someone,thatplatypus"), manifest, lockfile, out var inside).ShouldBeEmpty();
            ScopeCheck.Outside(Only("thatplatypus"), Project.Asking(), null, out var nothing).ShouldBeEmpty();
            (free, inside, nothing).ShouldBe((0, 0, 0));
        }
    }
}
