using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// Nothing in a resolution depends on the order of a manifest's tables or of a version's
    /// dependencies. A lockfile is committed and its diffs are read, so two people who resolve one
    /// manifest have to get the same bytes.
    /// </summary>
    public sealed class ResolverOrderTests
    {
        private static Universe Forwards() => new Universe()
            .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/c@1.0", "@thatplatypus/d@0.3")
            .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/a@1.0", "@thatplatypus/c@1.2")
            .Publish("@thatplatypus/c", "1.0.0")
            .Publish("@thatplatypus/c", "1.2.0", "@thatplatypus/d@0.3.1")
            .Publish("@thatplatypus/d", "0.3.0")
            .Publish("@thatplatypus/d", "0.3.1");

        private static Universe Backwards() => new Universe()
            .Publish("@thatplatypus/d", "0.3.1")
            .Publish("@thatplatypus/d", "0.3.0")
            .Publish("@thatplatypus/c", "1.2.0", "@thatplatypus/d@0.3.1")
            .Publish("@thatplatypus/c", "1.0.0")
            .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/c@1.2", "@thatplatypus/a@1.0")
            .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/d@0.3", "@thatplatypus/c@1.0");

        [Fact]
        public async Task One_graph_written_in_two_orders_gives_the_same_lockfile_byte_for_byte()
        {
            var forwards = Project.Named(Project.Name, ["@thatplatypus/a@1.0", "@thatplatypus/b@1.0"], ["@thatplatypus/d@0.3"]);
            var backwards = Project.Named(Project.Name, ["@thatplatypus/b@1.0", "@thatplatypus/a@1.0"], ["@thatplatypus/d@0.3"]);
            var one = Forwards();
            var other = Backwards();

            var first = LockfileWriter.Write((await one.Resolve(forwards)).ShouldSucceed().ToLockfile(forwards));
            var second = LockfileWriter.Write((await other.Resolve(backwards)).ShouldSucceed().ToLockfile(backwards));

            second.ShouldBe(first);
            other.Asked.ShouldBe(one.Asked);
            LockfileReader.Read(first).ShouldSucceed().Packages.Select(package => $"{package.Name}@{package.Version}")
                .ShouldBe(["@thatplatypus/a@1.0.0", "@thatplatypus/b@1.0.0", "@thatplatypus/c@1.2.0", "@thatplatypus/d@0.3.1"]);
        }

        [Fact]
        public async Task A_graph_that_cannot_be_resolved_gives_the_same_problems_in_the_same_order_however_it_was_written()
        {
            // Asking for c on a second line, and for versions of e and of f that were never published.
            var forwards = Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0", "@thatplatypus/c@2.0", "@thatplatypus/e@1.0", "@thatplatypus/f@1.0");
            var backwards = Project.Asking("@thatplatypus/f@1.0", "@thatplatypus/e@1.0", "@thatplatypus/c@2.0", "@thatplatypus/b@1.0", "@thatplatypus/a@1.0");

            var first = await Forwards().Publish("@thatplatypus/c", "2.0.0").Resolve(forwards);
            var second = await Backwards().Publish("@thatplatypus/c", "2.0.0").Resolve(backwards);

            first.Succeeded.ShouldBeFalse();
            first.Diagnostics.Count.ShouldBe(3);
            second.Diagnostics.ShouldBe(first.Diagnostics);
        }
    }
}
