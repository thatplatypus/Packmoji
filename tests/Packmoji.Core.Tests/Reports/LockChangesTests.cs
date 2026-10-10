using Packmoji.Core.Lockfiles;
using Packmoji.Core.Reports;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Reports
{
    public sealed class LockChangesTests
    {
        private static Lockfile Holding(params string[] packages) =>
            new(
                new RootRequirements([], []),
                packages
                    .Select(Sample.Asks)
                    .Select(package => new LockedPackage(package.Name, package.Requirement.Minimum, Sample.Repository("github.com/thatplatypus/x"), Sample.Sha('a'), VerificationLevel.Checksum, []))
                    .ToList());

        [Fact]
        public void With_no_lockfile_before_everything_is_added()
        {
            LockChanges.Between(null, Holding("@thatplatypus/b@1.0.0", "@thatplatypus/a@0.3.1")).ShouldBe(["+ @thatplatypus/a 0.3.1", "+ @thatplatypus/b 1.0.0"]);
        }

        [Fact]
        public void What_came_went_and_moved_is_said_in_order_of_name()
        {
            var before = Holding("@thatplatypus/stays@1.0.0", "@thatplatypus/moves@1.0.0", "@thatplatypus/goes@2.0.0");
            var after = Holding("@thatplatypus/comes@0.1.0", "@thatplatypus/moves@1.4.0", "@thatplatypus/stays@1.0.0");

            LockChanges.Between(before, after).ShouldBe(["+ @thatplatypus/comes 0.1.0", "- @thatplatypus/goes 2.0.0", "~ @thatplatypus/moves 1.0.0 to 1.4.0"]);
        }

        [Fact]
        public void The_same_versions_are_no_change_whatever_else_differs()
        {
            LockChanges.Between(Holding("@thatplatypus/a@1.0.0"), Holding("@thatplatypus/a@1.0.0")).ShouldBeEmpty();
            LockChanges.Between(Holding(), Holding()).ShouldBeEmpty();
        }
    }
}
