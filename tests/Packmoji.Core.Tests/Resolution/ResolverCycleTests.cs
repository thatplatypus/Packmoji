using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// Emojicode refuses packages that import one another in a circle, so a build of them could not
    /// succeed. The resolver says so, and shows the circle, before anything is fetched.
    /// </summary>
    public sealed class ResolverCycleTests
    {
        [Fact]
        public async Task Two_selected_versions_that_need_each_other_stop_the_resolution_and_the_circle_is_shown()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/b@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);

            diagnostic.Message.ShouldBe("\"@thatplatypus/a\" depends on itself through other packages.");
            diagnostic.Reason.ShouldEndWith("in a circle: @thatplatypus/a@1.0.0 → @thatplatypus/b@1.0.0 → @thatplatypus/a@1.0");
        }

        [Fact]
        public async Task The_circle_is_shown_at_the_versions_selected_and_closes_with_the_requirement_as_it_was_written()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0")
                .Publish("@thatplatypus/a", "1.2.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/c", "1.0.0", "@thatplatypus/a@1.2.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/c@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/a@1.2.0 → @thatplatypus/b@1.0.0 → @thatplatypus/c@1.0.0 → @thatplatypus/a@1.2.0");
        }

        [Fact]
        public async Task A_circle_that_only_a_superseded_version_makes_is_no_circle()
        {
            // a 1.0.0 needs b and b needs a 1.1, which needs nothing: what is built has no circle in it.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/a@1.1.0"]);
        }

        [Fact]
        public async Task A_version_that_names_its_own_package_is_a_circle_of_one()
        {
            // No manifest can say this, and a source is not held to what a manifest can say.
            var universe = new Universe().Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/a@1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);

            diagnostic.Message.ShouldBe("\"@thatplatypus/a\" depends on itself.");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/a@1.0.0 → @thatplatypus/a@1.0");
        }

        [Fact]
        public async Task Two_circles_are_two_problems_in_order_of_name()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/x", "1.0.0", "@thatplatypus/y@1.0")
                .Publish("@thatplatypus/y", "1.0.0", "@thatplatypus/x@1.0")
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/x@1.0", "@thatplatypus/a@1.0"));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Message).ShouldBe(
            [
                "\"@thatplatypus/a\" depends on itself through other packages.",
                "\"@thatplatypus/x\" depends on itself through other packages.",
            ]);
        }

        [Fact]
        public async Task A_version_that_depends_on_the_project_itself_stops_the_resolution()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/app@0.1")
                .Publish("@thatplatypus/app", "0.1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);

            diagnostic.Message.ShouldBe("\"@thatplatypus/app\" depends on itself.");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/app@0.1");
            diagnostic.Fix.ShouldBe(
                "ask in packmoji.json for a later version of \"@thatplatypus/grapevine\", one that does not depend on \"@thatplatypus/app\"; " +
                "if there is none, stop depending on what brings it in");
            universe.Asked.ShouldBe(["@thatplatypus/grapevine@0.3.0"]);
        }

        [Fact]
        public async Task A_version_that_depends_on_the_project_and_is_not_the_one_built_stops_nothing()
        {
            // As with a circle among packages: what is built is a 1.1.0, which needs nothing.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/app@0.1")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"));

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/a@1.1.0", "@thatplatypus/b@1.0.0"]);
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task Asking_for_a_later_version_that_does_not_depend_on_the_project_is_the_fix_and_it_works()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/tool", "1.0.0", "@thatplatypus/plugin@1.0")
                .Publish("@thatplatypus/plugin", "1.0.0", "@thatplatypus/app@0.1")
                .Publish("@thatplatypus/plugin", "1.1.0");

            var stopped = (await universe.Resolve(Project.Asking("@thatplatypus/tool@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);
            var fixedByAskingForMore = await universe.Resolve(Project.Asking("@thatplatypus/tool@1.0", "@thatplatypus/plugin@1.1"));

            stopped.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/tool@1.0.0 → @thatplatypus/plugin@1.0.0 → @thatplatypus/app@0.1");
            stopped.Fix.ShouldStartWith("ask in packmoji.json for a later version of \"@thatplatypus/plugin\"");
            fixedByAskingForMore.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/plugin@1.1.0", "@thatplatypus/tool@1.0.0"]);
        }

        [Fact]
        public async Task A_project_that_asks_for_itself_is_told_to_take_the_entry_out()
        {
            var diagnostic = (await new Universe().Resolve(Project.Asking("@thatplatypus/app@0.1"))).ShouldFailWith(DiagnosticCodes.ResolveCycle);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/app@0.1");
            diagnostic.Fix.ShouldBe("remove \"@thatplatypus/app\" from its own packmoji.json");
        }

        [Fact]
        public async Task A_circle_is_not_looked_for_while_a_package_has_no_selected_version()
        {
            // Without a version for every package there is no one graph to look for a circle in.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.0", "@thatplatypus/c@2.0")
                .Publish("@thatplatypus/c", "1.0.0")
                .Publish("@thatplatypus/c", "2.0.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/c@1.0"))).ShouldFailWith(DiagnosticCodes.ResolveLineConflict);
        }
    }
}
