using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// Emojicode imports a package by its bare name, so one build cannot hold two packages of one
    /// name, whoever owns them. A manifest is checked for that when it is read, and only a resolution
    /// can see the packages a manifest does not name.
    /// </summary>
    public sealed class ResolverNameCollisionTests
    {
        [Fact]
        public async Task Two_packages_that_share_a_bare_name_stop_the_resolution_and_each_is_shown_by_how_it_was_reached()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@banana/markdown@2.0")
                .Publish("@apple/markdown", "1.0.0")
                .Publish("@banana/markdown", "2.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@apple/markdown@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveNameCollision);

            diagnostic.Message.ShouldBe("\"@apple/markdown\" and \"@banana/markdown\" have the same name.");
            diagnostic.Reason.ShouldEndWith(
                "two packages named \"markdown\" cannot be in one build: " +
                "@thatplatypus/app → @apple/markdown@1.0; @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @banana/markdown@2.0");
            diagnostic.Fix.ShouldBe("depend on only one of them");
        }

        [Fact]
        public async Task A_package_that_shares_its_bare_name_with_the_project_stops_it_as_well()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@banana/app@1.0")
                .Publish("@banana/app", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveNameCollision);

            diagnostic.Message.ShouldBe("\"@banana/app\" has the same name as this project, \"@thatplatypus/app\".");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @banana/app@1.0");
            diagnostic.Fix.ShouldContain("another name");
        }

        [Fact]
        public async Task Three_packages_of_one_name_are_one_problem()
        {
            var universe = new Universe()
                .Publish("@apple/markdown", "1.0.0")
                .Publish("@banana/markdown", "1.0.0")
                .Publish("@cherry/markdown", "1.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@cherry/markdown@1.0", "@apple/markdown@1.0", "@banana/markdown@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveNameCollision);

            diagnostic.Message.ShouldBe("\"@apple/markdown\", \"@banana/markdown\" and \"@cherry/markdown\" have the same name.");
        }

        [Fact]
        public async Task A_name_counts_even_when_only_a_superseded_version_asks_for_the_package()
        {
            // Every requirement that can be reached counts, and so does every package one of them names.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@banana/markdown@1.0")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1")
                .Publish("@apple/markdown", "1.0.0")
                .Publish("@banana/markdown", "1.0.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0", "@apple/markdown@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveNameCollision);
        }

        [Fact]
        public async Task Each_shared_name_is_reported_once_in_the_order_of_its_first_package()
        {
            var universe = new Universe()
                .Publish("@apple/yaml", "1.0.0")
                .Publish("@banana/markdown", "1.0.0")
                .Publish("@cherry/markdown", "1.0.0")
                .Publish("@cherry/yaml", "1.0.0");

            var result = await universe.Resolve(Project.Asking("@cherry/yaml@1.0", "@cherry/markdown@1.0", "@banana/markdown@1.0", "@apple/yaml@1.0"));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Message).ShouldBe(
            [
                "\"@apple/yaml\" and \"@cherry/yaml\" have the same name.",
                "\"@banana/markdown\" and \"@cherry/markdown\" have the same name.",
            ]);
        }

        [Fact]
        public async Task One_package_at_two_versions_is_not_two_packages()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@apple/markdown@1.1")
                .Publish("@apple/markdown", "1.0.0")
                .Publish("@apple/markdown", "1.1.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@apple/markdown@1.0"))).ShouldSucceed();
        }
    }
}
