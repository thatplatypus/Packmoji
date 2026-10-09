using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// Minimal version selection: every requirement is a minimum, and the version a build uses is the
    /// highest minimum anyone asked for, never anything newer.
    /// </summary>
    public sealed class ResolverSelectionTests
    {
        [Fact]
        public async Task A_project_that_asks_for_nothing_gets_nothing_and_nothing_is_asked_of_the_source()
        {
            var universe = new Universe().Publish("@thatplatypus/crypto", "1.0.0");

            var graph = (await universe.Resolve(Project.Asking())).ShouldSucceed();

            graph.Packages.ShouldBeEmpty();
            universe.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_manifest_with_neither_table_is_a_project_that_asks_for_nothing()
        {
            var manifest = ManifestReader.Read(Fixtures.MinimalManifest).ShouldSucceed();

            var result = await new Universe().Resolve(manifest);

            result.ShouldSucceed().Packages.ShouldBeEmpty();
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task One_requirement_selects_the_version_it_names_and_nothing_newer()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.0.1")
                .Publish("@thatplatypus/crypto", "1.4.0")
                .Publish("@thatplatypus/crypto", "2.0.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.0.0"]);
        }

        [Fact]
        public async Task What_a_selected_version_depends_on_is_selected_too()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/deflate", "0.1.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            graph.Pins("@thatplatypus/grapevine").ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0"]);
            graph.Pins("@thatplatypus/crypto").ShouldBeEmpty();
        }

        [Fact]
        public async Task Of_several_requirements_on_one_package_the_highest_minimum_wins()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.2.0")
                .Publish("@thatplatypus/crypto", "1.3.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.2.0", "@thatplatypus/grapevine@0.3.0"]);
        }

        [Fact]
        public async Task A_dependency_is_paired_with_the_version_selected_and_not_with_the_one_it_asked_for()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.3.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.3", "@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.3.0", "@thatplatypus/grapevine@0.3.0"]);
            graph.Pins("@thatplatypus/grapevine").ShouldBe(["@thatplatypus/crypto@1.3.0"]);
        }

        [Fact]
        public async Task A_requirement_of_a_version_that_ends_up_superseded_still_counts()
        {
            // a 1.0.0 is superseded by a 1.1.0, and what a 1.0.0 asked of c still decides c.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/c@1.2")
                .Publish("@thatplatypus/a", "1.1.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1")
                .Publish("@thatplatypus/c", "1.0.0")
                .Publish("@thatplatypus/c", "1.2.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/a@1.1.0", "@thatplatypus/b@1.0.0", "@thatplatypus/c@1.2.0"]);
        }

        [Fact]
        public async Task A_package_that_only_a_superseded_version_asks_for_is_looked_at_and_is_not_in_the_build()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/d@1.0")
                .Publish("@thatplatypus/a", "1.1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1")
                .Publish("@thatplatypus/d", "1.0.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/a@1.1.0", "@thatplatypus/b@1.0.0"]);
            universe.Asked.ShouldContain("@thatplatypus/d@1.0.0");
        }

        [Fact]
        public async Task A_pre_release_that_is_asked_for_is_selected_though_a_release_follows_it()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/crypto", "1.0.0-beta.2")
                .Publish("@thatplatypus/crypto", "1.0.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0.0-beta.2"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.0.0-beta.2"]);
        }

        [Fact]
        public async Task A_pre_release_gives_way_to_a_higher_minimum_and_is_selected_when_it_is_the_highest()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/early", "1.0.0", "@thatplatypus/crypto@1.0.0-beta.2")
                .Publish("@thatplatypus/late", "1.0.0", "@thatplatypus/crypto@1.1.0-rc.1")
                .Publish("@thatplatypus/crypto", "1.0.0-beta.2")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.1.0-rc.1");

            var release = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/early@1.0"))).ShouldSucceed();
            var candidate = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/late@1.0"))).ShouldSucceed();

            release.Selected().ShouldContain("@thatplatypus/crypto@1.0.0");
            candidate.Selected().ShouldContain("@thatplatypus/crypto@1.1.0-rc.1");
        }

        [Fact]
        public async Task What_the_project_needs_only_to_develop_it_is_resolved_as_well()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.1.0")
                .Publish("@thatplatypus/testkit", "0.1.0", "@thatplatypus/crypto@1.1");
            var manifest = Project.Named(Project.Name, ["@thatplatypus/crypto@1.0"], ["@thatplatypus/testkit@0.1"]);

            var graph = (await universe.Resolve(manifest)).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.1.0", "@thatplatypus/testkit@0.1.0"]);
        }

        [Fact]
        public async Task Two_spellings_of_one_minimum_are_one_version()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2.0")
                .Publish("@thatplatypus/crypto", "1.2.0");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.2", "@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.2.0", "@thatplatypus/grapevine@0.3.0"]);
            universe.Asked.Count(asked => asked == "@thatplatypus/crypto@1.2.0").ShouldBe(1);
        }

        [Fact]
        public async Task Below_one_the_highest_minimum_of_a_minor_line_wins_in_the_same_way()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/deflate@0.1")
                .Publish("@thatplatypus/deflate", "0.1.0")
                .Publish("@thatplatypus/deflate", "0.1.4")
                .Publish("@thatplatypus/deflate", "0.1.9");

            var graph = (await universe.Resolve(Project.Asking("@thatplatypus/deflate@0.1.4", "@thatplatypus/grapevine@0.3"))).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/deflate@0.1.4", "@thatplatypus/grapevine@0.3.0"]);
        }
    }
}
