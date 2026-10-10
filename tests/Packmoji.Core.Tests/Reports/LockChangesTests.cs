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

        [Fact]
        public void For_a_tool_each_change_is_what_happened_to_a_package_and_the_versions_it_went_between()
        {
            var before = Holding("@thatplatypus/stays@1.0.0", "@thatplatypus/moves@1.0.0", "@thatplatypus/goes@2.0.0");
            var after = Holding("@thatplatypus/comes@0.1.0", "@thatplatypus/moves@1.4.0", "@thatplatypus/stays@1.0.0");

            LockChanges.Of(before, after).ShouldBe(
            [
                new LockChange(LockChangeKind.Added, Sample.Name("@thatplatypus/comes"), null, Sample.Version("0.1.0")),
                new LockChange(LockChangeKind.Removed, Sample.Name("@thatplatypus/goes"), Sample.Version("2.0.0"), null),
                new LockChange(LockChangeKind.Moved, Sample.Name("@thatplatypus/moves"), Sample.Version("1.0.0"), Sample.Version("1.4.0")),
            ]);
        }

        [Fact]
        public void A_version_that_went_down_has_moved_as_one_that_went_up_has()
        {
            LockChanges.Of(Holding("@thatplatypus/a@2.0.0"), Holding("@thatplatypus/a@1.0.0"))
                .ShouldBe([new LockChange(LockChangeKind.Moved, Sample.Name("@thatplatypus/a"), Sample.Version("2.0.0"), Sample.Version("1.0.0"))]);
        }

        [Fact]
        public void With_no_lockfile_before_or_no_change_the_data_says_what_the_lines_say()
        {
            LockChanges.Of(null, Holding("@thatplatypus/a@0.3.1")).ShouldBe([new LockChange(LockChangeKind.Added, Sample.Name("@thatplatypus/a"), null, Sample.Version("0.3.1"))]);
            LockChanges.Of(Holding("@thatplatypus/a@1.0.0"), Holding("@thatplatypus/a@1.0.0")).ShouldBeEmpty();
        }
    }
}
