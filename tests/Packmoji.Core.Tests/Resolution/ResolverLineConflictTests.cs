using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// A build holds one version of a package, and a requirement only ever selects within its own
    /// compatibility line. So one package asked for on two lines has no answer.
    /// </summary>
    public sealed class ResolverLineConflictTests
    {
        [Fact]
        public async Task One_package_asked_for_on_two_lines_stops_the_resolution_and_shows_who_asks_for_each()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "2.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@2.0", "@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveLineConflict);

            diagnostic.Message.ShouldBe("\"@thatplatypus/crypto\" is asked for on the compatibility lines 1.x and 2.x.");
            diagnostic.Reason.ShouldEndWith(
                ": 1.x is asked for by @thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0; " +
                "2.x is asked for by @thatplatypus/app → @thatplatypus/crypto@2.0");
            diagnostic.Fix.ShouldContain("\"@thatplatypus/crypto\"");
        }

        [Fact]
        public async Task Below_one_each_minor_version_is_a_line_of_its_own()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/deflate@0.1.4")
                .Publish("@thatplatypus/deflate", "0.1.4")
                .Publish("@thatplatypus/deflate", "0.2.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/deflate@0.2", "@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveLineConflict);

            diagnostic.Message.ShouldBe("\"@thatplatypus/deflate\" is asked for on the compatibility lines 0.1.x and 0.2.x.");
        }

        [Fact]
        public async Task A_pre_release_is_on_the_line_of_the_release_it_comes_before()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/early", "1.0.0", "@thatplatypus/crypto@1.0.0-beta.2")
                .Publish("@thatplatypus/next", "1.0.0", "@thatplatypus/crypto@2.0.0-rc.1")
                .Publish("@thatplatypus/crypto", "1.0.0-beta.2")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "2.0.0-rc.1");

            var agreeing = await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/early@1.0"));
            var conflicting = await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/next@1.0"));

            agreeing.ShouldSucceed();
            conflicting.ShouldFailWith(DiagnosticCodes.ResolveLineConflict).Message.ShouldContain("1.x and 2.x");
        }

        [Fact]
        public async Task A_conflict_is_reported_even_when_one_side_of_it_comes_from_a_version_that_ends_up_superseded()
        {
            // Which versions are superseded depends on the same graph, so every requirement counts.
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/a", "1.1.0", "@thatplatypus/c@2.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.1")
                .Publish("@thatplatypus/c", "1.0.0")
                .Publish("@thatplatypus/c", "2.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveLineConflict);

            diagnostic.Reason.ShouldContain("1.x is asked for by @thatplatypus/app → @thatplatypus/a@1.0.0 → @thatplatypus/c@1.0;");
            diagnostic.Reason.ShouldEndWith("2.x is asked for by @thatplatypus/app → @thatplatypus/b@1.0.0 → @thatplatypus/a@1.1.0 → @thatplatypus/c@2.0");
        }

        [Fact]
        public async Task Three_lines_are_all_named_from_the_oldest_to_the_newest()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/old", "1.0.0", "@thatplatypus/crypto@0.4")
                .Publish("@thatplatypus/new", "1.0.0", "@thatplatypus/crypto@2.1")
                .Publish("@thatplatypus/crypto", "0.4.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "2.1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/new@1.0", "@thatplatypus/crypto@1.0", "@thatplatypus/old@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveLineConflict);

            diagnostic.Message.ShouldBe("\"@thatplatypus/crypto\" is asked for on the compatibility lines 0.4.x, 1.x and 2.x.");
            diagnostic.Reason.ShouldContain("0.4.x is asked for by @thatplatypus/app → @thatplatypus/old@1.0.0 → @thatplatypus/crypto@0.4; 1.x is asked for by");
        }

        [Fact]
        public async Task A_line_is_shown_by_the_shortest_chain_that_asks_for_it()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.2", "@thatplatypus/crypto@2.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.2.0")
                .Publish("@thatplatypus/crypto", "2.0.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0", "@thatplatypus/grapevine@0.3")))
                .ShouldFailWith(DiagnosticCodes.ResolveLineConflict);

            diagnostic.Reason.ShouldContain(": 1.x is asked for by @thatplatypus/app → @thatplatypus/crypto@1.0;");
        }

        [Fact]
        public async Task Every_package_in_conflict_is_reported_in_order_of_name()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/grapevine", "0.3.0", "@thatplatypus/deflate@0.2", "@thatplatypus/crypto@1.0")
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "2.0.0")
                .Publish("@thatplatypus/deflate", "0.1.0")
                .Publish("@thatplatypus/deflate", "0.2.0");

            var result = await universe.Resolve(Project.Asking("@thatplatypus/grapevine@0.3", "@thatplatypus/deflate@0.1", "@thatplatypus/crypto@2.0"));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.ShouldBeComplete().Code).ShouldBe([DiagnosticCodes.ResolveLineConflict, DiagnosticCodes.ResolveLineConflict]);
            result.Diagnostics[0].Message.ShouldContain("\"@thatplatypus/crypto\"");
            result.Diagnostics[1].Message.ShouldContain("\"@thatplatypus/deflate\"");
        }
    }
}
